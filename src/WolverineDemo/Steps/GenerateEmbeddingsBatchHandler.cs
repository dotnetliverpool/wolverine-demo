using Microsoft.Extensions.Logging;
using Wolverine;
using Wolverine.ErrorHandling;
using Wolverine.Runtime.Handlers;
using WolverineDemo.Infrastructure;
using WolverineDemo.Sagas;

namespace WolverineDemo.Steps;

// Cache at the boundary, not in the saga. Only misses call the (fake) model,
// wrapped in TransientAiCall -- retries are cheap when most of the work is
// already cached.
public static class GenerateEmbeddingsBatchHandler
{
    // Must match RetryWithCooldown slots below (initial attempt + each cooldown).
    public static readonly TimeSpan[] MessageRetryCooldowns =
    [
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2)
    ];

    public static int MaxMessageAttempts => MessageRetryCooldowns.Length + 1;

    public static async Task<EmbeddingsBatchGenerated> Handle(
        GenerateEmbeddingsBatch cmd,
        Envelope envelope,
        IEmbeddingCacheStore cache,
        IEmbeddingClient client,
        IProgressPublisher progress)
    {
        var messageTry = Math.Max(1, envelope.Attempts);
        var batchLabel = $"batch {cmd.BatchIndex + 1} of {cmd.BatchCount}";

        await progress.PublishAsync(cmd.DocumentId, "embedding",
            $"Embedding {batchLabel} ({cmd.Chunks.Count} chunks) — try {messageTry} of {MaxMessageAttempts}");

        var hits = await cache.GetManyAsync(cmd.DocumentId, cmd.Chunks);
        var misses = cmd.Chunks.Where(c => !hits.ContainsKey(c)).ToList();

        await progress.PublishAsync(cmd.DocumentId, "embedding",
            $"{batchLabel} cache: {hits.Count} hit(s), {misses.Count} miss(es)");

        if (misses.Count == 0)
        {
            await progress.PublishAsync(cmd.DocumentId, "embedding",
                $"{batchLabel} all cache hits — skipping model.");
            return new EmbeddingsBatchGenerated(cmd.DocumentId, cmd.BatchIndex);
        }

        var callKey = $"{cmd.DocumentId}:{cmd.BatchIndex}";
        var fresh = await TransientAiCall.RunAsync(
            () => client.GenerateEmbeddingsAsync(misses, cmd.FailMode, callKey),
            msg => progress.PublishAsync(cmd.DocumentId, "embedding",
                $"{batchLabel} {msg}"));

        await cache.SaveManyAsync(cmd.DocumentId, fresh);
        await progress.PublishAsync(cmd.DocumentId, "embedding",
            $"{batchLabel} cached {fresh.Count} new embedding(s).");

        return new EmbeddingsBatchGenerated(cmd.DocumentId, cmd.BatchIndex);
    }

    // Layer 2 of the retry story: retry the message itself a few times with
    // cooldown; Wolverine dead-letters it automatically once that's exhausted.
    // FailureLogLevel is None because TransientAiCallException is expected and
    // handled -- stack traces would drown the talk demo. Cooldowns stay short
    // so dead letters land before the ingestion timeout. On final failure,
    // publish MarkIngestionFailed so the console shows our line, not Wolverine's.
    public static void Configure(HandlerChain chain)
    {
        chain.FailureLogLevel = LogLevel.None;
        chain
            .OnException<TransientAiCallException>()
            .RetryWithCooldown(MessageRetryCooldowns)
            .Then.MoveToErrorQueue()
            .And(async (_, lifecycle, _) =>
            {
                if (lifecycle.Envelope?.Message is not GenerateEmbeddingsBatch cmd)
                    return;
                if (lifecycle is not IMessageContext bus)
                    return;

                var tryNumber = Math.Max(1, lifecycle.Envelope.Attempts);
                var reason =
                    $"Batch {cmd.BatchIndex + 1} of {cmd.BatchCount} dead-lettered after retries " +
                    $"(try {tryNumber} of {MaxMessageAttempts}).";
                await bus.PublishAsync(new MarkIngestionFailed(cmd.DocumentId, reason));
            });
    }
}

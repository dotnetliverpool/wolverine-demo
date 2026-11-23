using System.Collections.Concurrent;

namespace WolverineDemo.Infrastructure;

public interface IEmbeddingClient
{
    Task<Dictionary<string, string>> GenerateEmbeddingsAsync(List<string> texts, string failMode, string callKey);
}

// Stands in for a real embedding model call. "failMode" drives the live demo:
//   none  -> always succeeds
//   flaky -> fails the first four calls for a given callKey, then succeeds --
//            deliberately more than layer 1's own 3-attempt budget, so the
//            demo needs BOTH layer 1 (TransientAiCall) and layer 2 (Wolverine's
//            message-level RetryWithCooldown on GenerateEmbeddingsBatchHandler)
//            before it finally succeeds.
//   dead  -> always fails, so it exhausts both layers and lands in the dead-letter table
//   stall -> never returns, to demo the IngestionTimeout forcing a stuck run to fail
// All failures throw the same "transient" exception that TransientAiCall and
// the handler's RetryWithCooldown chain are both configured to catch.
public class FakeEmbeddingClient : IEmbeddingClient
{
    private readonly ConcurrentDictionary<string, int> _attempts = new();

    public async Task<Dictionary<string, string>> GenerateEmbeddingsAsync(List<string> texts, string failMode, string callKey)
    {
        await Task.Delay(100);

        if (failMode == "stall")
            await Task.Delay(Timeout.InfiniteTimeSpan);

        if (failMode == "dead")
            throw new InvalidOperationException("transient");

        if (failMode == "flaky")
        {
            var attempt = _attempts.AddOrUpdate(callKey, 1, (_, n) => n + 1);
            if (attempt <= 4)
                throw new InvalidOperationException("transient");
        }

        return texts.ToDictionary(t => t, t => $"embedding::{Math.Abs(t.GetHashCode()):x8}");
    }
}

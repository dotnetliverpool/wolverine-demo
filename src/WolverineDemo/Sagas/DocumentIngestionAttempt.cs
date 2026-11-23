using Wolverine;
using Wolverine.Configuration;
using Wolverine.Transports.Local;
using WolverineDemo.Domain;
using WolverineDemo.Infrastructure;

namespace WolverineDemo.Sagas;

public enum IngestionStage
{
    Extracting,
    Chunking,
    Embedding,
    Reconciling,
    Complete,
    Failed
}

// The saga: state only. Routing and progress -- never business rules. It never
// reaches into Document's internals; it only calls Document's own methods.
public sealed class DocumentIngestionAttempt : Saga, IConfigureLocalQueue
{
    public Guid Id { get; set; }
    public IngestionStage Stage { get; set; }
    public string FailMode { get; set; } = "none";
    public int PendingBatches { get; set; }
    public int CompletedBatches { get; set; }
    public DateTimeOffset StartedAt { get; set; }

    // Serialize local queues for this saga's messages (fan-in of
    // EmbeddingsBatchGenerated). GenerateEmbeddingsBatch stays on its own
    // handler queue and still runs in parallel.
    public static void Configure(LocalQueueConfiguration configuration) =>
        configuration.Sequential();

    public static (DocumentIngestionAttempt, ExtractDocumentText, IngestionTimeout) Start(
        StartDocumentIngestion cmd, DocumentRepository documents)
    {
        var saga = new DocumentIngestionAttempt
        {
            Id = cmd.DocumentId,
            Stage = IngestionStage.Extracting,
            FailMode = cmd.FailMode,
            StartedAt = DateTimeOffset.UtcNow
        };

        documents.Create(cmd.DocumentId);

        return (saga, new ExtractDocumentText(cmd.DocumentId, cmd.FailMode), new IngestionTimeout(cmd.DocumentId));
    }

    public async Task<GenerateEmbeddingsBatch[]> Handle(
        DocumentTextExtracted @event, DocumentRepository documents, IProgressPublisher progress)
    {
        documents.Get(Id).MarkExtracted();

        Stage = IngestionStage.Chunking;
        var textChunks = TextChunker.Chunk(@event.Text, chunkCount: 4);
        await progress.PublishAsync(Id, "chunking", $"Split into {textChunks.Count} chunk(s).");

        Stage = IngestionStage.Embedding;
        var batches = WorkBatches.Split(textChunks, size: 2);
        PendingBatches = batches.Count;
        CompletedBatches = 0;
        await progress.PublishAsync(Id, "embedding", $"Embedding in {batches.Count} batch(es).");

        // Returning the concrete message type -- Wolverine's cascading-message
        // convention doesn't require boxing each one as object by hand.
        return batches
            .Select((batch, index) => new GenerateEmbeddingsBatch(Id, FailMode, index, batches.Count, batch))
            .ToArray();
    }

    public ReconcileComplete? Handle(EmbeddingsBatchGenerated @event)
    {
        CompletedBatches++;

        if (CompletedBatches < PendingBatches)
            return null;

        Stage = IngestionStage.Reconciling;
        return new ReconcileComplete(Id);
    }

    public async Task Handle(ReconcileComplete @event, DocumentRepository documents, IProgressPublisher progress)
    {
        var document = documents.Get(Id);
        document.MarkEmbedded();
        document.MarkComplete();

        Stage = IngestionStage.Complete;
        await progress.PublishAsync(Id, "complete", "Ingestion complete.");

        MarkCompleted();
    }

    public async Task Handle(MarkIngestionFailed command, DocumentRepository documents, IProgressPublisher progress)
    {
        documents.Get(Id).MarkFailed(command.Reason);

        Stage = IngestionStage.Failed;
        await progress.PublishAsync(Id, "failed", command.Reason);

        MarkCompleted();
    }

    // Wolverine's saga-timeout pattern: IngestionTimeout is a TimeoutMessage,
    // scheduled once from Start and delivered back to this same saga instance
    // by its identity. 20s is well past "flaky"'s worst-case legitimate retry
    // path (a few seconds) but short enough to demo live; a "dead" run's own
    // message-level retries can still take longer to exhaust on their own and
    // land in the dead-letter table afterwards -- that's a second, independent
    // safety net, not a race to avoid.
    public async Task Handle(IngestionTimeout timeout, DocumentRepository documents, IProgressPublisher progress)
    {
        documents.Get(Id).MarkFailed("ingestion timeout");

        Stage = IngestionStage.Failed;
        await progress.PublishAsync(Id, "failed", "Timed out waiting for the pipeline to finish.");

        MarkCompleted();
    }

    // The saga already finished (and MarkCompleted() deleted its row) before
    // the scheduled timeout fired. Nothing to do -- but Wolverine requires this
    // method to exist, even empty, or a late timeout throws.
    public static async Task NotFound(IngestionTimeout timeout, IProgressPublisher progress)
    {
        await progress.PublishAsync(
            timeout.DocumentId,
            "timeout",
            "Late timeout ignored; ingestion already finished.");
    }

    // Both embedding batches can DLQ and each publish MarkIngestionFailed; the
    // first completes the saga, so the second needs NotFound.
    public static async Task NotFound(MarkIngestionFailed command, IProgressPublisher progress)
    {
        await progress.PublishAsync(
            command.DocumentId,
            "failed",
            command.Reason);
    }
}

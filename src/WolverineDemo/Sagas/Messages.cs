using Wolverine;
using Wolverine.Persistence.Sagas;

namespace WolverineDemo.Sagas;

public record StartDocumentIngestion(Guid DocumentId, string FailMode);

public record ExtractDocumentText(Guid DocumentId, string FailMode);

// [SagaIdentity] tells Wolverine which property correlates this message back
// to the running DocumentIngestionAttempt instance -- by default it looks for
// a property named Id or {SagaTypeName}Id, neither of which DocumentId is.
public record DocumentTextExtracted([property: SagaIdentity] Guid DocumentId, string FailMode, string Text);

public record GenerateEmbeddingsBatch(
    Guid DocumentId,
    string FailMode,
    int BatchIndex,
    int BatchCount,
    List<string> Chunks);

public record EmbeddingsBatchGenerated([property: SagaIdentity] Guid DocumentId, int BatchIndex);

public record ReconcileComplete([property: SagaIdentity] Guid DocumentId);

public record MarkIngestionFailed([property: SagaIdentity] Guid DocumentId, string Reason);

// Wolverine's saga-timeout pattern: a TimeoutMessage subclass, scheduled from
// Start and delivered back to the saga instance it names by SagaIdentity.
public record IngestionTimeout([property: SagaIdentity] Guid DocumentId) : TimeoutMessage(TimeSpan.FromSeconds(50));

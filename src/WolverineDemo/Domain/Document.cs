namespace WolverineDemo.Domain;

public enum DocumentStage
{
    Uploaded,
    Extracted,
    Embedded
}

// A closed outcome type instead of a status enum + a nullable reason: a document
// can't be Complete and still carry a failure reason, and can't be Failed without
// one -- the compiler and the pattern match enforce it, not a convention.
public abstract record DocumentOutcome
{
    public sealed record InProgress(DocumentStage Stage) : DocumentOutcome;
    public sealed record Complete : DocumentOutcome;
    public sealed record Failed(string Reason) : DocumentOutcome;
}

// The aggregate. Owns its own invariants only -- it never tracks saga progress.
public class Document
{
    public Guid Id { get; }
    public DocumentOutcome Outcome { get; private set; }

    public Document(Guid id)
    {
        Id = id;
        Outcome = new DocumentOutcome.InProgress(DocumentStage.Uploaded);
    }

    public void MarkExtracted()
    {
        if (Outcome is not DocumentOutcome.InProgress(DocumentStage.Uploaded))
            throw new InvalidOperationException($"Cannot mark extracted from {Outcome}");

        Outcome = new DocumentOutcome.InProgress(DocumentStage.Extracted);
    }

    public void MarkEmbedded()
    {
        if (Outcome is not DocumentOutcome.InProgress(DocumentStage.Extracted))
            throw new InvalidOperationException($"Cannot mark embedded from {Outcome}");

        Outcome = new DocumentOutcome.InProgress(DocumentStage.Embedded);
    }

    public void MarkComplete()
    {
        if (Outcome is not DocumentOutcome.InProgress(DocumentStage.Embedded))
            throw new InvalidOperationException($"Cannot complete from {Outcome}");

        Outcome = new DocumentOutcome.Complete();
    }

    public void MarkFailed(string reason)
    {
        if (Outcome is DocumentOutcome.Complete)
            throw new InvalidOperationException("Cannot fail a completed document");

        Outcome = new DocumentOutcome.Failed(reason);
    }
}

public class DocumentRepository
{
    private readonly Dictionary<Guid, Document> _documents = new();

    // Creation and lookup are different things -- a caller that expects an
    // existing document should never silently get a freshly-made one instead.
    public Document Create(Guid id)
    {
        if (_documents.ContainsKey(id))
            throw new InvalidOperationException($"Document {id} already exists.");

        var doc = new Document(id);
        _documents[id] = doc;
        return doc;
    }

    public Document Get(Guid id)
    {
        if (!_documents.TryGetValue(id, out var doc))
            throw new InvalidOperationException($"Document {id} does not exist.");

        return doc;
    }
}

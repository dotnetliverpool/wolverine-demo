namespace WolverineDemo.Domain.BadExamples;

// ANTI-PATTERN -- for the talk only. Not referenced anywhere else in this app.
//
// This is what a "process-aware" aggregate looks like: it tracks its own step,
// waits on other things, and decides what happens next -- instead of just
// holding its own invariants and reacting to explicit calls. Compare to the
// real Document in ../Document.cs.
public enum DocumentStep
{
    Uploaded,
    Extracting,
    Chunking,
    Embedding,
    Reconciling,
    Complete,
    Failed
}

public class ProcessAwareDocument
{
    public Guid Id { get; }
    public DocumentStep CurrentStep { get; private set; } = DocumentStep.Uploaded;
    public bool WaitingForExtraction { get; private set; }
    public bool WaitingForEmbeddings { get; private set; }
    public bool WaitingForReconciliation { get; private set; }
    public int PendingBatches { get; private set; }
    public int CompletedBatches { get; private set; }

    public ProcessAwareDocument(Guid id) => Id = id;

    // The aggregate deciding to kick off the next step itself -- that's the
    // saga's job, not the aggregate's.
    public void StartExtraction()
    {
        CurrentStep = DocumentStep.Extracting;
        WaitingForExtraction = true;
    }

    public void OnTextExtracted(int chunkCount)
    {
        if (CurrentStep != DocumentStep.Extracting) return;

        WaitingForExtraction = false;
        CurrentStep = DocumentStep.Chunking;

        // ...and now it's tracking fan-out/fan-in counters that belong to the
        // saga, and deciding to wait on them itself.
        PendingBatches = chunkCount;
        CompletedBatches = 0;
        CurrentStep = DocumentStep.Embedding;
        WaitingForEmbeddings = true;
    }

    public void OnEmbeddingsBatchReady()
    {
        CompletedBatches++;

        if (CompletedBatches < PendingBatches) return;

        WaitingForEmbeddings = false;
        CurrentStep = DocumentStep.Reconciling;
        WaitingForReconciliation = true;
    }

    public void OnReconciled()
    {
        WaitingForReconciliation = false;
        CurrentStep = DocumentStep.Complete;
    }
}

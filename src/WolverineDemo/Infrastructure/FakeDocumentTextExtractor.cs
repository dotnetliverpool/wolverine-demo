namespace WolverineDemo.Infrastructure;

public interface IDocumentTextExtractor
{
    Task<string> ExtractAsync(Guid documentId);
}

// Stands in for something like Azure Document Intelligence. The saga's step
// handler calls this -- the saga itself never does I/O.
public class FakeDocumentTextExtractor : IDocumentTextExtractor
{
    public async Task<string> ExtractAsync(Guid documentId)
    {
        await Task.Delay(400);

        return "Wolverine coordinates long running workflows as sagas so your aggregates " +
               "never have to track process state that is not their own. Side effects such " +
               "as calling an extraction service or an embedding model live in handlers, " +
               "not in the saga, and the saga only records outcomes onto the aggregate.";
    }
}

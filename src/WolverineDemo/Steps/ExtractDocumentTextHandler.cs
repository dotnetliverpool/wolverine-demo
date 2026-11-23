using WolverineDemo.Infrastructure;
using WolverineDemo.Sagas;

namespace WolverineDemo.Steps;

// Side effects live elsewhere. The saga never calls the extractor itself --
// this handler does the I/O and hands back an event.
public static class ExtractDocumentTextHandler
{
    public static async Task<DocumentTextExtracted> Handle(
        ExtractDocumentText cmd,
        IDocumentTextExtractor extractor,
        IProgressPublisher progress)
    {
        await progress.PublishAsync(cmd.DocumentId, "extracting", "Extracting document text...");

        var text = await extractor.ExtractAsync(cmd.DocumentId);

        return new DocumentTextExtracted(cmd.DocumentId, cmd.FailMode, text);
    }
}

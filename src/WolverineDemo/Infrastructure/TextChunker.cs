namespace WolverineDemo.Infrastructure;

public static class TextChunker
{
    public static List<string> Chunk(string text, int chunkCount)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var perChunk = Math.Max(1, (int)Math.Ceiling(words.Length / (double)chunkCount));

        return words
            .Chunk(perChunk)
            .Select(w => string.Join(' ', w))
            .ToList();
    }
}

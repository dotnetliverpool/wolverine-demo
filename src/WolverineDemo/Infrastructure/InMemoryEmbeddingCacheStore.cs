using System.Collections.Concurrent;

namespace WolverineDemo.Infrastructure;

public interface IEmbeddingCacheStore
{
    Task<Dictionary<string, string>> GetManyAsync(Guid documentId, IEnumerable<string> texts);
    Task SaveManyAsync(Guid documentId, Dictionary<string, string> embeddings);
}

// Cache at the side-effect boundary, not in the saga. A real version of this
// would be a Postgres table; in-memory is enough to demo "misses only go out."
// Keys are scoped by document so each ingestion attempt has its own namespace —
// otherwise identical chunks from FakeDocumentTextExtractor would warm a
// process-wide cache and skip flaky/dead fail modes on later runs.
public class InMemoryEmbeddingCacheStore : IEmbeddingCacheStore
{
    private readonly ConcurrentDictionary<string, string> _cache = new();

    public Task<Dictionary<string, string>> GetManyAsync(Guid documentId, IEnumerable<string> texts)
    {
        var hits = new Dictionary<string, string>();
        foreach (var text in texts)
        {
            if (_cache.TryGetValue(Key(documentId, text), out var embedding))
                hits[text] = embedding;
        }

        return Task.FromResult(hits);
    }

    public Task SaveManyAsync(Guid documentId, Dictionary<string, string> embeddings)
    {
        foreach (var (text, embedding) in embeddings)
            _cache[Key(documentId, text)] = embedding;

        return Task.CompletedTask;
    }

    private static string Key(Guid documentId, string text) => $"{documentId:N}:{text}";
}

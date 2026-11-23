namespace WolverineDemo.Infrastructure;

public static class WorkBatches
{
    public static List<List<T>> Split<T>(IReadOnlyList<T> items, int size) =>
        items.Chunk(size).Select(batch => batch.ToList()).ToList();
}

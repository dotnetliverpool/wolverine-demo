namespace WolverineDemo.Infrastructure;

// "Swap the hub for anything -- here it's not even a hub." The saga calls
// IProgressPublisher.PublishAsync(...); this implementation just colors the
// console by stage. Replace this one class with a SignalR broadcaster and
// nothing else in the pipeline changes.
public class ConsoleProgressPublisher : IProgressPublisher
{
    private static readonly Dictionary<string, ConsoleColor> StageColors = new()
    {
        ["extracting"] = ConsoleColor.Cyan,
        ["chunking"] = ConsoleColor.Yellow,
        ["embedding"] = ConsoleColor.Magenta,
        ["complete"] = ConsoleColor.Green,
        ["failed"] = ConsoleColor.Red,
        ["timeout"] = ConsoleColor.DarkGray,
        ["sweep"] = ConsoleColor.DarkGray
    };

    public Task PublishAsync(Guid documentId, string stage, string message)
    {
        var color = StageColors.GetValueOrDefault(stage, ConsoleColor.White);
        var previous = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.WriteLine($"[{documentId.ToString()[..8]}] {stage,-10} {message}");
        Console.ForegroundColor = previous;
        return Task.CompletedTask;
    }
}

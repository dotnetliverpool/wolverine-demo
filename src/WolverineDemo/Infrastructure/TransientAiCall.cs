namespace WolverineDemo.Infrastructure;

public class TransientAiCallException(string message) : Exception(message);

// A fixed backoff around the one call that talks to the (fake) model.
// Layer 1 of the demo's two-layer retry story -- layer 2 is the Wolverine
// chain.OnException<T>().RetryWithCooldown(...) on GenerateEmbeddingsBatchHandler.
// Delays are short on purpose so a live talk can show both layers before the
// ingestion timeout.
public static class TransientAiCall
{
    public static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMilliseconds(200),
        TimeSpan.FromMilliseconds(500)
    ];

    public static int MaxAttempts => RetryDelays.Length + 1;

    public static async Task<T> RunAsync<T>(Func<Task<T>> call, Func<string, Task>? report = null)
    {
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                return await call();
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                if (attempt == MaxAttempts)
                {
                    if (report is not null)
                        await report($"(layer 1) try {attempt} of {MaxAttempts}: exhausted -- escalating to message retry");
                    throw new TransientAiCallException("Exhausted retries calling the embedding model.");
                }

                var delay = RetryDelays[attempt - 1];
                if (report is not null)
                    await report($"(layer 1) try {attempt} of {MaxAttempts}: transient failure, retrying in {delay.TotalSeconds:0.###}s...");
                await Task.Delay(delay);
            }
        }

        throw new InvalidOperationException("Unreachable");
    }

    private static bool IsTransient(Exception ex) => ex is InvalidOperationException { Message: "transient" };
}

namespace WolverineDemo.Infrastructure;

// The saga and its handlers depend on this abstraction only -- they never know
// how (or whether) progress actually reaches a person watching.
public interface IProgressPublisher
{
    Task PublishAsync(Guid documentId, string stage, string message);
}

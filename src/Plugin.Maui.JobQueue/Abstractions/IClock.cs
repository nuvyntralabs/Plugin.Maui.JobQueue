namespace Plugin.Maui.JobQueue;

/// <summary>
/// Clock abstraction so tests can advance time through backoff.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

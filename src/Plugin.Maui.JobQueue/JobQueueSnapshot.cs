namespace Plugin.Maui.JobQueue;

/// <summary>
/// Point-in-time counts for dashboards and sample UIs.
/// </summary>
public sealed class JobQueueSnapshot
{
    public int Pending { get; init; }

    /// <summary>Pending jobs whose next attempt is still in the future.</summary>
    public int Scheduled { get; init; }

    public int Running { get; init; }

    /// <summary>Failed jobs waiting for backoff.</summary>
    public int Failed { get; init; }

    public int DeadLetter { get; init; }

    public int Succeeded { get; init; }

    public int Cancelled { get; init; }

    public bool IsWorkerRunning { get; init; }

    public bool IsOnline { get; init; }
}

namespace Plugin.Maui.JobQueue;

/// <summary>
/// Persisted lifecycle of a job.
/// </summary>
public enum JobStatus
{
    /// <summary>Waiting to run, including delayed jobs whose <c>NextAttemptAt</c> is still in the future.</summary>
    Pending = 0,

    /// <summary>Claimed by a worker.</summary>
    Running = 1,

    /// <summary>Finished successfully and retained because <see cref="JobQueueOptions.DeleteOnSuccess"/> is false.</summary>
    Succeeded = 2,

    /// <summary>Last attempt failed; waiting for backoff before the next try.</summary>
    Failed = 3,

    /// <summary>Exhausted retries (or aborted). Stays until replayed or discarded.</summary>
    DeadLetter = 4,

    /// <summary>Cancelled before completion.</summary>
    Cancelled = 5
}

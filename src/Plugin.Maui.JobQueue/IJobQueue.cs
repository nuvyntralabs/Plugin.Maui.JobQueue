namespace Plugin.Maui.JobQueue;

/// <summary>
/// Durable job queue. Payloads live in SQLite until they succeed (and are deleted),
/// are cancelled, or land in the dead-letter queue.
/// </summary>
public interface IJobQueue
{
    /// <summary>True while the in-process worker is running.</summary>
    bool IsRunning { get; }

    event EventHandler<JobEventArgs>? JobQueued;

    event EventHandler<JobEventArgs>? JobStarted;

    event EventHandler<JobCompletedEventArgs>? JobCompleted;

    event EventHandler<JobFailedEventArgs>? JobFailed;

    event EventHandler<JobEventArgs>? JobDeadLettered;

    event EventHandler<JobEventArgs>? JobCancelled;

    /// <summary>Persists <paramref name="job"/> and wakes the worker.</summary>
    Task<string> EnqueueAsync<TJob>(TJob job, JobEnqueueOptions? options = null, CancellationToken cancellationToken = default)
        where TJob : class, IJob;

    /// <summary>Cancels a pending or retrying job. Running jobs are cancelled at the next cooperative check.</summary>
    Task<bool> CancelAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>Returns one job, including dead-lettered rows.</summary>
    Task<JobInfo?> GetAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>Lists persisted jobs matching <paramref name="query"/>.</summary>
    Task<IReadOnlyList<JobInfo>> ListAsync(JobListQuery? query = null, CancellationToken cancellationToken = default);

    /// <summary>Jobs that exhausted retries or were aborted.</summary>
    Task<IReadOnlyList<JobInfo>> GetDeadLettersAsync(CancellationToken cancellationToken = default);

    /// <summary>Moves one dead-lettered job back to pending with attempts reset.</summary>
    Task<bool> RequeueDeadLetterAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>Requeues every dead-lettered job. Returns how many were moved.</summary>
    Task<int> RequeueDeadLettersAsync(CancellationToken cancellationToken = default);

    /// <summary>Permanently deletes a dead-lettered job.</summary>
    Task<bool> DiscardDeadLetterAsync(string jobId, CancellationToken cancellationToken = default);

    /// <summary>Queue depth by status.</summary>
    Task<JobQueueSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes due jobs until the queue is idle or <paramref name="maxJobs"/> is reached.
    /// Use this from tests or from an OS background task that should drain work.
    /// </summary>
    Task<int> DrainAsync(int? maxJobs = null, CancellationToken cancellationToken = default);

    /// <summary>Starts the in-process worker. Safe to call more than once.</summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Stops the in-process worker. Persisted jobs stay in SQLite.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}

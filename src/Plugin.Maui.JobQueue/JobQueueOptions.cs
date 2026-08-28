namespace Plugin.Maui.JobQueue;

/// <summary>
/// Host configuration for the durable job queue.
/// </summary>
public sealed class JobQueueOptions
{
    internal List<Action<IServiceCollection>> HandlerRegistrations { get; } = [];

    /// <summary>Override the SQLite file path. When empty, uses app data + <see cref="DatabaseFileName"/>.</summary>
    public string? DatabasePath { get; set; }

    /// <summary>File name under the app data directory.</summary>
    public string DatabaseFileName { get; set; } = JobQueueDefaults.DatabaseFileName;

    /// <summary>Use an in-memory store. Intended for tests and demos.</summary>
    public bool UseInMemoryStore { get; set; }

    /// <summary>How many jobs run at once.</summary>
    public int WorkerCount { get; set; } = JobQueueDefaults.DefaultWorkerCount;

    /// <summary>Idle wait between drain passes.</summary>
    public TimeSpan PollInterval { get; set; } = JobQueueDefaults.DefaultPollInterval;

    /// <summary>How long a claimed job may stay <see cref="JobStatus.Running"/> before recovery.</summary>
    public TimeSpan LeaseDuration { get; set; } = JobQueueDefaults.DefaultLeaseDuration;

    /// <summary>Retry ceiling when the job and enqueue options do not specify one.</summary>
    public int DefaultMaxAttempts { get; set; } = JobQueueDefaults.DefaultMaxAttempts;

    /// <summary>Backoff applied after a failed attempt.</summary>
    public BackoffPolicy Backoff { get; set; } = BackoffPolicy.Exponential(
        JobQueueDefaults.DefaultInitialBackoff,
        JobQueueDefaults.DefaultMaxBackoff);

    /// <summary>Delete the SQLite row after a successful run. This is the durable-queue default.</summary>
    public bool DeleteOnSuccess { get; set; } = true;

    /// <summary>Start the worker from <c>IMauiInitializeService</c>.</summary>
    public bool AutoStart { get; set; } = true;

    /// <summary>Drain due jobs when the app returns to the foreground.</summary>
    public bool DrainOnResume { get; set; } = true;

    /// <summary>Replace the clock. Tests inject a fake.</summary>
    public IClock? Clock { get; set; }

    /// <summary>Replace the connectivity gate. Tests inject a manual gate.</summary>
    public INetworkGate? NetworkGate { get; set; }

    /// <summary>Replace the store. Tests inject <see cref="Storage.InMemoryJobStore"/>.</summary>
    public IJobStore? Store { get; set; }

    /// <summary>Optional deterministic random for backoff jitter. Tests set this to disable jitter.</summary>
    public Random? Random { get; set; }

    /// <summary>Registers a job type and its handler with the generic host.</summary>
    public JobQueueOptions Register<TJob, THandler>()
        where TJob : class, IJob
        where THandler : class, IJobHandler<TJob>
    {
        HandlerRegistrations.Add(static services => services.AddJobHandler<TJob, THandler>());
        return this;
    }
}

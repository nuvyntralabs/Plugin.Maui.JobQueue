namespace Plugin.Maui.JobQueue;

/// <summary>
/// Default knobs for the durable queue.
/// </summary>
public static class JobQueueDefaults
{
    public const string DefaultQueue = "default";

    public const string DatabaseFileName = "plugin.maui.jobqueue.db3";

    public const int DefaultMaxAttempts = 5;

    public const int DefaultWorkerCount = 1;

    public static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(1);

    public static readonly TimeSpan DefaultLeaseDuration = TimeSpan.FromMinutes(2);

    public static readonly TimeSpan DefaultInitialBackoff = TimeSpan.FromSeconds(2);

    public static readonly TimeSpan DefaultMaxBackoff = TimeSpan.FromMinutes(15);
}

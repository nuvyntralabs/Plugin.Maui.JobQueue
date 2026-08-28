namespace Plugin.Maui.JobQueue;

/// <summary>
/// Raised for queue lifecycle changes. Handlers may run on a worker thread.
/// </summary>
public class JobEventArgs : EventArgs
{
    public JobEventArgs(JobInfo job)
    {
        Job = job;
    }

    public JobInfo Job { get; }
}

/// <summary>
/// Raised after a successful execution.
/// </summary>
public sealed class JobCompletedEventArgs : JobEventArgs
{
    public JobCompletedEventArgs(JobInfo job, TimeSpan duration) : base(job)
    {
        Duration = duration;
    }

    public TimeSpan Duration { get; }
}

/// <summary>
/// Raised after a failed attempt that will retry or dead-letter.
/// </summary>
public sealed class JobFailedEventArgs : JobEventArgs
{
    public JobFailedEventArgs(JobInfo job, Exception? exception, bool willRetry) : base(job)
    {
        Exception = exception;
        WillRetry = willRetry;
    }

    public Exception? Exception { get; }

    public bool WillRetry { get; }
}

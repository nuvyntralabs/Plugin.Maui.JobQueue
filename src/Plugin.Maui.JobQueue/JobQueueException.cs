namespace Plugin.Maui.JobQueue;

/// <summary>
/// Thrown when the queue cannot persist or dispatch a job.
/// </summary>
public sealed class JobQueueException : Exception
{
    public JobQueueException(string message) : base(message)
    {
    }

    public JobQueueException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

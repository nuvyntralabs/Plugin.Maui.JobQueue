namespace Plugin.Maui.JobQueue;

/// <summary>
/// Thrown from a handler (or <see cref="JobContext.Abort"/>) to skip remaining retries and dead-letter the job.
/// </summary>
public sealed class JobAbortException : Exception
{
    public JobAbortException(string message) : base(message)
    {
    }

    public JobAbortException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

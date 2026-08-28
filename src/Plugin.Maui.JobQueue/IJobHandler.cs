namespace Plugin.Maui.JobQueue;

/// <summary>
/// Executes a persisted <typeparamref name="TJob"/>. Resolved from dependency injection.
/// </summary>
/// <remarks>
/// Complete successfully to delete the job (when <see cref="JobQueueOptions.DeleteOnSuccess"/> is true).
/// Throw to retry with backoff. Throw <see cref="JobAbortException"/> to skip retries and dead-letter immediately.
/// </remarks>
public interface IJobHandler<in TJob> where TJob : IJob
{
    /// <summary>
    /// Runs <paramref name="job"/>. The payload is deserialized from SQLite on every attempt.
    /// </summary>
    Task ExecuteAsync(TJob job, JobContext context, CancellationToken cancellationToken);
}

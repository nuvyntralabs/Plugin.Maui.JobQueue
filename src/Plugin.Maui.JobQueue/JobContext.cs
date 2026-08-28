namespace Plugin.Maui.JobQueue;

/// <summary>
/// Execution context passed to <see cref="IJobHandler{TJob}"/>.
/// </summary>
public sealed class JobContext
{
    internal JobContext(
        string jobId,
        string jobType,
        string queue,
        int attempt,
        int maxAttempts,
        string? correlationId,
        IReadOnlyDictionary<string, string> metadata)
    {
        JobId = jobId;
        JobType = jobType;
        Queue = queue;
        Attempt = attempt;
        MaxAttempts = maxAttempts;
        CorrelationId = correlationId;
        Metadata = metadata;
    }

    /// <summary>Stable identifier assigned at enqueue.</summary>
    public string JobId { get; }

    /// <summary>Registered job type name.</summary>
    public string JobType { get; }

    /// <summary>Named queue.</summary>
    public string Queue { get; }

    /// <summary>1-based attempt number for this execution.</summary>
    public int Attempt { get; }

    /// <summary>Configured retry ceiling.</summary>
    public int MaxAttempts { get; }

    /// <summary>Optional correlation id from <see cref="JobEnqueueOptions"/>.</summary>
    public string? CorrelationId { get; }

    /// <summary>Persisted metadata.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; }

    /// <summary>Moves the job to the dead-letter queue without further retries.</summary>
    public void Abort(string reason) => throw new JobAbortException(reason);
}

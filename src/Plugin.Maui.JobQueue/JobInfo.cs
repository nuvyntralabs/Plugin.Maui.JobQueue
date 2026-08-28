namespace Plugin.Maui.JobQueue;

/// <summary>
/// Read model for a persisted job.
/// </summary>
public sealed class JobInfo
{
    /// <summary>Stable identifier assigned at enqueue.</summary>
    public required string Id { get; init; }

    /// <summary>Registered job type name.</summary>
    public required string JobType { get; init; }

    /// <summary>Named queue.</summary>
    public required string Queue { get; init; }

    /// <summary>Current lifecycle state.</summary>
    public JobStatus Status { get; init; }

    /// <summary>Claim order among due jobs.</summary>
    public JobPriority Priority { get; init; }

    /// <summary>Completed attempts so far (includes the current run when <see cref="Status"/> is <see cref="JobStatus.Running"/>).</summary>
    public int Attempts { get; init; }

    /// <summary>Retry ceiling.</summary>
    public int MaxAttempts { get; init; }

    /// <summary>When the job was first persisted.</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the current or next attempt becomes due.</summary>
    public DateTimeOffset NextAttemptAt { get; init; }

    /// <summary>When the current run started, if running or previously started.</summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>When the job finished, if retained after success.</summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>Last exception message or abort reason.</summary>
    public string? LastError { get; init; }

    /// <summary>Idempotency key, when supplied.</summary>
    public string? IdempotencyKey { get; init; }

    /// <summary>Optional correlation id.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>Whether the worker waits for connectivity.</summary>
    public bool RequiresNetwork { get; init; }

    /// <summary>Persisted metadata.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

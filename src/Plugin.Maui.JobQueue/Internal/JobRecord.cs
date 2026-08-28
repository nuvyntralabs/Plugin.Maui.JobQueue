namespace Plugin.Maui.JobQueue;

/// <summary>
/// Internal persisted row. Mapped to SQLite and the in-memory store.
/// </summary>
public sealed class JobRecord
{
    public string Id { get; set; } = "";

    public string JobType { get; set; } = "";

    public string PayloadJson { get; set; } = "";

    public string Queue { get; set; } = JobQueueDefaults.DefaultQueue;

    public JobPriority Priority { get; set; } = JobPriority.Normal;

    public JobStatus Status { get; set; } = JobStatus.Pending;

    public int Attempts { get; set; }

    public int MaxAttempts { get; set; } = JobQueueDefaults.DefaultMaxAttempts;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset NextAttemptAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public string? LastError { get; set; }

    public string? IdempotencyKey { get; set; }

    public string? CorrelationId { get; set; }

    public bool RequiresNetwork { get; set; }

    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.Ordinal);

    public string? LeaseOwner { get; set; }

    public DateTimeOffset? LeaseExpiresAt { get; set; }

    public JobInfo ToInfo() => new()
    {
        Id = Id,
        JobType = JobType,
        Queue = Queue,
        Status = Status,
        Priority = Priority,
        Attempts = Attempts,
        MaxAttempts = MaxAttempts,
        CreatedAt = CreatedAt,
        NextAttemptAt = NextAttemptAt,
        StartedAt = StartedAt,
        CompletedAt = CompletedAt,
        LastError = LastError,
        IdempotencyKey = IdempotencyKey,
        CorrelationId = CorrelationId,
        RequiresNetwork = RequiresNetwork,
        Metadata = new Dictionary<string, string>(Metadata, StringComparer.Ordinal)
    };

    public JobRecord Clone() => new()
    {
        Id = Id,
        JobType = JobType,
        PayloadJson = PayloadJson,
        Queue = Queue,
        Priority = Priority,
        Status = Status,
        Attempts = Attempts,
        MaxAttempts = MaxAttempts,
        CreatedAt = CreatedAt,
        NextAttemptAt = NextAttemptAt,
        StartedAt = StartedAt,
        CompletedAt = CompletedAt,
        LastError = LastError,
        IdempotencyKey = IdempotencyKey,
        CorrelationId = CorrelationId,
        RequiresNetwork = RequiresNetwork,
        Metadata = new Dictionary<string, string>(Metadata, StringComparer.Ordinal),
        LeaseOwner = LeaseOwner,
        LeaseExpiresAt = LeaseExpiresAt
    };
}

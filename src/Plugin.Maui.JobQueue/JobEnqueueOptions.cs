namespace Plugin.Maui.JobQueue;

/// <summary>
/// Per-enqueue overrides. Unset values fall back to <see cref="JobAttribute"/> then <see cref="JobQueueOptions"/>.
/// </summary>
public sealed class JobEnqueueOptions
{
    /// <summary>Named queue. Defaults to <see cref="JobQueueDefaults.DefaultQueue"/>.</summary>
    public string? Queue { get; set; }

    /// <summary>Claim order among due jobs.</summary>
    public JobPriority Priority { get; set; } = JobPriority.Normal;

    /// <summary>Wait this long from now before the first attempt.</summary>
    public TimeSpan? Delay { get; set; }

    /// <summary>Run no earlier than this UTC instant. Wins over <see cref="Delay"/> when both are set.</summary>
    public DateTimeOffset? ScheduleAt { get; set; }

    /// <summary>Maximum attempts before dead-letter.</summary>
    public int? MaxAttempts { get; set; }

    /// <summary>
    /// When set, a second enqueue with the same key returns the existing job id
    /// if that job is still pending, running, or waiting to retry.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>Optional correlation id for logs and UI.</summary>
    public string? CorrelationId { get; set; }

    /// <summary>Skip this job while the device is offline.</summary>
    public bool? RequiresNetwork { get; set; }

    /// <summary>Opaque string pairs persisted with the job.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; set; }
}

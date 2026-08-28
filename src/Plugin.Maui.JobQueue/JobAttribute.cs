namespace Plugin.Maui.JobQueue;

/// <summary>
/// Optional metadata applied when a job is enqueued without overriding <see cref="JobEnqueueOptions"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class JobAttribute : Attribute
{
    /// <summary>
    /// Stable type name stored in SQLite. Defaults to the CLR type name.
    /// </summary>
    public string? Name { get; }

    /// <summary>
    /// Named queue, for example <c>uploads</c> or <c>analytics</c>.
    /// </summary>
    public string Queue { get; init; } = JobQueueDefaults.DefaultQueue;

    /// <summary>
    /// Maximum execution attempts before the job is dead-lettered.
    /// </summary>
    public int MaxAttempts { get; init; }

    /// <summary>
    /// When true, the worker skips the job while the device is offline.
    /// </summary>
    public bool RequiresNetwork { get; init; }

    /// <summary>
    /// Creates an attribute that uses the CLR type name.
    /// </summary>
    public JobAttribute()
    {
    }

    /// <summary>
    /// Creates an attribute with a stable persisted type name.
    /// </summary>
    public JobAttribute(string name)
    {
        Name = name;
    }
}

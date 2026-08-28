namespace Plugin.Maui.JobQueue;

/// <summary>
/// Filter for <see cref="IJobQueue.ListAsync"/>.
/// </summary>
public sealed class JobListQuery
{
    /// <summary>Restrict to one status.</summary>
    public JobStatus? Status { get; set; }

    /// <summary>Restrict to one named queue.</summary>
    public string? Queue { get; set; }

    /// <summary>Restrict to one registered job type name.</summary>
    public string? JobType { get; set; }

    /// <summary>Maximum rows to return. Defaults to 100.</summary>
    public int Take { get; set; } = 100;
}

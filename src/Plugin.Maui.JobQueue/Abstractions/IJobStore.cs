namespace Plugin.Maui.JobQueue;

/// <summary>
/// Persistence for job rows. SQLite by default; in-memory for tests.
/// </summary>
public interface IJobStore : IAsyncDisposable
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<JobRecord> InsertAsync(JobRecord record, CancellationToken cancellationToken = default);

    Task<JobRecord?> FindByIdAsync(string id, CancellationToken cancellationToken = default);

    Task<JobRecord?> FindByIdempotencyKeyAsync(string key, CancellationToken cancellationToken = default);

    Task<JobRecord?> ClaimNextAsync(ClaimRequest request, CancellationToken cancellationToken = default);

    Task UpdateAsync(JobRecord record, CancellationToken cancellationToken = default);

    Task DeleteAsync(string id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<JobRecord>> ListAsync(JobListQuery query, CancellationToken cancellationToken = default);

    Task<JobQueueCounts> CountAsync(DateTimeOffset utcNow, CancellationToken cancellationToken = default);

    Task<int> RecoverExpiredLeasesAsync(DateTimeOffset utcNow, CancellationToken cancellationToken = default);

    Task<int> RequeueDeadLettersAsync(string? jobId, DateTimeOffset utcNow, CancellationToken cancellationToken = default);
}

/// <summary>
/// Parameters for claiming the next due job.
/// </summary>
public readonly record struct ClaimRequest(
    DateTimeOffset UtcNow,
    bool IsOnline,
    string LeaseOwner,
    DateTimeOffset LeaseExpiresAt);

/// <summary>
/// Status counts used by <see cref="JobQueueSnapshot"/>.
/// </summary>
public readonly record struct JobQueueCounts(
    int Pending,
    int Scheduled,
    int Running,
    int Failed,
    int DeadLetter,
    int Succeeded,
    int Cancelled);

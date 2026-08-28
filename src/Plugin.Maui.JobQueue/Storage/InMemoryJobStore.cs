namespace Plugin.Maui.JobQueue.Storage;

/// <summary>
/// Process-local store for tests and demos. Does not survive process death.
/// </summary>
public sealed class InMemoryJobStore : IJobStore
{
    readonly object _gate = new();
    readonly Dictionary<string, JobRecord> _jobs = new(StringComparer.Ordinal);
    readonly Dictionary<string, string> _idempotency = new(StringComparer.Ordinal);

    public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task<JobRecord> InsertAsync(JobRecord record, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(record.IdempotencyKey) &&
                _idempotency.TryGetValue(record.IdempotencyKey, out var existingId) &&
                _jobs.TryGetValue(existingId, out var existing) &&
                IsActive(existing.Status))
            {
                return Task.FromResult(existing.Clone());
            }

            var copy = record.Clone();
            _jobs[copy.Id] = copy;
            if (!string.IsNullOrWhiteSpace(copy.IdempotencyKey))
            {
                _idempotency[copy.IdempotencyKey] = copy.Id;
            }

            return Task.FromResult(copy.Clone());
        }
    }

    public Task<JobRecord?> FindByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_jobs.TryGetValue(id, out var record) ? record.Clone() : null);
        }
    }

    public Task<JobRecord?> FindByIdempotencyKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_idempotency.TryGetValue(key, out var id) && _jobs.TryGetValue(id, out var record))
            {
                return Task.FromResult<JobRecord?>(record.Clone());
            }

            return Task.FromResult<JobRecord?>(null);
        }
    }

    public Task<JobRecord?> ClaimNextAsync(ClaimRequest request, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var next = _jobs.Values
                .Where(job => IsClaimable(job, request))
                .OrderByDescending(job => (int)job.Priority)
                .ThenBy(job => job.CreatedAt)
                .FirstOrDefault();

            if (next is null)
            {
                return Task.FromResult<JobRecord?>(null);
            }

            next.Status = JobStatus.Running;
            next.Attempts += 1;
            next.StartedAt = request.UtcNow;
            next.LeaseOwner = request.LeaseOwner;
            next.LeaseExpiresAt = request.LeaseExpiresAt;
            return Task.FromResult<JobRecord?>(next.Clone());
        }
    }

    public Task UpdateAsync(JobRecord record, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            _jobs[record.Id] = record.Clone();
            if (!string.IsNullOrWhiteSpace(record.IdempotencyKey))
            {
                _idempotency[record.IdempotencyKey] = record.Id;
            }

            return Task.CompletedTask;
        }
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_jobs.Remove(id, out var removed) && !string.IsNullOrWhiteSpace(removed.IdempotencyKey))
            {
                _idempotency.Remove(removed.IdempotencyKey);
            }

            return Task.CompletedTask;
        }
    }

    public Task<IReadOnlyList<JobRecord>> ListAsync(JobListQuery query, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            IEnumerable<JobRecord> items = _jobs.Values;
            if (query.Status is { } status)
            {
                items = items.Where(job => job.Status == status);
            }

            if (!string.IsNullOrWhiteSpace(query.Queue))
            {
                items = items.Where(job => string.Equals(job.Queue, query.Queue, StringComparison.Ordinal));
            }

            if (!string.IsNullOrWhiteSpace(query.JobType))
            {
                items = items.Where(job => string.Equals(job.JobType, query.JobType, StringComparison.Ordinal));
            }

            var take = query.Take > 0 ? query.Take : 100;
            var list = items
                .OrderByDescending(job => job.CreatedAt)
                .Take(take)
                .Select(job => job.Clone())
                .ToList();

            return Task.FromResult<IReadOnlyList<JobRecord>>(list);
        }
    }

    public Task<JobQueueCounts> CountAsync(DateTimeOffset utcNow, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var pending = 0;
            var scheduled = 0;
            var running = 0;
            var failed = 0;
            var dead = 0;
            var succeeded = 0;
            var cancelled = 0;

            foreach (var job in _jobs.Values)
            {
                switch (job.Status)
                {
                    case JobStatus.Pending:
                        if (job.NextAttemptAt > utcNow)
                        {
                            scheduled++;
                        }
                        else
                        {
                            pending++;
                        }

                        break;
                    case JobStatus.Running:
                        running++;
                        break;
                    case JobStatus.Failed:
                        failed++;
                        break;
                    case JobStatus.DeadLetter:
                        dead++;
                        break;
                    case JobStatus.Succeeded:
                        succeeded++;
                        break;
                    case JobStatus.Cancelled:
                        cancelled++;
                        break;
                }
            }

            return Task.FromResult(new JobQueueCounts(pending, scheduled, running, failed, dead, succeeded, cancelled));
        }
    }

    public Task<int> RecoverExpiredLeasesAsync(DateTimeOffset utcNow, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var recovered = 0;
            foreach (var job in _jobs.Values)
            {
                if (job.Status != JobStatus.Running)
                {
                    continue;
                }

                if (job.LeaseExpiresAt is { } expires && expires > utcNow)
                {
                    continue;
                }

                job.Status = JobStatus.Pending;
                job.LeaseOwner = null;
                job.LeaseExpiresAt = null;
                job.NextAttemptAt = utcNow;
                job.LastError = "Recovered after expired lease / process death.";
                recovered++;
            }

            return Task.FromResult(recovered);
        }
    }

    public Task<int> RequeueDeadLettersAsync(string? jobId, DateTimeOffset utcNow, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var count = 0;
            foreach (var job in _jobs.Values)
            {
                if (job.Status != JobStatus.DeadLetter)
                {
                    continue;
                }

                if (jobId is not null && !string.Equals(job.Id, jobId, StringComparison.Ordinal))
                {
                    continue;
                }

                job.Status = JobStatus.Pending;
                job.Attempts = 0;
                job.LastError = null;
                job.NextAttemptAt = utcNow;
                job.LeaseOwner = null;
                job.LeaseExpiresAt = null;
                count++;
            }

            return Task.FromResult(count);
        }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    static bool IsActive(JobStatus status) =>
        status is JobStatus.Pending or JobStatus.Running or JobStatus.Failed;

    static bool IsClaimable(JobRecord job, ClaimRequest request)
    {
        if (job.Status is not (JobStatus.Pending or JobStatus.Failed))
        {
            return false;
        }

        if (job.NextAttemptAt > request.UtcNow)
        {
            return false;
        }

        if (job.RequiresNetwork && !request.IsOnline)
        {
            return false;
        }

        return true;
    }
}

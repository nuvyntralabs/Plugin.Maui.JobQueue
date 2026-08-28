using SQLite;

namespace Plugin.Maui.JobQueue.Storage;

/// <summary>
/// SQLite-backed durable store. Jobs survive process death.
/// </summary>
public sealed class SqliteJobStore : IJobStore
{
    readonly string _path;
    readonly SemaphoreSlim _gate = new(1, 1);
    SQLiteAsyncConnection? _connection;

    public SqliteJobStore(JobQueueOptions options)
        : this(StoragePath.Resolve(options))
    {
    }

    public SqliteJobStore(string databasePath)
    {
        _path = databasePath;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_connection is not null)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_connection is not null)
            {
                return;
            }

            SQLitePCL.Batteries_V2.Init();
            var directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            _connection = new SQLiteAsyncConnection(
                _path,
                SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex);
            await _connection.CreateTableAsync<JobRow>().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<JobRecord> InsertAsync(JobRecord record, CancellationToken cancellationToken = default)
    {
        var db = await GetDbAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!string.IsNullOrWhiteSpace(record.IdempotencyKey))
            {
                var existing = await db.Table<JobRow>()
                    .Where(row => row.IdempotencyKey == record.IdempotencyKey)
                    .FirstOrDefaultAsync()
                    .ConfigureAwait(false);
                if (existing is not null && IsActive((JobStatus)existing.Status))
                {
                    return existing.ToRecord();
                }
            }

            await db.InsertAsync(JobRow.FromRecord(record)).ConfigureAwait(false);
            return record.Clone();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<JobRecord?> FindByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        var db = await GetDbAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.FindAsync<JobRow>(id).ConfigureAwait(false);
        return row?.ToRecord();
    }

    public async Task<JobRecord?> FindByIdempotencyKeyAsync(string key, CancellationToken cancellationToken = default)
    {
        var db = await GetDbAsync(cancellationToken).ConfigureAwait(false);
        var row = await db.Table<JobRow>()
            .Where(item => item.IdempotencyKey == key)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);
        return row?.ToRecord();
    }

    public async Task<JobRecord?> ClaimNextAsync(ClaimRequest request, CancellationToken cancellationToken = default)
    {
        var db = await GetDbAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            JobRecord? claimed = null;
            await db.RunInTransactionAsync(conn =>
            {
                var now = request.UtcNow.ToString("O");
                var online = request.IsOnline ? 1 : 0;
                var pending = (int)JobStatus.Pending;
                var failed = (int)JobStatus.Failed;
                var rows = conn.Query<JobRow>(
                    """
                    SELECT * FROM Jobs
                    WHERE Status IN (?, ?)
                      AND NextAttemptAtUtc <= ?
                      AND (RequiresNetwork = 0 OR ? = 1)
                    ORDER BY Priority DESC, CreatedAtUtc ASC
                    LIMIT 1
                    """,
                    pending,
                    failed,
                    now,
                    online);

                var row = rows.FirstOrDefault();
                if (row is null)
                {
                    return;
                }

                row.Status = (int)JobStatus.Running;
                row.Attempts += 1;
                row.StartedAtUtc = request.UtcNow.ToString("O");
                row.LeaseOwner = request.LeaseOwner;
                row.LeaseExpiresAtUtc = request.LeaseExpiresAt.ToString("O");
                conn.Update(row);
                claimed = row.ToRecord();
            }).ConfigureAwait(false);

            return claimed;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task UpdateAsync(JobRecord record, CancellationToken cancellationToken = default)
    {
        var db = await GetDbAsync(cancellationToken).ConfigureAwait(false);
        await db.InsertOrReplaceAsync(JobRow.FromRecord(record)).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        var db = await GetDbAsync(cancellationToken).ConfigureAwait(false);
        await db.DeleteAsync<JobRow>(id).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<JobRecord>> ListAsync(JobListQuery query, CancellationToken cancellationToken = default)
    {
        var db = await GetDbAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.Table<JobRow>().ToListAsync().ConfigureAwait(false);
        IEnumerable<JobRow> items = rows;
        if (query.Status is { } status)
        {
            var value = (int)status;
            items = items.Where(row => row.Status == value);
        }

        if (!string.IsNullOrWhiteSpace(query.Queue))
        {
            items = items.Where(row => string.Equals(row.Queue, query.Queue, StringComparison.Ordinal));
        }

        if (!string.IsNullOrWhiteSpace(query.JobType))
        {
            items = items.Where(row => string.Equals(row.JobType, query.JobType, StringComparison.Ordinal));
        }

        var take = query.Take > 0 ? query.Take : 100;
        return items
            .OrderByDescending(row => row.CreatedAtUtc)
            .Take(take)
            .Select(row => row.ToRecord())
            .ToList();
    }

    public async Task<JobQueueCounts> CountAsync(DateTimeOffset utcNow, CancellationToken cancellationToken = default)
    {
        var db = await GetDbAsync(cancellationToken).ConfigureAwait(false);
        var rows = await db.Table<JobRow>().ToListAsync().ConfigureAwait(false);
        var now = utcNow.ToString("O");
        var pending = 0;
        var scheduled = 0;
        var running = 0;
        var failed = 0;
        var dead = 0;
        var succeeded = 0;
        var cancelled = 0;

        foreach (var row in rows)
        {
            switch ((JobStatus)row.Status)
            {
                case JobStatus.Pending:
                    if (string.CompareOrdinal(row.NextAttemptAtUtc, now) > 0)
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

        return new JobQueueCounts(pending, scheduled, running, failed, dead, succeeded, cancelled);
    }

    public async Task<int> RecoverExpiredLeasesAsync(DateTimeOffset utcNow, CancellationToken cancellationToken = default)
    {
        var db = await GetDbAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var running = (int)JobStatus.Running;
            var rows = await db.Table<JobRow>()
                .Where(row => row.Status == running)
                .ToListAsync()
                .ConfigureAwait(false);

            var now = utcNow.ToString("O");
            var recovered = 0;
            foreach (var row in rows)
            {
                if (!string.IsNullOrWhiteSpace(row.LeaseExpiresAtUtc) &&
                    string.CompareOrdinal(row.LeaseExpiresAtUtc, now) > 0)
                {
                    continue;
                }

                row.Status = (int)JobStatus.Pending;
                row.LeaseOwner = null;
                row.LeaseExpiresAtUtc = null;
                row.NextAttemptAtUtc = now;
                row.LastError = "Recovered after expired lease / process death.";
                await db.UpdateAsync(row).ConfigureAwait(false);
                recovered++;
            }

            return recovered;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<int> RequeueDeadLettersAsync(string? jobId, DateTimeOffset utcNow, CancellationToken cancellationToken = default)
    {
        var db = await GetDbAsync(cancellationToken).ConfigureAwait(false);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var dead = (int)JobStatus.DeadLetter;
            var rows = await db.Table<JobRow>()
                .Where(row => row.Status == dead)
                .ToListAsync()
                .ConfigureAwait(false);

            var count = 0;
            foreach (var row in rows)
            {
                if (jobId is not null && !string.Equals(row.Id, jobId, StringComparison.Ordinal))
                {
                    continue;
                }

                row.Status = (int)JobStatus.Pending;
                row.Attempts = 0;
                row.LastError = null;
                row.NextAttemptAtUtc = utcNow.ToString("O");
                row.LeaseOwner = null;
                row.LeaseExpiresAtUtc = null;
                await db.UpdateAsync(row).ConfigureAwait(false);
                count++;
            }

            return count;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.CloseAsync().ConfigureAwait(false);
            _connection = null;
        }

        _gate.Dispose();
    }

    async Task<SQLiteAsyncConnection> GetDbAsync(CancellationToken cancellationToken)
    {
        await InitializeAsync(cancellationToken).ConfigureAwait(false);
        return _connection!;
    }

    static bool IsActive(JobStatus status) =>
        status is JobStatus.Pending or JobStatus.Running or JobStatus.Failed;

    [Table("Jobs")]
    sealed class JobRow
    {
        [PrimaryKey]
        public string Id { get; set; } = "";

        [Indexed]
        public string JobType { get; set; } = "";

        public string PayloadJson { get; set; } = "";

        [Indexed]
        public string Queue { get; set; } = JobQueueDefaults.DefaultQueue;

        public int Priority { get; set; }

        [Indexed]
        public int Status { get; set; }

        public int Attempts { get; set; }

        public int MaxAttempts { get; set; }

        public string CreatedAtUtc { get; set; } = "";

        [Indexed]
        public string NextAttemptAtUtc { get; set; } = "";

        public string? StartedAtUtc { get; set; }

        public string? CompletedAtUtc { get; set; }

        public string? LastError { get; set; }

        [Indexed]
        public string? IdempotencyKey { get; set; }

        public string? CorrelationId { get; set; }

        public int RequiresNetwork { get; set; }

        public string? MetadataJson { get; set; }

        public string? LeaseOwner { get; set; }

        public string? LeaseExpiresAtUtc { get; set; }

        public static JobRow FromRecord(JobRecord record) => new()
        {
            Id = record.Id,
            JobType = record.JobType,
            PayloadJson = record.PayloadJson,
            Queue = record.Queue,
            Priority = (int)record.Priority,
            Status = (int)record.Status,
            Attempts = record.Attempts,
            MaxAttempts = record.MaxAttempts,
            CreatedAtUtc = record.CreatedAt.ToString("O"),
            NextAttemptAtUtc = record.NextAttemptAt.ToString("O"),
            StartedAtUtc = record.StartedAt?.ToString("O"),
            CompletedAtUtc = record.CompletedAt?.ToString("O"),
            LastError = record.LastError,
            IdempotencyKey = string.IsNullOrWhiteSpace(record.IdempotencyKey) ? null : record.IdempotencyKey,
            CorrelationId = record.CorrelationId,
            RequiresNetwork = record.RequiresNetwork ? 1 : 0,
            MetadataJson = JobJson.WriteMetadata(record.Metadata),
            LeaseOwner = record.LeaseOwner,
            LeaseExpiresAtUtc = record.LeaseExpiresAt?.ToString("O")
        };

        public JobRecord ToRecord() => new()
        {
            Id = Id,
            JobType = JobType,
            PayloadJson = PayloadJson,
            Queue = Queue,
            Priority = (JobPriority)Priority,
            Status = (JobStatus)Status,
            Attempts = Attempts,
            MaxAttempts = MaxAttempts,
            CreatedAt = Parse(CreatedAtUtc),
            NextAttemptAt = Parse(NextAttemptAtUtc),
            StartedAt = ParseNullable(StartedAtUtc),
            CompletedAt = ParseNullable(CompletedAtUtc),
            LastError = LastError,
            IdempotencyKey = IdempotencyKey,
            CorrelationId = CorrelationId,
            RequiresNetwork = RequiresNetwork != 0,
            Metadata = JobJson.ReadMetadata(MetadataJson),
            LeaseOwner = LeaseOwner,
            LeaseExpiresAt = ParseNullable(LeaseExpiresAtUtc)
        };

        static DateTimeOffset Parse(string value) =>
            DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

        static DateTimeOffset? ParseNullable(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : Parse(value);
    }
}

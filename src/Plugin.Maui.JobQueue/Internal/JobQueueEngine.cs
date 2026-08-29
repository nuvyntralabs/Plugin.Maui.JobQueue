namespace Plugin.Maui.JobQueue;

sealed class JobQueueEngine : IJobQueue, IAsyncDisposable
{
    readonly JobQueueOptions _options;
    readonly IJobStore _store;
    readonly IClock _clock;
    readonly INetworkGate _network;
    readonly JobTypeRegistry _registry;
    readonly IServiceProvider _services;
    readonly Random _random;
    readonly SemaphoreSlim _signal = new(0, 1);
    readonly object _runGate = new();

    CancellationTokenSource? _workerCts;
    Task[] _workers = [];
    int _started;

    public JobQueueEngine(
        JobQueueOptions options,
        IJobStore store,
        IClock clock,
        INetworkGate network,
        JobTypeRegistry registry,
        IServiceProvider services)
    {
        _options = options;
        _store = store;
        _clock = clock;
        _network = network;
        _registry = registry;
        _services = services;
        _random = options.Random ?? Random.Shared;
        _network.ConnectivityChanged += OnConnectivityChanged;
    }

    public bool IsRunning => Volatile.Read(ref _started) == 1;

    public event EventHandler<JobEventArgs>? JobQueued;

    public event EventHandler<JobEventArgs>? JobStarted;

    public event EventHandler<JobCompletedEventArgs>? JobCompleted;

    public event EventHandler<JobFailedEventArgs>? JobFailed;

    public event EventHandler<JobEventArgs>? JobDeadLettered;

    public event EventHandler<JobEventArgs>? JobCancelled;

    public async Task<string> EnqueueAsync<TJob>(TJob job, JobEnqueueOptions? options = null, CancellationToken cancellationToken = default)
        where TJob : class, IJob
    {
        ArgumentNullException.ThrowIfNull(job);
        await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);

        var jobType = typeof(TJob);
        var attribute = JobTypeNames.Attribute(jobType);
        var typeName = JobTypeNames.For(jobType);
        if (!_registry.TryGet(typeName, out _))
        {
            throw new JobQueueException(
                $"Job type '{typeName}' is not registered. Call services.AddJobHandler<{jobType.Name}, YourHandler>().");
        }

        options ??= new JobEnqueueOptions();
        if (!string.IsNullOrWhiteSpace(options.IdempotencyKey))
        {
            var existing = await _store.FindByIdempotencyKeyAsync(options.IdempotencyKey, cancellationToken).ConfigureAwait(false);
            if (existing is not null && IsActive(existing.Status))
            {
                return existing.Id;
            }
        }

        var now = _clock.UtcNow;
        var nextAttempt = ResolveSchedule(now, options);
        var record = new JobRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            JobType = typeName,
            PayloadJson = JobJson.Serialize(job),
            Queue = FirstNonEmpty(options.Queue, attribute?.Queue, JobQueueDefaults.DefaultQueue),
            Priority = options.Priority,
            Status = JobStatus.Pending,
            Attempts = 0,
            MaxAttempts = ResolveMaxAttempts(options, attribute),
            CreatedAt = now,
            NextAttemptAt = nextAttempt,
            IdempotencyKey = string.IsNullOrWhiteSpace(options.IdempotencyKey) ? null : options.IdempotencyKey,
            CorrelationId = options.CorrelationId,
            RequiresNetwork = options.RequiresNetwork ?? attribute?.RequiresNetwork ?? false,
            Metadata = options.Metadata is null
                ? new Dictionary<string, string>(StringComparer.Ordinal)
                : new Dictionary<string, string>(options.Metadata, StringComparer.Ordinal)
        };

        var stored = await _store.InsertAsync(record, cancellationToken).ConfigureAwait(false);
        Raise(JobQueued, new JobEventArgs(stored.ToInfo()));
        Pulse();
        return stored.Id;
    }

    public async Task<bool> CancelAsync(string jobId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);

        var record = await _store.FindByIdAsync(jobId, cancellationToken).ConfigureAwait(false);
        if (record is null || record.Status is JobStatus.Succeeded or JobStatus.DeadLetter or JobStatus.Cancelled)
        {
            return false;
        }

        record.Status = JobStatus.Cancelled;
        record.CompletedAt = _clock.UtcNow;
        record.LeaseOwner = null;
        record.LeaseExpiresAt = null;
        await _store.UpdateAsync(record, cancellationToken).ConfigureAwait(false);
        Raise(JobCancelled, new JobEventArgs(record.ToInfo()));
        return true;
    }

    public async Task<JobInfo?> GetAsync(string jobId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var record = await _store.FindByIdAsync(jobId, cancellationToken).ConfigureAwait(false);
        return record?.ToInfo();
    }

    public async Task<IReadOnlyList<JobInfo>> ListAsync(JobListQuery? query = null, CancellationToken cancellationToken = default)
    {
        await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var records = await _store.ListAsync(query ?? new JobListQuery(), cancellationToken).ConfigureAwait(false);
        return records.Select(record => record.ToInfo()).ToList();
    }

    public Task<IReadOnlyList<JobInfo>> GetDeadLettersAsync(CancellationToken cancellationToken = default) =>
        ListAsync(new JobListQuery { Status = JobStatus.DeadLetter, Take = 500 }, cancellationToken);

    public async Task<bool> RequeueDeadLetterAsync(string jobId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var count = await _store.RequeueDeadLettersAsync(jobId, _clock.UtcNow, cancellationToken).ConfigureAwait(false);
        if (count > 0)
        {
            Pulse();
        }

        return count > 0;
    }

    public async Task<int> RequeueDeadLettersAsync(CancellationToken cancellationToken = default)
    {
        await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var count = await _store.RequeueDeadLettersAsync(null, _clock.UtcNow, cancellationToken).ConfigureAwait(false);
        if (count > 0)
        {
            Pulse();
        }

        return count;
    }

    public async Task<bool> DiscardDeadLetterAsync(string jobId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var record = await _store.FindByIdAsync(jobId, cancellationToken).ConfigureAwait(false);
        if (record is null || record.Status != JobStatus.DeadLetter)
        {
            return false;
        }

        await _store.DeleteAsync(jobId, cancellationToken).ConfigureAwait(false);
        return true;
    }

    public async Task<JobQueueSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
        var counts = await _store.CountAsync(_clock.UtcNow, cancellationToken).ConfigureAwait(false);
        return new JobQueueSnapshot
        {
            Pending = counts.Pending,
            Scheduled = counts.Scheduled,
            Running = counts.Running,
            Failed = counts.Failed,
            DeadLetter = counts.DeadLetter,
            Succeeded = counts.Succeeded,
            Cancelled = counts.Cancelled,
            IsWorkerRunning = IsRunning,
            IsOnline = _network.IsOnline
        };
    }

    public async Task<int> DrainAsync(int? maxJobs = null, CancellationToken cancellationToken = default)
    {
        await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _store.RecoverExpiredLeasesAsync(_clock.UtcNow, cancellationToken).ConfigureAwait(false);

        var processed = 0;
        var limit = maxJobs ?? int.MaxValue;
        while (processed < limit)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var request = new ClaimRequest(
                _clock.UtcNow,
                _network.IsOnline,
                Environment.ProcessId.ToString(CultureInfo.InvariantCulture),
                _clock.UtcNow + _options.LeaseDuration);

            var claimed = await _store.ClaimNextAsync(request, cancellationToken).ConfigureAwait(false);
            if (claimed is null)
            {
                break;
            }

            await ProcessClaimedAsync(claimed, cancellationToken).ConfigureAwait(false);
            processed++;
        }

        return processed;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _store.InitializeAsync(cancellationToken).ConfigureAwait(false);
        await _store.RecoverExpiredLeasesAsync(_clock.UtcNow, cancellationToken).ConfigureAwait(false);

        lock (_runGate)
        {
            if (IsRunning)
            {
                return;
            }

            // Do not link workers to the StartAsync token — hosts cancel that
            // token when startup finishes, which would silently kill the queue.
            _workerCts = new CancellationTokenSource();
            var token = _workerCts.Token;
            var count = Math.Max(1, _options.WorkerCount);
            _workers = Enumerable.Range(0, count)
                .Select(_ => Task.Run(() => WorkerLoopAsync(token), token))
                .ToArray();
            Volatile.Write(ref _started, 1);
        }

        Pulse();
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Task[] workers;
        lock (_runGate)
        {
            if (!IsRunning)
            {
                return;
            }

            _workerCts?.Cancel();
            workers = _workers;
            _workers = [];
            Volatile.Write(ref _started, 0);
        }

        try
        {
            await Task.WhenAll(workers).WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TaskCanceledException or AggregateException)
        {
            // Workers honor cancellation.
        }
        finally
        {
            _workerCts?.Dispose();
            _workerCts = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _network.ConnectivityChanged -= OnConnectivityChanged;
        await StopAsync().ConfigureAwait(false);
        await _store.DisposeAsync().ConfigureAwait(false);
        _signal.Dispose();
    }

    async Task WorkerLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await DrainAsync(maxJobs: 32, cancellationToken).ConfigureAwait(false);
                var wait = _signal.WaitAsync(_options.PollInterval, cancellationToken);
                await wait.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"JobQueue worker error: {ex}");
                try
                {
                    await Task.Delay(_options.PollInterval, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    async Task ProcessClaimedAsync(JobRecord record, CancellationToken cancellationToken)
    {
        var latest = await _store.FindByIdAsync(record.Id, cancellationToken).ConfigureAwait(false);
        if (latest is { Status: JobStatus.Cancelled })
        {
            return;
        }

        Raise(JobStarted, new JobEventArgs(record.ToInfo()));
        var started = _clock.UtcNow;
        var context = new JobContext(
            record.Id,
            record.JobType,
            record.Queue,
            record.Attempts,
            record.MaxAttempts,
            record.CorrelationId,
            record.Metadata);

        try
        {
            if (!_registry.TryGet(record.JobType, out var dispatcher))
            {
                throw new JobAbortException($"No handler registered for '{record.JobType}'.");
            }

            await dispatcher.ExecuteAsync(record.PayloadJson, context, _services, cancellationToken).ConfigureAwait(false);
            var after = await _store.FindByIdAsync(record.Id, CancellationToken.None).ConfigureAwait(false);
            if (after is null || after.Status == JobStatus.Cancelled)
            {
                return;
            }

            await CompleteSuccessAsync(record, started).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            record.Status = JobStatus.Pending;
            record.Attempts = Math.Max(0, record.Attempts - 1);
            record.LeaseOwner = null;
            record.LeaseExpiresAt = null;
            record.NextAttemptAt = _clock.UtcNow;
            record.LastError = "Worker stopped during execution.";
            await _store.UpdateAsync(record, CancellationToken.None).ConfigureAwait(false);
        }
        catch (JobAbortException ex)
        {
            await DeadLetterAsync(record, ex, started).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await FailOrDeadLetterAsync(record, ex, started).ConfigureAwait(false);
        }
    }

    async Task CompleteSuccessAsync(JobRecord record, DateTimeOffset started)
    {
        var now = _clock.UtcNow;
        if (_options.DeleteOnSuccess)
        {
            await _store.DeleteAsync(record.Id, CancellationToken.None).ConfigureAwait(false);
            record.Status = JobStatus.Succeeded;
            record.CompletedAt = now;
            record.LastError = null;
        }
        else
        {
            record.Status = JobStatus.Succeeded;
            record.CompletedAt = now;
            record.LastError = null;
            record.LeaseOwner = null;
            record.LeaseExpiresAt = null;
            await _store.UpdateAsync(record, CancellationToken.None).ConfigureAwait(false);
        }

        Raise(JobCompleted, new JobCompletedEventArgs(record.ToInfo(), now - started));
    }

    async Task FailOrDeadLetterAsync(JobRecord record, Exception exception, DateTimeOffset started)
    {
        _ = started;
        if (record.Attempts >= record.MaxAttempts)
        {
            await DeadLetterAsync(record, exception, started).ConfigureAwait(false);
            return;
        }

        var delay = _options.Backoff.Compute(record.Attempts, _random);
        record.Status = JobStatus.Failed;
        record.LastError = exception.Message;
        record.NextAttemptAt = _clock.UtcNow + delay;
        record.LeaseOwner = null;
        record.LeaseExpiresAt = null;
        await _store.UpdateAsync(record, CancellationToken.None).ConfigureAwait(false);
        Raise(JobFailed, new JobFailedEventArgs(record.ToInfo(), exception, willRetry: true));
    }

    async Task DeadLetterAsync(JobRecord record, Exception exception, DateTimeOffset started)
    {
        _ = started;
        record.Status = JobStatus.DeadLetter;
        record.LastError = exception.Message;
        record.CompletedAt = _clock.UtcNow;
        record.LeaseOwner = null;
        record.LeaseExpiresAt = null;
        await _store.UpdateAsync(record, CancellationToken.None).ConfigureAwait(false);
        Raise(JobFailed, new JobFailedEventArgs(record.ToInfo(), exception, willRetry: false));
        Raise(JobDeadLettered, new JobEventArgs(record.ToInfo()));
    }

    void OnConnectivityChanged(object? sender, EventArgs e) => Pulse();

    void Pulse()
    {
        if (_signal.CurrentCount == 0)
        {
            try
            {
                _signal.Release();
            }
            catch (SemaphoreFullException)
            {
            }
        }
    }

    static void Raise<TEvent>(EventHandler<TEvent>? handler, TEvent args) where TEvent : EventArgs
    {
        try
        {
            handler?.Invoke(null, args);
        }
        catch
        {
            // Subscriber failures must not stop the worker.
        }
    }

    DateTimeOffset ResolveSchedule(DateTimeOffset now, JobEnqueueOptions options)
    {
        if (options.ScheduleAt is { } at)
        {
            return at;
        }

        if (options.Delay is { } delay)
        {
            return now + delay;
        }

        return now;
    }

    int ResolveMaxAttempts(JobEnqueueOptions options, JobAttribute? attribute)
    {
        if (options.MaxAttempts is > 0)
        {
            return options.MaxAttempts.Value;
        }

        if (attribute is { MaxAttempts: > 0 })
        {
            return attribute.MaxAttempts;
        }

        return Math.Max(1, _options.DefaultMaxAttempts);
    }

    static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return JobQueueDefaults.DefaultQueue;
    }

    static bool IsActive(JobStatus status) =>
        status is JobStatus.Pending or JobStatus.Running or JobStatus.Failed;
}

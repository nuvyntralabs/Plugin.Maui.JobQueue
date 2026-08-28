namespace Plugin.Maui.JobQueue.Tests;

public sealed class QueueEngineTests
{
    [Fact]
    public async Task Enqueue_then_drain_deletes_on_success()
    {
        var (queue, _, _, store, _) = Harness.Create();

        var id = await queue.EnqueueAsync(new UploadPhotoJob { Path = "/tmp/a.jpg" });
        Assert.False(string.IsNullOrWhiteSpace(id));

        var processed = await queue.DrainAsync();
        Assert.Equal(1, processed);
        Assert.Equal(1, UploadPhotoJobHandler.Executions);
        Assert.Equal("/tmp/a.jpg", UploadPhotoJobHandler.Paths.Single());
        Assert.Null(await store.FindByIdAsync(id));
    }

    [Fact]
    public async Task Enqueue_typed_jobs_in_user_order()
    {
        var (queue, _, _, _, _) = Harness.Create();

        await queue.EnqueueAsync(new UploadPhotoJob { Path = "p" });
        await queue.EnqueueAsync(new SyncCustomerJob { CustomerId = "c1" });
        await queue.EnqueueAsync(new SendAnalyticsJob { EventName = "opened" });

        await queue.DrainAsync();

        Assert.Equal(1, UploadPhotoJobHandler.Executions);
        Assert.Equal(1, SyncCustomerJobHandler.Executions);
        Assert.Equal(1, SendAnalyticsJobHandler.Executions);
    }

    [Fact]
    public async Task Failure_retries_with_backoff_then_succeeds()
    {
        var (queue, clock, _, store, _) = Harness.Create();

        var id = await queue.EnqueueAsync(new FlakyJob { FailUntilAttempt = 3 });
        Assert.Equal(1, await queue.DrainAsync());

        var afterFirst = await store.FindByIdAsync(id);
        Assert.NotNull(afterFirst);
        Assert.Equal(JobStatus.Failed, afterFirst.Status);
        Assert.Equal(1, afterFirst.Attempts);
        Assert.Equal(clock.UtcNow + TimeSpan.FromMinutes(1), afterFirst.NextAttemptAt);

        Assert.Equal(0, await queue.DrainAsync());

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, await queue.DrainAsync());
        var afterSecond = await store.FindByIdAsync(id);
        Assert.Equal(JobStatus.Failed, afterSecond!.Status);
        Assert.Equal(2, afterSecond.Attempts);

        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, await queue.DrainAsync());
        Assert.Null(await store.FindByIdAsync(id));
    }

    [Fact]
    public async Task Exhausted_retries_go_to_dead_letter()
    {
        var (queue, clock, _, _, _) = Harness.Create(options => options.DefaultMaxAttempts = 2);

        var id = await queue.EnqueueAsync(new AlwaysFailJob { Reason = "disk full" });
        await queue.DrainAsync();
        clock.Advance(TimeSpan.FromMinutes(1));
        await queue.DrainAsync();

        var dead = await queue.GetDeadLettersAsync();
        var job = Assert.Single(dead);
        Assert.Equal(id, job.Id);
        Assert.Equal(JobStatus.DeadLetter, job.Status);
        Assert.Equal(2, job.Attempts);
        Assert.Equal("disk full", job.LastError);
    }

    [Fact]
    public async Task Abort_skips_retries()
    {
        var (queue, _, _, _, _) = Harness.Create();

        var id = await queue.EnqueueAsync(new AbortJob());
        await queue.DrainAsync();

        var job = await queue.GetAsync(id);
        Assert.Equal(JobStatus.DeadLetter, job!.Status);
        Assert.Equal(1, job.Attempts);
        Assert.Equal("business rule", job.LastError);
    }

    [Fact]
    public async Task Requeue_dead_letter_runs_again()
    {
        var (queue, _, _, _, _) = Harness.Create(options => options.DefaultMaxAttempts = 1);

        var id = await queue.EnqueueAsync(new AlwaysFailJob());
        await queue.DrainAsync();
        Assert.True(await queue.RequeueDeadLetterAsync(id));

        var pending = await queue.GetAsync(id);
        Assert.Equal(JobStatus.Pending, pending!.Status);
        Assert.Equal(0, pending.Attempts);
    }

    [Fact]
    public async Task Idempotency_key_returns_existing_job()
    {
        var (queue, _, _, _, _) = Harness.Create();
        var options = new JobEnqueueOptions { IdempotencyKey = "photo:42" };

        var first = await queue.EnqueueAsync(new UploadPhotoJob { Path = "a" }, options);
        var second = await queue.EnqueueAsync(new UploadPhotoJob { Path = "b" }, options);

        Assert.Equal(first, second);
        var snapshot = await queue.GetSnapshotAsync();
        Assert.Equal(1, snapshot.Pending);
    }

    [Fact]
    public async Task Cancel_prevents_execution()
    {
        var (queue, _, _, _, _) = Harness.Create();

        var id = await queue.EnqueueAsync(new UploadPhotoJob { Path = "x" });
        Assert.True(await queue.CancelAsync(id));
        await queue.DrainAsync();

        Assert.Equal(0, UploadPhotoJobHandler.Executions);
        Assert.Equal(JobStatus.Cancelled, (await queue.GetAsync(id))!.Status);
    }

    [Fact]
    public async Task Higher_priority_runs_first()
    {
        var (queue, _, _, _, _) = Harness.Create();

        await queue.EnqueueAsync(new UploadPhotoJob { Path = "low" }, new JobEnqueueOptions { Priority = JobPriority.Low });
        await queue.EnqueueAsync(new UploadPhotoJob { Path = "high" }, new JobEnqueueOptions { Priority = JobPriority.High });

        await queue.DrainAsync();
        Assert.Equal(["high", "low"], UploadPhotoJobHandler.Paths);
    }

    [Fact]
    public async Task Delayed_job_waits_for_clock()
    {
        var (queue, clock, _, _, _) = Harness.Create();

        await queue.EnqueueAsync(new UploadPhotoJob { Path = "later" }, new JobEnqueueOptions
        {
            Delay = TimeSpan.FromMinutes(5)
        });

        Assert.Equal(0, await queue.DrainAsync());
        var snapshot = await queue.GetSnapshotAsync();
        Assert.Equal(1, snapshot.Scheduled);

        clock.Advance(TimeSpan.FromMinutes(5));
        Assert.Equal(1, await queue.DrainAsync());
        Assert.Equal(1, UploadPhotoJobHandler.Executions);
    }

    [Fact]
    public async Task Requires_network_skips_when_offline()
    {
        var (queue, _, network, _, _) = Harness.Create();
        network.IsOnline = false;

        await queue.EnqueueAsync(new NetworkJob(), new JobEnqueueOptions { RequiresNetwork = true });
        Assert.Equal(0, await queue.DrainAsync());
        Assert.Equal(0, NetworkJobHandler.Executions);

        network.IsOnline = true;
        Assert.Equal(1, await queue.DrainAsync());
        Assert.Equal(1, NetworkJobHandler.Executions);
    }

    [Fact]
    public async Task Expired_lease_is_recovered_on_drain()
    {
        var clock = new FakeClock();
        var store = new InMemoryJobStore();
        await store.InsertAsync(new JobRecord
        {
            Id = "stuck",
            JobType = "UploadPhotoJob",
            PayloadJson = """{"path":"recovered.jpg"}""",
            Status = JobStatus.Running,
            Attempts = 1,
            MaxAttempts = 5,
            CreatedAt = clock.UtcNow,
            NextAttemptAt = clock.UtcNow,
            LeaseOwner = "dead-process",
            LeaseExpiresAt = clock.UtcNow.AddMinutes(-1)
        });

        var (queue, _, _, _, _) = Harness.Create(options =>
        {
            options.Store = store;
            options.Clock = clock;
        });

        await queue.DrainAsync();
        Assert.Equal(["recovered.jpg"], UploadPhotoJobHandler.Paths);
        Assert.Null(await store.FindByIdAsync("stuck"));
    }

    [Fact]
    public async Task Unregistered_job_cannot_be_enqueued()
    {
        var services = new ServiceCollection();
        services.AddMauiJobQueue(new JobQueueOptions { UseInMemoryStore = true, AutoStart = false });
        var provider = services.BuildServiceProvider();
        var queue = provider.GetRequiredService<IJobQueue>();

        await Assert.ThrowsAsync<JobQueueException>(() => queue.EnqueueAsync(new UploadPhotoJob()));
    }

    [Fact]
    public async Task Retain_succeeded_jobs_when_delete_disabled()
    {
        var (queue, _, _, _, _) = Harness.Create(options => options.DeleteOnSuccess = false);

        var id = await queue.EnqueueAsync(new SendAnalyticsJob { EventName = "tap" });
        await queue.DrainAsync();

        var job = await queue.GetAsync(id);
        Assert.Equal(JobStatus.Succeeded, job!.Status);
    }
}

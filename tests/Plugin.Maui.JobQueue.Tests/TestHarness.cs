namespace Plugin.Maui.JobQueue.Tests;

sealed class FakeClock : IClock
{
    public DateTimeOffset UtcNow { get; set; } = new(2026, 8, 28, 12, 0, 0, TimeSpan.Zero);

    public void Advance(TimeSpan duration) => UtcNow += duration;
}

sealed class UploadPhotoJob : IJob
{
    public string Path { get; set; } = "";
}

sealed class UploadPhotoJobHandler : IJobHandler<UploadPhotoJob>
{
    public static int Executions;
    public static List<string> Paths { get; } = [];

    public Task ExecuteAsync(UploadPhotoJob job, JobContext context, CancellationToken cancellationToken)
    {
        Executions++;
        Paths.Add(job.Path);
        return Task.CompletedTask;
    }

    public static void Reset()
    {
        Executions = 0;
        Paths.Clear();
    }
}

sealed class SyncCustomerJob : IJob
{
    public string CustomerId { get; set; } = "";
}

sealed class SyncCustomerJobHandler : IJobHandler<SyncCustomerJob>
{
    public static int Executions;

    public Task ExecuteAsync(SyncCustomerJob job, JobContext context, CancellationToken cancellationToken)
    {
        Executions++;
        return Task.CompletedTask;
    }

    public static void Reset() => Executions = 0;
}

sealed class SendAnalyticsJob : IJob
{
    public string EventName { get; set; } = "";
}

sealed class SendAnalyticsJobHandler : IJobHandler<SendAnalyticsJob>
{
    public static int Executions;

    public Task ExecuteAsync(SendAnalyticsJob job, JobContext context, CancellationToken cancellationToken)
    {
        Executions++;
        return Task.CompletedTask;
    }

    public static void Reset() => Executions = 0;
}

sealed class FlakyJob : IJob
{
    public int FailUntilAttempt { get; set; }
}

sealed class FlakyJobHandler : IJobHandler<FlakyJob>
{
    public Task ExecuteAsync(FlakyJob job, JobContext context, CancellationToken cancellationToken)
    {
        if (context.Attempt < job.FailUntilAttempt)
        {
            throw new InvalidOperationException($"transient failure on attempt {context.Attempt}");
        }

        return Task.CompletedTask;
    }
}

sealed class AlwaysFailJob : IJob
{
    public string Reason { get; set; } = "nope";
}

sealed class AlwaysFailJobHandler : IJobHandler<AlwaysFailJob>
{
    public Task ExecuteAsync(AlwaysFailJob job, JobContext context, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(job.Reason);
}

sealed class AbortJob : IJob;

sealed class AbortJobHandler : IJobHandler<AbortJob>
{
    public Task ExecuteAsync(AbortJob job, JobContext context, CancellationToken cancellationToken)
    {
        context.Abort("business rule");
        return Task.CompletedTask;
    }
}

sealed class NetworkJob : IJob;

sealed class NetworkJobHandler : IJobHandler<NetworkJob>
{
    public static int Executions;

    public Task ExecuteAsync(NetworkJob job, JobContext context, CancellationToken cancellationToken)
    {
        Executions++;
        return Task.CompletedTask;
    }

    public static void Reset() => Executions = 0;
}

static class Harness
{
    public static (IJobQueue Queue, FakeClock Clock, ManualNetworkGate Network, InMemoryJobStore Store, IServiceProvider Services)
        Create(Action<JobQueueOptions>? configure = null)
    {
        UploadPhotoJobHandler.Reset();
        SyncCustomerJobHandler.Reset();
        SendAnalyticsJobHandler.Reset();
        NetworkJobHandler.Reset();

        var clock = new FakeClock();
        var network = new ManualNetworkGate { IsOnline = true };
        var store = new InMemoryJobStore();
        var options = new JobQueueOptions
        {
            UseInMemoryStore = true,
            Store = store,
            Clock = clock,
            NetworkGate = network,
            AutoStart = false,
            DeleteOnSuccess = true,
            DefaultMaxAttempts = 5,
            Backoff = BackoffPolicy.Constant(TimeSpan.FromMinutes(1)),
            Random = new Random(1)
        };
        configure?.Invoke(options);

        var services = new ServiceCollection();
        services.AddMauiJobQueue(options);
        services.AddJobHandler<UploadPhotoJob, UploadPhotoJobHandler>();
        services.AddJobHandler<SyncCustomerJob, SyncCustomerJobHandler>();
        services.AddJobHandler<SendAnalyticsJob, SendAnalyticsJobHandler>();
        services.AddJobHandler<FlakyJob, FlakyJobHandler>();
        services.AddJobHandler<AlwaysFailJob, AlwaysFailJobHandler>();
        services.AddJobHandler<AbortJob, AbortJobHandler>();
        services.AddJobHandler<NetworkJob, NetworkJobHandler>();
        var provider = services.BuildServiceProvider();
        var queue = provider.GetRequiredService<IJobQueue>();
        return (queue, clock, network, store, provider);
    }
}

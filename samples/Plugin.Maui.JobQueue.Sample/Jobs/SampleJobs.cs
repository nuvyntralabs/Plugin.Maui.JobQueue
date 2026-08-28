namespace Plugin.Maui.JobQueue.Sample.Jobs;

[Job("UploadPhoto", Queue = "uploads", MaxAttempts = 5, RequiresNetwork = true)]
public sealed class UploadPhotoJob : IJob
{
    public string PhotoPath { get; set; } = "";

    public string PhotoId { get; set; } = "";
}

public sealed class UploadPhotoJobHandler : IJobHandler<UploadPhotoJob>
{
    public async Task ExecuteAsync(UploadPhotoJob job, JobContext context, CancellationToken cancellationToken)
    {
        await Task.Delay(400, cancellationToken);
    }
}

[Job("SyncCustomer", Queue = "sync", MaxAttempts = 8, RequiresNetwork = true)]
public sealed class SyncCustomerJob : IJob
{
    public string CustomerId { get; set; } = "";
}

public sealed class SyncCustomerJobHandler : IJobHandler<SyncCustomerJob>
{
    public async Task ExecuteAsync(SyncCustomerJob job, JobContext context, CancellationToken cancellationToken)
    {
        await Task.Delay(300, cancellationToken);
    }
}

[Job("SendAnalytics", Queue = "analytics", MaxAttempts = 3)]
public sealed class SendAnalyticsJob : IJob
{
    public string EventName { get; set; } = "";
}

public sealed class SendAnalyticsJobHandler : IJobHandler<SendAnalyticsJob>
{
    public async Task ExecuteAsync(SendAnalyticsJob job, JobContext context, CancellationToken cancellationToken)
    {
        await Task.Delay(150, cancellationToken);
    }
}

[Job("FlakySync", Queue = "sync", MaxAttempts = 4, RequiresNetwork = true)]
public sealed class FlakySyncJob : IJob
{
    public string Label { get; set; } = "flaky";
}

public sealed class FlakySyncJobHandler : IJobHandler<FlakySyncJob>
{
    public async Task ExecuteAsync(FlakySyncJob job, JobContext context, CancellationToken cancellationToken)
    {
        await Task.Delay(200, cancellationToken);
        if (context.Attempt < 3)
        {
            throw new InvalidOperationException($"Server 503 on attempt {context.Attempt}");
        }
    }
}

[Job("PoisonJob", Queue = "default", MaxAttempts = 2)]
public sealed class PoisonJob : IJob
{
    public string Reason { get; set; } = "invalid payload";
}

public sealed class PoisonJobHandler : IJobHandler<PoisonJob>
{
    public Task ExecuteAsync(PoisonJob job, JobContext context, CancellationToken cancellationToken)
    {
        context.Abort(job.Reason);
        return Task.CompletedTask;
    }
}

using Microsoft.Extensions.Logging;
using Plugin.Maui.JobQueue.Sample.Jobs;

namespace Plugin.Maui.JobQueue.Sample;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.Services.AddSingleton<MainPage>();

        builder
            .UseMauiApp<App>()
            .UseMauiJobQueue(options =>
            {
                options.WorkerCount = 2;
                options.PollInterval = TimeSpan.FromMilliseconds(400);
                options.DefaultMaxAttempts = 5;
                options.Backoff = BackoffPolicy.Exponential(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(8));
                options.DeleteOnSuccess = true;
                options.Register<UploadPhotoJob, UploadPhotoJobHandler>();
                options.Register<SyncCustomerJob, SyncCustomerJobHandler>();
                options.Register<SendAnalyticsJob, SendAnalyticsJobHandler>();
                options.Register<FlakySyncJob, FlakySyncJobHandler>();
                options.Register<PoisonJob, PoisonJobHandler>();
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}

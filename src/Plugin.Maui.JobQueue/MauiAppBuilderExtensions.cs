using Microsoft.Maui.Hosting;
using Microsoft.Maui.LifecycleEvents;

namespace Plugin.Maui.JobQueue;

/// <summary>
/// MAUI host registration for the durable job queue.
/// </summary>
public static class MauiAppBuilderExtensions
{
    /// <summary>
    /// Registers <see cref="IJobQueue"/>, job handlers, and optional lifecycle drain-on-resume.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.UseMauiJobQueue(options =>
    /// {
    ///     options.Register&lt;UploadPhotoJob, UploadPhotoJobHandler&gt;();
    ///     options.Register&lt;SyncCustomerJob, SyncCustomerJobHandler&gt;();
    /// });
    /// </code>
    /// </example>
    public static MauiAppBuilder UseMauiJobQueue(this MauiAppBuilder builder, Action<JobQueueOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new JobQueueOptions();
        configure?.Invoke(options);

        builder.Services.AddMauiJobQueue(options);
        builder.Services.AddTransient<IMauiInitializeService, JobQueueInitializer>();

        if (options.DrainOnResume)
        {
            builder.ConfigureLifecycleEvents(events =>
            {
#if ANDROID
                events.AddAndroid(android => android.OnResume(_ => ResumeDrain()));
#elif IOS || MACCATALYST
                events.AddiOS(ios => ios.OnActivated(_ => ResumeDrain()));
#elif WINDOWS
                events.AddWindows(windows => windows.OnActivated((_, _) => ResumeDrain()));
#endif
            });
        }

        return builder;
    }

    static void ResumeDrain()
    {
        if (!JobQueue.IsInitialized)
        {
            return;
        }

        if (JobQueue.Current.IsRunning)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await JobQueue.Current.DrainAsync().ConfigureAwait(false);
            }
            catch
            {
                // Resume drain is best-effort; failures surface through JobFailed.
            }
        });
    }
}

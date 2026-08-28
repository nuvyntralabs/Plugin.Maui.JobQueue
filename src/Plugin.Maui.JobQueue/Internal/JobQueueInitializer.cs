using Microsoft.Maui.Hosting;

namespace Plugin.Maui.JobQueue;

sealed class JobQueueInitializer : IMauiInitializeService
{
    public void Initialize(IServiceProvider services)
    {
        var queue = services.GetService<IJobQueue>();
        if (queue is null)
        {
            return;
        }

        JobQueue.SetDefault(queue);
        var options = services.GetService<JobQueueOptions>();
        if (options?.AutoStart != false)
        {
            _ = queue.StartAsync();
        }
    }
}

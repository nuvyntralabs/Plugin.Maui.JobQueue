namespace Plugin.Maui.JobQueue;

internal interface IJobDispatcher
{
    string JobTypeName { get; }

    Type JobClrType { get; }

    Task ExecuteAsync(string payloadJson, JobContext context, IServiceProvider services, CancellationToken cancellationToken);
}

internal sealed class JobDispatcher<TJob, THandler> : IJobDispatcher
    where TJob : class, IJob
    where THandler : class, IJobHandler<TJob>
{
    public string JobTypeName { get; } = JobTypeNames.For<TJob>();

    public Type JobClrType => typeof(TJob);

    public async Task ExecuteAsync(string payloadJson, JobContext context, IServiceProvider services, CancellationToken cancellationToken)
    {
        var job = JobJson.Deserialize<TJob>(payloadJson);
        var handler = services.GetService<IJobHandler<TJob>>()
                      ?? services.GetService<THandler>()
                      ?? throw new JobQueueException($"No handler registered for '{JobTypeName}'.");

        await handler.ExecuteAsync(job, context, cancellationToken).ConfigureAwait(false);
    }
}

internal static class JobTypeNames
{
    public static string For<TJob>() where TJob : IJob => For(typeof(TJob));

    public static string For(Type type)
    {
        var attribute = type.GetCustomAttributes(typeof(JobAttribute), inherit: false)
            .OfType<JobAttribute>()
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(attribute?.Name))
        {
            return attribute.Name;
        }

        return type.Name;
    }

    public static JobAttribute? Attribute(Type type) =>
        type.GetCustomAttributes(typeof(JobAttribute), inherit: false)
            .OfType<JobAttribute>()
            .FirstOrDefault();
}

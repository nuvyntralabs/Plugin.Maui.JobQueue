namespace Plugin.Maui.JobQueue;

/// <summary>
/// Registers the durable queue without MAUI lifecycle hooks.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds <see cref="IJobQueue"/> using the supplied options instance.
    /// </summary>
    public static IServiceCollection AddMauiJobQueue(this IServiceCollection services, JobQueueOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        foreach (var register in options.HandlerRegistrations)
        {
            register(services);
        }

        services.AddSingleton(options);
        services.TryAddSingleton<IClock>(sp =>
            sp.GetRequiredService<JobQueueOptions>().Clock ?? SystemClock.Instance);
        services.TryAddSingleton<INetworkGate>(sp =>
            sp.GetRequiredService<JobQueueOptions>().NetworkGate ?? JobQueue.CreateNetworkGate());
        services.TryAddSingleton<IJobStore>(sp =>
        {
            var resolved = sp.GetRequiredService<JobQueueOptions>();
            if (resolved.Store is not null)
            {
                return resolved.Store;
            }

            return resolved.UseInMemoryStore
                ? new Storage.InMemoryJobStore()
                : new Storage.SqliteJobStore(resolved);
        });
        services.TryAddSingleton<JobTypeRegistry>(sp => new JobTypeRegistry(sp.GetServices<IJobDispatcher>()));
        services.TryAddSingleton<IJobQueue>(sp =>
        {
            var queue = JobQueue.Create(sp, sp.GetRequiredService<JobQueueOptions>());
            JobQueue.SetDefault(queue);
            return queue;
        });

        return services;
    }

    /// <summary>
    /// Adds <see cref="IJobQueue"/> and applies <paramref name="configure"/> to a new options instance.
    /// </summary>
    public static IServiceCollection AddMauiJobQueue(this IServiceCollection services, Action<JobQueueOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        var options = new JobQueueOptions();
        configure?.Invoke(options);
        return services.AddMauiJobQueue(options);
    }

    /// <summary>
    /// Registers a typed job and the handler that executes it.
    /// </summary>
    public static IServiceCollection AddJobHandler<TJob, THandler>(this IServiceCollection services)
        where TJob : class, IJob
        where THandler : class, IJobHandler<TJob>
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddTransient<THandler>();
        services.TryAddTransient<IJobHandler<TJob>, THandler>();
        services.AddSingleton<IJobDispatcher, JobDispatcher<TJob, THandler>>();
        return services;
    }
}

namespace Plugin.Maui.JobQueue;

/// <summary>
/// Entry point when dependency injection is not used, and factory for tests.
/// </summary>
public static class JobQueue
{
    static IJobQueue? _current;

    /// <summary>
    /// Shared queue registered by <see cref="MauiAppBuilderExtensions.UseMauiJobQueue"/>.
    /// </summary>
    public static IJobQueue Current =>
        _current ?? throw new InvalidOperationException(
            "JobQueue has not been initialized. Call builder.UseMauiJobQueue() in MauiProgram.");

    /// <summary>True after <see cref="MauiAppBuilderExtensions.UseMauiJobQueue"/> or <see cref="SetDefault"/>.</summary>
    public static bool IsInitialized => _current is not null;

    /// <summary>Persists <paramref name="job"/> on the shared queue.</summary>
    public static Task<string> EnqueueAsync<TJob>(TJob job, JobEnqueueOptions? options = null, CancellationToken cancellationToken = default)
        where TJob : class, IJob =>
        Current.EnqueueAsync(job, options, cancellationToken);

    /// <summary>Creates a queue. Register handlers on <paramref name="services"/> first.</summary>
    public static IJobQueue Create(IServiceProvider services, JobQueueOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        options ??= services.GetService<JobQueueOptions>() ?? new JobQueueOptions();
        var store = options.Store
                    ?? services.GetService<IJobStore>()
                    ?? (options.UseInMemoryStore ? new Storage.InMemoryJobStore() : new Storage.SqliteJobStore(options));
        var clock = options.Clock ?? services.GetService<IClock>() ?? SystemClock.Instance;
        var network = options.NetworkGate ?? services.GetService<INetworkGate>() ?? CreateNetworkGate();
        var registry = services.GetService<JobTypeRegistry>()
                       ?? new JobTypeRegistry(services.GetServices<IJobDispatcher>());
        return new JobQueueEngine(options, store, clock, network, registry, services);
    }

    /// <summary>Replaces the shared instance. Intended for tests and custom hosts.</summary>
    public static void SetDefault(IJobQueue implementation) =>
        _current = implementation ?? throw new ArgumentNullException(nameof(implementation));

    internal static INetworkGate CreateNetworkGate()
    {
#if ANDROID || IOS || MACCATALYST || WINDOWS
        return new ConnectivityNetworkGate();
#else
        return new AlwaysOnlineNetworkGate();
#endif
    }
}

namespace Plugin.Maui.JobQueue;

internal sealed class JobTypeRegistry
{
    readonly Dictionary<string, IJobDispatcher> _dispatchers = new(StringComparer.Ordinal);

    public JobTypeRegistry(IEnumerable<IJobDispatcher> dispatchers)
    {
        foreach (var dispatcher in dispatchers)
        {
            _dispatchers[dispatcher.JobTypeName] = dispatcher;
        }
    }

    public bool TryGet(string jobTypeName, [NotNullWhen(true)] out IJobDispatcher? dispatcher) =>
        _dispatchers.TryGetValue(jobTypeName, out dispatcher);

    public IJobDispatcher GetRequired(string jobTypeName) =>
        TryGet(jobTypeName, out var dispatcher)
            ? dispatcher
            : throw new JobQueueException($"No job type registered as '{jobTypeName}'.");
}

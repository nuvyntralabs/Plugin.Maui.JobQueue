namespace Plugin.Maui.JobQueue;

/// <summary>
/// Tells the worker whether network-constrained jobs may run.
/// </summary>
public interface INetworkGate
{
    bool IsOnline { get; }

    event EventHandler? ConnectivityChanged;
}

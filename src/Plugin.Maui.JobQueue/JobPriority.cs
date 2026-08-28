namespace Plugin.Maui.JobQueue;

/// <summary>
/// Higher values are claimed first.
/// </summary>
public enum JobPriority
{
    Low = 0,
    Normal = 10,
    High = 20,
    Critical = 30
}

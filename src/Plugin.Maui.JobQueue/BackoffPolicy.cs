namespace Plugin.Maui.JobQueue;

/// <summary>
/// Computes the delay before the next retry.
/// </summary>
public sealed class BackoffPolicy
{
    /// <summary>Delay after the first failure.</summary>
    public TimeSpan InitialDelay { get; init; } = JobQueueDefaults.DefaultInitialBackoff;

    /// <summary>Upper bound for a single delay.</summary>
    public TimeSpan MaxDelay { get; init; } = JobQueueDefaults.DefaultMaxBackoff;

    /// <summary>Multiplied by the previous delay each attempt.</summary>
    public double Multiplier { get; init; } = 2.0;

    /// <summary>Fraction of the delay applied as +/- random jitter (0–1). 0.2 is ±20%.</summary>
    public double Jitter { get; init; } = 0.2;

    /// <summary>Exponential backoff from <paramref name="initial"/> capped at <paramref name="max"/>.</summary>
    public static BackoffPolicy Exponential(TimeSpan initial, TimeSpan max) =>
        new()
        {
            InitialDelay = initial,
            MaxDelay = max
        };

    /// <summary>Fixed delay on every retry.</summary>
    public static BackoffPolicy Constant(TimeSpan delay) =>
        new()
        {
            InitialDelay = delay,
            MaxDelay = delay,
            Multiplier = 1,
            Jitter = 0
        };

    /// <summary>
    /// Delay after <paramref name="failedAttempt"/> failures (1 = first failure).
    /// </summary>
    public TimeSpan Compute(int failedAttempt, Random? random = null)
    {
        if (failedAttempt < 1)
        {
            failedAttempt = 1;
        }

        var raw = InitialDelay.TotalMilliseconds * Math.Pow(Multiplier, failedAttempt - 1);
        var capped = Math.Min(raw, MaxDelay.TotalMilliseconds);
        if (capped < 0)
        {
            capped = 0;
        }

        if (Jitter <= 0)
        {
            return TimeSpan.FromMilliseconds(capped);
        }

        random ??= Random.Shared;
        var spread = capped * Jitter * ((random.NextDouble() * 2) - 1);
        return TimeSpan.FromMilliseconds(Math.Max(0, capped + spread));
    }
}

namespace Plugin.Maui.JobQueue.Tests;

public sealed class BackoffPolicyTests
{
    [Fact]
    public void Exponential_doubles_until_cap()
    {
        var policy = BackoffPolicy.Exponential(TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10));
        policy = new BackoffPolicy
        {
            InitialDelay = policy.InitialDelay,
            MaxDelay = policy.MaxDelay,
            Multiplier = 2,
            Jitter = 0
        };

        Assert.Equal(TimeSpan.FromSeconds(2), policy.Compute(1));
        Assert.Equal(TimeSpan.FromSeconds(4), policy.Compute(2));
        Assert.Equal(TimeSpan.FromSeconds(8), policy.Compute(3));
        Assert.Equal(TimeSpan.FromSeconds(10), policy.Compute(4));
    }

    [Fact]
    public void Constant_is_stable()
    {
        var policy = BackoffPolicy.Constant(TimeSpan.FromSeconds(3));
        Assert.Equal(TimeSpan.FromSeconds(3), policy.Compute(1));
        Assert.Equal(TimeSpan.FromSeconds(3), policy.Compute(8));
    }
}

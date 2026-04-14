using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Tests.Common;

namespace MultiSessionHost.Tests.State;

public sealed class RetryPolicyStateTests
{
    [Fact]
    public void RegisterFailure_UsesExponentialBackoffAndFaultsWhenMaxIsExceeded()
    {
        var definition = TestOptionsFactory.Create(
                TestOptionsFactory.Session("alpha", maxRetryCount: 2, initialBackoffMs: 100))
            .ToSessionDefinitions()
            .Single();

        var now = new DateTimeOffset(2026, 4, 14, 12, 0, 0, TimeSpan.Zero);
        var first = RetryPolicyState.None.RegisterFailure(definition, now);
        var second = first.RegisterFailure(definition, now);
        var third = second.RegisterFailure(definition, now);

        Assert.Equal(now.AddMilliseconds(100), first.NextRetryAtUtc);
        Assert.Equal(now.AddMilliseconds(200), second.NextRetryAtUtc);
        Assert.True(third.HasExceeded(definition));
        Assert.Null(third.NextRetryAtUtc);
    }
}

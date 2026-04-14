using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Infrastructure.Scheduling;
using MultiSessionHost.Tests.Common;

namespace MultiSessionHost.Tests.Scheduling;

public sealed class RoundRobinSessionSchedulerTests
{
    [Fact]
    public void GetDecisions_RotatesStartOrderAcrossCalls()
    {
        var scheduler = new RoundRobinSessionScheduler();
        var now = new DateTimeOffset(2026, 4, 14, 12, 0, 0, TimeSpan.Zero);
        var definitions = TestOptionsFactory.Create(
                TestOptionsFactory.Session("alpha"),
                TestOptionsFactory.Session("beta"),
                TestOptionsFactory.Session("gamma"))
            .ToSessionDefinitions();

        var snapshots = definitions
            .Select(definition => new SessionSnapshot(definition, SessionRuntimeState.Create(definition, now), 0))
            .ToArray();

        var first = scheduler.GetDecisions(snapshots, 1, now).Single(decision => decision.DecisionType == SchedulerDecisionType.Start);
        var second = scheduler.GetDecisions(snapshots, 1, now).Single(decision => decision.DecisionType == SchedulerDecisionType.Start);
        var third = scheduler.GetDecisions(snapshots, 1, now).Single(decision => decision.DecisionType == SchedulerDecisionType.Start);

        Assert.Equal("alpha", first.SessionId.Value);
        Assert.Equal("beta", second.SessionId.Value);
        Assert.Equal("gamma", third.SessionId.Value);
    }
}

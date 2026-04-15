using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.PolicyControl;
using MultiSessionHost.Infrastructure.State;
using MultiSessionHost.Tests.Common;

namespace MultiSessionHost.Tests.PolicyControl;

public sealed class SessionPolicyControlTests
{
    [Fact]
    public async Task GetAsync_InitializesMissingState()
    {
        var sessionId = new SessionId("policy-control-init");
        var clock = new FakeClock(DateTimeOffset.Parse("2026-04-15T12:00:00Z"));
        var store = new InMemorySessionPolicyControlStore(new SessionHostOptions(), clock);

        var state = await store.GetAsync(sessionId, CancellationToken.None);

        Assert.Equal(sessionId, state.SessionId);
        Assert.False(state.IsPolicyPaused);
        Assert.Null(state.LastChangedAtUtc);
    }

    [Fact]
    public async Task PauseResume_AreIdempotentAndSessionIsolated()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-04-15T12:00:00Z"));
        var store = new InMemorySessionPolicyControlStore(new SessionHostOptions(), clock);
        var alpha = new SessionId("policy-control-alpha");
        var beta = new SessionId("policy-control-beta");

        var pauseResult = await store.PauseAsync(
            alpha,
            new PolicyControlActionRequest("policy:paused", "paused for test", "tester", new Dictionary<string, string>()),
            CancellationToken.None);
        var pauseAgain = await store.PauseAsync(
            alpha,
            new PolicyControlActionRequest("policy:paused", "paused for test", "tester", new Dictionary<string, string>()),
            CancellationToken.None);
        var betaState = await store.GetAsync(beta, CancellationToken.None);
        var resumeResult = await store.ResumeAsync(
            alpha,
            new PolicyControlActionRequest("policy:resumed", "resumed for test", "tester", new Dictionary<string, string>()),
            CancellationToken.None);

        Assert.True(pauseResult.WasChanged);
        Assert.False(pauseAgain.WasChanged);
        Assert.True(resumeResult.WasChanged);
        Assert.False(betaState.IsPolicyPaused);
        Assert.Equal(2, (await store.GetHistoryAsync(alpha, CancellationToken.None)).Count);
    }

    [Fact]
    public async Task History_IsBoundedByConfiguredLimit()
    {
        var clock = new FakeClock(DateTimeOffset.Parse("2026-04-15T12:00:00Z"));
        var options = new SessionHostOptions
        {
            PolicyControl = new PolicyControlOptions
            {
                MaxHistoryEntries = 2
            }
        };
        var store = new InMemorySessionPolicyControlStore(options, clock);
        var sessionId = new SessionId("policy-control-history");

        await store.PauseAsync(sessionId, new PolicyControlActionRequest(null, null, null, new Dictionary<string, string>()), CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(1));
        await store.ResumeAsync(sessionId, new PolicyControlActionRequest(null, null, null, new Dictionary<string, string>()), CancellationToken.None);
        clock.Advance(TimeSpan.FromSeconds(1));
        await store.PauseAsync(sessionId, new PolicyControlActionRequest(null, null, null, new Dictionary<string, string>()), CancellationToken.None);

        var history = await store.GetHistoryAsync(sessionId, CancellationToken.None);

        Assert.Equal(2, history.Count);
        Assert.Equal(SessionPolicyControlAction.ResumePolicy, history[0].Action);
        Assert.Equal(SessionPolicyControlAction.PausePolicy, history[1].Action);
    }
}
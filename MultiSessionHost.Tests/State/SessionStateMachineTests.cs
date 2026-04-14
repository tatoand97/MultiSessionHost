using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Core.State;
using MultiSessionHost.Tests.Common;

namespace MultiSessionHost.Tests.State;

public sealed class SessionStateMachineTests
{
    [Fact]
    public void Transition_AllowsExpectedLifecycle()
    {
        var definition = TestOptionsFactory.Create(TestOptionsFactory.Session("alpha")).ToSessionDefinitions().Single();
        var now = new DateTimeOffset(2026, 4, 14, 12, 0, 0, TimeSpan.Zero);
        var state = SessionRuntimeState.Create(definition, now);

        state = SessionStateMachine.Transition(state, SessionStatus.Starting, now);
        state = SessionStateMachine.Transition(state, SessionStatus.Running, now);
        state = SessionStateMachine.Transition(state, SessionStatus.Paused, now);
        state = SessionStateMachine.Transition(state, SessionStatus.Running, now);
        state = SessionStateMachine.Transition(state, SessionStatus.Stopping, now);
        state = SessionStateMachine.Transition(state, SessionStatus.Stopped, now);

        Assert.Equal(SessionStatus.Stopped, state.CurrentStatus);
    }

    [Fact]
    public void Transition_ThrowsForInvalidTransition()
    {
        var definition = TestOptionsFactory.Create(TestOptionsFactory.Session("alpha")).ToSessionDefinitions().Single();
        var now = new DateTimeOffset(2026, 4, 14, 12, 0, 0, TimeSpan.Zero);
        var state = SessionRuntimeState.Create(definition, now);

        Assert.Throws<InvalidOperationException>(() => SessionStateMachine.Transition(state, SessionStatus.Running, now));
    }
}

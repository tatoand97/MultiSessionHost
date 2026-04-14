using MultiSessionHost.Core.Enums;

namespace MultiSessionHost.Core.Models;

public sealed record SchedulerDecision(
    SessionId SessionId,
    SchedulerDecisionType DecisionType,
    SessionWorkItem? WorkItem,
    string Reason);

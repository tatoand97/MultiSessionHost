namespace MultiSessionHost.Desktop.Behavior;

internal static class BehaviorReasonCodes
{
    public const string NoBehaviorPack = "behavior.pack.none";
    public const string UnknownBehaviorPack = "behavior.pack.unknown";
    public const string NoRoute = "behavior.travel.no-route";
    public const string Arrived = "behavior.travel.arrived";
    public const string AwaitingTransition = "behavior.travel.awaiting-transition";
    public const string AwaitingProgress = "behavior.travel.awaiting-progress";
    public const string BlockedPolicy = "behavior.travel.blocked-policy";
    public const string BlockedRecovery = "behavior.travel.blocked-recovery";
    public const string BlockedRisk = "behavior.travel.blocked-risk";
    public const string RefreshRequired = "behavior.travel.refresh-required";
    public const string PlanToggleAutopilot = "behavior.travel.plan-toggle-autopilot";
    public const string PlanSelectWaypoint = "behavior.travel.plan-next-waypoint";
    public const string PlanProgressAction = "behavior.travel.plan-progress-action";
}
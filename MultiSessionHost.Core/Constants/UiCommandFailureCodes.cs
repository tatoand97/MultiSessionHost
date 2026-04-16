namespace MultiSessionHost.Core.Constants;

public static class UiCommandFailureCodes
{
    public const string SessionNotFound = "SessionNotFound";
    public const string SessionNotActive = "SessionNotActive";
    public const string UiStateUnavailable = "UiStateUnavailable";
    public const string TargetNotAttached = "TargetNotAttached";
    public const string InteractionAdapterNotRegistered = "InteractionAdapterNotRegistered";
    public const string NodeNotFound = "NodeNotFound";
    public const string NodeNotVisible = "NodeNotVisible";
    public const string NodeDisabled = "NodeDisabled";
    public const string UnsupportedCommand = "UnsupportedCommand";
    public const string InvalidCommandPayload = "InvalidCommandPayload";
    public const string InteractionFailed = "InteractionFailed";
    public const string UiRefreshFailed = "UiRefreshFailed";
    public const string NativeElementNotFound = "native.element.not_found";
    public const string NativeElementStale = "native.element.stale";
    public const string NativePatternUnsupported = "native.pattern.unsupported";
    public const string NativePatternFailed = "native.pattern.failed";
    public const string NativeInputFallbackFailed = "native.input.fallback.failed";
    public const string NativeTargetLostDuringAction = "native.target.lost.during_action";
    public const string NativePostActionVerificationFailed = "native.post_action.verification_failed";
    public const string ScreenTravelMetadataMissing = "screen.travel.metadata_missing";
    public const string ScreenTravelSnapshotUnavailable = "screen.travel.snapshot_unavailable";
    public const string ScreenTravelSnapshotMismatch = "screen.travel.snapshot_mismatch";
    public const string ScreenTravelBoundsInvalid = "screen.travel.bounds_invalid";
    public const string ScreenTravelEvidenceInsufficient = "screen.travel.evidence_insufficient";
    public const string ScreenTravelInputFailed = "screen.travel.input_failed";
}

namespace MultiSessionHost.Desktop.Activity;

/// <summary>
/// Represents the current activity state of a session in its lifecycle.
/// States are mutually exclusive and represent high-level session behavior.
/// </summary>
public enum SessionActivityStateKind
{
    /// <summary>Unknown or uninitialized state.</summary>
    Unknown = 0,

    /// <summary>Session is idle with no active directives or objectives.</summary>
    Idle = 1,

    /// <summary>Session is selecting a worksite or job target.</summary>
    SelectingWorksite = 2,

    /// <summary>Session is actively traveling to a destination.</summary>
    Traveling = 3,

    /// <summary>Session has arrived at destination and is arriving phase.</summary>
    Arriving = 4,

    /// <summary>Session is at location waiting for spawn or engagement opportunity.</summary>
    WaitingForSpawn = 5,

    /// <summary>Session is actively engaged in combat or target engagement.</summary>
    Engaging = 6,

    /// <summary>Session is monitoring risk while activity continues but no stronger state applies.</summary>
    MonitoringRisk = 7,

    /// <summary>Session is withdrawing for safety or risk mitigation.</summary>
    Withdrawing = 8,

    /// <summary>Session is hiding or in pause-activity safe-seeking state.</summary>
    Hiding = 9,

    /// <summary>Session is recovering from prior blocking or degraded state.</summary>
    Recovering = 10,

    /// <summary>Session has encountered a fatal or terminal error condition.</summary>
    Faulted = 11
}

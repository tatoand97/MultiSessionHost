using MultiSessionHost.Core.Enums;

namespace MultiSessionHost.Core.Configuration;

public sealed class ExecutionCoordinationOptions
{
    private static readonly ExecutionOperationKind[] DefaultExclusiveKinds =
    [
        ExecutionOperationKind.WorkItem,
        ExecutionOperationKind.UiCommand,
        ExecutionOperationKind.UiRefresh,
        ExecutionOperationKind.AttachmentEnsure,
        ExecutionOperationKind.AttachmentInvalidate
    ];

    public bool EnableTargetCoordination { get; init; } = true;

    public bool EnableGlobalCoordination { get; init; }

    public int DefaultTargetCooldownMs { get; init; }

    public int MaxConcurrentGlobalTargetOperations { get; init; } = 1;

    public int WaitWarningThresholdMs { get; init; } = 1_000;

    public IReadOnlyList<ExecutionOperationKind> SessionExclusiveOperationKinds { get; init; } = DefaultExclusiveKinds;

    public IReadOnlyList<ExecutionOperationKind> TargetExclusiveOperationKinds { get; init; } = DefaultExclusiveKinds;

    public IReadOnlyList<ExecutionOperationKind> GlobalExclusiveOperationKinds { get; init; } = [];
}

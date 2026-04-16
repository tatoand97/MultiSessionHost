namespace MultiSessionHost.Contracts.Sessions;

public sealed record RuntimePersistenceStatusDto(
    bool Enabled,
    string Mode,
    string? BasePath,
    int SchemaVersion,
    DateTimeOffset CapturedAtUtc,
    IReadOnlyList<RuntimePersistenceSessionStatusDto> Sessions);

public sealed record RuntimePersistenceSessionStatusDto(
    string SessionId,
    bool Rehydrated,
    DateTimeOffset? LastLoadedAtUtc,
    DateTimeOffset? LastSavedAtUtc,
    string? LastError,
    string? PersistedPath,
    int ActivityHistoryCount,
    int OperationalMemoryHistoryCount,
    int DecisionPlanHistoryCount,
    int DecisionExecutionHistoryCount,
    int PolicyControlHistoryCount,
    int RecoveryHistoryCount);

public sealed record DecisionPlanHistoryEntryDto(
    string SessionId,
    DateTimeOffset RecordedAtUtc,
    DecisionPlanDto Plan);

public sealed record DecisionPlanHistoryDto(
    string SessionId,
    IReadOnlyList<DecisionPlanHistoryEntryDto> Entries);

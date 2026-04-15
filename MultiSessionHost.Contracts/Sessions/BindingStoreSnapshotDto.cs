namespace MultiSessionHost.Contracts.Sessions;

public sealed record BindingStoreSnapshotDto(
    long Version,
    DateTimeOffset LastUpdatedAtUtc,
    IReadOnlyCollection<SessionTargetBindingDto> Bindings);

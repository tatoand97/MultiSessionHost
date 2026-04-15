using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Bindings;

public sealed record BindingStoreSnapshot(
    long Version,
    DateTimeOffset LastUpdatedAtUtc,
    IReadOnlyCollection<SessionTargetBinding> Bindings);

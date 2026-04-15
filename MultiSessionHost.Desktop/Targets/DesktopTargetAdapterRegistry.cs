using MultiSessionHost.Core.Enums;
using MultiSessionHost.Desktop.Interfaces;

namespace MultiSessionHost.Desktop.Targets;

public sealed class DesktopTargetAdapterRegistry : IDesktopTargetAdapterRegistry
{
    private readonly IReadOnlyDictionary<DesktopTargetKind, IDesktopTargetAdapter> _adaptersByKind;

    public DesktopTargetAdapterRegistry(IEnumerable<IDesktopTargetAdapter> adapters)
    {
        ArgumentNullException.ThrowIfNull(adapters);

        _adaptersByKind = adapters.ToDictionary(static adapter => adapter.Kind);
    }

    public IDesktopTargetAdapter Resolve(DesktopTargetKind kind) =>
        _adaptersByKind.TryGetValue(kind, out var adapter)
            ? adapter
            : throw new InvalidOperationException($"Desktop target adapter '{kind}' is not registered.");
}

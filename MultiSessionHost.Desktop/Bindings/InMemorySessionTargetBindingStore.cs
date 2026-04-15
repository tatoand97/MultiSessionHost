using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Bindings;

public sealed class InMemorySessionTargetBindingStore : ISessionTargetBindingStore
{
    private readonly object _gate = new();
    private readonly IClock _clock;
    private Dictionary<SessionId, SessionTargetBinding> _bindingsBySessionId;
    private long _version;
    private DateTimeOffset _lastUpdatedAtUtc;

    public InMemorySessionTargetBindingStore(SessionHostOptions options, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(options);

        _clock = clock;
        _bindingsBySessionId = options.SessionTargetBindings
            .Select(SessionTargetBindingModelMapper.MapBinding)
            .ToDictionary(static binding => binding.SessionId);
        _lastUpdatedAtUtc = _clock.UtcNow;
    }

    public Task<IReadOnlyCollection<SessionTargetBinding>> GetAllAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyCollection<SessionTargetBinding>>(
                _bindingsBySessionId.Values
                    .OrderBy(static binding => binding.SessionId.Value, StringComparer.OrdinalIgnoreCase)
                    .Select(SessionTargetBindingModelMapper.NormalizeBinding)
                    .ToArray());
        }
    }

    public Task<SessionTargetBinding?> GetAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(
                _bindingsBySessionId.TryGetValue(sessionId, out var binding)
                    ? SessionTargetBindingModelMapper.NormalizeBinding(binding)
                    : null);
        }
    }

    public Task<SessionTargetBinding> UpsertAsync(SessionTargetBinding binding, CancellationToken cancellationToken)
    {
        var normalized = SessionTargetBindingModelMapper.NormalizeBinding(binding);

        lock (_gate)
        {
            _bindingsBySessionId[normalized.SessionId] = normalized;
            _version++;
            _lastUpdatedAtUtc = _clock.UtcNow;
        }

        return Task.FromResult(SessionTargetBindingModelMapper.NormalizeBinding(normalized));
    }

    public Task<bool> DeleteAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            var removed = _bindingsBySessionId.Remove(sessionId);

            if (removed)
            {
                _version++;
                _lastUpdatedAtUtc = _clock.UtcNow;
            }

            return Task.FromResult(removed);
        }
    }

    public Task<BindingStoreSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return Task.FromResult(
                new BindingStoreSnapshot(
                    _version,
                    _lastUpdatedAtUtc,
                    _bindingsBySessionId.Values
                        .OrderBy(static binding => binding.SessionId.Value, StringComparer.OrdinalIgnoreCase)
                        .Select(SessionTargetBindingModelMapper.NormalizeBinding)
                        .ToArray()));
        }
    }
}

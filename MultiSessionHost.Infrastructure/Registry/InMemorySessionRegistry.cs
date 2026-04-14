using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Infrastructure.Registry;

public sealed class InMemorySessionRegistry : ISessionRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<SessionId, SessionDefinition> _definitions = [];
    private readonly List<SessionId> _registrationOrder = [];

    public ValueTask RegisterAsync(SessionDefinition definition, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_definitions.ContainsKey(definition.Id))
            {
                throw new InvalidOperationException($"Session '{definition.Id}' is already registered.");
            }

            _definitions[definition.Id] = definition;
            _registrationOrder.Add(definition.Id);
        }

        return ValueTask.CompletedTask;
    }

    public IReadOnlyCollection<SessionDefinition> GetAll()
    {
        lock (_gate)
        {
            return _registrationOrder.Select(id => _definitions[id]).ToArray();
        }
    }

    public SessionDefinition? GetById(SessionId sessionId)
    {
        lock (_gate)
        {
            return _definitions.TryGetValue(sessionId, out var definition)
                ? definition
                : null;
        }
    }
}

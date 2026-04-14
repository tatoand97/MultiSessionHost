using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Attachments;

public sealed class InMemoryAttachedSessionStore : IAttachedSessionStore
{
    private readonly object _gate = new();
    private readonly Dictionary<SessionId, DesktopSessionAttachment> _attachments = [];

    public ValueTask<DesktopSessionAttachment?> GetAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            return ValueTask.FromResult(_attachments.TryGetValue(sessionId, out var attachment) ? attachment : null);
        }
    }

    public IReadOnlyCollection<DesktopSessionAttachment> GetAll()
    {
        lock (_gate)
        {
            return _attachments.Values.ToArray();
        }
    }

    public ValueTask SetAsync(DesktopSessionAttachment attachment, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _attachments[attachment.SessionId] = attachment;
        }

        return ValueTask.CompletedTask;
    }

    public ValueTask RemoveAsync(SessionId sessionId, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _attachments.Remove(sessionId);
        }

        return ValueTask.CompletedTask;
    }
}

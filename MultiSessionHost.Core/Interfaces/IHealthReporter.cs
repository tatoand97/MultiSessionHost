using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Core.Interfaces;

public interface IHealthReporter
{
    void RecordRegistration(SessionDefinition definition);

    void RecordTransition(SessionId sessionId, SessionStatus fromStatus, SessionStatus toStatus);

    void RecordHeartbeat(SessionHeartbeat heartbeat);

    void RecordTick(SessionId sessionId);

    void RecordError(SessionId sessionId, Exception exception);

    void RecordRetry(SessionId sessionId);

    ProcessHealthSnapshot CreateSnapshot(IReadOnlyCollection<SessionSnapshot> sessions, DateTimeOffset generatedAtUtc);
}

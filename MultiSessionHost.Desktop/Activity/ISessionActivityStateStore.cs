using MultiSessionHost.Core.Models;

namespace MultiSessionHost.Desktop.Activity;

/// <summary>
/// Provides per-session storage and retrieval of activity state snapshots and history.
/// Implementations must be thread-safe and maintain session isolation.
/// </summary>
public interface ISessionActivityStateStore
{
    /// <summary>
    /// Initializes the store with a bootstrap snapshot for a new session.
    /// Throws if the session is already initialized.
    /// </summary>
    ValueTask InitializeAsync(SessionId sessionId, SessionActivitySnapshot snapshot, CancellationToken cancellationToken);

    /// <summary>
    /// Gets the current activity snapshot for a session, or null if not initialized.
    /// </summary>
    ValueTask<SessionActivitySnapshot?> GetAsync(SessionId sessionId, CancellationToken cancellationToken);

    /// <summary>
    /// Upserts the activity snapshot for a session (creates or updates).
    /// </summary>
    ValueTask<SessionActivitySnapshot> UpsertAsync(SessionId sessionId, SessionActivitySnapshot snapshot, CancellationToken cancellationToken);

    /// <summary>
    /// Gets all activity snapshots across all sessions.
    /// </summary>
    ValueTask<IReadOnlyCollection<SessionActivitySnapshot>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gets the full history (bounded) for a session.
    /// Returns empty if session does not exist.
    /// </summary>
    ValueTask<IReadOnlyList<SessionActivityHistoryEntry>> GetHistoryAsync(SessionId sessionId, CancellationToken cancellationToken);

    /// <summary>
    /// Removes all data for a session (e.g., on session cleanup).
    /// </summary>
    ValueTask RemoveAsync(SessionId sessionId, CancellationToken cancellationToken);
}

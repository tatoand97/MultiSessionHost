namespace MultiSessionHost.Desktop.Activity;

/// <summary>
/// Evaluates the current activity state of a session based on domain state, risk assessment, and decision plan.
/// Produces deterministic state transitions with reasoning.
/// </summary>
public interface ISessionActivityStateEvaluator
{
    /// <summary>
    /// Evaluates activity state given current session signals and previous state.
    /// Returns a new snapshot with optional transition if state changed.
    /// </summary>
    ValueTask<SessionActivityEvaluationResult> EvaluateAsync(
        SessionActivityEvaluationContext context,
        CancellationToken cancellationToken);
}

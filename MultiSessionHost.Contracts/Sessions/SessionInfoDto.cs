namespace MultiSessionHost.Contracts.Sessions;

public sealed record SessionInfoDto(
    string SessionId,
    string DisplayName,
    bool Enabled,
    IReadOnlyCollection<string> Tags,
    SessionStateDto State,
    SessionMetricsDto Metrics);

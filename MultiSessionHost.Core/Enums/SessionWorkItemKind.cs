namespace MultiSessionHost.Core.Enums;

public enum SessionWorkItemKind
{
    Tick = 0,
    Heartbeat = 1,
    FetchUiSnapshot = 2,
    ProjectUiState = 3
}

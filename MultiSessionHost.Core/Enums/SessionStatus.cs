namespace MultiSessionHost.Core.Enums;

public enum SessionStatus
{
    Created = 0,
    Starting = 1,
    Running = 2,
    Paused = 3,
    Stopping = 4,
    Stopped = 5,
    Faulted = 6
}

using MultiSessionHost.Core.Interfaces;

namespace MultiSessionHost.Infrastructure.Time;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

using MultiSessionHost.Core.Interfaces;

namespace MultiSessionHost.Tests.Common;

public sealed class FakeClock : IClock
{
    public FakeClock(DateTimeOffset initialUtcNow)
    {
        UtcNow = initialUtcNow;
    }

    public DateTimeOffset UtcNow { get; private set; }

    public void Advance(TimeSpan delta)
    {
        UtcNow = UtcNow.Add(delta);
    }
}

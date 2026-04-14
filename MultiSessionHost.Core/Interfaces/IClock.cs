namespace MultiSessionHost.Core.Interfaces;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}

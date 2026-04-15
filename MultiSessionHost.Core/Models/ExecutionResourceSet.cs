namespace MultiSessionHost.Core.Models;

public sealed record ExecutionResourceSet(
    ExecutionResourceKey SessionResourceKey,
    ExecutionResourceKey? TargetResourceKey,
    ExecutionResourceKey? GlobalResourceKey,
    TimeSpan TargetCooldown)
{
    public IReadOnlyList<ExecutionResourceKey> GetAllKeys()
    {
        var keys = new List<ExecutionResourceKey>(capacity: 3)
        {
            SessionResourceKey
        };

        if (TargetResourceKey is not null)
        {
            keys.Add(TargetResourceKey);
        }

        if (GlobalResourceKey is not null)
        {
            keys.Add(GlobalResourceKey);
        }

        return keys;
    }
}

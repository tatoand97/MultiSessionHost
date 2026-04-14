namespace MultiSessionHost.Tests.Common;

public static class TestWait
{
    public static async Task UntilAsync(Func<bool> predicate, TimeSpan timeout, string failureMessage)
    {
        var startedAt = DateTimeOffset.UtcNow;

        while (!predicate())
        {
            if (DateTimeOffset.UtcNow - startedAt > timeout)
            {
                throw new Xunit.Sdk.XunitException(failureMessage);
            }

            await Task.Delay(20);
        }
    }
}

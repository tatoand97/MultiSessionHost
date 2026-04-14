using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Infrastructure.Registry;
using MultiSessionHost.Tests.Common;

namespace MultiSessionHost.Tests.Registry;

public sealed class InMemorySessionRegistryTests
{
    [Fact]
    public async Task RegisterAsync_PreservesRegistrationOrderAndAllowsLookup()
    {
        var registry = new InMemorySessionRegistry();
        var definitions = TestOptionsFactory.Create(
                TestOptionsFactory.Session("alpha"),
                TestOptionsFactory.Session("beta"))
            .ToSessionDefinitions();

        await registry.RegisterAsync(definitions[0], CancellationToken.None);
        await registry.RegisterAsync(definitions[1], CancellationToken.None);

        var all = registry.GetAll().ToArray();
        var lookup = registry.GetById(new SessionId("beta"));

        Assert.Equal(["alpha", "beta"], all.Select(definition => definition.Id.Value));
        Assert.NotNull(lookup);
        Assert.Equal("beta", lookup!.Id.Value);
    }
}

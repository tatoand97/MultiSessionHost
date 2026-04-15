using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Desktop.Bindings;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Targets;
using MultiSessionHost.Infrastructure.Registry;
using MultiSessionHost.Tests.Common;

namespace MultiSessionHost.Tests.Desktop;

public sealed class SessionTargetBindingManagerTests
{
    [Fact]
    public async Task UpsertAsync_FailsForUnknownSession()
    {
        var manager = await CreateManagerAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.UpsertAsync(
                new SessionTargetBinding(
                    new("missing"),
                    "test-app",
                    new Dictionary<string, string> { ["Port"] = "7100" },
                    Overrides: null),
                CancellationToken.None));

        Assert.Contains("does not match a configured session", exception.Message);
    }

    [Fact]
    public async Task UpsertAsync_FailsForUnknownProfile()
    {
        var manager = await CreateManagerAsync();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.UpsertAsync(
                new SessionTargetBinding(
                    new("alpha"),
                    "missing-profile",
                    new Dictionary<string, string> { ["Port"] = "7100" },
                    Overrides: null),
                CancellationToken.None));

        Assert.Contains("unknown profile", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpsertAsync_FailsForMissingTemplateVariables()
    {
        var options = new SessionHostOptions
        {
            DriverMode = DriverMode.DesktopTargetAdapter,
            EnableUiSnapshots = true,
            DesktopTargets =
            [
                new DesktopTargetProfileOptions
                {
                    ProfileName = "test-app",
                    Kind = DesktopTargetKind.DesktopTestApp,
                    ProcessName = "MultiSessionHost.TestDesktopApp",
                    WindowTitleFragment = "[SessionId: {SessionId}]",
                    CommandLineFragmentTemplate = "--session-id {SessionId} --tenant {Tenant}",
                    BaseAddressTemplate = "http://127.0.0.1:{Port}/",
                    MatchingMode = DesktopSessionMatchingMode.WindowTitleAndCommandLine,
                    SupportsUiSnapshots = true,
                    SupportsStateEndpoint = true
                }
            ],
            Sessions = [TestOptionsFactory.Session("alpha", startupDelayMs: 0)]
        };
        var registry = new InMemorySessionRegistry();
        await registry.RegisterAsync(options.ToSessionDefinitions().Single(), CancellationToken.None);
        var manager = new SessionTargetBindingManager(
            new InMemorySessionTargetBindingStore(options, new FakeClock(DateTimeOffset.UtcNow)),
            new NoOpSessionTargetBindingPersistence(),
            registry,
            new ConfiguredDesktopTargetProfileCatalog(options),
            new StubSessionAttachmentRuntime());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => manager.UpsertAsync(
                new SessionTargetBinding(
                    new("alpha"),
                    "test-app",
                    new Dictionary<string, string> { ["Port"] = "7100" },
                    Overrides: null),
                CancellationToken.None));

        Assert.Contains("Tenant", exception.Message);
    }

    private static async Task<SessionTargetBindingManager> CreateManagerAsync()
    {
        var options = new SessionHostOptions
        {
            DriverMode = DriverMode.DesktopTargetAdapter,
            EnableUiSnapshots = true,
            DesktopTargets = [TestOptionsFactory.DesktopTestAppProfile()],
            Sessions = [TestOptionsFactory.Session("alpha", startupDelayMs: 0)]
        };
        var registry = new InMemorySessionRegistry();
        await registry.RegisterAsync(options.ToSessionDefinitions().Single(), CancellationToken.None);

        return new SessionTargetBindingManager(
            new InMemorySessionTargetBindingStore(options, new FakeClock(DateTimeOffset.UtcNow)),
            new NoOpSessionTargetBindingPersistence(),
            registry,
            new ConfiguredDesktopTargetProfileCatalog(options),
            new StubSessionAttachmentRuntime());
    }

    private sealed class StubSessionAttachmentRuntime : ISessionAttachmentRuntime
    {
        public Task<DesktopSessionAttachment?> GetAsync(Core.Models.SessionId sessionId, CancellationToken cancellationToken) =>
            Task.FromResult<DesktopSessionAttachment?>(null);

        public Task<DesktopSessionAttachment> EnsureAttachedAsync(Core.Models.SessionSnapshot snapshot, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<bool> InvalidateAsync(Core.Models.SessionId sessionId, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }
}

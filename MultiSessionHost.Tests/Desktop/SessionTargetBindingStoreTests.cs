using Microsoft.Extensions.Hosting;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Desktop.Bindings;
using MultiSessionHost.Desktop.Models;
using MultiSessionHost.Desktop.Targets;
using MultiSessionHost.Tests.Common;

namespace MultiSessionHost.Tests.Desktop;

public sealed class SessionTargetBindingStoreTests
{
    [Fact]
    public async Task Store_InitializesFromConfiguredBindings()
    {
        var options = TestOptionsFactory.CreateDesktopTestAppOptions(7100, TestOptionsFactory.Session("alpha", startupDelayMs: 0));
        var store = new InMemorySessionTargetBindingStore(options, new FakeClock(DateTimeOffset.UtcNow));

        var binding = await store.GetAsync(new("alpha"), CancellationToken.None);

        Assert.NotNull(binding);
        Assert.Equal("test-app", binding!.TargetProfileName);
        Assert.Equal("7100", binding.Variables["Port"]);
    }

    [Fact]
    public async Task UpsertAsync_ReplacesExistingBindingForTheSameSession()
    {
        var options = TestOptionsFactory.CreateDesktopTestAppOptions(7100, TestOptionsFactory.Session("alpha", startupDelayMs: 0));
        var store = new InMemorySessionTargetBindingStore(options, new FakeClock(DateTimeOffset.UtcNow));

        await store.UpsertAsync(
            new(
                new("alpha"),
                "test-app",
                new Dictionary<string, string> { ["Port"] = "7200" },
                Overrides: null),
            CancellationToken.None);

        var binding = await store.GetAsync(new("alpha"), CancellationToken.None);

        Assert.NotNull(binding);
        Assert.Equal("7200", binding!.Variables["Port"]);
    }

    [Fact]
    public async Task DeleteAsync_RemovesTheBinding()
    {
        var options = TestOptionsFactory.CreateDesktopTestAppOptions(7100, TestOptionsFactory.Session("alpha", startupDelayMs: 0));
        var store = new InMemorySessionTargetBindingStore(options, new FakeClock(DateTimeOffset.UtcNow));

        var removed = await store.DeleteAsync(new("alpha"), CancellationToken.None);
        var binding = await store.GetAsync(new("alpha"), CancellationToken.None);

        Assert.True(removed);
        Assert.Null(binding);
    }

    [Fact]
    public async Task JsonPersistence_RoundTripsBindings()
    {
        var filePath = Path.Combine(Path.GetTempPath(), "msh-tests", $"{Guid.NewGuid():N}", "bindings.json");
        var options = CreatePersistenceOptions(filePath);
        var persistence = CreatePersistence(options);
        var bindings = new[]
        {
            new SessionTargetBinding(
                new("beta"),
                "test-app",
                new Dictionary<string, string> { ["Port"] = "7101" },
                new DesktopTargetProfileOverride(
                    ProcessName: null,
                    WindowTitleFragment: "[SessionId: {SessionId}]",
                    CommandLineFragmentTemplate: "--session-id {SessionId}",
                    BaseAddressTemplate: null,
                    MatchingMode: DesktopSessionMatchingMode.WindowTitleAndCommandLine,
                    Metadata: new Dictionary<string, string?> { ["UiSource"] = "DesktopTestApp" },
                    SupportsUiSnapshots: true,
                    SupportsStateEndpoint: true))
        };

        await persistence.SaveAsync(bindings, CancellationToken.None);
        var loaded = await persistence.LoadAsync(CancellationToken.None);

        var binding = Assert.Single(loaded);
        Assert.Equal("beta", binding.SessionId.Value);
        Assert.Equal("7101", binding.Variables["Port"]);
        Assert.NotNull(binding.Overrides);
        Assert.Equal(DesktopSessionMatchingMode.WindowTitleAndCommandLine, binding.Overrides!.MatchingMode);
    }

    [Fact]
    public async Task Bootstrapper_PersistedBindingOverridesConfiguredBinding()
    {
        var filePath = Path.Combine(Path.GetTempPath(), "msh-tests", $"{Guid.NewGuid():N}", "bindings.json");
        var options = CreatePersistenceOptions(filePath);
        var persistence = CreatePersistence(options);
        await persistence.SaveAsync(
            [
                new SessionTargetBinding(
                    new("alpha"),
                    "test-app",
                    new Dictionary<string, string> { ["Port"] = "7999" },
                    Overrides: null)
            ],
            CancellationToken.None);

        var store = new InMemorySessionTargetBindingStore(options, new FakeClock(DateTimeOffset.UtcNow));
        var bootstrapper = new SessionTargetBindingStoreBootstrapper(
            options,
            store,
            persistence,
            new ConfiguredDesktopTargetProfileCatalog(options));

        await bootstrapper.InitializeAsync(CancellationToken.None);
        var binding = await store.GetAsync(new("alpha"), CancellationToken.None);

        Assert.NotNull(binding);
        Assert.Equal("7999", binding!.Variables["Port"]);
    }

    private static SessionHostOptions CreatePersistenceOptions(string filePath) =>
        new()
        {
            DriverMode = DriverMode.DesktopTargetAdapter,
            EnableUiSnapshots = true,
            BindingStorePersistenceMode = BindingStorePersistenceMode.JsonFile,
            BindingStoreFilePath = filePath,
            DesktopTargets = [TestOptionsFactory.DesktopTestAppProfile()],
            SessionTargetBindings = [TestOptionsFactory.SessionTargetBinding("alpha", "test-app", "7100")],
            Sessions = [TestOptionsFactory.Session("alpha", startupDelayMs: 0)]
        };

    private static JsonFileSessionTargetBindingPersistence CreatePersistence(SessionHostOptions options) =>
        new(options, new TestHostEnvironment(Path.GetDirectoryName(options.BindingStoreFilePath!)!));

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string contentRootPath)
        {
            ContentRootPath = contentRootPath;
            ApplicationName = "Tests";
            EnvironmentName = "Development";
            ContentRootFileProvider = null!;
        }

        public string EnvironmentName { get; set; }

        public string ApplicationName { get; set; }

        public string ContentRootPath { get; set; }

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; }
    }
}

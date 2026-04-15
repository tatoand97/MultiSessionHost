using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Bindings;
using MultiSessionHost.Desktop.Targets;
using MultiSessionHost.Tests.Common;

namespace MultiSessionHost.Tests.Desktop;

public sealed class DesktopTargetProfileResolverTests
{
    [Fact]
    public void Resolve_RendersProfileTemplatesAndBindingVariables()
    {
        var options = new SessionHostOptions
        {
            DriverMode = DriverMode.DesktopTargetAdapter,
            EnableUiSnapshots = true,
            DesktopTargets =
            [
                new DesktopTargetProfileOptions
                {
                    ProfileName = "generic-http",
                    Kind = DesktopTargetKind.SelfHostedHttpDesktop,
                    ProcessName = "GenericDesktopApp",
                    WindowTitleFragment = "Session {SessionId}",
                    CommandLineFragmentTemplate = "--session-id {SessionId}",
                    BaseAddressTemplate = "http://127.0.0.1:{Port}/",
                    MatchingMode = DesktopSessionMatchingMode.WindowTitleAndCommandLine,
                    SupportsUiSnapshots = true,
                    SupportsStateEndpoint = false,
                    Metadata = new Dictionary<string, string?>
                    {
                        ["UiSource"] = "Generic {SessionId}"
                    }
                }
            ],
            SessionTargetBindings =
            [
                new SessionTargetBindingOptions
                {
                    SessionId = "alpha",
                    TargetProfileName = "generic-http",
                    Variables = new Dictionary<string, string?>
                    {
                        ["Port"] = "8110"
                    }
                }
            ],
            Sessions = [TestOptionsFactory.Session("alpha", startupDelayMs: 0)]
        };
        var resolver = CreateResolver(options);

        var context = resolver.Resolve(CreateSnapshot(options, "alpha"));

        Assert.Equal("Session alpha", context.Target.WindowTitleFragment);
        Assert.Equal("--session-id alpha", context.Target.CommandLineFragment);
        Assert.Equal("http://127.0.0.1:8110/", context.Target.BaseAddress!.ToString());
        Assert.Equal("Generic alpha", context.Target.Metadata["UiSource"]);
    }

    [Fact]
    public void Resolve_KeepsSessionsIsolatedAcrossBindings()
    {
        var options = new SessionHostOptions
        {
            DriverMode = DriverMode.DesktopTargetAdapter,
            EnableUiSnapshots = true,
            DesktopTargets = [TestOptionsFactory.DesktopTestAppProfile("shared-test-app")],
            SessionTargetBindings =
            [
                TestOptionsFactory.SessionTargetBinding("alpha", "shared-test-app", "7100"),
                TestOptionsFactory.SessionTargetBinding("beta", "shared-test-app", "7101")
            ],
            Sessions =
            [
                TestOptionsFactory.Session("alpha", startupDelayMs: 0),
                TestOptionsFactory.Session("beta", startupDelayMs: 0)
            ]
        };
        var resolver = CreateResolver(options);

        var alpha = resolver.Resolve(CreateSnapshot(options, "alpha"));
        var beta = resolver.Resolve(CreateSnapshot(options, "beta"));

        Assert.Equal("http://127.0.0.1:7100/", alpha.Target.BaseAddress!.ToString());
        Assert.Equal("http://127.0.0.1:7101/", beta.Target.BaseAddress!.ToString());
        Assert.NotEqual(alpha.Target.CommandLineFragment, beta.Target.CommandLineFragment);
        Assert.NotEqual(alpha.Target.WindowTitleFragment, beta.Target.WindowTitleFragment);
    }

    [Fact]
    public void Resolve_AppliesSessionOverridesBeforeRendering()
    {
        var options = new SessionHostOptions
        {
            DriverMode = DriverMode.DesktopTargetAdapter,
            EnableUiSnapshots = true,
            DesktopTargets = [TestOptionsFactory.DesktopTestAppProfile("test-app")],
            SessionTargetBindings =
            [
                new SessionTargetBindingOptions
                {
                    SessionId = "alpha",
                    TargetProfileName = "test-app",
                    Variables = new Dictionary<string, string?>
                    {
                        ["Port"] = "7200"
                    },
                    Overrides = new DesktopTargetProfileOverrideOptions
                    {
                        WindowTitleFragment = "Override {SessionId}",
                        BaseAddressTemplate = "http://127.0.0.1:{Port}/override/"
                    }
                }
            ],
            Sessions = [TestOptionsFactory.Session("alpha", startupDelayMs: 0)]
        };
        var resolver = CreateResolver(options);

        var context = resolver.Resolve(CreateSnapshot(options, "alpha"));

        Assert.Equal("Override alpha", context.Target.WindowTitleFragment);
        Assert.Equal("http://127.0.0.1:7200/override/", context.Target.BaseAddress!.ToString());
    }

    private static SessionSnapshot CreateSnapshot(SessionHostOptions options, string sessionId)
    {
        var definition = options.ToSessionDefinitions().Single(definition => definition.Id.Value == sessionId);
        var state = SessionRuntimeState.Create(definition, DateTimeOffset.UtcNow) with
        {
            DesiredStatus = SessionStatus.Running
        };

        return new SessionSnapshot(definition, state, PendingWorkItems: 0);
    }

    private static ConfiguredDesktopTargetProfileResolver CreateResolver(SessionHostOptions options) =>
        new(
            new ConfiguredDesktopTargetProfileCatalog(options),
            new InMemorySessionTargetBindingStore(options, new FakeClock(DateTimeOffset.UtcNow)));
}

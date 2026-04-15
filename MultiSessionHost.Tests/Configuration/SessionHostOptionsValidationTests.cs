using System.Text;
using Microsoft.Extensions.Configuration;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Tests.Common;

namespace MultiSessionHost.Tests.Configuration;

public sealed class SessionHostOptionsValidationTests
{
    [Fact]
    public void TryValidate_BindsAndAcceptsDesktopTargetsAndBindings()
    {
        const string json = """
        {
          "MultiSessionHost": {
            "DriverMode": "DesktopTargetAdapter",
            "EnableUiSnapshots": true,
            "DesktopTargets": [
              {
                "ProfileName": "test-app",
                "Kind": "DesktopTestApp",
                "ProcessName": "MultiSessionHost.TestDesktopApp",
                "WindowTitleFragment": "[SessionId: {SessionId}]",
                "CommandLineFragmentTemplate": "--session-id {SessionId}",
                "BaseAddressTemplate": "http://127.0.0.1:{Port}/",
                "MatchingMode": "WindowTitleAndCommandLine",
                "SupportsUiSnapshots": true,
                "SupportsStateEndpoint": true,
                "Metadata": {
                  "UiSource": "DesktopTestApp"
                }
              }
            ],
            "SessionTargetBindings": [
              {
                "SessionId": "alpha",
                "TargetProfileName": "test-app",
                "Variables": {
                  "Port": "7100"
                }
              }
            ],
            "Sessions": [
              {
                "SessionId": "alpha",
                "DisplayName": "Alpha Session",
                "Enabled": true,
                "TickIntervalMs": 1000,
                "StartupDelayMs": 0,
                "MaxParallelWorkItems": 1,
                "MaxRetryCount": 3,
                "InitialBackoffMs": 1000,
                "Tags": [ "desktop-test" ]
              }
            ]
          }
        }
        """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var configuration = new ConfigurationBuilder().AddJsonStream(stream).Build();
        var options = new SessionHostOptions();
        configuration.GetSection(SessionHostOptions.SectionName).Bind(options);

        var valid = options.TryValidate(out var error);

        Assert.True(valid, error);
        Assert.Single(options.DesktopTargets);
        Assert.Single(options.SessionTargetBindings);
        Assert.Equal(DriverMode.DesktopTargetAdapter, options.DriverMode);
        Assert.Equal(DesktopTargetKind.DesktopTestApp, options.DesktopTargets[0].Kind);
        Assert.Equal("7100", options.SessionTargetBindings[0].Variables["Port"]);
    }

    [Fact]
    public void TryValidate_FailsWhenAConfiguredSessionHasNoBinding()
    {
        var options = new SessionHostOptions
        {
            DriverMode = DriverMode.DesktopTargetAdapter,
            EnableUiSnapshots = true,
            DesktopTargets = [TestOptionsFactory.DesktopTestAppProfile()],
            Sessions = [TestOptionsFactory.Session("alpha", startupDelayMs: 0)]
        };

        var valid = options.TryValidate(out var error);

        Assert.False(valid);
        Assert.Contains("SessionTargetBindings must contain at least one binding", error);
    }

    [Fact]
    public void TryValidate_FailsWhenABindingReferencesAnUnknownProfile()
    {
        var options = new SessionHostOptions
        {
            DriverMode = DriverMode.DesktopTargetAdapter,
            EnableUiSnapshots = true,
            DesktopTargets = [TestOptionsFactory.DesktopTestAppProfile()],
            SessionTargetBindings = [TestOptionsFactory.SessionTargetBinding("alpha", "missing-profile", "7100")],
            Sessions = [TestOptionsFactory.Session("alpha", startupDelayMs: 0)]
        };

        var valid = options.TryValidate(out var error);

        Assert.False(valid);
        Assert.Contains("unknown profile", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryValidate_FailsWhenABindingMissesARequiredTemplateVariable()
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
                    BaseAddressTemplate = "http://127.0.0.1:{Port}/",
                    CommandLineFragmentTemplate = "--session-id {SessionId} --tenant {Tenant}",
                    MatchingMode = DesktopSessionMatchingMode.WindowTitleAndCommandLine,
                    SupportsUiSnapshots = true,
                    SupportsStateEndpoint = true
                }
            ],
            SessionTargetBindings =
            [
                new SessionTargetBindingOptions
                {
                    SessionId = "alpha",
                    TargetProfileName = "test-app",
                    Variables = new Dictionary<string, string?>
                    {
                        ["Port"] = "7100"
                    }
                }
            ],
            Sessions = [TestOptionsFactory.Session("alpha", startupDelayMs: 0)]
        };

        var valid = options.TryValidate(out var error);

        Assert.False(valid);
        Assert.Contains("Tenant", error);
    }
}

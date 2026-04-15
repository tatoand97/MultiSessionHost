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

    [Fact]
    public void TryValidate_FailsWhenJsonFilePersistenceHasNoPath()
    {
        var options = new SessionHostOptions
        {
            DriverMode = DriverMode.DesktopTargetAdapter,
            EnableUiSnapshots = true,
            BindingStorePersistenceMode = BindingStorePersistenceMode.JsonFile,
            DesktopTargets = [TestOptionsFactory.DesktopTestAppProfile()],
            SessionTargetBindings = [TestOptionsFactory.SessionTargetBinding("alpha", "test-app", "7100")],
            Sessions = [TestOptionsFactory.Session("alpha", startupDelayMs: 0)]
        };

        var valid = options.TryValidate(out var error);

        Assert.False(valid);
        Assert.Contains("BindingStoreFilePath", error);
    }

    [Fact]
    public void TryValidate_FailsWhenExecutionCoordinationCooldownIsNegative()
    {
        var options = new SessionHostOptions
        {
            ExecutionCoordination = new ExecutionCoordinationOptions
            {
                DefaultTargetCooldownMs = -1
            },
            Sessions = [TestOptionsFactory.Session("alpha", startupDelayMs: 0)]
        };

        var valid = options.TryValidate(out var error);

        Assert.False(valid);
        Assert.Contains("DefaultTargetCooldownMs", error);
    }

    [Fact]
    public void TryValidate_FailsWhenGlobalExecutionCoordinationLimitIsInvalid()
    {
        var options = new SessionHostOptions
        {
            ExecutionCoordination = new ExecutionCoordinationOptions
            {
                EnableGlobalCoordination = true,
                MaxConcurrentGlobalTargetOperations = 0
            },
            Sessions = [TestOptionsFactory.Session("alpha", startupDelayMs: 0)]
        };

        var valid = options.TryValidate(out var error);

        Assert.False(valid);
        Assert.Contains("MaxConcurrentGlobalTargetOperations", error);
    }

    [Fact]
    public void TryValidate_AcceptsConfiguredRiskRules()
    {
        var options = new SessionHostOptions
        {
            RiskClassification = TestOptionsFactory.GenericRiskClassification(),
            Sessions = [TestOptionsFactory.Session("alpha", startupDelayMs: 0)]
        };

        var valid = options.TryValidate(out var error);

        Assert.True(valid, error);
    }

    [Fact]
    public void TryValidate_FailsWhenRiskClassificationHasNoRules()
    {
        var options = new SessionHostOptions
        {
            RiskClassification = new RiskClassificationOptions
            {
                EnableRiskClassification = true
            },
            Sessions = [TestOptionsFactory.Session("alpha", startupDelayMs: 0)]
        };

        var valid = options.TryValidate(out var error);

        Assert.False(valid);
        Assert.Contains("Rules", error);
    }

    [Fact]
    public void TryValidate_FailsWhenRiskRuleNamesAreDuplicated()
    {
        var options = new SessionHostOptions
        {
            RiskClassification = new RiskClassificationOptions
            {
                EnableRiskClassification = true,
                Rules =
                [
                    new RiskRuleOptions { RuleName = "dup", MatchByName = ["safe"] },
                    new RiskRuleOptions { RuleName = "dup", MatchByType = ["Warning"] }
                ]
            },
            Sessions = [TestOptionsFactory.Session("alpha", startupDelayMs: 0)]
        };

        var valid = options.TryValidate(out var error);

        Assert.False(valid);
        Assert.Contains("duplicated", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryValidate_AcceptsDefaultPolicyEngineOptions()
    {
        var options = new SessionHostOptions
        {
            Sessions = [TestOptionsFactory.Session("alpha", startupDelayMs: 0)]
        };

        var valid = options.TryValidate(out var error);

        Assert.True(valid, error);
        Assert.Equal("AbortPolicy", options.PolicyEngine.PolicyOrder[0]);
    }

    [Fact]
    public void TryValidate_FailsWhenPolicyOrderContainsUnknownPolicy()
    {
        var options = new SessionHostOptions
        {
            PolicyEngine = new PolicyEngineOptions
            {
                PolicyOrder = ["AbortPolicy", "MissingPolicy"]
            },
            Sessions = [TestOptionsFactory.Session("alpha", startupDelayMs: 0)]
        };

        var valid = options.TryValidate(out var error);

        Assert.False(valid);
        Assert.Contains("MissingPolicy", error);
    }

    [Fact]
    public void TryValidate_FailsWhenPolicyThresholdsAreInvalid()
    {
        var options = new SessionHostOptions
        {
            PolicyEngine = new PolicyEngineOptions
            {
                ResourceUsagePolicy = new ResourceUsagePolicyOptions
                {
                    CriticalPercentThreshold = 80,
                    DegradedPercentThreshold = 25
                }
            },
            Sessions = [TestOptionsFactory.Session("alpha", startupDelayMs: 0)]
        };

        var valid = options.TryValidate(out var error);

        Assert.False(valid);
        Assert.Contains("CriticalPercentThreshold", error);
    }

    [Fact]
    public void TryValidate_FailsWhenPolicyRuleNamesAreDuplicated()
    {
        var options = new SessionHostOptions
        {
            PolicyEngine = new PolicyEngineOptions
            {
                Rules = new BehaviorRulesOptions
                {
                    ResourceUsage = new ResourceUsageRulesOptions
                    {
                        Rules =
                        [
                            new AllowRuleOptions { RuleName = "dup", MaxResourcePercent = 50, DirectiveKind = "ConserveResource" },
                            new AllowRuleOptions { RuleName = "dup", MaxResourcePercent = 20, DirectiveKind = "Withdraw" }
                        ]
                    }
                }
            },
            Sessions = [TestOptionsFactory.Session("alpha", startupDelayMs: 0)]
        };

        var valid = options.TryValidate(out var error);

        Assert.False(valid);
        Assert.Contains("duplicate", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TryValidate_FailsWhenPolicyRuleThresholdsContradict()
    {
        var options = new SessionHostOptions
        {
            PolicyEngine = new PolicyEngineOptions
            {
                Rules = new BehaviorRulesOptions
                {
                    Transit = new TransitRulesOptions
                    {
                        Rules =
                        [
                            new WaitRuleOptions
                            {
                                RuleName = "bad-progress",
                                MinProgressPercent = 80,
                                MaxProgressPercent = 20,
                                DirectiveKind = "Wait"
                            }
                        ]
                    }
                }
            },
            Sessions = [TestOptionsFactory.Session("alpha", startupDelayMs: 0)]
        };

        var valid = options.TryValidate(out var error);

        Assert.False(valid);
        Assert.Contains("minimum cannot be greater", error, StringComparison.OrdinalIgnoreCase);
    }
}

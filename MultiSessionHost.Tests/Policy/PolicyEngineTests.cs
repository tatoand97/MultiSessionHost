using Microsoft.Extensions.Logging.Abstractions;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Extraction;
using MultiSessionHost.Desktop.Policy;
using MultiSessionHost.Desktop.Risk;
using MultiSessionHost.Infrastructure.Queues;
using MultiSessionHost.Infrastructure.Registry;
using MultiSessionHost.Infrastructure.State;
using MultiSessionHost.Tests.Common;

namespace MultiSessionHost.Tests.Policy;

public sealed class PolicyEngineTests
{
    [Fact]
    public async Task AbortPolicy_ReturnsAbortDirectiveForFaultedRuntime()
    {
        var sessionId = new SessionId("policy-abort");
        var context = CreateContext(sessionId, runtimeStatus: SessionStatus.Faulted);
        var result = await new AbortPolicy(new SessionHostOptions()).EvaluateAsync(context, CancellationToken.None);

        Assert.True(result.DidAbort);
        Assert.Contains(result.Directives, static directive => directive.DirectiveKind == DecisionDirectiveKind.Abort);
    }

    [Fact]
    public async Task ThreatResponsePolicy_ReturnsWithdrawForCriticalWithdrawRisk()
    {
        var sessionId = new SessionId("policy-threat");
        var context = CreateContext(
            sessionId,
            domain: CreateDomain(sessionId) with
            {
                Threat = ThreatState.CreateDefault() with { Severity = ThreatSeverity.Critical, TopSuggestedPolicy = "Withdraw", TopEntityLabel = "critical-alert" }
            },
            riskAssessment: CreateRisk(sessionId, RiskPolicySuggestion.Withdraw, RiskSeverity.Critical, priority: 900));

        var result = await new ThreatResponsePolicy(new SessionHostOptions()).EvaluateAsync(context, CancellationToken.None);

        Assert.True(result.DidBlock);
        Assert.Contains(result.Directives, static directive => directive.DirectiveKind == DecisionDirectiveKind.Withdraw);
    }

    [Fact]
    public async Task TargetPrioritizationPolicy_SelectsTopPrioritizedThreat()
    {
        var sessionId = new SessionId("policy-target");
        var context = CreateContext(
            sessionId,
            riskAssessment: CreateRisk(sessionId, RiskPolicySuggestion.Prioritize, RiskSeverity.High, priority: 700));

        var result = await new TargetPrioritizationPolicy(new SessionHostOptions()).EvaluateAsync(context, CancellationToken.None);

        var directive = Assert.Single(result.Directives);
        Assert.Equal(DecisionDirectiveKind.PrioritizeTarget, directive.DirectiveKind);
        Assert.Equal("candidate-1", directive.TargetId);
    }

    [Fact]
    public async Task ResourceUsagePolicy_ConservesOrWithdrawsForDegradedResources()
    {
        var sessionId = new SessionId("policy-resource");
        var context = CreateContext(
            sessionId,
            domain: CreateDomain(sessionId) with
            {
                Resources = ResourceState.CreateDefault() with { IsDegraded = true, EnergyPercent = 25 }
            });

        var result = await new ResourceUsagePolicy(new SessionHostOptions()).EvaluateAsync(context, CancellationToken.None);

        Assert.Contains(result.Directives, static directive => directive.DirectiveKind == DecisionDirectiveKind.ConserveResource);
    }

    [Fact]
    public async Task TransitPolicy_ReturnsWaitWhileTransitioning()
    {
        var sessionId = new SessionId("policy-transit");
        var context = CreateContext(
            sessionId,
            domain: CreateDomain(sessionId) with
            {
                Navigation = NavigationState.CreateDefault() with { Status = NavigationStatus.InProgress, IsTransitioning = true, DestinationLabel = "next-site" }
            });

        var result = await new TransitPolicy(new SessionHostOptions()).EvaluateAsync(context, CancellationToken.None);

        Assert.True(result.DidBlock);
        Assert.Contains(result.Directives, static directive => directive.DirectiveKind == DecisionDirectiveKind.Wait);
    }

    [Fact]
    public async Task SelectNextSitePolicy_EmitsSiteSelectionWhenIdleAndSafe()
    {
        var sessionId = new SessionId("policy-site");
        var context = CreateContext(
            sessionId,
            domain: CreateDomain(sessionId) with
            {
                Threat = ThreatState.CreateDefault() with { Severity = ThreatSeverity.None, IsSafe = true }
            });

        var result = await new SelectNextSitePolicy(new SessionHostOptions()).EvaluateAsync(context, CancellationToken.None);

        Assert.Contains(result.Directives, static directive => directive.DirectiveKind == DecisionDirectiveKind.SelectSite);
    }

    [Fact]
    public void Aggregator_AbortOverridesAllDirectives()
    {
        var sessionId = new SessionId("aggregate-abort");
        var aggregator = new DefaultDecisionPlanAggregator(new SessionHostOptions());
        var plan = aggregator.Aggregate(
            sessionId,
            DateTimeOffset.Parse("2026-04-15T12:00:00Z"),
            [
                Result("AbortPolicy", Directive("AbortPolicy", DecisionDirectiveKind.Abort, 1000), didBlock: true, didAbort: true),
                Result("SelectNextSitePolicy", Directive("SelectNextSitePolicy", DecisionDirectiveKind.SelectSite, 250))
            ]);

        Assert.Equal(DecisionPlanStatus.Aborting, plan.PlanStatus);
        Assert.Single(plan.Directives);
        Assert.Equal(DecisionDirectiveKind.Abort, plan.Directives[0].DirectiveKind);
        Assert.Equal(1, plan.Summary.SuppressedDirectiveCounts["abort-overrides"]);
    }

    [Fact]
    public void Aggregator_ThreatResponseOverridesSiteSelection()
    {
        var sessionId = new SessionId("aggregate-threat");
        var aggregator = new DefaultDecisionPlanAggregator(new SessionHostOptions());
        var plan = aggregator.Aggregate(
            sessionId,
            DateTimeOffset.Parse("2026-04-15T12:00:00Z"),
            [
                Result("ThreatResponsePolicy", Directive("ThreatResponsePolicy", DecisionDirectiveKind.Withdraw, 900), didBlock: true),
                Result("SelectNextSitePolicy", Directive("SelectNextSitePolicy", DecisionDirectiveKind.SelectSite, 250))
            ]);

        Assert.Equal(DecisionPlanStatus.Blocked, plan.PlanStatus);
        Assert.DoesNotContain(plan.Directives, static directive => directive.DirectiveKind == DecisionDirectiveKind.SelectSite);
    }

    [Fact]
    public void Aggregator_TransitWaitSuppressesLowerPriorityActivity()
    {
        var sessionId = new SessionId("aggregate-transit");
        var aggregator = new DefaultDecisionPlanAggregator(new SessionHostOptions());
        var plan = aggregator.Aggregate(
            sessionId,
            DateTimeOffset.Parse("2026-04-15T12:00:00Z"),
            [
                Result("TransitPolicy", Directive("TransitPolicy", DecisionDirectiveKind.Wait, 650), didBlock: true),
                Result("TargetPrioritizationPolicy", Directive("TargetPrioritizationPolicy", DecisionDirectiveKind.PrioritizeTarget, 600)),
                Result("SelectNextSitePolicy", Directive("SelectNextSitePolicy", DecisionDirectiveKind.SelectSite, 250))
            ]);

        Assert.Equal(DecisionPlanStatus.Blocked, plan.PlanStatus);
        Assert.Single(plan.Directives);
        Assert.Equal(DecisionDirectiveKind.Wait, plan.Directives[0].DirectiveKind);
    }

    [Fact]
    public void Aggregator_SuppressionRulesAreConfigurable()
    {
        var sessionId = new SessionId("aggregate-configurable");
        var options = new SessionHostOptions
        {
            PolicyEngine = new PolicyEngineOptions
            {
                AggregationRules = new DecisionPlanAggregationRulesOptions
                {
                    SuppressionRules = [],
                    StatusRules = []
                }
            }
        };
        var aggregator = new DefaultDecisionPlanAggregator(options);

        var plan = aggregator.Aggregate(
            sessionId,
            DateTimeOffset.Parse("2026-04-15T12:00:00Z"),
            [
                Result("AbortPolicy", Directive("AbortPolicy", DecisionDirectiveKind.Abort, 1000), didBlock: true, didAbort: true),
                Result("SelectNextSitePolicy", Directive("SelectNextSitePolicy", DecisionDirectiveKind.SelectSite, 250))
            ]);

        Assert.Equal(2, plan.Directives.Count);
        Assert.Equal(DecisionPlanStatus.Ready, plan.PlanStatus);
        Assert.Empty(plan.Summary.SuppressedDirectiveCounts);
    }

    [Fact]
    public void Aggregator_CustomSuppressionRuleControlsDirectiveKindSemantics()
    {
        var sessionId = new SessionId("aggregate-custom-rule");
        var options = new SessionHostOptions
        {
            PolicyEngine = new PolicyEngineOptions
            {
                AggregationRules = new DecisionPlanAggregationRulesOptions
                {
                    SuppressionRules =
                    [
                        new DirectiveSuppressionRuleOptions
                        {
                            RuleName = "custom-observe-suppresses-use",
                            TriggerDirectiveKinds = ["Observe"],
                            SuppressedDirectiveKinds = ["UseResource"]
                        }
                    ],
                    StatusRules =
                    [
                        new DecisionPlanStatusRuleOptions
                        {
                            RuleName = "observe-blocked",
                            Status = "Blocked",
                            DirectiveKinds = ["Observe"]
                        }
                    ]
                }
            }
        };
        var aggregator = new DefaultDecisionPlanAggregator(options);

        var plan = aggregator.Aggregate(
            sessionId,
            DateTimeOffset.Parse("2026-04-15T12:00:00Z"),
            [
                Result("ThreatResponsePolicy", Directive("ThreatResponsePolicy", DecisionDirectiveKind.Observe, 300)),
                Result("ResourceUsagePolicy", Directive("ResourceUsagePolicy", DecisionDirectiveKind.UseResource, 250))
            ]);

        Assert.Single(plan.Directives);
        Assert.Equal(DecisionDirectiveKind.Observe, plan.Directives[0].DirectiveKind);
        Assert.Equal(DecisionPlanStatus.Blocked, plan.PlanStatus);
        Assert.Equal(1, plan.Summary.SuppressedDirectiveCounts["custom-observe-suppresses-use"]);
    }

    [Fact]
    public async Task PolicyEngine_EvaluatesPersistsAndKeepsSessionsIsolated()
    {
        var alphaId = new SessionId("engine-alpha");
        var betaId = new SessionId("engine-beta");
        var clock = new FakeClock(DateTimeOffset.Parse("2026-04-15T12:00:00Z"));
        var registry = new InMemorySessionRegistry();
        var stateStore = new InMemorySessionStateStore();
        var uiStore = new InMemorySessionUiStateStore();
        var domainStore = new InMemorySessionDomainStateStore();
        var semanticStore = new InMemorySessionSemanticExtractionStore();
        var riskStore = new InMemorySessionRiskAssessmentStore();
        var queue = new ChannelBasedWorkQueue();
        var planStore = new InMemorySessionDecisionPlanStore();
        var options = new SessionHostOptions();

        foreach (var sessionId in new[] { alphaId, betaId })
        {
            var definition = CreateDefinition(sessionId);
            await registry.RegisterAsync(definition, CancellationToken.None);
            await stateStore.InitializeAsync(SessionRuntimeState.Create(definition, clock.UtcNow) with { CurrentStatus = SessionStatus.Running }, CancellationToken.None);
            await uiStore.InitializeAsync(SessionUiState.Create(sessionId), CancellationToken.None);
            await domainStore.InitializeAsync(
                CreateDomain(sessionId) with { Threat = ThreatState.CreateDefault() with { Severity = ThreatSeverity.None, IsSafe = true } },
                CancellationToken.None);
        }

        var engine = new DefaultPolicyEngine(
            options,
            registry,
            stateStore,
            queue,
            uiStore,
            domainStore,
            semanticStore,
            riskStore,
            CreatePolicies(options),
            new DefaultDecisionPlanAggregator(options),
            planStore,
            clock,
            NullLogger<DefaultPolicyEngine>.Instance);

        var alphaPlan = await engine.EvaluateAsync(alphaId, CancellationToken.None);

        Assert.Equal(alphaId, alphaPlan.SessionId);
        Assert.Contains(alphaPlan.Directives, static directive => directive.DirectiveKind == DecisionDirectiveKind.SelectSite);
        Assert.NotNull(await planStore.GetLatestAsync(alphaId, CancellationToken.None));
        Assert.Null(await planStore.GetLatestAsync(betaId, CancellationToken.None));
    }

    [Fact]
    public async Task SiteSelectionAllowlist_ChangesSelectedSiteWhenConfigChanges()
    {
        var sessionId = new SessionId("policy-site-rules");
        var context = CreateContext(
            sessionId,
            domain: CreateDomain(sessionId) with
            {
                Location = LocationState.CreateDefault() with { ContextLabel = "alpha-site", IsUnknown = false },
                Threat = ThreatState.CreateDefault() with { Severity = ThreatSeverity.None }
            });
        var blockedOptions = OptionsWithRules(siteRules:
        [
            new AllowRuleOptions
            {
                RuleName = "only-beta",
                MatchLabels = ["beta-site"],
                DirectiveKind = "SelectSite",
                Priority = 400,
                SuggestedPolicy = "SelectSite"
            }
        ]);
        var allowedOptions = OptionsWithRules(siteRules:
        [
            new AllowRuleOptions
            {
                RuleName = "only-alpha",
                MatchLabels = ["alpha-site"],
                DirectiveKind = "SelectSite",
                Priority = 401,
                SuggestedPolicy = "SelectSite"
            }
        ]);

        var blocked = await new SelectNextSitePolicy(blockedOptions).EvaluateAsync(context, CancellationToken.None);
        var allowed = await new SelectNextSitePolicy(allowedOptions).EvaluateAsync(context, CancellationToken.None);

        Assert.Empty(blocked.Directives);
        var directive = Assert.Single(allowed.Directives);
        Assert.Equal(DecisionDirectiveKind.SelectSite, directive.DirectiveKind);
        Assert.Equal("only-alpha", directive.Metadata["matchedRuleName"]);
        Assert.Equal(401, directive.Priority);
    }

    [Fact]
    public async Task ThreatDenyRule_ForcesConfiguredWithdraw()
    {
        var sessionId = new SessionId("policy-threat-deny");
        var options = OptionsWithRules(threatRules:
        [
            new RetreatRuleOptions
            {
                RuleName = "deny-alert-label",
                MatchLabels = ["candidate-label"],
                DirectiveKind = "Withdraw",
                Priority = 777,
                SuggestedPolicy = "Withdraw",
                Blocks = true,
                MinimumWaitMs = 12_000,
                Reason = "Configured deny label."
            }
        ]);
        var context = CreateContext(
            sessionId,
            riskAssessment: CreateRisk(sessionId, RiskPolicySuggestion.Observe, RiskSeverity.Low, priority: 100));

        var result = await new ThreatResponsePolicy(options).EvaluateAsync(context, CancellationToken.None);

        var directive = Assert.Single(result.Directives);
        Assert.True(result.DidBlock);
        Assert.Equal(DecisionDirectiveKind.Withdraw, directive.DirectiveKind);
        Assert.Equal("deny-alert-label", directive.Metadata["matchedRuleName"]);
        Assert.Equal("12000", directive.Metadata["minimumWaitMs"]);
        Assert.True(directive.Metadata.ContainsKey("notBeforeUtc"));
    }

    [Fact]
    public void PolicyRuleProvider_PreservesEffectiveRuleFamilyIdentity()
    {
        var options = OptionsWithRules(
            threatRules:
            [
                new RetreatRuleOptions
                {
                    RuleName = "retreat-only",
                    MatchSuggestedPolicies = [RiskPolicySuggestion.Withdraw],
                    DirectiveKind = "Withdraw",
                    Priority = 700,
                    SuggestedPolicy = "Withdraw"
                }
            ],
            threatDenyRules:
            [
                new DenyRuleOptions
                {
                    RuleName = "deny-only",
                    MatchLabels = ["avoid-me"],
                    DirectiveKind = "AvoidTarget",
                    Priority = 701,
                    SuggestedPolicy = "Avoid"
                }
            ]);

        var rules = new ConfiguredPolicyRuleProvider(options).GetRules();

        Assert.Single(rules.ThreatResponseRetreatRules);
        Assert.Single(rules.ThreatResponseDenyRules);
        Assert.Equal("ThreatResponse.RetreatRules", rules.ThreatResponseRetreatRules[0].RuleFamily);
        Assert.Equal("ThreatResponse.DenyRules", rules.ThreatResponseDenyRules[0].RuleFamily);
    }

    [Fact]
    public async Task ThreatRetreatRules_ChangeRetreatWithoutChangingDenyFamily()
    {
        var sessionId = new SessionId("policy-threat-family");
        var options = OptionsWithRules(
            threatRules:
            [
                new RetreatRuleOptions
                {
                    RuleName = "custom-retreat",
                    MatchSuggestedPolicies = [RiskPolicySuggestion.Withdraw],
                    DirectiveKind = "PauseActivity",
                    Priority = 710,
                    SuggestedPolicy = "PauseActivity",
                    Blocks = true
                }
            ],
            threatDenyRules:
            [
                new DenyRuleOptions
                {
                    RuleName = "custom-deny",
                    MatchLabels = ["never-this-label"],
                    DirectiveKind = "AvoidTarget",
                    Priority = 720,
                    SuggestedPolicy = "Avoid"
                }
            ]);
        var context = CreateContext(
            sessionId,
            riskAssessment: CreateRisk(sessionId, RiskPolicySuggestion.Withdraw, RiskSeverity.High, priority: 800));

        var result = await new ThreatResponsePolicy(options).EvaluateAsync(context, CancellationToken.None);

        var directive = Assert.Single(result.Directives);
        Assert.Equal(DecisionDirectiveKind.PauseActivity, directive.DirectiveKind);
        Assert.Equal("ThreatResponse.RetreatRules", directive.Metadata["policyRuleFamily"]);
        Assert.DoesNotContain(result.Explanation!.RuleTraces, trace => trace.RuleName == "custom-deny" && trace.Outcome == PolicyRuleEvaluationOutcome.Matched);
    }

    [Fact]
    public async Task TargetDenyRules_ChangeAvoidWithoutChangingPriorityFamily()
    {
        var sessionId = new SessionId("policy-target-deny-family");
        var options = OptionsWithRules(
            targetRules:
            [
                new AllowRuleOptions
                {
                    RuleName = "priority-only",
                    MatchSuggestedPolicies = [RiskPolicySuggestion.Prioritize],
                    DirectiveKind = "PrioritizeTarget",
                    Priority = 650,
                    SuggestedPolicy = "Prioritize"
                }
            ],
            targetDenyRules:
            [
                new DenyRuleOptions
                {
                    RuleName = "deny-avoid-label",
                    MatchLabels = ["candidate-label"],
                    DirectiveKind = "AvoidTarget",
                    Priority = 640,
                    SuggestedPolicy = "Avoid"
                }
            ]);
        var context = CreateContext(
            sessionId,
            riskAssessment: CreateRisk(sessionId, RiskPolicySuggestion.Observe, RiskSeverity.Low, priority: 100));

        var result = await new TargetPrioritizationPolicy(options).EvaluateAsync(context, CancellationToken.None);

        var directive = Assert.Single(result.Directives);
        Assert.Equal(DecisionDirectiveKind.AvoidTarget, directive.DirectiveKind);
        Assert.Equal("TargetPrioritization.DenyRules", directive.Metadata["policyRuleFamily"]);
        Assert.Contains(result.Explanation!.RuleTraces, trace => trace.RuleName == "priority-only" && trace.Outcome == PolicyRuleEvaluationOutcome.Rejected);
    }

    [Fact]
    public async Task FallbackRule_ActivatesWhenExplicitRulesDoNotMatch()
    {
        var sessionId = new SessionId("policy-fallback");
        var options = OptionsWithRules(
            siteRules:
            [
                new AllowRuleOptions
                {
                    RuleName = "only-beta",
                    MatchLabels = ["beta-site"],
                    DirectiveKind = "SelectSite",
                    Priority = 400,
                    SuggestedPolicy = "SelectSite"
                }
            ],
            siteFallback: new FallbackRuleOptions
            {
                RuleName = "site-fallback",
                Enabled = true,
                DirectiveKind = "Observe",
                Priority = 199,
                SuggestedPolicy = "Observe",
                Reason = "Fallback site observation."
            });
        var context = CreateContext(
            sessionId,
            domain: CreateDomain(sessionId) with
            {
                Location = LocationState.CreateDefault() with { ContextLabel = "alpha-site", IsUnknown = false }
            });

        var result = await new SelectNextSitePolicy(options).EvaluateAsync(context, CancellationToken.None);

        var directive = Assert.Single(result.Directives);
        Assert.Equal(DecisionDirectiveKind.Observe, directive.DirectiveKind);
        Assert.Equal("True", directive.Metadata["isFallback"]);
        Assert.True(result.Explanation!.FallbackUsed);
    }

    [Fact]
    public async Task PolicyExplanation_CapturesConsideredMatchedAndRejectedRules()
    {
        var sessionId = new SessionId("policy-explanation");
        var options = OptionsWithRules(resourceRules:
        [
            new AllowRuleOptions
            {
                RuleName = "reject-low-threshold",
                MaxResourcePercent = 10,
                DirectiveKind = "ConserveResource",
                Priority = 500,
                SuggestedPolicy = "ConserveResource"
            },
            new AllowRuleOptions
            {
                RuleName = "match-high-threshold",
                MaxResourcePercent = 50,
                DirectiveKind = "ConserveResource",
                Priority = 501,
                SuggestedPolicy = "ConserveResource"
            }
        ]);
        var context = CreateContext(
            sessionId,
            domain: CreateDomain(sessionId) with
            {
                Resources = ResourceState.CreateDefault() with { EnergyPercent = 25 }
            });

        var result = await new ResourceUsagePolicy(options).EvaluateAsync(context, CancellationToken.None);

        Assert.NotNull(result.Explanation);
        Assert.Contains(result.Explanation!.RuleTraces, trace => trace.RuleName == "reject-low-threshold" && trace.Outcome == PolicyRuleEvaluationOutcome.Rejected && trace.RejectedReason is not null);
        Assert.Contains(result.Explanation.RuleTraces, trace => trace.RuleName == "match-high-threshold" && trace.Outcome == PolicyRuleEvaluationOutcome.Matched);
        Assert.Equal("match-high-threshold", result.Explanation.MatchedRuleName);
    }

    [Fact]
    public void Aggregator_ExplanationIncludesSuppressionAndStatusRules()
    {
        var sessionId = new SessionId("aggregate-explanation");
        var aggregator = new DefaultDecisionPlanAggregator(new SessionHostOptions());
        var plan = aggregator.Aggregate(
            sessionId,
            DateTimeOffset.Parse("2026-04-15T12:00:00Z"),
            [
                Result("TransitPolicy", Directive("TransitPolicy", DecisionDirectiveKind.Wait, 650), didBlock: true),
                Result("SelectNextSitePolicy", Directive("SelectNextSitePolicy", DecisionDirectiveKind.SelectSite, 250))
            ]);

        Assert.NotNull(plan.Explanation);
        Assert.Contains(plan.Explanation!.AggregationRulesApplied, trace => trace.RuleName == "transit-wait-stability" && trace.RuleType == "Suppression");
        Assert.Contains(plan.Explanation.AggregationRulesApplied, trace => trace.RuleName == "blocked-directives" && trace.RuleType == "Status" && trace.ResultStatus == "Blocked");
    }

    [Fact]
    public async Task DirectiveMetadata_IsConsistentAcrossPolicies()
    {
        var sessionId = new SessionId("policy-metadata");
        var context = CreateContext(
            sessionId,
            domain: CreateDomain(sessionId) with
            {
                Resources = ResourceState.CreateDefault() with { IsDegraded = true, EnergyPercent = 25 },
                Navigation = NavigationState.CreateDefault() with { Status = NavigationStatus.InProgress, IsTransitioning = true }
            },
            riskAssessment: CreateRisk(sessionId, RiskPolicySuggestion.Prioritize, RiskSeverity.High, priority: 700));
        var results = new[]
        {
            await new TargetPrioritizationPolicy(new SessionHostOptions()).EvaluateAsync(context, CancellationToken.None),
            await new ResourceUsagePolicy(new SessionHostOptions()).EvaluateAsync(context, CancellationToken.None),
            await new TransitPolicy(new SessionHostOptions()).EvaluateAsync(context, CancellationToken.None)
        };

        foreach (var directive in results.SelectMany(static result => result.Directives))
        {
            Assert.True(directive.Metadata.ContainsKey("matchedRuleName"));
            Assert.True(directive.Metadata.ContainsKey("reasonRuleName"));
            Assert.True(directive.Metadata.ContainsKey("matchedCriteria"));
            Assert.True(directive.Metadata.ContainsKey("policyRuleFamily"));
            Assert.True(directive.Metadata.ContainsKey("policyName"));
            Assert.True(directive.Metadata.ContainsKey("ruleIntent"));
            Assert.True(directive.Metadata.ContainsKey("sourceScope"));
            Assert.True(directive.Metadata.ContainsKey("isFallback"));
        }
    }

    [Fact]
    public async Task ResourceRules_ChangeBehaviorWhenThresholdChanges()
    {
        var sessionId = new SessionId("policy-resource-rules");
        var context = CreateContext(
            sessionId,
            domain: CreateDomain(sessionId) with
            {
                Resources = ResourceState.CreateDefault() with { EnergyPercent = 30 }
            });
        var conserveOptions = OptionsWithRules(resourceRules:
        [
            new AllowRuleOptions
            {
                RuleName = "conserve-under-35",
                MaxResourcePercent = 35,
                DirectiveKind = "ConserveResource",
                Priority = 500,
                SuggestedPolicy = "ConserveResource"
            }
        ]);
        var withdrawOptions = OptionsWithRules(resourceRules:
        [
            new AllowRuleOptions
            {
                RuleName = "withdraw-under-35",
                MaxResourcePercent = 35,
                DirectiveKind = "Withdraw",
                Priority = 800,
                SuggestedPolicy = "Withdraw",
                Blocks = true
            }
        ]);

        var conserve = await new ResourceUsagePolicy(conserveOptions).EvaluateAsync(context, CancellationToken.None);
        var withdraw = await new ResourceUsagePolicy(withdrawOptions).EvaluateAsync(context, CancellationToken.None);

        Assert.Equal(DecisionDirectiveKind.ConserveResource, Assert.Single(conserve.Directives).DirectiveKind);
        Assert.Equal(DecisionDirectiveKind.Withdraw, Assert.Single(withdraw.Directives).DirectiveKind);
    }

    [Fact]
    public async Task TransitWaitRule_EmitsConfiguredWaitMetadata()
    {
        var sessionId = new SessionId("policy-transit-wait-rules");
        var options = OptionsWithRules(transitRules:
        [
            new WaitRuleOptions
            {
                RuleName = "wait-progress-under-half",
                MatchNavigationStatuses = [NavigationStatus.InProgress],
                MaxProgressPercent = 50,
                DirectiveKind = "Wait",
                Priority = 678,
                SuggestedPolicy = "Wait",
                Blocks = true,
                MinimumWaitMs = 9_000
            }
        ]);
        var context = CreateContext(
            sessionId,
            domain: CreateDomain(sessionId) with
            {
                Navigation = NavigationState.CreateDefault() with { Status = NavigationStatus.InProgress, ProgressPercent = 25, DestinationLabel = "next-site" }
            });

        var result = await new TransitPolicy(options).EvaluateAsync(context, CancellationToken.None);

        var directive = Assert.Single(result.Directives);
        Assert.Equal(DecisionDirectiveKind.Wait, directive.DirectiveKind);
        Assert.Equal("wait-progress-under-half", directive.Metadata["matchedRuleName"]);
        Assert.Equal("9000", directive.Metadata["minimumWaitMs"]);
    }

    [Fact]
    public async Task AbortRules_EmitPauseOrAbortAccordingToConfig()
    {
        var sessionId = new SessionId("policy-abort-rules");
        var context = CreateContext(
            sessionId,
            runtimeStatus: SessionStatus.Faulted);
        var pauseOptions = OptionsWithRules(abortRules:
        [
            new RetreatRuleOptions
            {
                RuleName = "faulted-pauses",
                MatchSessionStatuses = [SessionStatus.Faulted],
                DirectiveKind = "PauseActivity",
                Priority = 700,
                SuggestedPolicy = "PauseActivity",
                Blocks = true
            }
        ]);
        var abortOptions = OptionsWithRules(abortRules:
        [
            new RetreatRuleOptions
            {
                RuleName = "faulted-aborts",
                MatchSessionStatuses = [SessionStatus.Faulted],
                DirectiveKind = "Abort",
                Priority = 900,
                SuggestedPolicy = "Abort",
                Blocks = true,
                Aborts = true
            }
        ]);

        var pause = await new AbortPolicy(pauseOptions).EvaluateAsync(context, CancellationToken.None);
        var abort = await new AbortPolicy(abortOptions).EvaluateAsync(context, CancellationToken.None);

        Assert.Equal(DecisionDirectiveKind.PauseActivity, Assert.Single(pause.Directives).DirectiveKind);
        Assert.False(pause.DidAbort);
        Assert.Equal(DecisionDirectiveKind.Abort, Assert.Single(abort.Directives).DirectiveKind);
        Assert.True(abort.DidAbort);
    }

    private static IReadOnlyList<IPolicy> CreatePolicies(SessionHostOptions options) =>
    [
        new AbortPolicy(options),
        new ThreatResponsePolicy(options),
        new TransitPolicy(options),
        new ResourceUsagePolicy(options),
        new TargetPrioritizationPolicy(options),
        new SelectNextSitePolicy(options)
    ];

    private static SessionHostOptions OptionsWithRules(
        IReadOnlyList<AllowRuleOptions>? siteRules = null,
        FallbackRuleOptions? siteFallback = null,
        IReadOnlyList<RetreatRuleOptions>? threatRules = null,
        IReadOnlyList<DenyRuleOptions>? threatDenyRules = null,
        FallbackRuleOptions? threatFallback = null,
        IReadOnlyList<AllowRuleOptions>? targetRules = null,
        IReadOnlyList<DenyRuleOptions>? targetDenyRules = null,
        FallbackRuleOptions? targetFallback = null,
        IReadOnlyList<AllowRuleOptions>? resourceRules = null,
        FallbackRuleOptions? resourceFallback = null,
        IReadOnlyList<WaitRuleOptions>? transitRules = null,
        FallbackRuleOptions? transitFallback = null,
        IReadOnlyList<RetreatRuleOptions>? abortRules = null,
        FallbackRuleOptions? abortFallback = null) =>
        new()
        {
            PolicyEngine = new PolicyEngineOptions
            {
                Rules = new BehaviorRulesOptions
                {
                    SiteSelection = new SiteSelectionRulesOptions
                    {
                        AllowRules = siteRules ?? [],
                        IgnoreNonAllowlistedSites = true,
                        Fallback = siteFallback ?? new FallbackRuleOptions { RuleName = "disabled-site-fallback", Enabled = false }
                    },
                    ThreatResponse = new ThreatResponseRulesOptions
                    {
                        RetreatRules = threatRules ?? [],
                        DenyRules = threatDenyRules ?? [],
                        Fallback = threatFallback ?? new FallbackRuleOptions { RuleName = "disabled-threat-fallback", Enabled = false }
                    },
                    TargetPrioritization = new TargetPrioritizationRulesOptions
                    {
                        PriorityRules = targetRules ?? [],
                        DenyRules = targetDenyRules ?? [],
                        Fallback = targetFallback ?? new FallbackRuleOptions { RuleName = "disabled-target-fallback", Enabled = false }
                    },
                    ResourceUsage = new ResourceUsageRulesOptions
                    {
                        Rules = resourceRules ?? [],
                        Fallback = resourceFallback ?? new FallbackRuleOptions { RuleName = "disabled-resource-fallback", Enabled = false }
                    },
                    Transit = new TransitRulesOptions
                    {
                        Rules = transitRules ?? [],
                        Fallback = transitFallback ?? new FallbackRuleOptions { RuleName = "disabled-transit-fallback", Enabled = false }
                    },
                    Abort = new AbortRulesOptions
                    {
                        Rules = abortRules ?? [],
                        Fallback = abortFallback ?? new FallbackRuleOptions { RuleName = "disabled-abort-fallback", Enabled = false }
                    }
                }
            }
        };

    private static PolicyEvaluationContext CreateContext(
        SessionId sessionId,
        SessionStatus runtimeStatus = SessionStatus.Running,
        SessionDomainState? domain = null,
        RiskAssessmentResult? riskAssessment = null) =>
        new(
            sessionId,
            new SessionSnapshot(
                CreateDefinition(sessionId),
                SessionRuntimeState.Create(CreateDefinition(sessionId), DateTimeOffset.Parse("2026-04-15T12:00:00Z")) with { CurrentStatus = runtimeStatus },
                PendingWorkItems: 0),
            SessionUiState.Create(sessionId),
            domain ?? CreateDomain(sessionId),
            UiSemanticExtractionResult.Empty(sessionId, DateTimeOffset.Parse("2026-04-15T12:00:00Z")),
            riskAssessment,
            ResolvedDesktopTargetContext: null,
            DesktopSessionAttachment: null,
            DateTimeOffset.Parse("2026-04-15T12:00:00Z"));

    private static SessionDefinition CreateDefinition(SessionId sessionId) =>
        new(
            sessionId,
            $"{sessionId.Value}-display",
            Enabled: true,
            TickInterval: TimeSpan.FromMilliseconds(100),
            StartupDelay: TimeSpan.Zero,
            MaxParallelWorkItems: 1,
            MaxRetryCount: 3,
            InitialBackoff: TimeSpan.FromMilliseconds(100),
            Tags: []);

    private static SessionDomainState CreateDomain(SessionId sessionId) =>
        SessionDomainState.CreateBootstrap(sessionId, DateTimeOffset.Parse("2026-04-15T12:00:00Z")) with
        {
            Navigation = NavigationState.CreateDefault() with { Status = NavigationStatus.Idle },
            Combat = CombatState.CreateDefault() with { Status = CombatStatus.Idle },
            Target = TargetState.CreateDefault(),
            Location = LocationState.CreateDefault() with { ContextLabel = "unit-worksite", IsUnknown = false }
        };

    private static RiskAssessmentResult CreateRisk(
        SessionId sessionId,
        RiskPolicySuggestion policySuggestion,
        RiskSeverity severity,
        int priority) =>
        new(
            sessionId,
            DateTimeOffset.Parse("2026-04-15T12:00:00Z"),
            [
                new RiskEntityAssessment(
                    "candidate-1",
                    RiskEntitySource.Target,
                    "candidate-label",
                    "candidate-type",
                    ["active"],
                    RiskDisposition.Threat,
                    severity,
                    priority,
                    policySuggestion,
                    "test-rule",
                    ["test reason"],
                    1,
                    new Dictionary<string, string>())
            ],
            new RiskAssessmentSummary(
                SafeCount: 0,
                UnknownCount: 0,
                ThreatCount: 1,
                HighestSeverity: severity,
                HighestPriority: priority,
                HasWithdrawPolicy: policySuggestion == RiskPolicySuggestion.Withdraw,
                TopCandidateId: "candidate-1",
                TopCandidateName: "candidate-label",
                TopCandidateType: "candidate-type",
                TopSuggestedPolicy: policySuggestion),
            []);

    private static PolicyEvaluationResult Result(
        string policyName,
        DecisionDirective directive,
        bool didBlock = false,
        bool didAbort = false) =>
        new(policyName, [directive], [], [], DidMatch: true, didBlock, didAbort);

    private static DecisionDirective Directive(string policyName, DecisionDirectiveKind kind, int priority) =>
        new(
            $"{policyName}:{kind}".ToLowerInvariant(),
            kind,
            priority,
            policyName,
            TargetId: null,
            TargetLabel: null,
            SuggestedPolicy: kind.ToString(),
            new Dictionary<string, string>(),
            []);
}

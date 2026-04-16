using Microsoft.Extensions.DependencyInjection;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Desktop.Activity;
using MultiSessionHost.Desktop.Adapters;
using MultiSessionHost.Desktop.Automation;
using MultiSessionHost.Desktop.Attachments;
using MultiSessionHost.Desktop.Behavior;
using MultiSessionHost.Desktop.Bindings;
using MultiSessionHost.Desktop.Commands;
using MultiSessionHost.Desktop.Drivers;
using MultiSessionHost.Desktop.Extraction;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Memory;
using MultiSessionHost.Desktop.Persistence;
using MultiSessionHost.Desktop.Observability;
using MultiSessionHost.Desktop.Recovery;
using MultiSessionHost.Desktop.Policy;
using MultiSessionHost.Desktop.PolicyControl;
using MultiSessionHost.Desktop.Processes;
using MultiSessionHost.Desktop.Risk;
using MultiSessionHost.Desktop.Snapshots;
using MultiSessionHost.Desktop.Targets;
using MultiSessionHost.Desktop.Windows;
using MultiSessionHost.UiModel.Interfaces;
using MultiSessionHost.UiModel.Services;

namespace MultiSessionHost.Desktop.DependencyInjection;

public static class DesktopServiceCollectionExtensions
{
    public const string DesktopTargetHttpClientName = "MultiSessionHost.Desktop.Targets";

    public static IServiceCollection AddDesktopSessionServices(this IServiceCollection services)
    {
        services.AddHttpClient(
            DesktopTargetHttpClientName,
            static client =>
            {
                client.Timeout = TimeSpan.FromSeconds(5);
            });

        services.AddSingleton<IProcessLocator, Win32ProcessLocator>();
        services.AddSingleton<IWindowLocator, Win32WindowLocator>();
        services.AddSingleton<IDesktopTargetProfileCatalog, ConfiguredDesktopTargetProfileCatalog>();
        services.AddSingleton<InMemorySessionTargetBindingStore>();
        services.AddSingleton<ISessionTargetBindingStore>(static serviceProvider => serviceProvider.GetRequiredService<InMemorySessionTargetBindingStore>());
        services.AddSingleton<ISessionTargetBindingPersistence>(
            static serviceProvider =>
            {
                var options = serviceProvider.GetRequiredService<SessionHostOptions>();

                return options.BindingStorePersistenceMode switch
                {
                    BindingStorePersistenceMode.None => new NoOpSessionTargetBindingPersistence(),
                    BindingStorePersistenceMode.JsonFile => ActivatorUtilities.CreateInstance<JsonFileSessionTargetBindingPersistence>(serviceProvider),
                    _ => throw new InvalidOperationException($"BindingStorePersistenceMode '{options.BindingStorePersistenceMode}' is not supported.")
                };
            });
        services.AddSingleton<ISessionTargetBindingBootstrapper, SessionTargetBindingStoreBootstrapper>();
        services.AddSingleton<ISessionTargetBindingManager, SessionTargetBindingManager>();
        services.AddSingleton<IDesktopTargetProfileResolver, ConfiguredDesktopTargetProfileResolver>();
        services.AddSingleton<IExecutionResourceResolver, DefaultExecutionResourceResolver>();
        services.AddSingleton<IDesktopTargetMatcher, DefaultDesktopTargetMatcher>();
        services.AddSingleton<ISessionAttachmentResolver, DefaultSessionAttachmentResolver>();
        services.AddSingleton<IAttachedSessionStore, InMemoryAttachedSessionStore>();
        services.AddSingleton<ISessionAttachmentOperations, DefaultSessionAttachmentOperations>();
        services.AddSingleton<ISessionAttachmentRuntime, DefaultSessionAttachmentRuntime>();
        services.AddSingleton<ISessionRecoveryStateStore, InMemorySessionRecoveryStateStore>();
        services.AddSingleton<ISessionObservabilityStore, InMemorySessionObservabilityStore>();
        services.AddSingleton<IObservabilityRecorder, DefaultObservabilityRecorder>();
        services.AddSingleton<IUiSnapshotSerializer, JsonUiSnapshotSerializer>();
        services.AddSingleton<IUiSnapshotProvider, SelfHostedHttpUiSnapshotProvider>();
        services.AddSingleton<IUiTreeQueryService, DefaultUiTreeQueryService>();
        services.AddSingleton<IUiSemanticClassifier, DefaultUiSemanticClassifier>();
        services.AddSingleton<IUiSemanticExtractor, ListDetectorExtractor>();
        services.AddSingleton<IUiSemanticExtractor, TargetDetectorExtractor>();
        services.AddSingleton<IUiSemanticExtractor, AlertDetectorExtractor>();
        services.AddSingleton<IUiSemanticExtractor, TransitStateDetectorExtractor>();
        services.AddSingleton<IUiSemanticExtractor, ResourceCapabilityDetectorExtractor>();
        services.AddSingleton<IUiSemanticExtractor, PresenceEntityDetectorExtractor>();
        services.AddSingleton<ITargetSemanticPackage, EveLikeSemanticPackage>();
        services.AddSingleton<ITargetSemanticPackageResolver, DefaultTargetSemanticPackageResolver>();
        services.AddSingleton<IUiSemanticExtractionPipeline, UiSemanticExtractionPipeline>();
        services.AddSingleton<ISessionSemanticExtractionStore, InMemorySessionSemanticExtractionStore>();
        services.AddSingleton<IRiskRuleProvider, ConfiguredRiskRuleProvider>();
        services.AddSingleton<IRiskCandidateBuilder, DefaultRiskCandidateBuilder>();
        services.AddSingleton<IRiskClassifier, DefaultRiskClassifier>();
        services.AddSingleton<ISessionRiskAssessmentStore, InMemorySessionRiskAssessmentStore>();
        services.AddSingleton<IRiskClassificationPipeline, DefaultRiskClassificationPipeline>();
        services.AddSingleton<ISessionDomainStateProjectionService, DefaultSessionDomainStateProjectionService>();
        services.AddSingleton<IPolicyRuleProvider, ConfiguredPolicyRuleProvider>();
        services.AddSingleton<IPolicyRuleMatcher, DefaultPolicyRuleMatcher>();
        services.AddSingleton<IPolicy>(
            static serviceProvider => new AbortPolicy(
                serviceProvider.GetRequiredService<SessionHostOptions>(),
                serviceProvider.GetRequiredService<IPolicyRuleProvider>(),
                serviceProvider.GetRequiredService<IPolicyRuleMatcher>()));
        services.AddSingleton<IPolicy>(
            static serviceProvider => new ThreatResponsePolicy(
                serviceProvider.GetRequiredService<SessionHostOptions>(),
                serviceProvider.GetRequiredService<IPolicyRuleProvider>(),
                serviceProvider.GetRequiredService<IPolicyRuleMatcher>()));
        services.AddSingleton<IPolicy>(
            static serviceProvider => new TransitPolicy(
                serviceProvider.GetRequiredService<SessionHostOptions>(),
                serviceProvider.GetRequiredService<IPolicyRuleProvider>(),
                serviceProvider.GetRequiredService<IPolicyRuleMatcher>()));
        services.AddSingleton<IPolicy>(
            static serviceProvider => new ResourceUsagePolicy(
                serviceProvider.GetRequiredService<IPolicyRuleProvider>(),
                serviceProvider.GetRequiredService<IPolicyRuleMatcher>()));
        services.AddSingleton<IPolicy>(
            static serviceProvider => new TargetPrioritizationPolicy(
                serviceProvider.GetRequiredService<IPolicyRuleProvider>(),
                serviceProvider.GetRequiredService<IPolicyRuleMatcher>()));
        services.AddSingleton<IPolicy>(
            static serviceProvider => new SelectNextSitePolicy(
                serviceProvider.GetRequiredService<SessionHostOptions>(),
                serviceProvider.GetRequiredService<IPolicyRuleProvider>(),
                serviceProvider.GetRequiredService<IPolicyRuleMatcher>()));
        services.AddSingleton<IDecisionPlanAggregator, DefaultDecisionPlanAggregator>();
        services.AddSingleton<ISessionDecisionPlanStore, InMemorySessionDecisionPlanStore>();
        services.AddSingleton<ISessionPolicyControlStore, InMemorySessionPolicyControlStore>();
        services.AddSingleton<ISessionPolicyControlService, DefaultSessionPolicyControlService>();
        services.AddSingleton<IPolicyEngine, DefaultPolicyEngine>();
        services.AddSingleton<ISessionDecisionPlanExecutionStore, InMemorySessionDecisionPlanExecutionStore>();
        services.AddSingleton<ISessionControlGateway, DefaultSessionControlGateway>();
        services.AddSingleton<IDecisionDirectiveHandler, ObserveDirectiveHandler>();
        services.AddSingleton<IDecisionDirectiveHandler, NavigateDirectiveHandler>();
        services.AddSingleton<IDecisionDirectiveHandler, WaitDirectiveHandler>();
        services.AddSingleton<IDecisionDirectiveHandler, PauseActivityDirectiveHandler>();
        services.AddSingleton<IDecisionDirectiveHandler, AbortDirectiveHandler>();
        services.AddSingleton<IDecisionPlanExecutor, DefaultDecisionPlanExecutor>();
        services.AddSingleton<ISessionActivityStateStore, InMemorySessionActivityStateStore>();
        services.AddSingleton<ISessionActivityStateEvaluator, DefaultSessionActivityStateEvaluator>();
        services.AddSingleton<InMemorySessionOperationalMemoryStore>();
        services.AddSingleton<ISessionOperationalMemoryStore>(static serviceProvider => serviceProvider.GetRequiredService<InMemorySessionOperationalMemoryStore>());
        services.AddSingleton<ISessionOperationalMemoryReader>(static serviceProvider => serviceProvider.GetRequiredService<InMemorySessionOperationalMemoryStore>());
        services.AddSingleton<ISessionOperationalMemoryUpdater, DefaultSessionOperationalMemoryUpdater>();
        services.AddSingleton<IPolicyMemoryContextBuilder, DefaultPolicyMemoryContextBuilder>();
        services.AddSingleton<ITravelAutopilotActionSelector, TravelAutopilotActionSelector>();
        services.AddSingleton<ITargetBehaviorPack, EveLikeTravelAutopilotBehaviorPack>();
        services.AddSingleton<ITargetBehaviorPackResolver, DefaultTargetBehaviorPackResolver>();
        services.AddSingleton<ITargetBehaviorPackPlanner, DefaultTargetBehaviorPackPlanner>();
        services.AddSingleton<IRuntimePersistenceBackend>(
            static serviceProvider =>
            {
                var options = serviceProvider.GetRequiredService<SessionHostOptions>();

                if (!options.RuntimePersistence.EnableRuntimePersistence)
                {
                    return new NoOpRuntimePersistenceBackend();
                }

                return options.RuntimePersistence.Mode switch
                {
                    RuntimePersistenceMode.None => new NoOpRuntimePersistenceBackend(),
                    RuntimePersistenceMode.JsonFile => ActivatorUtilities.CreateInstance<JsonFileRuntimePersistenceBackend>(serviceProvider),
                    _ => throw new InvalidOperationException($"RuntimePersistence.Mode '{options.RuntimePersistence.Mode}' is not supported.")
                };
            });
        services.AddSingleton<IRuntimePersistenceCoordinator, RuntimePersistenceCoordinator>();
        services.AddSingleton<ISessionUiRefreshService, DefaultSessionUiRefreshService>();
        services.AddSingleton<SelfHostedHttpUiTreeNormalizer>();
        services.AddSingleton<TestAppUiTreeNormalizer>();
        services.AddSingleton<WindowsUiAutomationUiTreeNormalizer>();
        services.AddSingleton<IUiTreeNormalizerResolver, DefaultUiTreeNormalizerResolver>();
        services.AddSingleton<DefaultButtonWorkItemPlanner>();
        services.AddSingleton<TestAppWorkItemPlanner>();
        services.AddSingleton<IWorkItemPlannerResolver, DefaultWorkItemPlannerResolver>();
        services.AddSingleton<IUiNodeSelector, DefaultUiNodeSelector>();
        services.AddSingleton<IUiStateProjector, DefaultUiStateProjector>();
        services.AddSingleton<IUiActionResolver, DefaultUiActionResolver>();
        services.AddSingleton<MultiSessionHost.Core.Interfaces.IUiCommandExecutor, UiCommandExecutor>();
        services.AddSingleton<NativeUiAutomationIdentityBuilder>();
        services.AddSingleton<INativeUiAutomationReader, WindowsUiAutomationReader>();
        services.AddSingleton<INativeUiAutomationElementProvider, WindowsUiAutomationElementProvider>();
        services.AddSingleton<INativeUiAutomationElementLocator, NativeUiAutomationElementLocator>();
        services.AddSingleton<INativeInputFallbackExecutor, DisabledNativeInputFallbackExecutor>();
        services.AddSingleton<IDesktopTargetAdapter, SelfHostedHttpDesktopTargetAdapter>();
        services.AddSingleton<IDesktopTargetAdapter, DesktopTestAppTargetAdapter>();
        services.AddSingleton<IDesktopTargetAdapter, WindowsUiAutomationDesktopTargetAdapter>();
        services.AddSingleton<IDesktopTargetAdapterRegistry, DesktopTargetAdapterRegistry>();
        services.AddSingleton<IUiInteractionAdapter, TestDesktopAppUiInteractionAdapter>();
        services.AddSingleton<IUiInteractionAdapter, WindowsUiAutomationUiInteractionAdapter>();
        services.AddSingleton<DesktopTargetSessionDriver>();

        return services;
    }
}

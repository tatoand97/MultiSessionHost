using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MultiSessionHost.AdminDesktop.Api;
using MultiSessionHost.AdminDesktop.Services;
using MultiSessionHost.Contracts.Sessions;
using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.AdminDesktop.ViewModels;

public sealed partial class ShellViewModel : ObservableObject
{
    private readonly IAdminApiClient apiClient;
    private readonly IRefreshCoordinator refreshCoordinator;
    private CancellationTokenSource? autoRefreshCts;

    public ShellViewModel(IAdminApiClient apiClient, IRefreshCoordinator refreshCoordinator)
    {
        this.apiClient = apiClient;
        this.refreshCoordinator = refreshCoordinator;

        BaseUrl = "http://127.0.0.1:5000";
        RefreshIntervalSeconds = 5;
        SessionsView = CollectionViewSource.GetDefaultView(Sessions);
        SessionsView.Filter = FilterSession;

        SessionTabs =
        [
            new SessionOverviewViewModel(apiClient),
            new TargetBindingViewModel(apiClient),
            new UiViewModel(apiClient),
            new SemanticViewModel(apiClient),
            new RiskViewModel(apiClient),
            new DomainViewModel(apiClient),
            new ActivityViewModel(apiClient),
            new DecisionPlanViewModel(apiClient),
            new DecisionExecutionViewModel(apiClient),
            new MemoryViewModel(apiClient),
            new PersistenceViewModel(apiClient),
            new CommandsViewModel(apiClient)
        ];

        GlobalTabs =
        [
            new TargetsCatalogViewModel(apiClient),
            new BindingsCatalogViewModel(apiClient),
            new CoordinationViewModel(apiClient),
            new PolicyRulesViewModel(apiClient),
            new PolicyControlViewModel(apiClient),
            new PersistenceCatalogViewModel(apiClient)
        ];
    }

    public ObservableCollection<SessionListItemViewModel> Sessions { get; } = [];

    public ICollectionView SessionsView { get; }

    public ObservableCollection<LoadableTabViewModelBase> SessionTabs { get; }

    public ObservableCollection<LoadableTabViewModelBase> GlobalTabs { get; }

    [ObservableProperty]
    private string baseUrl = string.Empty;

    [ObservableProperty]
    private string connectionStatus = "Disconnected";

    [ObservableProperty]
    private string lastRefreshText = "Never refreshed";

    [ObservableProperty]
    private string errorText = string.Empty;

    [ObservableProperty]
    private string selectedSessionText = "No session selected";

    [ObservableProperty]
    private string sessionSearchText = string.Empty;

    [ObservableProperty]
    private string? selectedRuntimeStatus;

    [ObservableProperty]
    private string? selectedPolicyPaused;

    [ObservableProperty]
    private string? selectedActivityState;

    [ObservableProperty]
    private int refreshIntervalSeconds;

    [ObservableProperty]
    private bool isAutoRefreshEnabled;

    [ObservableProperty]
    private SessionListItemViewModel? selectedSession;

    [ObservableProperty]
    private LoadableTabViewModelBase? selectedSessionTab;

    [ObservableProperty]
    private LoadableTabViewModelBase? selectedGlobalTab;

    public IEnumerable<string> RuntimeStatusOptions { get; } = ["", "Created", "Running", "Paused", "Stopped", "Faulted"];

    public IEnumerable<string> PolicyPausedOptions { get; } = ["", "Paused", "Running"];

    public IEnumerable<string> ActivityStateOptions { get; } = ["", "Idle", "Active", "Paused", "Waiting", "Completed", "Failed", "Terminal"];

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.PropertyName is nameof(SessionSearchText) or nameof(SelectedRuntimeStatus) or nameof(SelectedPolicyPaused) or nameof(SelectedActivityState))
        {
            SessionsView.Refresh();
        }

        if (e.PropertyName is nameof(IsAutoRefreshEnabled))
        {
            _ = UpdateAutoRefreshAsync();
        }
    }

    partial void OnSelectedSessionChanged(SessionListItemViewModel? value)
    {
        SelectedSessionText = value is null
            ? "No session selected"
            : $"Selected: {value.SessionId} - {value.DisplayName}";

        _ = LoadSelectedSessionTabsAsync();
    }

    [RelayCommand]
    private async Task ConnectAsync()
    {
        try
        {
            apiClient.ConfigureBaseAddress(BaseUrl);
            ConnectionStatus = $"Connected to {apiClient.BaseAddress}";
            ErrorText = string.Empty;
            await RefreshNowAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            ConnectionStatus = "Connection failed";
            ErrorText = exception.Message;
        }
    }

    [RelayCommand]
    private async Task RefreshNowAsync()
    {
        if (apiClient.BaseAddress is null)
        {
            ConnectionStatus = "Configure a base URL first";
            return;
        }

        try
        {
            await refreshCoordinator.RunOnceAsync(RefreshAllAsync, CancellationToken.None).ConfigureAwait(true);
            LastRefreshText = $"Refreshed at {DateTimeOffset.Now:HH:mm:ss}";
            ConnectionStatus = $"Connected to {apiClient.BaseAddress}";
            ErrorText = string.Empty;
        }
        catch (AdminApiException exception)
        {
            ConnectionStatus = exception.IsUnauthorized ? "Unauthorized" : "API error";
            ErrorText = exception.Message;
        }
        catch (Exception exception)
        {
            ConnectionStatus = "Refresh failed";
            ErrorText = exception.Message;
        }
    }

    private async Task UpdateAutoRefreshAsync()
    {
        autoRefreshCts?.Cancel();
        autoRefreshCts?.Dispose();
        autoRefreshCts = null;

        if (!IsAutoRefreshEnabled || RefreshIntervalSeconds <= 0)
        {
            return;
        }

        autoRefreshCts = new CancellationTokenSource();
        var token = autoRefreshCts.Token;
        var interval = TimeSpan.FromSeconds(RefreshIntervalSeconds);
        _ = Task.Run(() => refreshCoordinator.RunPeriodicAsync(interval, RefreshAllAsync, token), token);
        await Task.CompletedTask;
    }

    private async Task RefreshAllAsync(CancellationToken cancellationToken)
    {
        var sessions = await apiClient.GetSessionsAsync(cancellationToken).ConfigureAwait(false);
        var policyStates = await apiClient.GetPolicyStatesAsync(cancellationToken).ConfigureAwait(false);

        var sessionSnapshots = new List<(SessionInfoDto Session, SessionPolicyControlStateDto? PolicyState, SessionActivitySnapshotDto? Activity, DecisionPlanSummaryDto? Plan, DecisionPlanExecutionDto? Execution, RiskAssessmentSummaryDto? Risk, SessionTargetDto? Target)>();

        foreach (var session in sessions)
        {
            var activity = await apiClient.GetActivityAsync(session.SessionId, cancellationToken).ConfigureAwait(false);
            var plan = await apiClient.GetDecisionPlanSummaryAsync(session.SessionId, cancellationToken).ConfigureAwait(false);
            var execution = await apiClient.GetDecisionExecutionAsync(session.SessionId, cancellationToken).ConfigureAwait(false);
            var risk = await apiClient.GetRiskSummaryAsync(session.SessionId, cancellationToken).ConfigureAwait(false);
            var target = await apiClient.GetTargetAsync(session.SessionId, cancellationToken).ConfigureAwait(false);
            sessionSnapshots.Add((session, policyStates.FirstOrDefault(state => string.Equals(state.SessionId, session.SessionId, StringComparison.OrdinalIgnoreCase)), activity, plan, execution, risk, target));
        }

        await Application.Current.Dispatcher.InvokeAsync(
            () =>
            {
                var existing = Sessions.ToDictionary(static item => item.SessionId, StringComparer.OrdinalIgnoreCase);
                Sessions.Clear();

                foreach (var snapshot in sessionSnapshots)
                {
                    var item = existing.TryGetValue(snapshot.Session.SessionId, out var current)
                        ? current
                        : new SessionListItemViewModel();

                    item.Apply(snapshot.Session, snapshot.PolicyState, snapshot.Activity, snapshot.Plan, snapshot.Execution, snapshot.Risk, snapshot.Target);
                    Sessions.Add(item);
                }

                SessionsView.Refresh();
            });

        await RefreshSelectedTabAsync(cancellationToken).ConfigureAwait(true);
        await RefreshGlobalTabsAsync(cancellationToken).ConfigureAwait(true);
    }

    private async Task LoadSelectedSessionTabsAsync()
    {
        if (SelectedSession is null)
        {
            return;
        }

        foreach (var tab in SessionTabs)
        {
            await tab.LoadAsync(SelectedSession.SessionId).ConfigureAwait(true);
        }

        SelectedSessionTab ??= SessionTabs.OfType<SessionOverviewViewModel>().FirstOrDefault();
    }

    private async Task RefreshSelectedTabAsync(CancellationToken cancellationToken)
    {
        if (SelectedSession is null)
        {
            return;
        }

        if (SelectedSessionTab is not null)
        {
            await Application.Current.Dispatcher.InvokeAsync(() => SelectedSessionTab.LoadAsync(SelectedSession.SessionId, cancellationToken)).Task.Unwrap();
        }
    }

    private async Task RefreshGlobalTabsAsync(CancellationToken cancellationToken)
    {
        foreach (var tab in GlobalTabs)
        {
            await Application.Current.Dispatcher.InvokeAsync(() => tab.RefreshAsync(cancellationToken)).Task.Unwrap();
        }
    }

    private bool FilterSession(object value)
    {
        if (value is not SessionListItemViewModel item)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(SessionSearchText) &&
            !item.SessionId.Contains(SessionSearchText, StringComparison.OrdinalIgnoreCase) &&
            !item.DisplayName.Contains(SessionSearchText, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(SelectedRuntimeStatus) && !string.Equals(item.RuntimeStatus, SelectedRuntimeStatus, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(SelectedPolicyPaused) && !string.Equals(item.PolicyPausedText, SelectedPolicyPaused, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(SelectedActivityState) && !string.Equals(item.ActivityState, SelectedActivityState, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }
}

public abstract partial class SessionTabViewModelBase : LoadableTabViewModelBase
{
    protected SessionTabViewModelBase(string title, IAdminApiClient apiClient)
    {
        TitleValue = title;
        ApiClient = apiClient;
    }

    protected IAdminApiClient ApiClient { get; }

    protected string TitleValue { get; }

    public override string Title => TitleValue;
}

public sealed partial class SessionOverviewViewModel : SessionTabViewModelBase
{
    public SessionOverviewViewModel(IAdminApiClient apiClient)
        : base("Overview", apiClient)
    {
    }

    [ObservableProperty]
    private string runtimeStatus = string.Empty;

    [ObservableProperty]
    private string policyState = string.Empty;

    [ObservableProperty]
    private string policyPausedText = string.Empty;

    [ObservableProperty]
    private string activityState = string.Empty;

    [ObservableProperty]
    private string planStatus = string.Empty;

    [ObservableProperty]
    private string executionStatus = string.Empty;

    public ObservableCollection<string> Highlights { get; } = [];

    public override async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (SessionId is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var session = await ApiClient.GetSessionAsync(SessionId, cancellationToken).ConfigureAwait(true);
            var policy = await ApiClient.GetSessionPolicyStateAsync(SessionId, cancellationToken).ConfigureAwait(true);
            var activity = await ApiClient.GetActivityAsync(SessionId, cancellationToken).ConfigureAwait(true);
            var plan = await ApiClient.GetDecisionPlanSummaryAsync(SessionId, cancellationToken).ConfigureAwait(true);
            var execution = await ApiClient.GetDecisionExecutionAsync(SessionId, cancellationToken).ConfigureAwait(true);

            if (session is null)
            {
                StatusMessage = "Session no longer exists.";
                return;
            }

            RuntimeStatus = session.State.CurrentStatus;
            PolicyState = policy is null ? "Unknown" : policy.Reason ?? "Policy state loaded";
            PolicyPausedText = policy?.IsPolicyPaused == true ? "Paused" : "Running";
            ActivityState = activity?.CurrentState ?? string.Empty;
            PlanStatus = plan?.PlanStatus ?? string.Empty;
            ExecutionStatus = execution?.ExecutionStatus ?? string.Empty;
            Highlights.Clear();
            Highlights.Add($"Display name: {session.DisplayName}");
            Highlights.Add($"Runtime desired: {session.State.DesiredStatus}");
            Highlights.Add($"Plan directives: {plan?.DirectiveCount ?? 0}");
            Highlights.Add($"Execution status: {execution?.ExecutionStatus ?? "Unavailable"}");
            StatusMessage = $"Loaded overview for {SessionId}.";
            ErrorText = string.Empty;
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task StartSessionAsync()
    {
        if (SessionId is null)
        {
            return;
        }

        try
        {
            await ApiClient.StartSessionAsync(SessionId, new StartSessionRequest("desktop console"), CancellationToken.None).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
    }

    [RelayCommand]
    private async Task StopSessionAsync()
    {
        if (SessionId is null)
        {
            return;
        }

        try
        {
            await ApiClient.StopSessionAsync(SessionId, new StopSessionRequest("desktop console"), CancellationToken.None).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
    }

    [RelayCommand]
    private async Task PauseSessionAsync()
    {
        if (SessionId is null)
        {
            return;
        }

        try
        {
            await ApiClient.PauseSessionAsync(SessionId, new PauseSessionRequest("desktop console"), CancellationToken.None).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
    }

    [RelayCommand]
    private async Task ResumeSessionAsync()
    {
        if (SessionId is null)
        {
            return;
        }

        try
        {
            await ApiClient.ResumeSessionAsync(SessionId, new ResumeSessionRequest("desktop console"), CancellationToken.None).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
    }

    [RelayCommand]
    private async Task PausePolicyAsync()
    {
        if (SessionId is null)
        {
            return;
        }

        try
        {
            await ApiClient.PausePolicyAsync(SessionId, cancellationToken: CancellationToken.None).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
    }

    [RelayCommand]
    private async Task ResumePolicyAsync()
    {
        if (SessionId is null)
        {
            return;
        }

        try
        {
            await ApiClient.ResumePolicyAsync(SessionId, cancellationToken: CancellationToken.None).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
    }

    [RelayCommand]
    private async Task RefreshUiAsync()
    {
        if (SessionId is null)
        {
            return;
        }

        try
        {
            await ApiClient.RefreshSessionUiAsync(SessionId, CancellationToken.None).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
    }

    [RelayCommand]
    private async Task EvaluatePlanAsync()
    {
        if (SessionId is null)
        {
            return;
        }

        try
        {
            await ApiClient.EvaluateDecisionPlanAsync(SessionId, CancellationToken.None).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
    }

    [RelayCommand]
    private async Task ExecutePlanAsync()
    {
        if (SessionId is null)
        {
            return;
        }

        try
        {
            await ApiClient.ExecuteDecisionPlanAsync(SessionId, CancellationToken.None).ConfigureAwait(true);
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
    }
}

public sealed partial class TargetBindingViewModel : SessionTabViewModelBase
{
    public TargetBindingViewModel(IAdminApiClient apiClient)
        : base("Target & Binding", apiClient)
    {
    }

    [ObservableProperty]
    private string targetProfileName = string.Empty;

    [ObservableProperty]
    private string resolvedTargetText = string.Empty;

    [ObservableProperty]
    private string overridesJson = string.Empty;

    public ObservableCollection<KeyValueViewModel> Variables { get; } = [];

    public ObservableCollection<KeyValueViewModel> Metadata { get; } = [];

    public override async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (SessionId is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var target = await ApiClient.GetTargetAsync(SessionId, cancellationToken).ConfigureAwait(true);
            var binding = await ApiClient.GetBindingAsync(SessionId, cancellationToken).ConfigureAwait(true);

            TargetProfileName = binding?.TargetProfileName ?? target?.Binding.TargetProfileName ?? string.Empty;
            Variables.Clear();
            Metadata.Clear();

            foreach (var pair in binding?.Variables ?? target?.Binding.Variables ?? new Dictionary<string, string>())
            {
                Variables.Add(new KeyValueViewModel(pair.Key, pair.Value));
            }

            if (binding?.Overrides is not null)
            {
                OverridesJson = FormatDto(binding.Overrides);
                foreach (var pair in binding.Overrides.Metadata)
                {
                    Metadata.Add(new KeyValueViewModel(pair.Key, pair.Value));
                }
            }
            else
            {
                OverridesJson = string.Empty;
            }

            ResolvedTargetText = FormatDto(target);
            StatusMessage = $"Loaded binding for {SessionId}.";
            ErrorText = string.Empty;
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void AddVariable()
    {
        Variables.Add(new KeyValueViewModel());
    }

    [RelayCommand]
    private void RemoveVariable(KeyValueViewModel? variable)
    {
        if (variable is not null)
        {
            Variables.Remove(variable);
        }
    }

    [RelayCommand]
    private async Task SaveBindingAsync()
    {
        if (SessionId is null)
        {
            return;
        }

        try
        {
            var variableDictionary = Variables
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Key))
                .ToDictionary(entry => entry.Key!.Trim(), entry => entry.Value ?? string.Empty, StringComparer.Ordinal);

            DesktopTargetProfileOverrideDto? overrides = null;
            if (!string.IsNullOrWhiteSpace(OverridesJson))
            {
                overrides = JsonSerializer.Deserialize<DesktopTargetProfileOverrideDto>(OverridesJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
            }

            var saved = await ApiClient.SaveBindingAsync(SessionId, new SessionTargetBindingUpsertRequest(TargetProfileName, variableDictionary, overrides), CancellationToken.None).ConfigureAwait(true);
            StatusMessage = $"Saved binding for {saved.SessionId}.";
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
    }

    [RelayCommand]
    private async Task DeleteBindingAsync()
    {
        if (SessionId is null)
        {
            return;
        }

        try
        {
            await ApiClient.DeleteBindingAsync(SessionId, CancellationToken.None).ConfigureAwait(true);
            StatusMessage = $"Deleted binding for {SessionId}.";
            await RefreshAsync().ConfigureAwait(true);
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
    }
}

public sealed partial class UiViewModel : SessionTabViewModelBase
{
    public UiViewModel(IAdminApiClient apiClient)
        : base("UI", apiClient)
    {
    }

    public ObservableCollection<UiTreeNodeViewModel> TreeNodes { get; } = [];

    public ObservableCollection<string> PlannedWorkItems { get; } = [];

    [ObservableProperty]
    private string rawJson = string.Empty;

    [ObservableProperty]
    private string metadataText = string.Empty;

    public override async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (SessionId is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var ui = await ApiClient.GetSessionUiAsync(SessionId, cancellationToken).ConfigureAwait(true);
            var raw = await ApiClient.GetSessionUiRawAsync(SessionId, cancellationToken).ConfigureAwait(true);

            TreeNodes.Clear();
            PlannedWorkItems.Clear();

            if (ui?.Tree is not null)
            {
                TreeNodes.Add(ToNode(ui.Tree.Root));
                MetadataText = FormatDto(ui.Tree.Metadata);
                foreach (var workItem in ui.PlannedWorkItems)
                {
                    PlannedWorkItems.Add($"{workItem.Kind}: {workItem.Description}");
                }
            }
            else
            {
                MetadataText = string.Empty;
            }

            RawJson = raw?.RawSnapshot?.GetRawText() ?? string.Empty;
            StatusMessage = $"Loaded UI for {SessionId}.";
            ErrorText = string.Empty;
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static UiTreeNodeViewModel ToNode(UiNode node)
    {
        var viewModel = new UiTreeNodeViewModel(node.Name ?? node.Id.Value, node.Role);
        foreach (var child in node.Children)
        {
            viewModel.Children.Add(ToNode(child));
        }

        return viewModel;
    }
}

public sealed partial class SemanticViewModel : SessionTabViewModelBase
{
    public SemanticViewModel(IAdminApiClient apiClient)
        : base("Semantic", apiClient)
    {
    }

    [ObservableProperty]
    private string summaryText = string.Empty;

    public ObservableCollection<string> Lists { get; } = [];

    public ObservableCollection<string> Alerts { get; } = [];

    public ObservableCollection<string> Targets { get; } = [];

    public ObservableCollection<string> Resources { get; } = [];

    public ObservableCollection<string> Capabilities { get; } = [];

    public ObservableCollection<string> PresenceEntities { get; } = [];

    public override async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (SessionId is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var semantic = await ApiClient.GetSemanticAsync(SessionId, cancellationToken).ConfigureAwait(true);
            var summary = await ApiClient.GetSemanticSummaryAsync(SessionId, cancellationToken).ConfigureAwait(true);

            SummaryText = summary is null ? string.Empty : FormatDto(summary);
            Lists.Clear();
            Alerts.Clear();
            Targets.Clear();
            Resources.Clear();
            Capabilities.Clear();
            PresenceEntities.Clear();

            if (semantic is not null)
            {
                foreach (var item in semantic.Lists) Lists.Add($"{item.Kind} {item.NodeId} {item.Label}");
                foreach (var item in semantic.Alerts) Alerts.Add($"{item.Severity} {item.Message}");
                foreach (var item in semantic.Targets) Targets.Add($"{item.Kind} {item.NodeId} {item.Label}");
                foreach (var item in semantic.Resources) Resources.Add($"{item.Kind} {item.NodeId} {item.Name}");
                foreach (var item in semantic.Capabilities) Capabilities.Add($"{item.Name} {item.Status}");
                foreach (var item in semantic.PresenceEntities) PresenceEntities.Add($"{item.Kind} {item.NodeId} {item.Label}");
            }

            StatusMessage = $"Loaded semantic state for {SessionId}.";
            ErrorText = string.Empty;
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }
}

public sealed partial class RiskViewModel : SessionTabViewModelBase
{
    public RiskViewModel(IAdminApiClient apiClient)
        : base("Risk", apiClient)
    {
    }

    [ObservableProperty]
    private string summaryText = string.Empty;

    public ObservableCollection<string> Threats { get; } = [];

    public override async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (SessionId is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            var risk = await ApiClient.GetRiskAsync(SessionId, cancellationToken).ConfigureAwait(true);
            var summary = await ApiClient.GetRiskSummaryAsync(SessionId, cancellationToken).ConfigureAwait(true);
            var entities = await ApiClient.GetRiskThreatsAsync(SessionId, cancellationToken).ConfigureAwait(true);

            SummaryText = summary is null ? string.Empty : FormatDto(summary);
            Threats.Clear();
            foreach (var threat in entities)
            {
                Threats.Add($"{threat.Name} | {threat.Severity} | {threat.SuggestedPolicy}");
            }

            if (risk is not null)
            {
                SummaryText = string.Join(Environment.NewLine, SummaryText, FormatDto(risk.Summary));
            }

            StatusMessage = $"Loaded risk state for {SessionId}.";
            ErrorText = string.Empty;
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
        finally
        {
            IsBusy = false;
        }
    }
}

public sealed partial class DomainViewModel : SessionTabViewModelBase
{
    public DomainViewModel(IAdminApiClient apiClient)
        : base("Domain", apiClient)
    {
    }

    [ObservableProperty]
    private string rawText = string.Empty;

    public override async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (SessionId is null)
        {
            return;
        }

        try
        {
            RawText = FormatDto(await ApiClient.GetDomainAsync(SessionId, cancellationToken).ConfigureAwait(true));
            ErrorText = string.Empty;
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
    }
}

public sealed partial class ActivityViewModel : SessionTabViewModelBase
{
    public ActivityViewModel(IAdminApiClient apiClient)
        : base("Activity", apiClient)
    {
    }

    [ObservableProperty]
    private string rawText = string.Empty;

    public override async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (SessionId is null)
        {
            return;
        }

        try
        {
            RawText = FormatDto(await ApiClient.GetActivityAsync(SessionId, cancellationToken).ConfigureAwait(true));
            ErrorText = string.Empty;
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
    }
}

public sealed partial class DecisionPlanViewModel : SessionTabViewModelBase
{
    public DecisionPlanViewModel(IAdminApiClient apiClient)
        : base("Decision Plan", apiClient)
    {
    }

    [ObservableProperty]
    private string rawText = string.Empty;

    public override async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (SessionId is null)
        {
            return;
        }

        try
        {
            RawText = FormatDto(await ApiClient.GetDecisionPlanExplanationAsync(SessionId, cancellationToken).ConfigureAwait(true));
            ErrorText = string.Empty;
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
    }
}

public sealed partial class DecisionExecutionViewModel : SessionTabViewModelBase
{
    public DecisionExecutionViewModel(IAdminApiClient apiClient)
        : base("Decision Execution", apiClient)
    {
    }

    [ObservableProperty]
    private string rawText = string.Empty;

    public override async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (SessionId is null)
        {
            return;
        }

        try
        {
            RawText = FormatDto(await ApiClient.GetDecisionExecutionAsync(SessionId, cancellationToken).ConfigureAwait(true));
            ErrorText = string.Empty;
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
    }
}

public sealed partial class MemoryViewModel : SessionTabViewModelBase
{
    public MemoryViewModel(IAdminApiClient apiClient)
        : base("Memory", apiClient)
    {
    }

    [ObservableProperty]
    private string rawText = string.Empty;

    public override async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (SessionId is null)
        {
            return;
        }

        try
        {
            RawText = FormatDto(await ApiClient.GetMemoryAsync(SessionId, cancellationToken).ConfigureAwait(true));
            ErrorText = string.Empty;
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
    }
}

public sealed partial class PersistenceViewModel : SessionTabViewModelBase
{
    public PersistenceViewModel(IAdminApiClient apiClient)
        : base("Persistence", apiClient)
    {
    }

    public string RawText { get; set; } = string.Empty;

    public override async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (SessionId is null)
        {
            return;
        }

        try
        {
            RawText = FormatDto(await ApiClient.GetSessionPersistenceAsync(SessionId, cancellationToken).ConfigureAwait(true));
            ErrorText = string.Empty;
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
    }
}

public sealed partial class CommandsViewModel : SessionTabViewModelBase
{
    private readonly string[] kinds = ["click", "invoke", "text", "toggle", "select"];

    public CommandsViewModel(IAdminApiClient apiClient)
        : base("Commands", apiClient)
    {
        SelectedKind = kinds[0];
    }

    public IEnumerable<string> Kinds => kinds;

    [ObservableProperty]
    private string nodeId = string.Empty;

    [ObservableProperty]
    private string selectedKind = string.Empty;

    [ObservableProperty]
    private string actionName = string.Empty;

    [ObservableProperty]
    private string textValue = string.Empty;

    [ObservableProperty]
    private bool? boolValue;

    [ObservableProperty]
    private string selectedValue = string.Empty;

    [ObservableProperty]
    private string metadataText = string.Empty;

    [ObservableProperty]
    private string resultText = string.Empty;

    public override Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    [RelayCommand]
    private async Task SendCommandAsync()
    {
        if (SessionId is null)
        {
            return;
        }

        try
        {
            var metadata = ParseMetadata(MetadataText);
            var request = new UiCommandRequest(
                string.IsNullOrWhiteSpace(NodeId) ? null : NodeId,
                SelectedKind,
                string.IsNullOrWhiteSpace(ActionName) ? null : ActionName,
                string.IsNullOrWhiteSpace(TextValue) ? null : TextValue,
                BoolValue,
                string.IsNullOrWhiteSpace(SelectedValue) ? null : SelectedValue,
                metadata.Count == 0 ? null : metadata);
            var result = await ApiClient.SendCommandAsync(SessionId, request, CancellationToken.None).ConfigureAwait(true);
            ResultText = FormatDto(result);
            ErrorText = string.Empty;
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
    }

    private static Dictionary<string, string?> ParseMetadata(string rawText)
    {
        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var line in rawText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var index = line.IndexOf('=');
            if (index <= 0)
            {
                continue;
            }

            result[line[..index].Trim()] = line[(index + 1)..].Trim();
        }

        return result;
    }
}

public sealed partial class TargetsCatalogViewModel : LoadableTabViewModelBase
{
    private readonly IAdminApiClient apiClient;

    public TargetsCatalogViewModel(IAdminApiClient apiClient)
    {
        this.apiClient = apiClient;
    }

    public override string Title => "Targets";

    [ObservableProperty]
    private string rawText = string.Empty;

    public override async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            RawText = FormatDto(await apiClient.GetTargetsAsync(cancellationToken).ConfigureAwait(true));
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
    }
}

public sealed partial class BindingsCatalogViewModel : LoadableTabViewModelBase
{
    private readonly IAdminApiClient apiClient;

    public BindingsCatalogViewModel(IAdminApiClient apiClient)
    {
        this.apiClient = apiClient;
    }

    public override string Title => "Bindings";

    [ObservableProperty]
    private string rawText = string.Empty;

    public override async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            RawText = FormatDto(await apiClient.GetBindingsAsync(cancellationToken).ConfigureAwait(true));
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
    }
}

public sealed partial class CoordinationViewModel : LoadableTabViewModelBase
{
    private readonly IAdminApiClient apiClient;

    public CoordinationViewModel(IAdminApiClient apiClient)
    {
        this.apiClient = apiClient;
    }

    public override string Title => "Coordination";

    [ObservableProperty]
    private string rawText = string.Empty;

    public override async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            RawText = FormatDto(await apiClient.GetCoordinationAsync(cancellationToken).ConfigureAwait(true));
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
    }
}

public sealed partial class PolicyRulesViewModel : LoadableTabViewModelBase
{
    private readonly IAdminApiClient apiClient;

    public PolicyRulesViewModel(IAdminApiClient apiClient)
    {
        this.apiClient = apiClient;
    }

    public override string Title => "Policy Rules";

    [ObservableProperty]
    private string rawText = string.Empty;

    public override async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            RawText = FormatDto(await apiClient.GetPolicyRulesAsync(cancellationToken).ConfigureAwait(true));
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
    }
}

public sealed partial class PolicyControlViewModel : LoadableTabViewModelBase
{
    private readonly IAdminApiClient apiClient;

    public PolicyControlViewModel(IAdminApiClient apiClient)
    {
        this.apiClient = apiClient;
    }

    public override string Title => "Policy Control";

    [ObservableProperty]
    private string rawText = string.Empty;

    public override async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            RawText = FormatDto(await apiClient.GetPolicyStatesAsync(cancellationToken).ConfigureAwait(true));
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
    }
}

public sealed partial class PersistenceCatalogViewModel : LoadableTabViewModelBase
{
    private readonly IAdminApiClient apiClient;

    public PersistenceCatalogViewModel(IAdminApiClient apiClient)
    {
        this.apiClient = apiClient;
    }

    public override string Title => "Persistence Global";

    [ObservableProperty]
    private string rawText = string.Empty;

    public override async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            RawText = FormatDto(await apiClient.GetPersistenceAsync(cancellationToken).ConfigureAwait(true));
        }
        catch (Exception exception)
        {
            SetError(exception);
        }
    }
}

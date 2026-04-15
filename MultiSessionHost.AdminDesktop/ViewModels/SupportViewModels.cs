using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using MultiSessionHost.Contracts.Sessions;

namespace MultiSessionHost.AdminDesktop.ViewModels;

public sealed partial class KeyValueViewModel : ObservableObject
{
    [ObservableProperty]
    private string? key;

    [ObservableProperty]
    private string? value;

    public KeyValueViewModel()
    {
    }

    public KeyValueViewModel(string? key, string? value)
    {
        this.key = key;
        this.value = value;
    }
}

public sealed partial class UiTreeNodeViewModel : ObservableObject
{
    public UiTreeNodeViewModel(string label, string role)
    {
        Label = label;
        Role = role;
    }

    public string Label { get; }

    public string Role { get; }

    public ObservableCollection<UiTreeNodeViewModel> Children { get; } = [];
}

public abstract partial class LoadableTabViewModelBase : ObservableObject
{
    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string? statusMessage;

    [ObservableProperty]
    private string? errorText;

    [ObservableProperty]
    private string? sessionId;

    public abstract string Title { get; }

    public virtual Task LoadAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        SessionId = sessionId;
        return RefreshAsync(cancellationToken);
    }

    public abstract Task RefreshAsync(CancellationToken cancellationToken = default);

    protected void SetError(Exception exception)
    {
        ErrorText = exception.Message;
        StatusMessage = exception.Message;
    }

    protected static string FormatDto(object? value) =>
        value is null
            ? string.Empty
            : JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
}

public sealed class SessionListItemViewModel : ObservableObject
{
    private string sessionId = string.Empty;
    private string displayName = string.Empty;
    private string runtimeStatus = string.Empty;
    private string policyPausedText = string.Empty;
    private string activityState = string.Empty;
    private string planStatus = string.Empty;
    private string executionStatus = string.Empty;
    private string threatSeverity = string.Empty;
    private string targetProfile = string.Empty;
    private string resolvedTargetSummary = string.Empty;
    private DateTimeOffset? lastUpdatedAtUtc;

    public string SessionId
    {
        get => sessionId;
        set => SetProperty(ref sessionId, value);
    }

    public string DisplayName
    {
        get => displayName;
        set => SetProperty(ref displayName, value);
    }

    public string RuntimeStatus
    {
        get => runtimeStatus;
        set => SetProperty(ref runtimeStatus, value);
    }

    public string PolicyPausedText
    {
        get => policyPausedText;
        set => SetProperty(ref policyPausedText, value);
    }

    public string ActivityState
    {
        get => activityState;
        set => SetProperty(ref activityState, value);
    }

    public string PlanStatus
    {
        get => planStatus;
        set => SetProperty(ref planStatus, value);
    }

    public string ExecutionStatus
    {
        get => executionStatus;
        set => SetProperty(ref executionStatus, value);
    }

    public string ThreatSeverity
    {
        get => threatSeverity;
        set => SetProperty(ref threatSeverity, value);
    }

    public string TargetProfile
    {
        get => targetProfile;
        set => SetProperty(ref targetProfile, value);
    }

    public string ResolvedTargetSummary
    {
        get => resolvedTargetSummary;
        set => SetProperty(ref resolvedTargetSummary, value);
    }

    public DateTimeOffset? LastUpdatedAtUtc
    {
        get => lastUpdatedAtUtc;
        set => SetProperty(ref lastUpdatedAtUtc, value);
    }

    public void Apply(
        SessionInfoDto session,
        SessionPolicyControlStateDto? policyState,
        SessionActivitySnapshotDto? activity,
        DecisionPlanSummaryDto? plan,
        DecisionPlanExecutionDto? execution,
        RiskAssessmentSummaryDto? risk,
        SessionTargetDto? target)
    {
        SessionId = session.SessionId;
        DisplayName = session.DisplayName;
        RuntimeStatus = session.State.CurrentStatus;
        PolicyPausedText = policyState is not null && policyState.IsPolicyPaused ? "Paused" : "Running";
        ActivityState = activity?.CurrentState ?? string.Empty;
        PlanStatus = plan?.PlanStatus ?? string.Empty;
        ExecutionStatus = execution?.ExecutionStatus ?? string.Empty;
        ThreatSeverity = risk?.HighestSeverity ?? string.Empty;
        TargetProfile = target?.Binding.TargetProfileName ?? string.Empty;
        ResolvedTargetSummary = target is null
            ? string.Empty
            : $"{target.Target.ProfileName} | {target.Target.ProcessName} | {target.Target.MatchingMode}";
        LastUpdatedAtUtc = execution?.ExecutedAtUtc
            ?? plan?.PlannedAtUtc
            ?? activity?.LastTransitionAtUtc
            ?? policyState?.LastChangedAtUtc
            ?? session.State.LastHeartbeatUtc;
    }
}

public sealed class NavigationItemViewModel : ObservableObject
{
    public NavigationItemViewModel(string title, object viewModel)
    {
        Title = title;
        ViewModel = viewModel;
    }

    public string Title { get; }

    public object ViewModel { get; }
}

public abstract class DetailViewModelBase : LoadableTabViewModelBase
{
    protected DetailViewModelBase(string title)
    {
        TitleValue = title;
    }

    protected string TitleValue { get; }

    public override string Title => TitleValue;
}

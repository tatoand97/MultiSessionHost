using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Enums;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Desktop.Interfaces;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.Desktop.Targets;

public sealed class DefaultExecutionResourceResolver : IExecutionResourceResolver
{
    private const string GlobalTargetOperationsKey = "global:target-facing-operations";
    private readonly SessionHostOptions _options;
    private readonly IClock _clock;

    public DefaultExecutionResourceResolver(SessionHostOptions options, IClock clock)
    {
        _options = options;
        _clock = clock;
    }

    public ExecutionTargetIdentity CreateTargetIdentity(ResolvedDesktopTargetContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return CreateTargetIdentity(context.Target);
    }

    public ExecutionTargetIdentity CreateTargetIdentity(DesktopSessionAttachment attachment)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        return CreateTargetIdentity(attachment.Target);
    }

    public ExecutionRequest CreateForWorkItem(
        SessionSnapshot snapshot,
        ResolvedDesktopTargetContext context,
        SessionWorkItem workItem)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(workItem);

        var operationKind = workItem.Kind is SessionWorkItemKind.FetchUiSnapshot or SessionWorkItemKind.ProjectUiState
            ? ExecutionOperationKind.UiRefresh
            : ExecutionOperationKind.WorkItem;

        return CreateRequest(
            snapshot.SessionId,
            context,
            operationKind,
            workItem.Kind,
            uiCommandKind: null,
            workItem.Reason);
    }

    public ExecutionRequest CreateForUiCommand(
        SessionSnapshot snapshot,
        ResolvedDesktopTargetContext context,
        UiCommand command)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(command);

        var operationKind = command.Kind == UiCommandKind.RefreshUi
            ? ExecutionOperationKind.UiRefresh
            : ExecutionOperationKind.UiCommand;
        var description = command.NodeId is null
            ? $"UI command '{command.Kind}'."
            : $"UI command '{command.Kind}' for node '{command.NodeId}'.";

        return CreateRequest(
            snapshot.SessionId,
            context,
            operationKind,
            workItemKind: null,
            command.Kind,
            description);
    }

    public ExecutionRequest CreateForAttachmentEnsure(
        SessionSnapshot snapshot,
        ResolvedDesktopTargetContext context)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(context);

        return CreateRequest(
            snapshot.SessionId,
            context,
            ExecutionOperationKind.AttachmentEnsure,
            workItemKind: null,
            uiCommandKind: null,
            $"Ensure attachment for session '{snapshot.SessionId}'.");
    }

    public ExecutionRequest CreateForAttachmentInvalidate(
        SessionId sessionId,
        DesktopSessionAttachment attachment,
        string? description = null)
    {
        ArgumentNullException.ThrowIfNull(attachment);

        var resourceSet = CreateResourceSet(
            sessionId,
            CreateTargetIdentity(attachment).CanonicalKey,
            ExecutionOperationKind.AttachmentInvalidate);

        return new ExecutionRequest(
            Guid.NewGuid(),
            sessionId,
            ExecutionOperationKind.AttachmentInvalidate,
            workItemKind: null,
            uiCommandKind: null,
            _clock.UtcNow,
            resourceSet,
            description ?? $"Invalidate attachment for session '{sessionId}'.");
    }

    private ExecutionRequest CreateRequest(
        SessionId sessionId,
        ResolvedDesktopTargetContext context,
        ExecutionOperationKind operationKind,
        SessionWorkItemKind? workItemKind,
        UiCommandKind? uiCommandKind,
        string? description)
    {
        var targetIdentity = CreateTargetIdentity(context);

        return new ExecutionRequest(
            Guid.NewGuid(),
            sessionId,
            operationKind,
            workItemKind,
            uiCommandKind,
            _clock.UtcNow,
            CreateResourceSet(sessionId, targetIdentity.CanonicalKey, operationKind),
            description);
    }

    private ExecutionResourceSet CreateResourceSet(
        SessionId sessionId,
        string targetKey,
        ExecutionOperationKind operationKind)
    {
        var coordinationOptions = _options.ExecutionCoordination;
        var globalResourceKey = coordinationOptions.EnableGlobalCoordination &&
            coordinationOptions.GlobalExclusiveOperationKinds.Contains(operationKind)
                ? ExecutionResourceKey.ForGlobal(GlobalTargetOperationsKey)
                : null;

        return new ExecutionResourceSet(
            ExecutionResourceKey.ForSession(sessionId),
            ExecutionResourceKey.ForTarget(targetKey),
            globalResourceKey,
            TimeSpan.FromMilliseconds(coordinationOptions.DefaultTargetCooldownMs));
    }

    private static ExecutionTargetIdentity CreateTargetIdentity(DesktopSessionTarget target)
    {
        var baseAddress = target.BaseAddress?.AbsoluteUri;
        var identity = new ExecutionTargetIdentity(
            target.Kind,
            Normalize(target.ProfileName),
            Normalize(target.ProcessName),
            NormalizeNullable(baseAddress),
            NormalizeNullable(target.WindowTitleFragment),
            NormalizeNullable(target.CommandLineFragment),
            CanonicalKey: string.Empty);

        return identity with { CanonicalKey = BuildCanonicalKey(identity) };
    }

    private static string BuildCanonicalKey(ExecutionTargetIdentity identity)
    {
        var parts = new Dictionary<string, string?>
        {
            ["kind"] = identity.Kind.ToString(),
            ["profile"] = identity.ProfileName,
            ["process"] = identity.ProcessName,
            ["base"] = identity.BaseAddress,
            ["window"] = identity.WindowTitleFragment,
            ["cmd"] = identity.CommandLineFragment
        };

        return "target:" + string.Join(
            ";",
            parts.Select(static pair => $"{pair.Key}={Uri.EscapeDataString(pair.Value ?? string.Empty)}"));
    }

    private static string Normalize(string value) => value.Trim();

    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

using MultiSessionHost.UiModel.Models;

namespace MultiSessionHost.Desktop.Extraction;

public sealed class DefaultUiSemanticClassifier : IUiSemanticClassifier
{
    public UiSemanticClassification<ListKind> ClassifyList(UiNode node, IUiTreeQueryService query)
    {
        var roleFamilies = query.DeriveRoleFamilies(node);
        var itemCount = ParseInt(query.GetAttribute(node, "itemCount"));
        var label = CombinedText(node, query);

        if (roleFamilies.Contains("list") || itemCount > 0)
        {
            return new UiSemanticClassification<ListKind>(
                InferListKind(label),
                itemCount > 0 ? DetectionConfidence.High : DetectionConfidence.Medium,
                "Role or item metadata indicates a list-like node.");
        }

        if (node.Children.Count >= 3 && node.Children.Select(static child => child.Role).Distinct(StringComparer.OrdinalIgnoreCase).Count() <= 2)
        {
            return new UiSemanticClassification<ListKind>(
                InferListKind(label),
                DetectionConfidence.Low,
                "Repeated child shape suggests a list-like container.");
        }

        return new UiSemanticClassification<ListKind>(ListKind.Unknown, DetectionConfidence.Unknown, null);
    }

    public UiSemanticClassification<TargetKind> ClassifyTarget(UiNode node, IUiTreeQueryService query)
    {
        var text = CombinedText(node, query);
        var focused = IsTrue(query.GetAttribute(node, "focused"));
        var active = IsTrue(query.GetAttribute(node, "active")) || ContainsAny(text, "active", "current");

        if (ContainsAny(text, "selected: none", "selected none", "target: none", "target none"))
        {
            return new UiSemanticClassification<TargetKind>(TargetKind.Unknown, DetectionConfidence.Unknown, null);
        }

        if (node.Selected)
        {
            return new UiSemanticClassification<TargetKind>(TargetKind.SelectedItem, DetectionConfidence.High, "Node is selected.");
        }

        if (active)
        {
            return new UiSemanticClassification<TargetKind>(TargetKind.ActiveItem, DetectionConfidence.Medium, "Node text or attributes indicate active state.");
        }

        if (focused)
        {
            return new UiSemanticClassification<TargetKind>(TargetKind.FocusedElement, DetectionConfidence.Medium, "Node is focused.");
        }

        if (ContainsAny(text, "selected", "target", "current"))
        {
            return new UiSemanticClassification<TargetKind>(TargetKind.ActionTarget, DetectionConfidence.Low, "Node text hints at target-like meaning.");
        }

        return new UiSemanticClassification<TargetKind>(TargetKind.Unknown, DetectionConfidence.Unknown, null);
    }

    public UiSemanticClassification<AlertSeverity> ClassifyAlert(UiNode node, IUiTreeQueryService query)
    {
        var text = CombinedText(node, query);
        var role = node.Role;
        var severityAttribute = query.GetAttribute(node, "severity");

        if (!ContainsAny(role, "alert", "status", "banner") &&
            !ContainsAny(text, "alert", "warning", "error", "critical", "blocked", "failed") &&
            string.IsNullOrWhiteSpace(severityAttribute))
        {
            return new UiSemanticClassification<AlertSeverity>(AlertSeverity.Unknown, DetectionConfidence.Unknown, null);
        }

        var severity = InferAlertSeverity($"{severityAttribute} {text}");
        var confidence = ContainsAny(role, "alert", "banner") || !string.IsNullOrWhiteSpace(severityAttribute)
            ? DetectionConfidence.High
            : DetectionConfidence.Medium;

        return new UiSemanticClassification<AlertSeverity>(severity, confidence, "Role, severity, or text indicates alert-like node.");
    }

    public UiSemanticClassification<TransitStatus> ClassifyTransit(UiNode node, IUiTreeQueryService query)
    {
        var text = CombinedText(node, query);
        var role = node.Role;

        if (ContainsAny(role, "progress") || query.GetAttribute(node, "progressPercent") is not null || query.GetAttribute(node, "valuePercent") is not null)
        {
            return new UiSemanticClassification<TransitStatus>(TransitStatus.InProgress, DetectionConfidence.High, "Progress role or value metadata is present.");
        }

        if (ContainsAny(text, "progress", "loading", "running", "pending", "transition", "busy"))
        {
            return new UiSemanticClassification<TransitStatus>(TransitStatus.InProgress, DetectionConfidence.Medium, "Text indicates transition.");
        }

        if (ContainsAny(text, "blocked", "waiting"))
        {
            return new UiSemanticClassification<TransitStatus>(TransitStatus.Blocked, DetectionConfidence.Medium, "Text indicates blocked transition.");
        }

        return new UiSemanticClassification<TransitStatus>(TransitStatus.Unknown, DetectionConfidence.Unknown, null);
    }

    public UiSemanticClassification<ResourceKind> ClassifyResource(UiNode node, IUiTreeQueryService query)
    {
        var text = CombinedText(node, query);
        var hasValue = query.GetAttribute(node, "value") is not null ||
            query.GetAttribute(node, "valuePercent") is not null ||
            query.GetAttribute(node, "progressPercent") is not null;

        if (!hasValue && !ContainsAny(text, "resource", "health", "capacity", "energy", "charge", "percent"))
        {
            return new UiSemanticClassification<ResourceKind>(ResourceKind.Unknown, DetectionConfidence.Unknown, null);
        }

        return new UiSemanticClassification<ResourceKind>(
            InferResourceKind(text),
            hasValue ? DetectionConfidence.High : DetectionConfidence.Medium,
            "Value metadata or resource-like text is present.");
    }

    public UiSemanticClassification<CapabilityStatus> ClassifyCapability(UiNode node, IUiTreeQueryService query)
    {
        var text = CombinedText(node, query);
        var actions = query.GetAttribute(node, "semanticActions");
        var roleFamilies = query.DeriveRoleFamilies(node);

        if (!roleFamilies.Contains("action") &&
            string.IsNullOrWhiteSpace(actions) &&
            !ContainsAny(text, "capability", "enabled", "disabled", "active", "cooldown"))
        {
            return new UiSemanticClassification<CapabilityStatus>(CapabilityStatus.Unknown, DetectionConfidence.Unknown, null);
        }

        var status = InferCapabilityStatus(text, node.Enabled);
        var confidence = ContainsAny(text, "capability", "enabled", "disabled", "active", "cooldown")
            ? DetectionConfidence.Medium
            : DetectionConfidence.Low;

        if (!string.IsNullOrWhiteSpace(actions))
        {
            confidence = DetectionConfidence.High;
        }

        return new UiSemanticClassification<CapabilityStatus>(status, confidence, "Action metadata or capability-like text is present.");
    }

    public UiSemanticClassification<PresenceEntityKind> ClassifyPresenceEntity(UiNode node, IUiTreeQueryService query)
    {
        var text = CombinedText(node, query);

        if (ContainsAny(text, "presence", "nearby", "present", "member", "entity"))
        {
            return new UiSemanticClassification<PresenceEntityKind>(PresenceEntityKind.Group, DetectionConfidence.Medium, "Text indicates present or nearby entities.");
        }

        if (node.Children.Count > 0 && ContainsAny(text, "items", "selected"))
        {
            return new UiSemanticClassification<PresenceEntityKind>(PresenceEntityKind.Item, DetectionConfidence.Low, "Collection text may describe present items.");
        }

        return new UiSemanticClassification<PresenceEntityKind>(PresenceEntityKind.Unknown, DetectionConfidence.Unknown, null);
    }

    private static ListKind InferListKind(string text)
    {
        if (ContainsAny(text, "presence", "nearby", "present"))
        {
            return ListKind.Presence;
        }

        if (ContainsAny(text, "option", "capability"))
        {
            return ListKind.Options;
        }

        if (ContainsAny(text, "result"))
        {
            return ListKind.Results;
        }

        if (ContainsAny(text, "navigation", "route"))
        {
            return ListKind.Navigation;
        }

        return ContainsAny(text, "item") ? ListKind.Items : ListKind.Unknown;
    }

    private static AlertSeverity InferAlertSeverity(string text)
    {
        if (ContainsAny(text, "critical", "fatal"))
        {
            return AlertSeverity.Critical;
        }

        if (ContainsAny(text, "error", "failed", "failure"))
        {
            return AlertSeverity.Error;
        }

        if (ContainsAny(text, "warning", "blocked", "degraded"))
        {
            return AlertSeverity.Warning;
        }

        return ContainsAny(text, "alert", "info", "notice") ? AlertSeverity.Info : AlertSeverity.Unknown;
    }

    private static ResourceKind InferResourceKind(string text)
    {
        if (ContainsAny(text, "health"))
        {
            return ResourceKind.Health;
        }

        if (ContainsAny(text, "capacity"))
        {
            return ResourceKind.Capacity;
        }

        if (ContainsAny(text, "energy"))
        {
            return ResourceKind.Energy;
        }

        if (ContainsAny(text, "charge"))
        {
            return ResourceKind.Charge;
        }

        return ContainsAny(text, "resource") ? ResourceKind.Capacity : ResourceKind.Unknown;
    }

    private static CapabilityStatus InferCapabilityStatus(string text, bool enabled)
    {
        if (ContainsAny(text, "cooldown", "cooling"))
        {
            return CapabilityStatus.CoolingDown;
        }

        if (ContainsAny(text, "active", "running"))
        {
            return CapabilityStatus.Active;
        }

        if (!enabled || ContainsAny(text, "disabled"))
        {
            return CapabilityStatus.Disabled;
        }

        return enabled ? CapabilityStatus.Enabled : CapabilityStatus.Unknown;
    }

    private static string CombinedText(UiNode node, IUiTreeQueryService query) =>
        string.Join(' ', query.GatherTextCandidates(node));

    private static bool IsTrue(string? value) =>
        bool.TryParse(value, out var parsed) && parsed;

    private static int? ParseInt(string? value) =>
        int.TryParse(value, out var parsed) ? parsed : null;

    private static bool ContainsAny(string? value, params string[] fragments) =>
        !string.IsNullOrWhiteSpace(value) &&
        fragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase));
}

using System.Text.Json;
using MultiSessionHost.Contracts.Sessions;

namespace MultiSessionHost.AdminDesktop.ViewModels;

internal static class ViewModelExtensions
{
    public static string ToIndentedJson(this object? value) =>
        value is null
            ? string.Empty
            : JsonSerializer.Serialize(value, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });

    public static string ToDisplayText(this SessionPolicyControlStateDto? policyState) =>
        policyState is null
            ? string.Empty
            : $"Paused: {policyState.IsPolicyPaused} | Reason: {policyState.Reason} | ChangedBy: {policyState.ChangedBy}";
}

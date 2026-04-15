namespace MultiSessionHost.Desktop.Models;

public sealed record UiInvokeRequest(
    string? ActionName,
    IReadOnlyDictionary<string, string?>? Metadata);

public sealed record UiTextRequest(
    string? TextValue,
    IReadOnlyDictionary<string, string?>? Metadata);

public sealed record UiToggleRequest(
    bool? BoolValue,
    IReadOnlyDictionary<string, string?>? Metadata);

public sealed record UiSelectRequest(
    string? SelectedValue,
    IReadOnlyDictionary<string, string?>? Metadata);

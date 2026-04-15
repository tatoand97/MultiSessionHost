using System.Net.Http;
using System.Net;

namespace MultiSessionHost.AdminDesktop.Api;

public sealed class AdminApiException : Exception
{
    public AdminApiException(HttpStatusCode? statusCode, string message, string? responseText = null)
        : base(message)
    {
        StatusCode = statusCode;
        ResponseText = responseText;
    }

    public HttpStatusCode? StatusCode { get; }

    public string? ResponseText { get; }

    public bool IsUnauthorized => StatusCode == HttpStatusCode.Unauthorized;

    public bool IsNotFound => StatusCode == HttpStatusCode.NotFound;

    public bool IsConflict => StatusCode == HttpStatusCode.Conflict;

    public static async Task<AdminApiException> FromResponseAsync(HttpResponseMessage response)
    {
        var responseText = response.Content is null ? null : await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var message = BuildMessage(response.StatusCode, responseText);
        return new AdminApiException(response.StatusCode, message, responseText);
    }

    private static string BuildMessage(HttpStatusCode statusCode, string? responseText)
    {
        if (!string.IsNullOrWhiteSpace(responseText))
        {
            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(responseText);
                if (document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object)
                {
                    if (document.RootElement.TryGetProperty("error", out var errorProperty) && errorProperty.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        return errorProperty.GetString() ?? statusCode.ToString();
                    }

                    if (document.RootElement.TryGetProperty("Error", out var legacyErrorProperty) && legacyErrorProperty.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        return legacyErrorProperty.GetString() ?? statusCode.ToString();
                    }
                }
            }
            catch
            {
            }
        }

        return responseText is { Length: > 0 }
            ? $"{statusCode}: {responseText}"
            : statusCode.ToString();
    }
}

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using MultiSessionHost.Core.Constants;
using MultiSessionHost.Desktop.Models;

namespace MultiSessionHost.TestDesktopApp;

public static class Program
{
    [STAThread]
    public static async Task Main(string[] args)
    {
        var options = TestDesktopAppOptions.Parse(args);

        ApplicationConfiguration.Initialize();

        using var form = new MainForm(options);

        var builder = WebApplication.CreateSlimBuilder(args);
        builder.WebHost.UseUrls($"http://127.0.0.1:{options.Port}");
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(form);

        var app = builder.Build();
        MapEndpoints(app);

        await app.StartAsync().ConfigureAwait(false);

        try
        {
            Application.Run(form);
        }
        finally
        {
            await app.StopAsync().ConfigureAwait(false);
            await app.DisposeAsync().ConfigureAwait(false);
        }
    }

    private static void MapEndpoints(WebApplication app)
    {
        app.MapGet("/state", (MainForm form) => form.CaptureStateAsync());
        app.MapGet("/ui-snapshot", (MainForm form) => form.CaptureUiSnapshotAsync());
        app.MapPost("/ui/nodes/{nodeId}/click", async Task<IResult> (string nodeId, MainForm form) => ToResult(await form.ClickNodeAsync(nodeId).ConfigureAwait(false)));
        app.MapPost(
            "/ui/nodes/{nodeId}/invoke",
            async Task<IResult> (string nodeId, UiInvokeRequest? request, MainForm form) =>
                ToResult(await form.InvokeNodeActionAsync(nodeId, request?.ActionName).ConfigureAwait(false)));
        app.MapPost(
            "/ui/nodes/{nodeId}/text",
            async Task<IResult> (string nodeId, UiTextRequest? request, MainForm form) =>
                ToResult(await form.SetNodeTextAsync(nodeId, request?.TextValue).ConfigureAwait(false)));
        app.MapPost(
            "/ui/nodes/{nodeId}/toggle",
            async Task<IResult> (string nodeId, UiToggleRequest? request, MainForm form) =>
                ToResult(await form.ToggleNodeAsync(nodeId, request?.BoolValue).ConfigureAwait(false)));
        app.MapPost(
            "/ui/nodes/{nodeId}/select",
            async Task<IResult> (string nodeId, UiSelectRequest? request, MainForm form) =>
                ToResult(await form.SelectItemAsync(nodeId, request?.SelectedValue).ConfigureAwait(false)));
        app.MapPost("/start", (MainForm form) => form.StartSessionAsync());
        app.MapPost("/pause", (MainForm form) => form.PauseSessionAsync());
        app.MapPost("/resume", (MainForm form) => form.ResumeSessionAsync());
        app.MapPost("/stop", (MainForm form) => form.StopSessionAsync());
        app.MapPost("/shutdown", async Task<IResult> (MainForm form) =>
        {
            await form.RequestShutdownAsync().ConfigureAwait(false);
            return Results.Ok();
        });
        app.MapPost("/tick", (MainForm form) => form.TickAsync());
        app.MapPost("/test/delay", (TestDelayRequest request, MainForm form) => Results.Ok(form.SetArtificialDelay(request.Milliseconds)));

        app.MapGet("/", () => Results.Ok(new { Status = "ok" }));
    }

    private static IResult ToResult(UiInteractionResult result) =>
        result.Succeeded
            ? Results.Ok(result)
            : result.FailureCode switch
            {
                UiCommandFailureCodes.NodeNotFound => Results.NotFound(result),
                UiCommandFailureCodes.InvalidCommandPayload => Results.BadRequest(result),
                _ => Results.Conflict(result)
            };

    public sealed record TestDelayRequest(int Milliseconds);
}

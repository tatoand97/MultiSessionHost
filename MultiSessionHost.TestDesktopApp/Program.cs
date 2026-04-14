using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

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
        app.MapPost("/start", (MainForm form) => form.StartSessionAsync());
        app.MapPost("/pause", (MainForm form) => form.PauseSessionAsync());
        app.MapPost("/resume", (MainForm form) => form.ResumeSessionAsync());
        app.MapPost("/stop", (MainForm form) => form.StopSessionAsync());
        app.MapPost("/tick", (MainForm form) => form.TickAsync());

        app.MapGet("/", () => Results.Ok(new { Status = "ok" }));
    }
}

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MultiSessionHost.AdminApi;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Core.Interfaces;
using MultiSessionHost.Core.Models;
using MultiSessionHost.Infrastructure.DependencyInjection;
using MultiSessionHost.Worker;

namespace MultiSessionHost.Tests.Common;

public sealed class WorkerHostHarness : IAsyncDisposable
{
    private WorkerHostHarness(IHost host, HttpClient? client, Uri? baseAddress)
    {
        Host = host;
        Client = client;
        BaseAddress = baseAddress;
    }

    public IHost Host { get; }

    public HttpClient? Client { get; }

    public Uri? BaseAddress { get; }

    public ISessionCoordinator Coordinator => Host.Services.GetRequiredService<ISessionCoordinator>();

    public ISessionRegistry Registry => Host.Services.GetRequiredService<ISessionRegistry>();

    public ISessionStateStore StateStore => Host.Services.GetRequiredService<ISessionStateStore>();

    public IWorkQueue WorkQueue => Host.Services.GetRequiredService<IWorkQueue>();

    public static async Task<WorkerHostHarness> StartAsync(SessionHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var hostBuilder = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .ConfigureLogging(
                logging =>
                {
                    logging.ClearProviders();
                    logging.SetMinimumLevel(LogLevel.None);
                })
            .ConfigureServices(
                (_, services) =>
                {
                    services.AddSingleton<IOptions<SessionHostOptions>>(Options.Create(options));
                    services.AddSingleton(static serviceProvider => serviceProvider.GetRequiredService<IOptions<SessionHostOptions>>().Value);
                    services.AddMultiSessionHostRuntime();
                    services.AddAdminApiServices();
                    services.AddHostedService<WorkerHostService>();
                });

        if (options.EnableAdminApi)
        {
            hostBuilder.ConfigureWebHostDefaults(
                webBuilder =>
                {
                    webBuilder.UseUrls(options.AdminApiUrl);
                    webBuilder.Configure(
                        app =>
                        {
                            app.UseRouting();
                            app.UseEndpoints(static endpoints => endpoints.MapAdminApiEndpoints());
                        });
                });
        }

        var host = hostBuilder.Build();

        try
        {
            await host.StartAsync().ConfigureAwait(false);
            await TestWait.UntilAsync(
                () => host.Services.GetRequiredService<ISessionCoordinator>().GetSessions().Count == options.Sessions.Count,
                TimeSpan.FromSeconds(5),
                "Worker host did not initialize the session runtime in time.").ConfigureAwait(false);

            if (!options.EnableAdminApi)
            {
                return new WorkerHostHarness(host, client: null, baseAddress: null);
            }

            var server = host.Services.GetRequiredService<IServer>();
            var addressesFeature = server.Features.Get<IServerAddressesFeature>()
                ?? throw new InvalidOperationException("The admin API server did not expose bound addresses.");
            var address = addressesFeature.Addresses.Single();
            var baseAddress = new Uri(address, UriKind.Absolute);
            var client = new HttpClient { BaseAddress = baseAddress };

            return new WorkerHostHarness(host, client, baseAddress);
        }
        catch
        {
            await host.StopAsync().ConfigureAwait(false);
            host.Dispose();
            throw;
        }
    }

    public Task<SessionRuntimeState?> GetStateAsync(string sessionId, CancellationToken cancellationToken = default) =>
        StateStore.GetAsync(new SessionId(sessionId), cancellationToken).AsTask();

    public async ValueTask DisposeAsync()
    {
        Client?.Dispose();
        await Host.StopAsync().ConfigureAwait(false);
        Host.Dispose();
    }
}

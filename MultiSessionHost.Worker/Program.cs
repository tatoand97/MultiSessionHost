using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using MultiSessionHost.Core.Configuration;
using MultiSessionHost.AdminApi;
using MultiSessionHost.Infrastructure.DependencyInjection;

namespace MultiSessionHost.Worker;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var bootstrapOptions = LoadBootstrapOptions(args);
        var hostBuilder = Host.CreateDefaultBuilder(args)
            .UseWindowsService(options => options.ServiceName = "MultiSessionHost.Worker")
            .ConfigureLogging(
                logging =>
                {
                    logging.AddSimpleConsole(
                        options =>
                        {
                            options.SingleLine = true;
                            options.TimestampFormat = "yyyy-MM-dd HH:mm:ss ";
                        });
                })
            .ConfigureServices(
                (context, services) =>
                {
                    services
                        .AddOptions<SessionHostOptions>()
                        .Bind(context.Configuration.GetSection(SessionHostOptions.SectionName))
                        .Validate(
                            static options => options.TryValidate(out _),
                            "The MultiSessionHost configuration is invalid.")
                        .ValidateOnStart();

                    services.AddSingleton(static serviceProvider => serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<SessionHostOptions>>().Value);
                    services.AddMultiSessionHostRuntime();
                    services.AddAdminApiServices();
                    services.AddHostedService<WorkerHostService>();
                });

        if (bootstrapOptions.EnableAdminApi)
        {
            hostBuilder.ConfigureWebHostDefaults(
                webBuilder =>
                {
                    webBuilder.UseUrls(bootstrapOptions.AdminApiUrl);
                    webBuilder.Configure(
                        app =>
                        {
                            app.UseRouting();
                            app.UseEndpoints(static endpoints => endpoints.MapAdminApiEndpoints());
                        });
                });
        }

        var host = hostBuilder.Build();

        await host.RunAsync().ConfigureAwait(false);
    }

    private static SessionHostOptions LoadBootstrapOptions(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        var options = new SessionHostOptions();
        builder.Configuration.GetSection(SessionHostOptions.SectionName).Bind(options);

        if (options.EnableAdminApi && !Uri.TryCreate(options.AdminApiUrl, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("AdminApiUrl must be a valid absolute URL when EnableAdminApi is true.");
        }

        return options;
    }
}

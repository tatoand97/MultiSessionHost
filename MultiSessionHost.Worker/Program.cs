using MultiSessionHost.Core.Configuration;
using MultiSessionHost.Infrastructure.DependencyInjection;

namespace MultiSessionHost.Worker;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var host = Host.CreateDefaultBuilder(args)
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
                    services.AddHostedService<WorkerHostService>();
                })
            .Build();

        await host.RunAsync().ConfigureAwait(false);
    }
}

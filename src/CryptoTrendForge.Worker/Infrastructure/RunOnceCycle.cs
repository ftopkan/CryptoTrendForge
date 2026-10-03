using CryptoTrendForge.Worker.Configuration;
using CryptoTrendForge.Worker.Services;
using Microsoft.Extensions.Options;
using Serilog;

namespace CryptoTrendForge.Worker.Infrastructure;

internal static class RunOnceCycle
{
    public static async Task ExecuteAsync(string[] args, CancellationToken cancellationToken)
    {
        var environment =
            Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? "Production";

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: false)
            .AddJsonFile($"appsettings.{environment}.local.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(logging => logging.AddSerilog(Log.Logger, dispose: false));
        WorkerServiceRegistration.AddWorkerServices(services, configuration);

        // Do not dispose the provider or the scope. HttpClient/DbContext disposal can block
        // forever after a successful scan, which keeps the cron process alive and skips the next run.
        var provider = services.BuildServiceProvider();
        ValidateOptions(provider);

        Log.Information(
            "RunOnce cycle starting. Environment={Environment} ContentRoot={ContentRoot} CurrentDirectory={CurrentDirectory}",
            environment,
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory());

        try
        {
            await provider.GetRequiredService<StartupInitializationService>().StartAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Database startup failed. Killing leftover worker processes and retrying once.");
            StuckWorkerCleanup.KillOlderSiblingWorkers(TimeSpan.FromSeconds(5));
            await provider.GetRequiredService<StartupInitializationService>().StartAsync(cancellationToken);
        }

        var scope = provider.CreateScope();
        var scoped = scope.ServiceProvider;
        await scoped.GetRequiredService<CryptoEntryScanRunner>().RunAsync(cancellationToken);
        await scoped.GetRequiredService<StockEntryScanRunner>().RunAsync(cancellationToken);
        await scoped.GetRequiredService<SignalOutcomeRunner>().RunAsync(cancellationToken);
        Log.Information("RunOnce cycle finished.");
    }

    private static void ValidateOptions(IServiceProvider provider)
    {
        Validate(provider.GetServices<IValidateOptions<BotOptions>>(), provider.GetRequiredService<IOptions<BotOptions>>().Value);
        Validate(provider.GetServices<IValidateOptions<StockOptions>>(), provider.GetRequiredService<IOptions<StockOptions>>().Value);
        Validate(provider.GetServices<IValidateOptions<BybitOptions>>(), provider.GetRequiredService<IOptions<BybitOptions>>().Value);
    }

    private static void Validate<T>(IEnumerable<IValidateOptions<T>> validators, T options)
        where T : class
    {
        foreach (var validator in validators)
        {
            var result = validator.Validate(Options.DefaultName, options);
            if (result.Failed)
            {
                throw new OptionsValidationException(typeof(T).Name, typeof(T), new[] { result.FailureMessage ?? "Invalid options." });
            }
        }
    }
}

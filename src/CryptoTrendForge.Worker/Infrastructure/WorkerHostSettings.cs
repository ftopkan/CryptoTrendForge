using Microsoft.Extensions.Configuration;

namespace CryptoTrendForge.Worker.Infrastructure;

internal static class WorkerHostSettings
{
    public static bool IsRunOnce(string baseDirectory, string[] args)
    {
        var environment =
            Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? "Production";

        var configuration = new ConfigurationBuilder()
            .SetBasePath(baseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: false)
            .AddJsonFile($"appsettings.{environment}.local.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .AddCommandLine(args)
            .Build();

        return configuration.GetValue("BotSettings:RunOnce", false);
    }
}

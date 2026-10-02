using CryptoTrendForge.Core.Domain;
using CryptoTrendForge.Worker.Configuration;
using CryptoTrendForge.Worker.Services;
using Microsoft.Extensions.Options;

namespace CryptoTrendForge.Worker.Workers;

public sealed class SignalScanWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BotOptions _botOptions;
    private readonly ILogger<SignalScanWorker> _logger;

    public SignalScanWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<BotOptions> botOptions,
        ILogger<SignalScanWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _botOptions = botOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = CandleClock.DelayUntilCandleReady(
                DateTimeOffset.UtcNow,
                TimeSpan.FromMinutes(_botOptions.EntryCandleMinutes),
                TimeSpan.FromSeconds(10));
            _logger.LogInformation("Next crypto entry scan in {DelaySeconds} seconds.", (int)delay.TotalSeconds);
            await Task.Delay(delay, stoppingToken);
            await RunScanIterationAsync(stoppingToken);
        }
    }

    private async Task RunScanIterationAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<CryptoEntryScanRunner>().RunAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Signal scan iteration failed.");
        }
    }
}

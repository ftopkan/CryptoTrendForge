using CryptoTrendForge.Worker.Services;

namespace CryptoTrendForge.Worker.Workers;

public sealed class SignalOutcomeWorker : BackgroundService
{
    private static readonly int[] SnapshotMinutes = [15, 30, 60, 240];
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SignalOutcomeWorker> _logger;

    public SignalOutcomeWorker(IServiceScopeFactory scopeFactory, ILogger<SignalOutcomeWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CaptureOutcomesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Signal outcome iteration failed.");
            }

            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }

    private async Task CaptureOutcomesAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var signalRepository = scope.ServiceProvider.GetRequiredService<SignalRepository>();
        var bybitService = scope.ServiceProvider.GetRequiredService<BybitService>();

        var activeSignals = await signalRepository.GetActiveSignalsWithCoinAsync(cancellationToken);
        foreach (var signal in activeSignals)
        {
            if (signal.Coin is null)
            {
                continue;
            }

            var elapsedMinutes = (int)Math.Floor((DateTimeOffset.UtcNow - signal.CreatedAt).TotalMinutes);
            foreach (var minuteMark in SnapshotMinutes)
            {
                if (elapsedMinutes < minuteMark)
                {
                    continue;
                }

                var exists = await signalRepository.OutcomeExistsAsync(signal.Id, minuteMark, cancellationToken);
                if (exists)
                {
                    continue;
                }

                var ticker = await bybitService.GetTickerAsync(signal.Coin.Symbol, cancellationToken);
                if (ticker is null)
                {
                    continue;
                }

                await signalRepository.AddOutcomeAsync(
                    signal.Id,
                    minuteMark,
                    ticker.LastPrice,
                    signal.SignalPrice,
                    DateTimeOffset.UtcNow,
                    cancellationToken);
            }
        }
    }
}

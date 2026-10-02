using CryptoTrendForge.Core.Domain;
using CryptoTrendForge.Core.Domain.Enums;
using CryptoTrendForge.Worker.Configuration;
using CryptoTrendForge.Worker.Infrastructure;
using CryptoTrendForge.Worker.Services;
using Microsoft.Extensions.Options;

namespace CryptoTrendForge.Worker.Workers;

public sealed class SignalOutcomeWorker : BackgroundService
{
    private static readonly int[] SnapshotMinutes = [15, 30, 60, 240];
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BotOptions _botOptions;
    private readonly StockOptions _stockOptions;
    private readonly RunOnceCoordinator _runOnceCoordinator;
    private readonly ILogger<SignalOutcomeWorker> _logger;

    public SignalOutcomeWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<BotOptions> botOptions,
        IOptions<StockOptions> stockOptions,
        RunOnceCoordinator runOnceCoordinator,
        ILogger<SignalOutcomeWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _botOptions = botOptions.Value;
        _stockOptions = stockOptions.Value;
        _runOnceCoordinator = runOnceCoordinator;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_botOptions.RunOnce)
        {
            try
            {
                await RunOutcomeIterationAsync(stoppingToken);
            }
            finally
            {
                _runOnceCoordinator.NotifyWorkerCompleted(nameof(SignalOutcomeWorker));
            }

            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOutcomeIterationAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }

    private async Task RunOutcomeIterationAsync(CancellationToken stoppingToken)
    {
        try
        {
            await CaptureOutcomesAsync(stoppingToken);
            await EvaluateExitTargetsAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected during shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Signal outcome iteration failed.");
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

            var ticker = await bybitService.GetTickerAsync(signal.Coin.Symbol, cancellationToken);
            if (ticker is null)
            {
                continue;
            }

            await signalRepository.InvalidateIfBrokenSupportAsync(signal.CoinId, ticker.LastPrice, cancellationToken);

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

                await signalRepository.AddOutcomeAsync(
                    signal.Id,
                    minuteMark,
                    ticker.LastPrice,
                    signal.SignalPrice,
                    DateTimeOffset.UtcNow,
                    cancellationToken);
            }
        }

        await signalRepository.ExpireDueSignalsAsync(DateTimeOffset.UtcNow, cancellationToken);
    }

    private async Task EvaluateExitTargetsAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var signalRepository = scope.ServiceProvider.GetRequiredService<SignalRepository>();
        var bybitService = scope.ServiceProvider.GetRequiredService<BybitService>();
        var now = DateTimeOffset.UtcNow;
        var pending = await signalRepository.GetSignalsPendingExitEvaluationAsync(now, cancellationToken);
        var changed = false;

        foreach (var signal in pending)
        {
            if (signal.Coin is null)
            {
                continue;
            }

            var klines = await bybitService.GetKlinesAsync(signal.Coin.Symbol, "15", 200, cancellationToken);
            if (klines.Count == 0)
            {
                continue;
            }

            DateTimeOffset? countUntil = null;
            if (signal.Coin.CoinType == CoinType.Stock && signal.ExpiresAt is DateTimeOffset expiresAt)
            {
                countUntil = UsEquitySession.EvaluationEnd(signal.CreatedAt, expiresAt, _stockOptions);
            }

            ExitTargetEvaluator.Apply(signal, klines, now, countUntil);
            changed = true;
        }

        if (changed)
        {
            await signalRepository.SaveChangesAsync(cancellationToken);
        }
    }
}

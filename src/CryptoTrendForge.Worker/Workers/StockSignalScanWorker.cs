using CryptoTrendForge.Core.Domain.Enums;
using CryptoTrendForge.Core.Domain.Models;
using CryptoTrendForge.Core.Infrastructure.Cache;
using CryptoTrendForge.Worker.Configuration;
using CryptoTrendForge.Worker.Infrastructure;
using CryptoTrendForge.Worker.Services;
using Microsoft.Extensions.Options;

namespace CryptoTrendForge.Worker.Workers;

public sealed class StockSignalScanWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BotOptions _botOptions;
    private readonly StockOptions _stockOptions;
    private readonly MarketDataCache _marketDataCache;
    private readonly RunOnceCoordinator _runOnceCoordinator;
    private readonly ILogger<StockSignalScanWorker> _logger;

    public StockSignalScanWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<BotOptions> botOptions,
        IOptions<StockOptions> stockOptions,
        MarketDataCache marketDataCache,
        RunOnceCoordinator runOnceCoordinator,
        ILogger<StockSignalScanWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _botOptions = botOptions.Value;
        _stockOptions = stockOptions.Value;
        _marketDataCache = marketDataCache;
        _runOnceCoordinator = runOnceCoordinator;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_botOptions.RunOnce)
        {
            await RunScanIterationAsync(stoppingToken);
            _runOnceCoordinator.NotifyWorkerCompleted(nameof(StockSignalScanWorker));
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            await RunScanIterationAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromSeconds(_botOptions.ScanIntervalSeconds), stoppingToken);
        }
    }

    private async Task RunScanIterationAsync(CancellationToken stoppingToken)
    {
        try
        {
            await ScanAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stock signal scan iteration failed.");
        }
    }

    private async Task ScanAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (_stockOptions.ScanOnlyDuringMarketHours && !UsEquitySession.IsOpen(now, _stockOptions))
        {
            _logger.LogInformation("Stock scan skipped because the US equity market is closed.");
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var marketDataService = scope.ServiceProvider.GetRequiredService<MarketDataService>();
        var riskFilterService = scope.ServiceProvider.GetRequiredService<StockRiskFilterService>();
        var signalEngine = scope.ServiceProvider.GetRequiredService<StockSignalEngine>();
        var signalRepository = scope.ServiceProvider.GetRequiredService<SignalRepository>();
        var telegramService = scope.ServiceProvider.GetRequiredService<TelegramService>();

        await signalRepository.ExpireDueSignalsAsync(now, cancellationToken);

        var stocks = await marketDataService.GetActiveStocksAsync(cancellationToken);
        var bestScore = 0;
        string? bestSymbol = null;
        foreach (var stock in stocks)
        {
            var snapshot = await marketDataService.GetStockMarketSnapshotAsync(stock.Symbol, cancellationToken);
            if (snapshot is null)
            {
                continue;
            }

            await signalRepository.InvalidateIfBrokenSupportAsync(stock.Id, snapshot.CurrentPrice, cancellationToken);

            var filterResult = riskFilterService.Check(snapshot, now);
            var scoreResult = signalEngine.CalculateScore(snapshot);
            await signalRepository.RecordNearMissIfNeededAsync(
                stock,
                scoreResult,
                _stockOptions.Candidate,
                filterResult.IsBlocked,
                filterResult.Reason,
                now,
                cancellationToken);
            if (filterResult.IsBlocked)
            {
                _logger.LogDebug("Stock signal blocked for {Symbol}: {Reason}", stock.Symbol, filterResult.Reason);
                continue;
            }
            if (scoreResult.TotalScore > bestScore)
            {
                bestScore = scoreResult.TotalScore;
                bestSymbol = stock.Symbol;
            }

            if (scoreResult.TotalScore < _stockOptions.Candidate)
            {
                if (scoreResult.TotalScore >= 60)
                {
                    _logger.LogInformation(
                        "Stock watchlist {Symbol}: score {Score} under threshold {Threshold}.",
                        stock.Symbol,
                        scoreResult.TotalScore,
                        _stockOptions.Candidate);
                }

                continue;
            }

            var activeSignal = await ResolveActiveSignalAsync(signalRepository, stock.Id, stock.Symbol, cancellationToken);
            var latestSignal = await signalRepository.GetLatestSignalByCoinIdAsync(stock.Id, cancellationToken);
            if (latestSignal is not null)
            {
                var inCooldown = now < latestSignal.CreatedAt.AddHours(_botOptions.CooldownHours);
                var elapsed = now - latestSignal.CreatedAt;
                var canBypass = _botOptions.AllowCooldownBypass
                    && elapsed >= TimeSpan.FromMinutes(_botOptions.CooldownBypassMinElapsedMinutes)
                    && scoreResult.TotalScore >= latestSignal.TotalScore + _botOptions.CooldownBypassMinScoreDelta;

                if (inCooldown && !canBypass)
                {
                    continue;
                }
            }

            if (activeSignal is not null)
            {
                if (scoreResult.TotalScore <= activeSignal.TotalScore)
                {
                    continue;
                }

                await signalRepository.MarkSupersededAsync(activeSignal.Id, cancellationToken);
            }

            var signalType = scoreResult.TotalScore >= _stockOptions.Strong
                ? SignalType.StrongLongCandidate
                : SignalType.LongCandidate;

            var signal = await signalRepository.CreateSignalAsync(
                stock,
                scoreResult,
                signalType,
                MarketRegime.Neutral,
                snapshot.CurrentPrice,
                scoreResult.SupportLevel,
                scoreResult.SupportDistancePct,
                snapshot.FundingRate,
                null,
                now.AddHours(_botOptions.CooldownHours),
                cancellationToken);

            var sent = await telegramService.SendSignalAsync(signal, scoreResult, snapshot, CoinType.Stock, cancellationToken);
            if (sent)
            {
                await signalRepository.ActivateSignalAsync(signal.Id, cancellationToken);
                _marketDataCache.Set($"active_signal:{stock.Symbol}".ToLowerInvariant(), true, TimeSpan.FromHours(_botOptions.CooldownHours));
                _logger.LogInformation(
                    "Stock signal activated for {Symbol}: id={SignalId}, type={SignalType}, totalScore={Score}.",
                    stock.Symbol,
                    signal.Id,
                    signalType,
                    scoreResult.TotalScore);
            }
            else
            {
                _logger.LogWarning(
                    "Stock signal remains pending for {Symbol}: id={SignalId}, type={SignalType}, totalScore={Score}.",
                    stock.Symbol,
                    signal.Id,
                    signalType,
                    scoreResult.TotalScore);
            }
        }

        _logger.LogInformation(
            "Stock scan finished. Best score {BestScore} on {BestSymbol}. Candidate threshold {Threshold}.",
            bestScore,
            bestSymbol ?? "-",
            _stockOptions.Candidate);
    }

    private async Task<Signal?> ResolveActiveSignalAsync(
        SignalRepository signalRepository,
        int coinId,
        string symbol,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"active_signal:{symbol}".ToLowerInvariant();
        if (_marketDataCache.TryGet<bool>(cacheKey, out var activeCached) && activeCached)
        {
            return await signalRepository.GetActiveSignalByCoinIdAsync(coinId, cancellationToken);
        }

        var activeSignal = await signalRepository.GetActiveSignalByCoinIdAsync(coinId, cancellationToken);
        if (activeSignal is not null)
        {
            _marketDataCache.Set(cacheKey, true, TimeSpan.FromHours(_botOptions.CooldownHours));
        }

        return activeSignal;
    }
}

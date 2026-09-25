using CryptoTrendForge.Core.Domain.Enums;
using CryptoTrendForge.Core.Domain.Models;
using CryptoTrendForge.Core.Infrastructure.Cache;
using CryptoTrendForge.Worker.Configuration;
using CryptoTrendForge.Worker.Services;
using Microsoft.Extensions.Options;

namespace CryptoTrendForge.Worker.Workers;

public sealed class SignalScanWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly BotOptions _botOptions;
    private readonly MarketDataCache _marketDataCache;
    private readonly ILogger<SignalScanWorker> _logger;

    public SignalScanWorker(
        IServiceScopeFactory scopeFactory,
        IOptions<BotOptions> botOptions,
        MarketDataCache marketDataCache,
        ILogger<SignalScanWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _botOptions = botOptions.Value;
        _marketDataCache = marketDataCache;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ScanAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Signal scan iteration failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(_botOptions.ScanIntervalSeconds), stoppingToken);
        }
    }

    private async Task ScanAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var marketDataService = scope.ServiceProvider.GetRequiredService<MarketDataService>();
        var btcRegimeService = scope.ServiceProvider.GetRequiredService<BtcRegimeService>();
        var riskFilterService = scope.ServiceProvider.GetRequiredService<RiskFilterService>();
        var signalEngine = scope.ServiceProvider.GetRequiredService<SignalEngine>();
        var signalRepository = scope.ServiceProvider.GetRequiredService<SignalRepository>();
        var telegramService = scope.ServiceProvider.GetRequiredService<TelegramService>();

        var now = DateTimeOffset.UtcNow;
        await signalRepository.ExpireDueSignalsAsync(now, cancellationToken);

        var btcSnapshot = await marketDataService.GetBtcSnapshotAsync(cancellationToken);
        if (btcSnapshot is null)
        {
            _logger.LogWarning("BTC snapshot could not be fetched; skipping this scan cycle.");
            return;
        }

        var regimeResult = btcRegimeService.DetermineRegime(btcSnapshot);
        if (regimeResult.SkipScan)
        {
            _logger.LogWarning("Skipping scan because BTC regime requested skip. Reason: {Reason}", regimeResult.SkipReason);
            return;
        }

        var coins = await marketDataService.GetActiveCoinsAsync(cancellationToken);
        foreach (var coin in coins)
        {
            var snapshot = await marketDataService.GetMarketSnapshotAsync(coin.Symbol, cancellationToken);
            if (snapshot is null)
            {
                continue;
            }

            await signalRepository.InvalidateIfBrokenSupportAsync(coin.Id, snapshot.CurrentPrice, cancellationToken);

            var filterResult = riskFilterService.Check(snapshot, btcSnapshot, regimeResult);
            if (filterResult.IsBlocked)
            {
                _logger.LogDebug("Signal blocked for {Symbol}: {Reason}", coin.Symbol, filterResult.Reason);
                continue;
            }

            if (_botOptions.BtcRiskOffBlockEnabled && regimeResult.Regime == MarketRegime.RiskOff)
            {
                _logger.LogDebug("Risk-off blocking enabled, skipping signal generation for {Symbol}.", coin.Symbol);
                continue;
            }

            var scoreResult = signalEngine.CalculateScore(snapshot, regimeResult.Regime);
            var (candidateThreshold, strongThreshold) = ResolveThresholds(regimeResult.Regime);

            if (scoreResult.TotalScore < candidateThreshold)
            {
                if (scoreResult.TotalScore >= 60)
                {
                    _logger.LogInformation(
                        "Watchlist {Symbol}: score {Score} under threshold {Threshold}.",
                        coin.Symbol,
                        scoreResult.TotalScore,
                        candidateThreshold);
                }

                continue;
            }

            var activeSignal = await ResolveActiveSignalAsync(signalRepository, coin.Id, coin.Symbol, cancellationToken);
            var latestSignal = await signalRepository.GetLatestSignalByCoinIdAsync(coin.Id, cancellationToken);

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

            var signalType = scoreResult.TotalScore >= strongThreshold
                ? SignalType.StrongLongCandidate
                : SignalType.LongCandidate;

            var signal = await signalRepository.CreateSignalAsync(
                coin,
                scoreResult,
                signalType,
                regimeResult.Regime,
                snapshot.CurrentPrice,
                scoreResult.SupportLevel,
                scoreResult.SupportDistancePct,
                snapshot.FundingRate,
                regimeResult.Regime.ToString(),
                now.AddHours(_botOptions.CooldownHours),
                cancellationToken);

            var sent = await telegramService.SendSignalAsync(signal, scoreResult, snapshot, cancellationToken);
            if (sent)
            {
                await signalRepository.ActivateSignalAsync(signal.Id, cancellationToken);
                _marketDataCache.Set($"active_signal:{coin.Symbol}".ToLowerInvariant(), true, TimeSpan.FromHours(_botOptions.CooldownHours));
                _logger.LogInformation(
                    "Signal activated for {Symbol}: id={SignalId}, type={SignalType}, totalScore={Score}.",
                    coin.Symbol,
                    signal.Id,
                    signalType,
                    scoreResult.TotalScore);
            }
            else
            {
                _logger.LogWarning(
                    "Signal remains pending for {Symbol}: id={SignalId}, type={SignalType}, totalScore={Score}.",
                    coin.Symbol,
                    signal.Id,
                    signalType,
                    scoreResult.TotalScore);
            }
        }
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

    private (int Candidate, int Strong) ResolveThresholds(MarketRegime regime)
    {
        return regime switch
        {
            MarketRegime.RiskOn => (_botOptions.ScoreThresholds.RiskOn.Candidate, _botOptions.ScoreThresholds.RiskOn.Strong),
            MarketRegime.Neutral => (_botOptions.ScoreThresholds.Neutral.Candidate, _botOptions.ScoreThresholds.Neutral.Strong),
            _ => (int.MaxValue, int.MaxValue)
        };
    }
}

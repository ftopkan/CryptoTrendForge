using CryptoTrendForge.Core.Domain;
using CryptoTrendForge.Core.Domain.Enums;
using CryptoTrendForge.Core.Domain.Models;
using CryptoTrendForge.Core.Infrastructure.Cache;
using CryptoTrendForge.Worker.Configuration;
using Microsoft.Extensions.Options;

namespace CryptoTrendForge.Worker.Services;

public sealed class CryptoEntryScanRunner
{
    private readonly BotOptions _botOptions;
    private readonly MarketDataCache _marketDataCache;
    private readonly MarketDataService _marketDataService;
    private readonly BtcRegimeService _btcRegimeService;
    private readonly RiskFilterService _riskFilterService;
    private readonly SignalEngine _signalEngine;
    private readonly SignalRepository _signalRepository;
    private readonly TelegramService _telegramService;
    private readonly ILogger<CryptoEntryScanRunner> _logger;

    public CryptoEntryScanRunner(
        IOptions<BotOptions> botOptions,
        MarketDataCache marketDataCache,
        MarketDataService marketDataService,
        BtcRegimeService btcRegimeService,
        RiskFilterService riskFilterService,
        SignalEngine signalEngine,
        SignalRepository signalRepository,
        TelegramService telegramService,
        ILogger<CryptoEntryScanRunner> logger)
    {
        _botOptions = botOptions.Value;
        _marketDataCache = marketDataCache;
        _marketDataService = marketDataService;
        _btcRegimeService = btcRegimeService;
        _riskFilterService = riskFilterService;
        _signalEngine = signalEngine;
        _signalRepository = signalRepository;
        _telegramService = telegramService;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        await _signalRepository.ExpireDueSignalsAsync(now, cancellationToken);

        var btcSnapshot = await _marketDataService.GetBtcSnapshotAsync(cancellationToken);
        if (btcSnapshot is null)
        {
            _logger.LogWarning("BTC snapshot could not be fetched; skipping this scan cycle.");
            await SendAdminSummaryAsync(
                now,
                "BTC verisi alınamadı; tarama atlandı.",
                null,
                0,
                0,
                cancellationToken);
            return;
        }

        var regimeResult = _btcRegimeService.DetermineRegime(btcSnapshot);
        if (regimeResult.SkipScan)
        {
            _logger.LogWarning("Skipping scan because BTC regime requested skip. Reason: {Reason}", regimeResult.SkipReason);
            var (cand, _) = ResolveThresholds(regimeResult.Regime);
            await SendAdminSummaryAsync(
                now,
                $"Rejim taramayı durdurdu: {regimeResult.SkipReason}",
                regimeResult.Regime,
                0,
                cand,
                cancellationToken);
            return;
        }

        var coins = await _marketDataService.GetActiveCoinsAsync(cancellationToken);
        var bestScore = 0;
        string? bestSymbol = null;
        var topScores = new List<(string Symbol, int Score)>();
        foreach (var coin in coins)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var snapshot = await _marketDataService.GetMarketSnapshotAsync(coin.Symbol, cancellationToken);
            if (snapshot is null)
            {
                continue;
            }

            await _signalRepository.InvalidateIfBrokenSupportAsync(coin.Id, snapshot.CurrentPrice, cancellationToken);

            var filterResult = _riskFilterService.Check(snapshot, btcSnapshot, regimeResult);
            if (_botOptions.BtcRiskOffBlockEnabled && regimeResult.Regime == MarketRegime.RiskOff)
            {
                continue;
            }

            var scoreResult = _signalEngine.CalculateScore(snapshot, regimeResult.Regime);
            var (candidateThreshold, strongThreshold) = ResolveThresholds(regimeResult.Regime);
            await _signalRepository.RecordNearMissIfNeededAsync(
                coin,
                scoreResult,
                candidateThreshold,
                filterResult.IsBlocked,
                filterResult.Reason,
                now,
                cancellationToken);
            if (filterResult.IsBlocked)
            {
                continue;
            }

            if (scoreResult.TotalScore > bestScore)
            {
                bestScore = scoreResult.TotalScore;
                bestSymbol = coin.Symbol;
            }

            topScores.Add((coin.Symbol, scoreResult.TotalScore));

            if (scoreResult.TotalScore < candidateThreshold)
            {
                continue;
            }

            var activeSignal = await ResolveActiveSignalAsync(coin.Id, coin.Symbol, cancellationToken);
            var latestSignal = await _signalRepository.GetLatestSignalByCoinIdAsync(coin.Id, cancellationToken);
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

                await _signalRepository.MarkSupersededAsync(activeSignal.Id, cancellationToken);
            }

            var signalType = scoreResult.TotalScore >= strongThreshold
                ? SignalType.StrongLongCandidate
                : SignalType.LongCandidate;

            var signal = await _signalRepository.CreateSignalAsync(
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

            var sent = await _telegramService.SendSignalAsync(signal, scoreResult, snapshot, CoinType.Crypto, cancellationToken);
            if (sent)
            {
                await _signalRepository.ActivateSignalAsync(signal.Id, cancellationToken);
                _marketDataCache.Set($"active_signal:{coin.Symbol}".ToLowerInvariant(), true, TimeSpan.FromHours(_botOptions.CooldownHours));
            }
        }

        var top3 = topScores.OrderByDescending(x => x.Score).Take(3).ToList();
        var (candThreshold, _) = ResolveThresholds(regimeResult.Regime);
        _logger.LogInformation(
            "Scan finished. Regime {Regime}. Best score {BestScore} on {BestSymbol}. Top3: {Top3}",
            regimeResult.Regime,
            bestScore,
            bestSymbol ?? "-",
            string.Join(", ", top3.Select(x => $"{x.Symbol} {x.Score}")));

        var top3Lines = top3.Count == 0
            ? "• (puan yok)"
            : string.Join("\n", top3.Select(x => $"• {x.Symbol}: {x.Score}"));
        var summary = $"📊 Kripto tarama bitti — {TurkeyTime.Format(now)}\n" +
                      $"Rejim: {regimeResult.Regime}  Eşik: {candThreshold}\n" +
                      top3Lines;
        await _telegramService.SendAdminAlertAsync(summary, cancellationToken);

        if (_botOptions.RunOnce)
        {
            WriteLastRunMarker(now, regimeResult.Regime, bestSymbol, bestScore, candThreshold);
        }
    }

    private async Task SendAdminSummaryAsync(
        DateTimeOffset now,
        string note,
        MarketRegime? regime,
        int bestScore,
        int threshold,
        CancellationToken cancellationToken)
    {
        var summary = $"📊 Kripto tarama bitti — {TurkeyTime.Format(now)}\n" +
                      (regime is null ? note : $"Rejim: {regime}  Eşik: {threshold}\n{note}");
        await _telegramService.SendAdminAlertAsync(summary, cancellationToken);
        if (_botOptions.RunOnce && regime is not null)
        {
            WriteLastRunMarker(now, regime.Value, null, bestScore, threshold);
        }
    }

    private static void WriteLastRunMarker(
        DateTimeOffset now,
        MarketRegime regime,
        string? bestSymbol,
        int bestScore,
        int threshold)
    {
        try
        {
            var logsDir = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(logsDir);
            var path = Path.Combine(logsDir, "last-run.txt");
            File.WriteAllText(
                path,
                $"{now:O} | regime={regime} | best={bestSymbol ?? "-"} {bestScore}/{threshold}{Environment.NewLine}");
        }
        catch (Exception)
        {
        }
    }

    private async Task<Signal?> ResolveActiveSignalAsync(int coinId, string symbol, CancellationToken cancellationToken)
    {
        var cacheKey = $"active_signal:{symbol}".ToLowerInvariant();
        if (_marketDataCache.TryGet<bool>(cacheKey, out var activeCached) && activeCached)
        {
            return await _signalRepository.GetActiveSignalByCoinIdAsync(coinId, cancellationToken);
        }

        var activeSignal = await _signalRepository.GetActiveSignalByCoinIdAsync(coinId, cancellationToken);
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

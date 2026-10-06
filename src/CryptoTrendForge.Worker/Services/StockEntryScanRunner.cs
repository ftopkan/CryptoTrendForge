using CryptoTrendForge.Core.Domain;
using CryptoTrendForge.Core.Domain.Enums;
using CryptoTrendForge.Core.Domain.Models;
using CryptoTrendForge.Core.Infrastructure.Cache;
using CryptoTrendForge.Worker.Configuration;
using Microsoft.Extensions.Options;

namespace CryptoTrendForge.Worker.Services;

public sealed class StockEntryScanRunner
{
    private readonly BotOptions _botOptions;
    private readonly StockOptions _stockOptions;
    private readonly MarketDataCache _marketDataCache;
    private readonly MarketDataService _marketDataService;
    private readonly StockRiskFilterService _riskFilterService;
    private readonly StockSignalEngine _signalEngine;
    private readonly SignalRepository _signalRepository;
    private readonly TelegramService _telegramService;
    private readonly ILogger<StockEntryScanRunner> _logger;

    public StockEntryScanRunner(
        IOptions<BotOptions> botOptions,
        IOptions<StockOptions> stockOptions,
        MarketDataCache marketDataCache,
        MarketDataService marketDataService,
        StockRiskFilterService riskFilterService,
        StockSignalEngine signalEngine,
        SignalRepository signalRepository,
        TelegramService telegramService,
        ILogger<StockEntryScanRunner> logger)
    {
        _botOptions = botOptions.Value;
        _stockOptions = stockOptions.Value;
        _marketDataCache = marketDataCache;
        _marketDataService = marketDataService;
        _riskFilterService = riskFilterService;
        _signalEngine = signalEngine;
        _signalRepository = signalRepository;
        _telegramService = telegramService;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (_stockOptions.ScanOnlyDuringMarketHours && !UsEquitySession.IsOpen(now, _stockOptions))
        {
            _logger.LogInformation("Stock scan skipped because the US equity market is closed.");
            if (_botOptions.RunOnce)
            {
                var closedSummary = $"📊 Hisse taraması bitti — {TurkeyTime.Format(now)}\nABD borsası kapalı; tarama yapılmadı.";
                await _telegramService.SendAdminAlertAsync(closedSummary, cancellationToken);
            }

            return;
        }

        await _signalRepository.ExpireDueSignalsAsync(now, cancellationToken);

        var stocks = await _marketDataService.GetActiveStocksAsync(cancellationToken);
        var bestScore = 0;
        string? bestSymbol = null;
        var topScores = new List<(string Symbol, int Score)>();
        var pendingSignals = new List<(Signal Signal, ScoreResult Score, MarketSnapshot Snapshot)>();
        foreach (var stock in stocks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var snapshot = await _marketDataService.GetStockMarketSnapshotAsync(stock.Symbol, cancellationToken);
            if (snapshot is null)
            {
                continue;
            }

            await _signalRepository.InvalidateIfBrokenSupportAsync(stock.Id, snapshot.CurrentPrice, cancellationToken);

            var filterResult = _riskFilterService.Check(snapshot, now);
            var scoreResult = _signalEngine.CalculateScore(snapshot);
            await _signalRepository.RecordNearMissIfNeededAsync(
                stock,
                scoreResult,
                _stockOptions.Candidate,
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
                bestSymbol = stock.Symbol;
            }

            topScores.Add((stock.Symbol, scoreResult.TotalScore));

            if (scoreResult.TotalScore < _stockOptions.Candidate)
            {
                continue;
            }

            var activeSignal = await ResolveActiveSignalAsync(stock.Id, stock.Symbol, cancellationToken);
            var latestSignal = await _signalRepository.GetLatestSignalByCoinIdAsync(stock.Id, cancellationToken);
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

            var signalType = scoreResult.TotalScore >= _stockOptions.Strong
                ? SignalType.StrongLongCandidate
                : SignalType.LongCandidate;

            var signal = await _signalRepository.CreateSignalAsync(
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
                cancellationToken: cancellationToken);
            pendingSignals.Add((signal, scoreResult, snapshot));
        }

        var top3 = topScores.OrderByDescending(x => x.Score).Take(3).ToList();
        foreach (var pending in pendingSignals)
        {
            var sent = await _telegramService.SendSignalAsync(
                pending.Signal,
                pending.Score,
                pending.Snapshot,
                CoinType.Stock,
                leaders: top3,
                cancellationToken: cancellationToken);
            if (sent)
            {
                await _signalRepository.ActivateSignalAsync(pending.Signal.Id, cancellationToken);
                _marketDataCache.Set($"active_signal:{pending.Snapshot.Symbol}".ToLowerInvariant(), true, TimeSpan.FromHours(_botOptions.CooldownHours));
            }
        }
        var top3Lines = top3.Count == 0
            ? "• (puan yok)"
            : string.Join("\n", top3.Select(x => $"• {x.Symbol}: {x.Score}"));
        var summary = $"📊 Hisse taraması bitti — {TurkeyTime.Format(now)}\n" +
                      $"Eşik: {_stockOptions.Candidate}\n" +
                      top3Lines;
        await _telegramService.SendAdminAlertAsync(summary, cancellationToken);
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
}

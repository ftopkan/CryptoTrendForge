using CryptoTrendForge.Core.Domain.Models;
using CryptoTrendForge.Worker.Configuration;
using CryptoTrendForge.Worker.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace CryptoTrendForge.Tests;

public sealed class StockSignalTests
{
    private static readonly StockOptions Settings = new();

    [Fact]
    public void Session_IsOpenOnATuesdayMorningInNewYork()
    {
        var utc = new DateTimeOffset(2026, 9, 29, 15, 0, 0, TimeSpan.Zero);

        Assert.True(UsEquitySession.IsOpen(utc, Settings));
    }

    [Fact]
    public void Session_IsClosedBeforeTheOpeningBell()
    {
        var utc = new DateTimeOffset(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);

        Assert.False(UsEquitySession.IsOpen(utc, Settings));
    }

    [Fact]
    public void Session_IsClosedOnSaturday()
    {
        var utc = new DateTimeOffset(2026, 9, 26, 15, 0, 0, TimeSpan.Zero);

        Assert.False(UsEquitySession.IsOpen(utc, Settings));
    }

    [Fact]
    public void Session_UsesEasternTimeAfterDaylightSavingEnds()
    {
        var beforeOpen = new DateTimeOffset(2026, 11, 3, 14, 0, 0, TimeSpan.Zero);
        var duringSession = new DateTimeOffset(2026, 11, 3, 15, 0, 0, TimeSpan.Zero);

        Assert.False(UsEquitySession.IsOpen(beforeOpen, Settings));
        Assert.True(UsEquitySession.IsOpen(duringSession, Settings));
    }

    [Fact]
    public void Filter_BlocksWhenTheMarketIsClosed()
    {
        var filter = new StockRiskFilterService(Options.Create(Settings), new TechnicalAnalysisService());
        var closed = new DateTimeOffset(2026, 9, 26, 15, 0, 0, TimeSpan.Zero);

        var result = filter.Check(new MarketSnapshot { CurrentPrice = 100m }, closed);

        Assert.True(result.IsBlocked);
    }

    [Fact]
    public void Filter_AllowsAClosedMarketWhenTheSessionGateIsOff()
    {
        var settings = new StockOptions { ScanOnlyDuringMarketHours = false };
        var filter = new StockRiskFilterService(Options.Create(settings), new TechnicalAnalysisService());
        var closed = new DateTimeOffset(2026, 9, 26, 15, 0, 0, TimeSpan.Zero);

        var result = filter.Check(new MarketSnapshot { CurrentPrice = 100m, Klines4H = [], Klines1H = [] }, closed);

        Assert.False(result.IsBlocked);
    }

    [Fact]
    public void Filter_BlocksFundingAboveTheStockLimit()
    {
        var filter = new StockRiskFilterService(Options.Create(Settings), new TechnicalAnalysisService());
        var open = new DateTimeOffset(2026, 9, 29, 15, 0, 0, TimeSpan.Zero);
        var snapshot = new MarketSnapshot { CurrentPrice = 100m, FundingRate = 0.016m };

        var result = filter.Check(snapshot, open);

        Assert.True(result.IsBlocked);
    }

    [Fact]
    public void Filter_AllowsFundingInsideTheStockLimit()
    {
        var filter = new StockRiskFilterService(Options.Create(Settings), new TechnicalAnalysisService());
        var open = new DateTimeOffset(2026, 9, 29, 15, 0, 0, TimeSpan.Zero);
        var snapshot = new MarketSnapshot { CurrentPrice = 100m, FundingRate = 0.014m };

        var result = filter.Check(snapshot, open);

        Assert.False(result.IsBlocked);
    }

    [Fact]
    public void Engine_IgnoresWeekendVolume()
    {
        var engine = CreateEngine();
        var snapshot = new MarketSnapshot
        {
            Symbol = "SNDKUSDT",
            CurrentPrice = 100m,
            Klines1H = SessionCandles(new DateTimeOffset(2026, 9, 26, 0, 0, 0, TimeSpan.Zero))
        };

        var score = engine.CalculateScore(snapshot);

        Assert.Equal(0, score.Breakdown["volume"]);
    }

    [Fact]
    public void Engine_ScoresSessionVolumeWithoutBitcoin()
    {
        var engine = CreateEngine();
        var snapshot = new MarketSnapshot
        {
            Symbol = "SNDKUSDT",
            CurrentPrice = 100m,
            Klines1H = SessionCandles(
                new DateTimeOffset(2026, 9, 29, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.Zero),
                new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero))
        };

        var score = engine.CalculateScore(snapshot);

        Assert.Equal(20, score.Breakdown["volume"]);
        Assert.DoesNotContain(score.Risks, risk => risk.Contains("Bitcoin", StringComparison.Ordinal));
    }

    private static StockSignalEngine CreateEngine()
    {
        return new StockSignalEngine(
            new TechnicalAnalysisService(),
            Options.Create(new BotOptions()),
            Options.Create(Settings));
    }

    private static IReadOnlyList<Kline> SessionCandles(params DateTimeOffset[] days)
    {
        var candles = new List<Kline>();
        var index = 0;
        foreach (var day in days)
        {
            foreach (var hour in new[] { 14, 15, 16, 17, 18, 19 })
            {
                var close = 100m + index;
                candles.Add(new Kline
                {
                    OpenTime = day.AddHours(hour),
                    Open = close - 1m,
                    High = close + 1m,
                    Low = close - 2m,
                    Close = close,
                    Volume = 100m + (index * 10m)
                });
                index++;
            }
        }

        return candles;
    }
}

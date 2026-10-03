using CryptoTrendForge.Core.Domain;
using CryptoTrendForge.Core.Domain.Enums;
using CryptoTrendForge.Core.Domain.Models;
using CryptoTrendForge.Worker.Configuration;
using CryptoTrendForge.Worker.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace CryptoTrendForge.Tests;

public sealed class SignalEngineTests
{
    [Fact]
    public void CalculateScore_ReturnsExpectedRangeAndBreakdown()
    {
        var options = Options.Create(new BotOptions());
        var engine = new SignalEngine(new TechnicalAnalysisService(), options);
        var snapshot = new MarketSnapshot
        {
            Symbol = "TESTUSDT",
            CurrentPrice = 100m,
            FundingRate = 0.0001m,
            OpenInterestChangePct1H = 2m,
            OpenInterestChangePct4H = 5m,
            Klines15M = BuildTrendKlines(120, 100m, 0.15m),
            Klines1H = BuildTrendKlines(220, 90m, 0.2m),
            Klines4H = BuildTrendKlines(260, 80m, 0.25m)
        };

        var result = engine.CalculateScore(snapshot, MarketRegime.RiskOn);

        Assert.InRange(result.BaseScore, 0, 100);
        Assert.Equal(result.BaseScore + result.PatternBonus, result.TotalScore);
        Assert.True(result.Breakdown.ContainsKey("trend"));
        Assert.True(result.Breakdown.ContainsKey("rsi"));
        Assert.True(result.Breakdown.ContainsKey("volume"));
        Assert.True(result.Breakdown.ContainsKey("support"));
        Assert.True(result.Breakdown.ContainsKey("oi"));
        Assert.True(result.Breakdown.ContainsKey("pattern_bonus"));
    }

    [Fact]
    public void CalculateScore_UsesTheClosedFourHourPatternAndFivePointBonus()
    {
        var options = Options.Create(new BotOptions());
        var engine = new SignalEngine(new TechnicalAnalysisService(), options);
        var closedAt = new DateTimeOffset(2026, 9, 30, 12, 0, 0, TimeSpan.Zero);
        var asOf = closedAt.AddMinutes(30);
        var snapshot = new MarketSnapshot
        {
            Symbol = "TESTUSDT",
            CurrentPrice = 101m,
            Klines4H =
            [
                Candle(closedAt.AddHours(-12), 110m, 100m, 111m, 99m),
                Candle(closedAt.AddHours(-8), 100m, 90m, 101m, 89m),
                Hammer(closedAt.AddHours(-4)),
                Candle(closedAt, 100m, 130m, 131m, 99m)
            ]
        };

        var result = engine.CalculateScore(snapshot, MarketRegime.RiskOn, asOf);

        Assert.Equal(5, result.PatternBonus);
        Assert.Equal("Dipten seken çekiç mum", result.PatternName);
    }

    [Fact]
    public void CalculateScore_KeepsTheTrendScoreAfterTheOpenFourHourCandleIsDropped()
    {
        var options = Options.Create(new BotOptions());
        var engine = new SignalEngine(new TechnicalAnalysisService(), options);
        var now = DateTimeOffset.UtcNow;
        var open = CandleClock.Floor(now, TimeSpan.FromHours(4));
        var klines = new List<Kline>(201);
        var price = 100m;
        for (var i = 200; i >= 0; i--)
        {
            var close = price;
            klines.Add(new Kline
            {
                OpenTime = open.AddHours(-4 * i),
                Open = close - 0.05m,
                High = close + 0.1m,
                Low = close - 0.1m,
                Close = close,
                Volume = 1000m
            });
            price += 0.2m;
        }

        var result = engine.CalculateScore(
            new MarketSnapshot
            {
                Symbol = "TESTUSDT",
                CurrentPrice = klines[^2].Close,
                Klines4H = klines
            },
            MarketRegime.Neutral,
            now);

        Assert.Equal(25, result.Breakdown["trend"]);
    }

    [Fact]
    public void CalculateScore_ComparesOpenInterestWithTheLastClosedFourHourCandle()
    {
        var options = Options.Create(new BotOptions());
        var engine = new SignalEngine(new TechnicalAnalysisService(), options);
        var asOf = new DateTimeOffset(2026, 9, 30, 12, 30, 0, TimeSpan.Zero);
        var snapshot = new MarketSnapshot
        {
            Symbol = "TESTUSDT",
            CurrentPrice = 80m,
            OpenInterestChangePct4H = 4m,
            Klines4H =
            [
                Candle(asOf.AddHours(-8), 100m, 90m, 101m, 89m),
                Candle(asOf.AddHours(-4), 90m, 100m, 101m, 89m),
                Candle(asOf, 100m, 80m, 101m, 79m)
            ]
        };

        var result = engine.CalculateScore(snapshot, MarketRegime.RiskOn, asOf);

        Assert.Equal(15, result.Breakdown["oi"]);
        Assert.Contains(result.Reasons, reason => reason.Contains("açık işlem", StringComparison.Ordinal));
    }

    private static Kline Candle(DateTimeOffset openTime, decimal open, decimal close, decimal high, decimal low)
    {
        return new Kline
        {
            OpenTime = openTime,
            Open = open,
            High = high,
            Low = low,
            Close = close,
            Volume = 1000m
        };
    }

    private static Kline Hammer(DateTimeOffset openTime)
    {
        return new Kline
        {
            OpenTime = openTime,
            Open = 100m,
            Close = 101m,
            High = 101.2m,
            Low = 98m,
            Volume = 1000m
        };
    }

    private static IReadOnlyList<Kline> BuildTrendKlines(int count, decimal start, decimal step)
    {
        var list = new List<Kline>(count);
        var price = start;
        for (var i = 0; i < count; i++)
        {
            var open = price;
            var close = price + step;
            list.Add(new Kline
            {
                OpenTime = DateTimeOffset.UtcNow.AddMinutes(-count + i),
                Open = open,
                High = Math.Max(open, close) + (step * 0.4m),
                Low = Math.Min(open, close) - (step * 0.4m),
                Close = close,
                Volume = 1000m + (i % 20)
            });
            price += step;
        }

        return list;
    }
}

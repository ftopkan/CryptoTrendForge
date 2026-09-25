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

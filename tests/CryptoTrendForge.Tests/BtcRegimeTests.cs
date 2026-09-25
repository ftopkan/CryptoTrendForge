using CryptoTrendForge.Core.Domain.Enums;
using CryptoTrendForge.Core.Domain.Models;
using CryptoTrendForge.Worker.Configuration;
using CryptoTrendForge.Worker.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace CryptoTrendForge.Tests;

public sealed class BtcRegimeTests
{
    private static readonly BotOptions DefaultBotOptions = new();

    [Fact]
    public void DetermineRegime_ReturnsRiskOn_When4hBullishAndRsiNormal()
    {
        var service = new BtcRegimeService(new TechnicalAnalysisService(), Microsoft.Extensions.Options.Options.Create(DefaultBotOptions));
        var snapshot = new BtcSnapshot
        {
            Klines4H = BuildTrendKlines(230, 100m, 1m),
            Klines1H = BuildTrendKlines(80, 100m, 0.3m)
        };

        var result = service.DetermineRegime(snapshot);

        Assert.Equal(MarketRegime.RiskOn, result.Regime);
        Assert.False(result.SkipScan);
    }

    [Fact]
    public void DetermineRegime_SetsSkip_WhenAnomalousDumpDetected()
    {
        var service = new BtcRegimeService(new TechnicalAnalysisService(), Microsoft.Extensions.Options.Options.Create(DefaultBotOptions));
        var klines = BuildTrendKlines(230, 100m, 0.5m).ToList();
        var prev = klines[^2];
        klines[^1] = new Kline
        {
            OpenTime = prev.OpenTime.AddHours(4),
            Open = prev.Close,
            High = prev.Close,
            Low = prev.Close * 0.9m,
            Close = prev.Close * 0.9m,
            Volume = prev.Volume
        };

        var snapshot = new BtcSnapshot
        {
            Klines4H = klines,
            Klines1H = BuildTrendKlines(80, 100m, 0.2m)
        };

        var result = service.DetermineRegime(snapshot);

        Assert.True(result.SkipScan);
        Assert.Equal(MarketRegime.RiskOff, result.Regime);
    }

    private static IReadOnlyList<Kline> BuildTrendKlines(int count, decimal start, decimal step)
    {
        var list = new List<Kline>(count);
        var price = start;
        for (var i = 0; i < count; i++)
        {
            list.Add(new Kline
            {
                OpenTime = DateTimeOffset.UtcNow.AddHours(-count + i),
                Open = price,
                High = price + (step * 0.5m),
                Low = price - (step * 0.5m),
                Close = price + step,
                Volume = 1000 + i
            });
            price += step;
        }

        return list;
    }
}

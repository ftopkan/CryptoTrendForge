using CryptoTrendForge.Core.Domain.Models;
using CryptoTrendForge.Worker.Configuration;
using CryptoTrendForge.Worker.Services;
using Microsoft.Extensions.Options;
using Xunit;

namespace CryptoTrendForge.Tests;

public sealed class RiskFilterTests
{
    [Fact]
    public void Check_Blocks_WhenFundingRateExceedsThreshold()
    {
        var options = Options.Create(new BotOptions { FundingRateHardFilterPct = 0.07m });
        var service = new RiskFilterService(options, new TechnicalAnalysisService());
        var market = new MarketSnapshot
        {
            FundingRate = 0.001m, // 0.10%
            Klines4H = BuildFlatKlines(220),
            Klines1H = BuildFlatKlines(80)
        };
        var btc = new BtcSnapshot { Klines4H = BuildFlatKlines(220), Klines1H = BuildFlatKlines(80) };

        var result = service.Check(market, btc, new BtcRegimeResult());

        Assert.True(result.IsBlocked);
    }

    [Fact]
    public void Check_Allows_WhenNoHardFilterTriggered()
    {
        var options = Options.Create(new BotOptions());
        var service = new RiskFilterService(options, new TechnicalAnalysisService());
        var market = new MarketSnapshot
        {
            FundingRate = 0.0001m,
            OpenInterestChangePct1H = 1m,
            Klines4H = BuildFlatKlines(220),
            Klines1H = BuildFlatKlines(80)
        };
        var btc = new BtcSnapshot { Klines4H = BuildFlatKlines(220), Klines1H = BuildFlatKlines(80) };

        var result = service.Check(market, btc, new BtcRegimeResult());

        Assert.False(result.IsBlocked);
    }

    private static IReadOnlyList<Kline> BuildFlatKlines(int count)
    {
        var list = new List<Kline>(count);
        for (var i = 0; i < count; i++)
        {
            list.Add(new Kline
            {
                OpenTime = DateTimeOffset.UtcNow.AddHours(-count + i),
                Open = 100m,
                High = 101m,
                Low = 99m,
                Close = 100m,
                Volume = 1000m
            });
        }

        return list;
    }
}

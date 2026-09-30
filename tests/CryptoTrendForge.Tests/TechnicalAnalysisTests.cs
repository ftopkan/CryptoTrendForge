using CryptoTrendForge.Core.Domain;
using CryptoTrendForge.Core.Domain.Enums;
using CryptoTrendForge.Core.Domain.Models;
using CryptoTrendForge.Worker.Services;
using Xunit;

namespace CryptoTrendForge.Tests;

public sealed class TechnicalAnalysisTests
{
    private readonly TechnicalAnalysisService _service = new();

    [Fact]
    public void CalculateRsi_ReturnsHighValueForStrongUptrend()
    {
        var klines = Enumerable.Range(1, 40)
            .Select(i => new Kline
            {
                OpenTime = DateTimeOffset.UtcNow.AddMinutes(i),
                Close = 100 + i,
                Volume = 1000
            })
            .ToArray();

        var rsi = _service.CalculateRsi(klines, 14);

        Assert.True(rsi >= 90m);
    }

    [Fact]
    public void CalculateRsi_ThrowsWhenDataInsufficient()
    {
        var klines = Enumerable.Range(1, 20)
            .Select(i => new Kline { Close = 100 + i })
            .ToArray();

        Assert.Throws<ArgumentException>(() => _service.CalculateRsi(klines, 14));
    }

    [Fact]
    public void CalculateEma_ComputesExpectedLengthAndSeed()
    {
        var closes = new decimal[] { 10, 11, 12, 13, 14, 15, 16, 17, 18, 19 };
        var ema = _service.CalculateEma(closes, 5);

        Assert.Equal(closes.Length, ema.Length);
        Assert.Equal(12m, ema[4]); // Seed from SMA of first 5 values.
        Assert.True(ema[^1] > ema[4]);
    }

    [Fact]
    public void CalculateEma_ThrowsWhenDataInsufficient()
    {
        var closes = new decimal[] { 10, 11, 12 };

        Assert.Throws<ArgumentException>(() => _service.CalculateEma(closes, 5));
    }

    [Fact]
    public void CalculateRsi_ReturnsLowValueForStrongDowntrend()
    {
        var klines = Enumerable.Range(1, 40)
            .Select(i => new Kline
            {
                OpenTime = DateTimeOffset.UtcNow.AddMinutes(i),
                Close = 200 - i,
                Volume = 1000
            })
            .ToArray();

        var rsi = _service.CalculateRsi(klines, 14);

        Assert.True(rsi <= 10m);
    }

    [Fact]
    public void EvaluateTrend_KeepsFullScoreWhenPriceStaysNearTheShortAverage()
    {
        var trend = _service.EvaluateTrend(Rising(220, 100m, 0.2m));

        Assert.Equal(25, trend.Score);
        Assert.False(trend.IsBearAligned);
    }

    [Fact]
    public void EvaluateTrend_ReducesScoreWhenPriceRunsAwayFromTheShortAverage()
    {
        var closes = Rising(220, 100m, 0.2m);
        closes[^1] = closes[^2] * 1.25m;

        var trend = _service.EvaluateTrend(closes);

        Assert.Equal(18, trend.Score);
        Assert.Contains("uzamış", trend.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void EvaluateTrend_MarksAFallingStackAsBearish()
    {
        var closes = new decimal[220];
        var price = 300m;
        for (var i = 0; i < closes.Length; i++)
        {
            closes[i] = price;
            price -= 0.4m;
        }

        var trend = _service.EvaluateTrend(closes);

        Assert.Equal(0, trend.Score);
        Assert.True(trend.IsBearAligned);
    }

    [Fact]
    public void OpenInterestChange_UsesThePreviousSampleNotTheOldest()
    {
        var percent = OpenInterestChange.PercentFromNewestFirst([110m, 100m, 50m]);

        Assert.Equal(10m, percent);
    }

    [Fact]
    public void NearMissRule_RecordsTheBandUnderTheThresholdAndBlockedPasses()
    {
        Assert.True(NearMissRule.ShouldRecord(60, 70, blocked: false));
        Assert.False(NearMissRule.ShouldRecord(54, 70, blocked: false));
        Assert.False(NearMissRule.ShouldRecord(70, 70, blocked: false));
        Assert.True(NearMissRule.ShouldRecord(72, 70, blocked: true));
        Assert.False(NearMissRule.ShouldRecord(40, int.MaxValue, blocked: false));
    }

    [Fact]
    public void AssessHourVolumePace_ScoresBuyingWhenTheHourHasAlreadyBeatenItsShare()
    {
        var now = new DateTimeOffset(2026, 9, 30, 10, 30, 0, TimeSpan.Zero);
        var pace = _service.AssessHourVolumePace(
            Hourly(now, volume: 100m, close: 100m),
            QuarterHours(now, 30m, 30m),
            currentPrice: 110m,
            baselineCandles: 10,
            now);

        Assert.True(pace.IsReady);
        Assert.Equal(VolumeSentiment.StrongBuying, pace.Sentiment);
    }

    [Fact]
    public void AssessHourVolumePace_DoesNotProjectAShortBurstIntoAFullHour()
    {
        var now = new DateTimeOffset(2026, 9, 30, 10, 30, 0, TimeSpan.Zero);
        var pace = _service.AssessHourVolumePace(
            Hourly(now, volume: 100m, close: 100m),
            QuarterHours(now, 20m, 20m),
            currentPrice: 110m,
            baselineCandles: 10,
            now);

        Assert.True(pace.IsReady);
        Assert.Equal(VolumeSentiment.WeakMove, pace.Sentiment);
    }

    [Fact]
    public void AssessHourVolumePace_WaitsForTwoClosedQuarterHours()
    {
        var now = new DateTimeOffset(2026, 9, 30, 10, 15, 0, TimeSpan.Zero);
        var pace = _service.AssessHourVolumePace(
            Hourly(now, volume: 100m, close: 100m),
            QuarterHours(now, 80m),
            currentPrice: 110m,
            baselineCandles: 10,
            now);

        Assert.False(pace.IsReady);
    }

    [Fact]
    public void DelayUntilCandleReady_WaitsForTheCandleToSettle()
    {
        var settle = TimeSpan.FromSeconds(10);
        var length = TimeSpan.FromMinutes(15);
        var atClose = new DateTimeOffset(2026, 9, 30, 10, 30, 0, TimeSpan.Zero);
        var afterSettle = new DateTimeOffset(2026, 9, 30, 10, 30, 15, TimeSpan.Zero);

        Assert.Equal(TimeSpan.FromSeconds(10), CandleClock.DelayUntilCandleReady(atClose, length, settle));
        Assert.Equal(TimeSpan.FromMinutes(14) + TimeSpan.FromSeconds(55), CandleClock.DelayUntilCandleReady(afterSettle, length, settle));
    }

    private static IReadOnlyList<Kline> Hourly(DateTimeOffset now, decimal volume, decimal close)
    {
        var hourOpen = CandleClock.Floor(now, TimeSpan.FromHours(1));
        var candles = new List<Kline>();
        for (var i = 10; i >= 1; i--)
        {
            candles.Add(new Kline
            {
                OpenTime = hourOpen.AddHours(-i),
                Open = close,
                High = close,
                Low = close,
                Close = close,
                Volume = volume
            });
        }

        return candles;
    }

    private static IReadOnlyList<Kline> QuarterHours(DateTimeOffset now, params decimal[] volumes)
    {
        var hourOpen = CandleClock.Floor(now, TimeSpan.FromHours(1));
        return volumes.Select((volume, index) => new Kline
        {
            OpenTime = hourOpen.AddMinutes(index * 15),
            Open = 100m,
            High = 101m,
            Low = 99m,
            Close = 100m,
            Volume = volume
        }).ToArray();
    }

    private static decimal[] Rising(int count, decimal start, decimal step)
    {
        var closes = new decimal[count];
        var price = start;
        for (var i = 0; i < count; i++)
        {
            closes[i] = price;
            price += step;
        }

        return closes;
    }
}

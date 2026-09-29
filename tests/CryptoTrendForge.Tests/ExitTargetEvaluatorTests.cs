using CryptoTrendForge.Core.Domain;
using CryptoTrendForge.Core.Domain.Models;
using Xunit;

namespace CryptoTrendForge.Tests;

public sealed class ExitTargetEvaluatorTests
{
    private static readonly DateTimeOffset CreatedAt = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_UsesCurrentPriceWhenPriceIsNearSupport()
    {
        var plan = PositionPlan.Create(100m, 99m);

        Assert.Equal(100m, plan.Entry);
        Assert.Equal("güncel fiyat", plan.EntryNote);
        var risk = plan.Entry - plan.Stop;
        Assert.Equal(plan.Entry + (risk * 0.5m), plan.CautiousExit);
        Assert.Equal(plan.Entry + risk, plan.BalancedExit);
        Assert.Equal(plan.Entry + (risk * 1.5m), plan.WideExit);
    }

    [Fact]
    public void Apply_RecordsFirstTouchInsideTheFourHourWindow()
    {
        var signal = NewSignal();
        signal.CautiousExit = 102m;
        signal.BalancedExit = 106m;
        signal.WideExit = 112m;
        var klines = new[]
        {
            Candle(CreatedAt.AddMinutes(15), high: 103m),
            Candle(CreatedAt.AddMinutes(30), high: 107m),
            Candle(CreatedAt.AddMinutes(45), high: 113m)
        };

        ExitTargetEvaluator.Apply(signal, klines, CreatedAt.AddHours(1));

        Assert.Equal(15, signal.CautiousMinutes);
        Assert.Equal(30, signal.BalancedMinutes);
        Assert.Equal(45, signal.WideMinutes);
        Assert.Null(signal.TargetsClosedAt);
    }

    [Fact]
    public void Apply_IgnoresTouchesAfterExpiry()
    {
        var signal = NewSignal();
        var klines = new[]
        {
            Candle(CreatedAt.AddHours(5), high: 120m)
        };

        ExitTargetEvaluator.Apply(signal, klines, CreatedAt.AddHours(5));

        Assert.Null(signal.CautiousReachedAt);
        Assert.Null(signal.BalancedReachedAt);
        Assert.Null(signal.WideReachedAt);
        Assert.NotNull(signal.TargetsClosedAt);
    }

    [Fact]
    public void Apply_MarksCloserTargetsWhenOneCandleClearsAll()
    {
        var signal = NewSignal();
        var klines = new[]
        {
            Candle(CreatedAt.AddMinutes(60), high: 120m)
        };

        ExitTargetEvaluator.Apply(signal, klines, CreatedAt.AddHours(5));

        Assert.Equal(60, signal.CautiousMinutes);
        Assert.Equal(60, signal.BalancedMinutes);
        Assert.Equal(60, signal.WideMinutes);
        Assert.NotNull(signal.TargetsClosedAt);
    }

    [Fact]
    public void Apply_WaitsForPullbackFillBeforeCountingTargets()
    {
        var signal = NewSignal();
        signal.SignalPrice = 110m;
        signal.EntryPrice = 100m;
        var klines = new[]
        {
            Candle(CreatedAt.AddMinutes(15), high: 120m, low: 108m),
            Candle(CreatedAt.AddMinutes(30), high: 120m, low: 99m)
        };

        ExitTargetEvaluator.Apply(signal, klines, CreatedAt.AddHours(1));

        Assert.Equal(30, signal.WideMinutes);
        Assert.Equal(30, signal.CautiousMinutes);
    }

    [Fact]
    public void Apply_RecordsStopWhenTheFilledCandleTradesThroughIt()
    {
        var signal = NewSignal();
        var klines = new[]
        {
            Candle(CreatedAt.AddMinutes(15), high: 101m, low: 99m),
            Candle(CreatedAt.AddMinutes(45), high: 100m, low: 90m)
        };

        ExitTargetEvaluator.Apply(signal, klines, CreatedAt.AddHours(1));

        Assert.Equal(45, signal.StopMinutes);
        Assert.Null(signal.TargetsClosedAt);
    }

    [Fact]
    public void Apply_DoesNotRecordStopBeforeThePullbackEntryFills()
    {
        var signal = NewSignal();
        signal.SignalPrice = 110m;
        signal.EntryPrice = 100m;
        var klines = new[]
        {
            Candle(CreatedAt.AddMinutes(15), high: 112m, low: 105m)
        };

        ExitTargetEvaluator.Apply(signal, klines, CreatedAt.AddHours(1));

        Assert.Null(signal.StopMinutes);
        Assert.Null(signal.CautiousMinutes);
    }

    [Fact]
    public void Apply_IgnoresTouchesAfterTheCountingWindow()
    {
        var signal = NewSignal();
        var countUntil = CreatedAt.AddHours(2);
        var klines = new[]
        {
            Candle(CreatedAt.AddHours(3), high: 120m, low: 90m)
        };

        ExitTargetEvaluator.Apply(signal, klines, CreatedAt.AddHours(3), countUntil);

        Assert.Null(signal.CautiousReachedAt);
        Assert.Null(signal.StopReachedAt);
        Assert.NotNull(signal.TargetsClosedAt);
    }

    private static Signal NewSignal()
    {
        var plan = PositionPlan.Create(100m, 99m);
        return new Signal
        {
            SignalPrice = 100m,
            EntryPrice = plan.Entry,
            StopPrice = plan.Stop,
            CautiousExit = plan.CautiousExit,
            BalancedExit = plan.BalancedExit,
            WideExit = plan.WideExit,
            CreatedAt = CreatedAt,
            ExpiresAt = CreatedAt.AddHours(4)
        };
    }

    private static Kline Candle(DateTimeOffset openTime, decimal high, decimal low = 99m)
    {
        return new Kline
        {
            OpenTime = openTime,
            Open = 100m,
            High = high,
            Low = low,
            Close = high,
            Volume = 1m
        };
    }
}

using CryptoTrendForge.Core.Domain.Models;

namespace CryptoTrendForge.Core.Domain;

public static class ExitTargetEvaluator
{
    public const decimal BtcBreakDropPct = 3m;
    public const string BtcBreakReason = "BTC bozulması";

    public static void Apply(
        Signal signal,
        IReadOnlyList<Kline> klines,
        DateTimeOffset now,
        DateTimeOffset? countUntil = null,
        IReadOnlyList<Kline>? btcKlines = null)
    {
        if (signal.TargetsClosedAt is not null
            || signal.EntryPrice is null
            || signal.StopPrice is null
            || signal.CautiousExit is null
            || signal.BalancedExit is null
            || signal.WideExit is null
            || signal.ExpiresAt is null
            || klines.Count == 0)
        {
            return;
        }

        var windowStart = signal.CreatedAt;
        var windowEnd = signal.ExpiresAt.Value;
        if (countUntil is DateTimeOffset until && until < windowEnd)
        {
            windowEnd = until;
        }
        var entry = signal.EntryPrice.Value;
        var filled = entry >= signal.SignalPrice;
        var btcBreakAt = FindBtcBreak(signal.BtcEntryPrice, btcKlines, windowStart, windowEnd);

        foreach (var candle in klines.OrderBy(x => x.OpenTime))
        {
            if (candle.OpenTime < windowStart || candle.OpenTime >= windowEnd)
            {
                continue;
            }

            if (btcBreakAt is DateTimeOffset brokenAt && candle.OpenTime >= brokenAt)
            {
                continue;
            }

            if (!filled)
            {
                if (candle.Low > entry)
                {
                    continue;
                }

                filled = true;
            }

            var minutes = (int)Math.Floor((candle.OpenTime - windowStart).TotalMinutes);
            if (minutes < 0)
            {
                continue;
            }

            if (signal.StopPrice is decimal stop && signal.StopReachedAt is null && candle.Low <= stop)
            {
                signal.StopReachedAt = candle.OpenTime;
                signal.StopMinutes = minutes;
            }

            MarkIfTouched(candle.High, signal.CautiousExit.Value, candle.OpenTime, minutes, signal.CautiousReachedAt, value =>
            {
                signal.CautiousReachedAt = value.ReachedAt;
                signal.CautiousMinutes = value.Minutes;
            });
            MarkIfTouched(candle.High, signal.BalancedExit.Value, candle.OpenTime, minutes, signal.BalancedReachedAt, value =>
            {
                signal.BalancedReachedAt = value.ReachedAt;
                signal.BalancedMinutes = value.Minutes;
            });
            MarkIfTouched(candle.High, signal.WideExit.Value, candle.OpenTime, minutes, signal.WideReachedAt, value =>
            {
                signal.WideReachedAt = value.ReachedAt;
                signal.WideMinutes = value.Minutes;
            });
        }

        if (btcBreakAt is not null)
        {
            signal.TargetsClosedAt = now;
            signal.TargetsCloseReason = BtcBreakReason;
        }
        else if (now >= windowEnd)
        {
            signal.TargetsClosedAt = now;
        }
    }

    private static DateTimeOffset? FindBtcBreak(
        decimal? btcEntryPrice,
        IReadOnlyList<Kline>? btcKlines,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd)
    {
        if (btcEntryPrice is not > 0m || btcKlines is null)
        {
            return null;
        }

        var floor = btcEntryPrice.Value * (1m - (BtcBreakDropPct / 100m));
        foreach (var candle in btcKlines.OrderBy(x => x.OpenTime))
        {
            if (candle.OpenTime < windowStart || candle.OpenTime >= windowEnd)
            {
                continue;
            }

            if (candle.Low <= floor)
            {
                return candle.OpenTime;
            }
        }

        return null;
    }

    private static void MarkIfTouched(
        decimal high,
        decimal target,
        DateTimeOffset openTime,
        int minutes,
        DateTimeOffset? alreadyReached,
        Action<(DateTimeOffset ReachedAt, int Minutes)> assign)
    {
        if (alreadyReached is not null || high < target)
        {
            return;
        }

        assign((openTime, minutes));
    }
}

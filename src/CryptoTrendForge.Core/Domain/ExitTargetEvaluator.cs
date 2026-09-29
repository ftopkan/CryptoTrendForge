using CryptoTrendForge.Core.Domain.Models;

namespace CryptoTrendForge.Core.Domain;

public static class ExitTargetEvaluator
{
    public static void Apply(Signal signal, IReadOnlyList<Kline> klines, DateTimeOffset now, DateTimeOffset? countUntil = null)
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

        foreach (var candle in klines.OrderBy(x => x.OpenTime))
        {
            if (candle.OpenTime < windowStart || candle.OpenTime >= windowEnd)
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

        if (now >= windowEnd)
        {
            signal.TargetsClosedAt = now;
        }
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

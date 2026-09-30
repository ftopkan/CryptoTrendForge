using CryptoTrendForge.Core.Domain.Models;

namespace CryptoTrendForge.Core.Domain;

public static class CandleClock
{
    public static DateTimeOffset Floor(DateTimeOffset instant, TimeSpan candleLength)
    {
        var utc = instant.ToUniversalTime();
        var size = candleLength.Ticks;
        var floored = utc.UtcDateTime.Ticks - (utc.UtcDateTime.Ticks % size);
        return new DateTimeOffset(floored, TimeSpan.Zero);
    }

    public static TimeSpan TimeUntilNextClose(DateTimeOffset now, TimeSpan candleLength)
    {
        var remaining = Floor(now, candleLength) + candleLength - now.ToUniversalTime();
        return remaining < TimeSpan.FromSeconds(1)
            ? TimeSpan.FromSeconds(1)
            : remaining;
    }

    public static TimeSpan DelayUntilCandleReady(DateTimeOffset now, TimeSpan candleLength, TimeSpan settle)
    {
        var open = Floor(now, candleLength);
        var settled = open + settle;
        if (now.ToUniversalTime() < settled)
        {
            return settled - now.ToUniversalTime();
        }

        var delay = open + candleLength + settle - now.ToUniversalTime();
        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
    }

    public static IReadOnlyList<Kline> Closed(IReadOnlyList<Kline> klines, TimeSpan candleLength, DateTimeOffset now)
    {
        if (klines.Count == 0)
        {
            return klines;
        }

        var last = klines[^1];
        if (last.OpenTime + candleLength <= now.ToUniversalTime())
        {
            return klines;
        }

        if (klines.Count == 1)
        {
            return [];
        }

        var closed = new Kline[klines.Count - 1];
        for (var i = 0; i < closed.Length; i++)
        {
            closed[i] = klines[i];
        }

        return closed;
    }
}

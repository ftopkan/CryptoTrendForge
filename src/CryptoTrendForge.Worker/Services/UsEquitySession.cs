using CryptoTrendForge.Core.Domain.Models;
using CryptoTrendForge.Worker.Configuration;

namespace CryptoTrendForge.Worker.Services;

public readonly record struct TradingWindow(DateTimeOffset StartUtc, DateTimeOffset EndUtc);

public static class UsEquitySession
{
    private static readonly TimeSpan MinimumCandleOverlap = TimeSpan.FromHours(3);
    private static readonly TimeZoneInfo Eastern = ResolveEastern();

    public static bool IsOpen(DateTimeOffset utc, StockOptions options)
    {
        var local = TimeZoneInfo.ConvertTime(utc, Eastern);
        if (local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return false;
        }

        var minuteOfDay = (local.Hour * 60) + local.Minute;
        var open = (options.MarketOpenHour * 60) + options.MarketOpenMinute;
        var close = (options.MarketCloseHour * 60) + options.MarketCloseMinute;
        return minuteOfDay >= open && minuteOfDay < close;
    }

    public static DateTimeOffset EvaluationEnd(DateTimeOffset createdUtc, DateTimeOffset expiresUtc, StockOptions options)
    {
        var local = TimeZoneInfo.ConvertTime(createdUtc, Eastern);
        if (local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return expiresUtc;
        }

        var closeMinute = (options.MarketCloseHour * 60) + options.MarketCloseMinute;
        var closeLocal = DateTime.SpecifyKind(local.Date.AddMinutes(closeMinute), DateTimeKind.Unspecified);
        var closeUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(closeLocal, Eastern));
        if (closeUtc <= createdUtc)
        {
            return expiresUtc;
        }

        return closeUtc < expiresUtc ? closeUtc : expiresUtc;
    }

    public static MarketSnapshot WithSessionFourHourCandles(MarketSnapshot snapshot, StockOptions options)
    {
        return new MarketSnapshot
        {
            Symbol = snapshot.Symbol,
            CurrentPrice = snapshot.CurrentPrice,
            FundingRate = snapshot.FundingRate,
            OpenInterestChangePct1H = snapshot.OpenInterestChangePct1H,
            OpenInterestChangePct4H = snapshot.OpenInterestChangePct4H,
            Volume24h = snapshot.Volume24h,
            Turnover24h = snapshot.Turnover24h,
            Klines15M = snapshot.Klines15M,
            Klines1H = snapshot.Klines1H,
            Klines4H = FourHourCandlesOverlappingSession(snapshot.Klines4H, options)
        };
    }

    public static IReadOnlyList<Kline> FourHourCandlesOverlappingSession(IReadOnlyList<Kline> klines, StockOptions options)
    {
        return klines
            .Where(candle => OverlapsSession(candle.OpenTime, TimeSpan.FromHours(4), options))
            .ToArray();
    }

    public static bool IsOpeningVolumeInterval(DateTimeOffset utc, StockOptions options)
    {
        var local = TimeZoneInfo.ConvertTime(utc, Eastern);
        if (local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            return false;
        }

        var minuteOfDay = (local.Hour * 60) + local.Minute;
        var open = (options.MarketOpenHour * 60) + options.MarketOpenMinute;
        return minuteOfDay >= open + 30 && minuteOfDay < open + 60;
    }

    public static IReadOnlyList<TradingWindow> RecentOpeningWindows(DateTimeOffset utc, StockOptions options, int count)
    {
        var local = TimeZoneInfo.ConvertTime(utc, Eastern);
        var windows = new List<TradingWindow>(count);
        var day = local.Date;
        while (windows.Count < count)
        {
            if (day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            {
                windows.Add(ToOpeningWindow(day, options));
            }

            day = day.AddDays(-1);
        }

        windows.Reverse();
        return windows;
    }

    public static bool OverlapsSession(DateTimeOffset candleOpenUtc, TimeSpan candleLength, StockOptions options)
    {
        var candleEndUtc = candleOpenUtc + candleLength;
        if (candleEndUtc <= candleOpenUtc)
        {
            return false;
        }

        var localStart = TimeZoneInfo.ConvertTime(candleOpenUtc, Eastern);
        var localEnd = TimeZoneInfo.ConvertTime(candleEndUtc, Eastern);
        var overlap = TimeSpan.Zero;
        for (var day = localStart.Date; day <= localEnd.Date; day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            var openMinute = (options.MarketOpenHour * 60) + options.MarketOpenMinute;
            var closeMinute = (options.MarketCloseHour * 60) + options.MarketCloseMinute;
            var sessionStartLocal = DateTime.SpecifyKind(day.AddMinutes(openMinute), DateTimeKind.Unspecified);
            var sessionEndLocal = DateTime.SpecifyKind(day.AddMinutes(closeMinute), DateTimeKind.Unspecified);
            var sessionStartUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(sessionStartLocal, Eastern));
            var sessionEndUtc = new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(sessionEndLocal, Eastern));
            var overlapStart = candleOpenUtc > sessionStartUtc ? candleOpenUtc : sessionStartUtc;
            var overlapEnd = candleEndUtc < sessionEndUtc ? candleEndUtc : sessionEndUtc;
            if (overlapEnd > overlapStart)
            {
                overlap += overlapEnd - overlapStart;
            }
        }

        return overlap >= MinimumCandleOverlap;
    }

    private static TradingWindow ToOpeningWindow(DateTime date, StockOptions options)
    {
        var openMinute = (options.MarketOpenHour * 60) + options.MarketOpenMinute;
        var startLocal = DateTime.SpecifyKind(date.AddMinutes(openMinute), DateTimeKind.Unspecified);
        var endLocal = DateTime.SpecifyKind(date.AddMinutes(openMinute + 30), DateTimeKind.Unspecified);
        return new TradingWindow(
            new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(startLocal, Eastern)),
            new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(endLocal, Eastern)));
    }

    private static TimeZoneInfo ResolveEastern()
    {
        foreach (var id in new[] { "America/New_York", "Eastern Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
            }
            catch (InvalidTimeZoneException)
            {
            }
        }

        throw new TimeZoneNotFoundException("US Eastern time zone was not found on this machine.");
    }
}

using CryptoTrendForge.Worker.Configuration;

namespace CryptoTrendForge.Worker.Services;

public static class UsEquitySession
{
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

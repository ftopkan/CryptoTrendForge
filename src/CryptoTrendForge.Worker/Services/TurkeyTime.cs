using System.Globalization;

namespace CryptoTrendForge.Worker.Services;

public static class TurkeyTime
{
    private static readonly TimeZoneInfo Zone = Resolve();

    public static string Format(DateTimeOffset instant)
    {
        var local = TimeZoneInfo.ConvertTime(instant, Zone);
        return local.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " TSİ";
    }

    private static TimeZoneInfo Resolve()
    {
        foreach (var id in new[] { "Europe/Istanbul", "Turkey Standard Time" })
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

        throw new TimeZoneNotFoundException("Turkey time zone was not found on this machine.");
    }
}

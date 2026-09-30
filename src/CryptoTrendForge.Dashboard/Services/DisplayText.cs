using CryptoTrendForge.Core.Domain.Enums;

namespace CryptoTrendForge.Dashboard.Services;

public static class DisplayText
{
    private static readonly TimeZoneInfo Turkey = ResolveTurkey();

    public static string TurkeyTime(DateTimeOffset instant)
    {
        return TimeZoneInfo.ConvertTime(instant, Turkey).ToString("dd.MM.yyyy HH:mm");
    }

    public static string Status(SignalStatus status)
    {
        return status switch
        {
            SignalStatus.Pending => "Bekliyor",
            SignalStatus.Active => "Açık",
            SignalStatus.Expired => "Süresi doldu",
            SignalStatus.Invalidated => "Geçersiz",
            SignalStatus.Superseded => "Yenilendi",
            _ => status.ToString()
        };
    }

    public static string StatusBadge(SignalStatus status)
    {
        return status switch
        {
            SignalStatus.Active => "text-bg-success",
            SignalStatus.Pending => "text-bg-warning",
            SignalStatus.Invalidated => "text-bg-danger",
            SignalStatus.Superseded => "text-bg-info",
            _ => "text-bg-secondary"
        };
    }

    public static string Regime(MarketRegime regime)
    {
        return regime switch
        {
            MarketRegime.RiskOn => "Risk açık",
            MarketRegime.RiskOff => "Risk kapalı",
            _ => "Nötr"
        };
    }

    public static string LongKind(SignalType type)
    {
        return type == SignalType.StrongLongCandidate ? "Güçlü long" : "Long";
    }

    public static string Breakdown(string key)
    {
        return key switch
        {
            "trend" => "Trend",
            "rsi" => "RSI",
            "volume" => "Hacim",
            "support" => "Destek",
            "oi" => "Açık işlem",
            "pattern_bonus" => "Mum bonusu",
            _ => key
        };
    }

    private static TimeZoneInfo ResolveTurkey()
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

        return TimeZoneInfo.Utc;
    }
}

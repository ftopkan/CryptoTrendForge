using CryptoTrendForge.Core.Domain;
using CryptoTrendForge.Core.Domain.Models;
using CryptoTrendForge.Core.Domain.Enums;

namespace CryptoTrendForge.Worker.Services;

public readonly record struct TrendAssessment(int Score, bool IsBearAligned, string? Reason, decimal? Ema20ExtensionPct = null);

public readonly record struct HourVolumePace(bool IsReady, VolumeSentiment Sentiment);

public sealed class TechnicalAnalysisService
{
    public const int TrendMinBarsPartial = 50;
    public const int TrendMinBarsFull = 100;
    public const int TrendLongEmaPeriodExtended = 200;
    public decimal CalculateRsi(IEnumerable<Kline> klines, int period)
    {
        var closes = klines.Select(x => x.Close).ToArray();
        if (closes.Length < (period * 2))
        {
            throw new ArgumentException($"At least {period * 2} candles are required for RSI.");
        }

        var gains = new decimal[closes.Length - 1];
        var losses = new decimal[closes.Length - 1];

        for (var i = 1; i < closes.Length; i++)
        {
            var delta = closes[i] - closes[i - 1];
            gains[i - 1] = delta > 0 ? delta : 0;
            losses[i - 1] = delta < 0 ? Math.Abs(delta) : 0;
        }

        var avgGain = gains.Take(period).Average();
        var avgLoss = losses.Take(period).Average();

        for (var i = period; i < gains.Length; i++)
        {
            avgGain = ((avgGain * (period - 1)) + gains[i]) / period;
            avgLoss = ((avgLoss * (period - 1)) + losses[i]) / period;
        }

        if (avgLoss == 0)
        {
            return 100m;
        }

        var rs = avgGain / avgLoss;
        var rsi = 100m - (100m / (1 + rs));
        return Math.Round(rsi, 2);
    }

    public decimal[] CalculateEma(IEnumerable<decimal> closes, int period)
    {
        var values = closes.ToArray();
        if (values.Length < period)
        {
            throw new ArgumentException($"At least {period} prices are required for EMA.");
        }

        var result = new decimal[values.Length];
        var multiplier = 2m / (period + 1);

        var seed = values.Take(period).Average();
        for (var i = 0; i < period - 1; i++)
        {
            result[i] = 0;
        }

        result[period - 1] = seed;
        for (var i = period; i < values.Length; i++)
        {
            result[i] = ((values[i] - result[i - 1]) * multiplier) + result[i - 1];
        }

        return result;
    }

    public TrendAssessment EvaluateTrend(IReadOnlyList<decimal> closes)
    {
        if (closes.Count < TrendMinBarsPartial)
        {
            return new TrendAssessment(0, false, null);
        }

        var ema20 = CalculateEma(closes, 20)[^1];
        var ema50 = CalculateEma(closes, 50)[^1];
        var close = closes[^1];
        var extensionPct = ema20 == 0m ? 0m : Math.Round(((close - ema20) / ema20) * 100m, 2);

        if (closes.Count < TrendMinBarsFull)
        {
            if (ema20 > ema50)
            {
                return new TrendAssessment(
                    12,
                    false,
                    "4 saatlik uzun ortalama için yeterli mum yok; kısa ortalama ortanın üstünde.",
                    extensionPct);
            }

            if (ema20 < ema50)
            {
                return new TrendAssessment(0, true, null);
            }

            return new TrendAssessment(0, false, null);
        }

        var longPeriod = ResolveTrendLongEmaPeriod(closes.Count);
        var emaLong = CalculateEma(closes, longPeriod)[^1];

        if (ema20 > ema50 && ema50 > emaLong)
        {
            if (extensionPct > 8m)
            {
                return new TrendAssessment(18, false, "4 saatlik yükseliş uzamış; fiyat kısa ortalamadan kopmuş.", extensionPct);
            }

            return new TrendAssessment(25, false, "4 saatlikte kısa ortalama, uzun ortalamanın üstünde.", extensionPct);
        }

        if (ema20 > ema50 && ema50 < emaLong)
        {
            return new TrendAssessment(12, false, "4 saatlik ortalamalar henüz net bir yöne oturmamış.");
        }

        if (ema20 < ema50 && ema50 < emaLong)
        {
            return new TrendAssessment(0, true, null);
        }

        return new TrendAssessment(0, false, null);
    }

    public static int ResolveTrendLongEmaPeriod(int closedBarCount) =>
        closedBarCount >= TrendLongEmaPeriodExtended ? TrendLongEmaPeriodExtended : TrendMinBarsFull;

    public decimal[] FindSwingLows(IEnumerable<Kline> klines, int lookback, int neighborCount)
    {
        var values = klines.TakeLast(lookback).ToArray();
        var lows = new List<decimal>();
        if (values.Length < (neighborCount * 2) + 1)
        {
            return [];
        }

        for (var i = neighborCount; i < values.Length - neighborCount; i++)
        {
            var current = values[i].Low;
            var leftAllHigher = true;
            var rightAllHigher = true;

            for (var n = 1; n <= neighborCount; n++)
            {
                if (values[i - n].Low <= current)
                {
                    leftAllHigher = false;
                    break;
                }
            }

            for (var n = 1; n <= neighborCount; n++)
            {
                if (values[i + n].Low <= current)
                {
                    rightAllHigher = false;
                    break;
                }
            }

            if (leftAllHigher && rightAllHigher)
            {
                lows.Add(current);
            }
        }

        return lows.ToArray();
    }

    public VolumeSentiment DetermineVolumeSentiment(
        IReadOnlyList<Kline> klines,
        int recentCandles,
        int baselineCandles)
    {
        if (klines.Count < recentCandles + baselineCandles + recentCandles)
        {
            throw new ArgumentException("Not enough candles for volume sentiment.");
        }

        var recent = klines.TakeLast(recentCandles).ToArray();
        var baseline = klines.Skip(klines.Count - recentCandles - baselineCandles).Take(baselineCandles).ToArray();
        var previousPriceBlock = klines.Skip(klines.Count - (recentCandles * 2)).Take(recentCandles).ToArray();

        var recentPriceAvg = recent.Average(x => x.Close);
        var previousPriceAvg = previousPriceBlock.Average(x => x.Close);
        var recentVolumeAvg = recent.Average(x => x.Volume);
        var baselineVolumeAvg = baseline.Average(x => x.Volume);

        if (baselineVolumeAvg == 0m)
        {
            return VolumeSentiment.WeakMove;
        }

        var priceUp = recentPriceAvg > previousPriceAvg;
        var priceDown = recentPriceAvg < previousPriceAvg;
        var volumeUp = recentVolumeAvg > baselineVolumeAvg;
        var volumeDown = recentVolumeAvg < baselineVolumeAvg;

        if (priceUp && volumeUp)
        {
            return VolumeSentiment.StrongBuying;
        }

        if (!priceUp && !priceDown && volumeUp)
        {
            return VolumeSentiment.Accumulation;
        }

        if (priceDown && volumeDown)
        {
            return VolumeSentiment.SellingPressureFading;
        }

        if (priceUp && volumeDown)
        {
            return VolumeSentiment.WeakMove;
        }

        if (priceDown && volumeUp)
        {
            return VolumeSentiment.Distribution;
        }

        return VolumeSentiment.WeakMove;
    }

    public HourVolumePace AssessHourVolumePace(
        IReadOnlyList<Kline> hourly,
        IReadOnlyList<Kline> fifteenMinute,
        int baselineCandles,
        DateTimeOffset now,
        Func<Kline, bool>? includeCandle = null)
    {
        const int minimumClosedQuarterHours = 2;
        var hourOpen = CandleClock.Floor(now, TimeSpan.FromHours(1));
        var closedHourly = CandleClock.Closed(hourly, TimeSpan.FromHours(1), now);
        var baseline = closedHourly
            .Where(x => x.OpenTime < hourOpen && (includeCandle is null || includeCandle(x)))
            .OrderBy(x => x.OpenTime)
            .TakeLast(baselineCandles)
            .ToArray();
        if (baseline.Length < baselineCandles)
        {
            return new HourVolumePace(false, VolumeSentiment.WeakMove);
        }

        var closedQuarterHours = ClosedQuarterHours(fifteenMinute, hourOpen, now, includeCandle);
        if (closedQuarterHours.Length < minimumClosedQuarterHours)
        {
            return new HourVolumePace(false, VolumeSentiment.WeakMove);
        }

        var baselineAverage = baseline.Average(x => x.Volume);
        if (baselineAverage <= 0m)
        {
            return new HourVolumePace(false, VolumeSentiment.WeakMove);
        }

        var elapsedMinutes = closedQuarterHours.Length * 15m;
        var requiredVolume = baselineAverage * elapsedMinutes / 60m;
        var actualVolume = closedQuarterHours.Sum(x => x.Volume);
        return PaceFromClosedWindow(closedQuarterHours, actualVolume, requiredVolume);
    }

    public HourVolumePace AssessWindowVolumePace(
        IReadOnlyList<Kline> fifteenMinute,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        IReadOnlyList<(DateTimeOffset Start, DateTimeOffset End)> priorWindows,
        int baselineWindows)
    {
        var current = ClosedQuarterHours(fifteenMinute, windowStart, windowEnd, includeCandle: null);
        if (current.Length < 2)
        {
            return new HourVolumePace(false, VolumeSentiment.WeakMove);
        }

        var baseline = new List<decimal>();
        foreach (var (start, end) in priorWindows)
        {
            var bars = ClosedQuarterHours(fifteenMinute, start, end, includeCandle: null);
            if (bars.Length < 2)
            {
                continue;
            }

            baseline.Add(bars.Sum(x => x.Volume));
        }

        var sample = baseline.TakeLast(baselineWindows).ToArray();
        if (sample.Length < baselineWindows)
        {
            return new HourVolumePace(false, VolumeSentiment.WeakMove);
        }

        var requiredVolume = sample.Average();
        if (requiredVolume <= 0m)
        {
            return new HourVolumePace(false, VolumeSentiment.WeakMove);
        }

        return PaceFromClosedWindow(current, current.Sum(x => x.Volume), requiredVolume);
    }

    public static decimal NearestSupport(IEnumerable<decimal> swingLows, decimal price)
    {
        var supports = swingLows.Where(x => x > 0m).Distinct().OrderByDescending(x => x).ToArray();
        if (supports.Length == 0 || price <= 0m)
        {
            return 0m;
        }

        foreach (var level in supports)
        {
            if (level <= price)
            {
                return level;
            }
        }

        return supports[^1];
    }

    private static Kline[] ClosedQuarterHours(
        IReadOnlyList<Kline> fifteenMinute,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd,
        Func<Kline, bool>? includeCandle)
    {
        return fifteenMinute
            .Where(x => x.OpenTime >= windowStart && x.OpenTime.AddMinutes(15) <= windowEnd && (includeCandle is null || includeCandle(x)))
            .OrderBy(x => x.OpenTime)
            .ToArray();
    }

    private static HourVolumePace PaceFromClosedWindow(IReadOnlyList<Kline> closedQuarterHours, decimal actualVolume, decimal requiredVolume)
    {
        var volumeUp = actualVolume >= requiredVolume;
        var volumeDown = actualVolume < requiredVolume;
        var priceUp = closedQuarterHours[^1].Close > closedQuarterHours[0].Open;
        var priceDown = closedQuarterHours[^1].Close < closedQuarterHours[0].Open;

        if (priceUp && volumeUp)
        {
            return new HourVolumePace(true, VolumeSentiment.StrongBuying);
        }

        if (!priceUp && !priceDown && volumeUp)
        {
            return new HourVolumePace(true, VolumeSentiment.Accumulation);
        }

        if (priceDown && volumeDown)
        {
            return new HourVolumePace(true, VolumeSentiment.SellingPressureFading);
        }

        if (priceUp && volumeDown)
        {
            return new HourVolumePace(true, VolumeSentiment.WeakMove);
        }

        if (priceDown && volumeUp)
        {
            return new HourVolumePace(true, VolumeSentiment.Distribution);
        }

        return new HourVolumePace(true, VolumeSentiment.WeakMove);
    }

    public string? DetectCandlestickPattern(IReadOnlyList<Kline> klines)
    {
        if (klines.Count < 3)
        {
            return null;
        }

        var a = klines[^3];
        var b = klines[^2];
        var c = klines[^1];

        if (IsBullishEngulfing(b, c))
        {
            return "Düşüş mumunu kapatan yükseliş";
        }

        if (IsHammer(c))
        {
            return "Dipten seken çekiç mum";
        }

        if (IsInvertedHammer(c))
        {
            return "Uzun üst fitilli dip mumu";
        }

        if (IsMorningStar(a, b, c))
        {
            return "Üç mumluk dip dönüşü";
        }

        if (IsDragonflyDoji(c))
        {
            return "Uzun alt fitilli kararsız mum";
        }

        if (IsPiercingLine(b, c))
        {
            return "Düşüşün yarısını geri alan yükseliş";
        }

        return null;
    }

    private static bool IsBullishEngulfing(Kline previous, Kline current)
    {
        var previousBearish = previous.Close < previous.Open;
        var currentBullish = current.Close > current.Open;
        return previousBearish
            && currentBullish
            && current.Open <= previous.Close
            && current.Close >= previous.Open;
    }

    private static bool IsHammer(Kline candle)
    {
        var body = Math.Abs(candle.Close - candle.Open);
        var lowerWick = Math.Min(candle.Open, candle.Close) - candle.Low;
        var upperWick = candle.High - Math.Max(candle.Open, candle.Close);
        if (body == 0m)
        {
            return false;
        }

        return lowerWick >= body * 2m && upperWick <= body;
    }

    private static bool IsInvertedHammer(Kline candle)
    {
        var body = Math.Abs(candle.Close - candle.Open);
        var lowerWick = Math.Min(candle.Open, candle.Close) - candle.Low;
        var upperWick = candle.High - Math.Max(candle.Open, candle.Close);
        if (body == 0m)
        {
            return false;
        }

        return upperWick >= body * 2m && lowerWick <= body;
    }

    private static bool IsMorningStar(Kline a, Kline b, Kline c)
    {
        var aBearish = a.Close < a.Open;
        var cBullish = c.Close > c.Open;
        var bSmallBody = Math.Abs(b.Close - b.Open) <= Math.Abs(a.Close - a.Open) * 0.5m;
        var cClosesAboveMid = c.Close >= ((a.Open + a.Close) / 2m);
        return aBearish && bSmallBody && cBullish && cClosesAboveMid;
    }

    private static bool IsDragonflyDoji(Kline candle)
    {
        var range = candle.High - candle.Low;
        if (range == 0m)
        {
            return false;
        }

        var body = Math.Abs(candle.Close - candle.Open);
        var upperWick = candle.High - Math.Max(candle.Open, candle.Close);
        var lowerWick = Math.Min(candle.Open, candle.Close) - candle.Low;
        return body <= range * 0.1m && upperWick <= range * 0.1m && lowerWick >= range * 0.6m;
    }

    private static bool IsPiercingLine(Kline previous, Kline current)
    {
        var previousBearish = previous.Close < previous.Open;
        var currentBullish = current.Close > current.Open;
        var previousMid = (previous.Open + previous.Close) / 2m;
        return previousBearish
            && currentBullish
            && current.Open < previous.Close
            && current.Close > previousMid
            && current.Close < previous.Open;
    }
}

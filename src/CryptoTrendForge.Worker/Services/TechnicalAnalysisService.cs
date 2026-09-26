using CryptoTrendForge.Core.Domain.Models;
using CryptoTrendForge.Core.Domain.Enums;

namespace CryptoTrendForge.Worker.Services;

public sealed class TechnicalAnalysisService
{
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

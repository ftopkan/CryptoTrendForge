using CryptoTrendForge.Core.Domain.Models;
using CryptoTrendForge.Worker.Configuration;
using Microsoft.Extensions.Options;

namespace CryptoTrendForge.Worker.Services;

public sealed class RiskFilterService
{
    private readonly BotOptions _botOptions;
    private readonly TechnicalAnalysisService _technicalAnalysisService;

    public RiskFilterService(
        IOptions<BotOptions> botOptions,
        TechnicalAnalysisService technicalAnalysisService)
    {
        _botOptions = botOptions.Value;
        _technicalAnalysisService = technicalAnalysisService;
    }

    public FilterResult Check(MarketSnapshot snapshot, BtcSnapshot btcSnapshot, BtcRegimeResult regimeResult)
    {
        if (Is4hBearAligned(snapshot.Klines4H))
        {
            return FilterResult.Blocked("4H EMA20 < EMA50 < EMA200 hard filter.");
        }

        // Funding from Bybit arrives as decimal ratio; convert to percentage for threshold comparison.
        var fundingPct = snapshot.FundingRate * 100m;
        if (fundingPct > _botOptions.FundingRateHardFilterPct)
        {
            return FilterResult.Blocked("Funding rate hard filter exceeded.");
        }

        if (snapshot.OpenInterestChangePct1H > _botOptions.OiSpikeHardFilterPct && IsPriceFalling(snapshot.Klines1H, 1))
        {
            return FilterResult.Blocked("Open interest spike with falling price indicates aggressive short build-up.");
        }

        if (IsPriceDump(snapshot.Klines4H, _botOptions.PriceDumpHardFilterPct))
        {
            return FilterResult.Blocked("Last closed 4H candle dropped beyond configured hard filter.");
        }

        if (regimeResult.SkipScan)
        {
            return FilterResult.Blocked(regimeResult.SkipReason ?? "BTC anomalous dump skip.");
        }

        return FilterResult.Allowed();
    }

    private bool Is4hBearAligned(IReadOnlyList<Kline> klines)
    {
        if (klines.Count < 200)
        {
            return false;
        }

        var closes = klines.Select(x => x.Close);
        var ema20 = _technicalAnalysisService.CalculateEma(closes, 20)[^1];
        var ema50 = _technicalAnalysisService.CalculateEma(closes, 50)[^1];
        var ema200 = _technicalAnalysisService.CalculateEma(closes, 200)[^1];

        return ema20 < ema50 && ema50 < ema200;
    }

    private static bool IsPriceFalling(IReadOnlyList<Kline> klines, int lookbackCandles)
    {
        if (klines.Count < lookbackCandles + 1)
        {
            return false;
        }

        return klines[^1].Close < klines[klines.Count - 1 - lookbackCandles].Close;
    }

    private static bool IsPriceDump(IReadOnlyList<Kline> klines, decimal thresholdPct)
    {
        if (klines.Count < 2)
        {
            return false;
        }

        var previous = klines[^2].Close;
        var latest = klines[^1].Close;
        if (previous == 0m)
        {
            return false;
        }

        var dropPct = ((previous - latest) / previous) * 100m;
        return dropPct >= thresholdPct;
    }
}

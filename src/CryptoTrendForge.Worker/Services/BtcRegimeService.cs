using CryptoTrendForge.Core.Domain.Enums;
using CryptoTrendForge.Core.Domain.Models;
using CryptoTrendForge.Worker.Configuration;
using Microsoft.Extensions.Options;

namespace CryptoTrendForge.Worker.Services;

public sealed class BtcRegimeService
{
    private readonly TechnicalAnalysisService _technicalAnalysisService;
    private readonly BotOptions _botOptions;

    public BtcRegimeService(
        TechnicalAnalysisService technicalAnalysisService,
        IOptions<BotOptions> botOptions)
    {
        _technicalAnalysisService = technicalAnalysisService;
        _botOptions = botOptions.Value;
    }

    public BtcRegimeResult DetermineRegime(BtcSnapshot snapshot)
    {
        var result = new BtcRegimeResult();

        if (IsAnomalousDump(snapshot))
        {
            result.Regime = MarketRegime.RiskOff;
            result.SkipScan = true;
            result.SkipReason = "BTC closed 4H candle dropped beyond anomalous dump threshold.";
            return result;
        }

        var ema4h20 = GetLatestEma(snapshot.Klines4H, 20);
        var ema4h50 = GetLatestEma(snapshot.Klines4H, 50);

        if (ema4h20 == 0m || ema4h50 == 0m)
        {
            result.Regime = MarketRegime.Neutral;
            return result;
        }

        var diffPct = ema4h50 == 0m ? 0m : ((ema4h20 - ema4h50) / ema4h50) * 100m;

        if (ema4h20 > ema4h50 && diffPct > _botOptions.BtcEmaNeutralBandPct)
        {
            result.Regime = MarketRegime.RiskOn;
        }
        else if (ema4h20 < ema4h50)
        {
            result.Regime = MarketRegime.RiskOff;
        }
        else
        {
            result.Regime = MarketRegime.Neutral;
        }

        if (snapshot.Klines1H.Count >= _botOptions.RsiPeriod * 2)
        {
            var rsi1h = _technicalAnalysisService.CalculateRsi(snapshot.Klines1H, _botOptions.RsiPeriod);

            if (result.Regime == MarketRegime.RiskOn && rsi1h > 75m)
            {
                result.Regime = MarketRegime.Neutral;
            }
            else if (result.Regime == MarketRegime.RiskOff && rsi1h < 25m)
            {
                result.Regime = MarketRegime.Neutral;
            }
        }

        return result;
    }

    private bool IsAnomalousDump(BtcSnapshot snapshot)
    {
        if (snapshot.Klines4H.Count < 2)
        {
            return false;
        }

        var previous = snapshot.Klines4H[^2].Close;
        var latest = snapshot.Klines4H[^1].Close;
        if (previous == 0m)
        {
            return false;
        }

        var dropPct = ((previous - latest) / previous) * 100m;
        return dropPct >= _botOptions.BtcAnomalousDumpPct;
    }

    private decimal GetLatestEma(IReadOnlyList<Kline> klines, int period)
    {
        if (klines.Count < period)
        {
            return 0m;
        }

        var ema = _technicalAnalysisService.CalculateEma(klines.Select(x => x.Close), period);
        return ema[^1];
    }
}

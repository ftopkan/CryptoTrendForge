using CryptoTrendForge.Core.Domain.Enums;
using CryptoTrendForge.Core.Domain.Models;
using CryptoTrendForge.Worker.Configuration;
using Microsoft.Extensions.Options;

namespace CryptoTrendForge.Worker.Services;

public sealed class SignalEngine
{
    private readonly TechnicalAnalysisService _technicalAnalysisService;
    private readonly BotOptions _botOptions;

    public SignalEngine(
        TechnicalAnalysisService technicalAnalysisService,
        IOptions<BotOptions> botOptions)
    {
        _technicalAnalysisService = technicalAnalysisService;
        _botOptions = botOptions.Value;
    }

    public ScoreResult CalculateScore(MarketSnapshot snapshot, MarketRegime regime)
    {
        var breakdown = new Dictionary<string, int>();
        var reasons = new List<string>();
        var risks = new List<string>();

        var trendScore = ScoreTrend(snapshot, out var isBearAligned, out var trendReason);
        breakdown["trend"] = trendScore;
        if (!string.IsNullOrWhiteSpace(trendReason))
        {
            reasons.Add(trendReason);
        }

        if (isBearAligned)
        {
            risks.Add("4H EMA alignment remains bearish.");
        }

        var rsiScore = ScoreRsi(snapshot, reasons, risks);
        breakdown["rsi"] = rsiScore;

        var volumeScore = ScoreVolume(snapshot, reasons, risks);
        breakdown["volume"] = volumeScore;

        var supportScore = ScoreSupport(snapshot, reasons, risks, out var supportDistancePct, out var supportLevel);
        breakdown["support"] = supportScore;

        var oiScore = ScoreOpenInterest(snapshot, reasons, risks);
        breakdown["oi"] = oiScore;

        var baseScore = trendScore + rsiScore + volumeScore + supportScore + oiScore;
        baseScore = Math.Clamp(baseScore, 0, 100);

        var patternMain = DetectMainPattern(snapshot);
        var pattern15m = _technicalAnalysisService.DetectCandlestickPattern(snapshot.Klines15M);
        var patternBonus = _botOptions.PatternBonusEnabled && patternMain is not null
            ? _botOptions.PatternBonusPoints
            : 0;
        breakdown["pattern_bonus"] = patternBonus;

        if (patternBonus > 0)
        {
            reasons.Add($"Pattern confirmation: {patternMain}.");
            if (supportScore >= 13)
            {
                reasons.Add($"Support zone + {patternMain} confirmation suggests stronger reversal context.");
            }
        }

        ApplyRegimeRiskNotes(regime, risks);

        return new ScoreResult
        {
            BaseScore = baseScore,
            PatternBonus = patternBonus,
            TotalScore = baseScore + patternBonus,
            SupportLevel = supportLevel,
            SupportDistancePct = Math.Round(supportDistancePct, 2),
            PatternName = patternMain,
            PatternName15m = pattern15m,
            Breakdown = breakdown,
            Reasons = reasons,
            Risks = risks,
            RawSnapshot = new
            {
                snapshot.Symbol,
                snapshot.CurrentPrice,
                snapshot.FundingRate,
                snapshot.OpenInterestChangePct1H,
                snapshot.OpenInterestChangePct4H,
                supportLevel,
                supportDistancePct
            }
        };
    }

    private int ScoreTrend(MarketSnapshot snapshot, out bool isBearAligned, out string? reason)
    {
        isBearAligned = false;
        reason = null;
        if (snapshot.Klines4H.Count < 200 || snapshot.Klines1H.Count < 50)
        {
            return 0;
        }

        var ema4h20 = _technicalAnalysisService.CalculateEma(snapshot.Klines4H.Select(x => x.Close), 20)[^1];
        var ema4h50 = _technicalAnalysisService.CalculateEma(snapshot.Klines4H.Select(x => x.Close), 50)[^1];
        var ema4h200 = _technicalAnalysisService.CalculateEma(snapshot.Klines4H.Select(x => x.Close), 200)[^1];

        var baseTrendScore = 0;
        if (ema4h20 > ema4h50 && ema4h50 > ema4h200)
        {
            baseTrendScore = 25;
            reason = "4H EMA structure shows full bullish alignment.";
        }
        else if (ema4h20 > ema4h50 && ema4h50 < ema4h200)
        {
            baseTrendScore = 12;
            reason = "4H EMA structure is in transition.";
        }
        else if (ema4h20 < ema4h50 && ema4h50 > ema4h200)
        {
            baseTrendScore = 0;
        }
        else if (ema4h20 < ema4h50 && ema4h50 < ema4h200)
        {
            baseTrendScore = 0;
            isBearAligned = true;
        }

        var ema1h20 = _technicalAnalysisService.CalculateEma(snapshot.Klines1H.Select(x => x.Close), 20)[^1];
        var ema1h50 = _technicalAnalysisService.CalculateEma(snapshot.Klines1H.Select(x => x.Close), 50)[^1];
        if (ema1h20 > ema1h50 && baseTrendScore > 0)
        {
            baseTrendScore = Math.Min(25, baseTrendScore + 3);
        }

        return baseTrendScore;
    }

    private int ScoreRsi(MarketSnapshot snapshot, List<string> reasons, List<string> risks)
    {
        if (snapshot.Klines4H.Count < _botOptions.RsiPeriod * 2
            || snapshot.Klines1H.Count < _botOptions.RsiPeriod * 2
            || snapshot.Klines15M.Count < _botOptions.RsiPeriod * 2)
        {
            return 0;
        }

        var rsi4h = _technicalAnalysisService.CalculateRsi(snapshot.Klines4H, _botOptions.RsiPeriod);
        var rsi1h = _technicalAnalysisService.CalculateRsi(snapshot.Klines1H, _botOptions.RsiPeriod);
        var rsi15m = _technicalAnalysisService.CalculateRsi(snapshot.Klines15M, _botOptions.RsiPeriod);

        var weighted = (ScoreRsiBucket(rsi4h) * 0.5m) + (ScoreRsiBucket(rsi1h) * 0.3m) + (ScoreRsiBucket(rsi15m) * 0.2m);
        var score = (int)Math.Round(weighted, MidpointRounding.AwayFromZero);

        if (rsi4h <= 35m || rsi1h <= 35m)
        {
            reasons.Add("RSI profile indicates oversold recovery potential across higher timeframes.");
        }

        if (rsi4h > 65m)
        {
            risks.Add("4H RSI is elevated; upside continuation may be limited.");
        }

        return score;
    }

    private int ScoreVolume(MarketSnapshot snapshot, List<string> reasons, List<string> risks)
    {
        if (snapshot.Klines15M.Count < _botOptions.VolumeRecentCandles + _botOptions.VolumeBaselineCandles + _botOptions.VolumeRecentCandles)
        {
            return 0;
        }

        var sentiment = _technicalAnalysisService.DetermineVolumeSentiment(
            snapshot.Klines15M,
            _botOptions.VolumeRecentCandles,
            _botOptions.VolumeBaselineCandles);

        var score = sentiment switch
        {
            VolumeSentiment.StrongBuying => 20,
            VolumeSentiment.Accumulation => 10,
            VolumeSentiment.SellingPressureFading => 8,
            VolumeSentiment.WeakMove => 3,
            VolumeSentiment.Distribution => -10,
            _ => 0
        };

        if (score >= 10)
        {
            reasons.Add("Volume context supports buying-side participation.");
        }
        else if (sentiment == VolumeSentiment.Distribution)
        {
            risks.Add("Volume expands while price weakens (distribution risk).");
        }

        return score;
    }

    private int ScoreSupport(
        MarketSnapshot snapshot,
        List<string> reasons,
        List<string> risks,
        out decimal supportDistancePct,
        out decimal supportLevel)
    {
        supportDistancePct = 0m;
        supportLevel = 0m;

        var swing4h = _technicalAnalysisService.FindSwingLows(snapshot.Klines4H, _botOptions.SwingLookbackCandles, _botOptions.SwingNeighborCount);
        var swing1h = _technicalAnalysisService.FindSwingLows(snapshot.Klines1H, _botOptions.SwingLookbackCandles, _botOptions.SwingNeighborCount);
        var supports = swing4h.Concat(swing1h)
            .Where(x => x > 0m)
            .Distinct()
            .OrderByDescending(x => x)
            .ToArray();

        if (supports.Length == 0 || snapshot.CurrentPrice <= 0m)
        {
            return 0;
        }

        var belowOrNear = supports.Where(x => x <= snapshot.CurrentPrice).Take(2).ToArray();
        if (belowOrNear.Length == 0)
        {
            belowOrNear = supports.Take(2).ToArray();
        }

        supportLevel = belowOrNear.Average();
        supportDistancePct = ((snapshot.CurrentPrice - supportLevel) / supportLevel) * 100m;

        int score;
        if (snapshot.CurrentPrice < supportLevel)
        {
            score = -15;
            risks.Add("Price is below computed support zone.");
        }
        else if (supportDistancePct <= 1.5m)
        {
            score = 20;
        }
        else if (supportDistancePct <= 3m)
        {
            score = 13;
        }
        else if (supportDistancePct <= 5m)
        {
            score = 5;
        }
        else
        {
            score = 0;
        }

        if (score >= 13)
        {
            reasons.Add("Price is trading close to support zone.");
        }

        if (HasBreakoutRetest(snapshot.Klines1H))
        {
            score = Math.Min(20, score + 5);
            reasons.Add("Recent breakout-retest structure adds support confirmation.");
        }

        return score;
    }

    private int ScoreOpenInterest(MarketSnapshot snapshot, List<string> reasons, List<string> risks)
    {
        if (snapshot.Klines1H.Count < 2)
        {
            return 0;
        }

        var latest = snapshot.Klines1H[^1].Close;
        var previous = snapshot.Klines1H[^2].Close;
        var priceUp = latest > previous;
        var priceDown = latest < previous;
        var oiUp = snapshot.OpenInterestChangePct4H > 0m;
        var oiDown = snapshot.OpenInterestChangePct4H < 0m;

        var score = 0;
        if (priceUp && oiUp)
        {
            score = 15;
            reasons.Add("Price rise with rising OI supports sustained participation.");
        }
        else if (priceDown && oiDown)
        {
            score = 8;
        }
        else if (priceUp && oiDown)
        {
            score = 5;
        }
        else if (priceDown && oiUp)
        {
            score = -5;
            risks.Add("Price weakness with rising OI can indicate short pressure.");
        }

        return score;
    }

    private static int ScoreRsiBucket(decimal rsi)
    {
        return rsi switch
        {
            <= 25m => 20,
            <= 35m => 18,
            <= 45m => 10,
            <= 55m => 4,
            <= 65m => 0,
            _ => -5
        };
    }

    private static bool HasBreakoutRetest(IReadOnlyList<Kline> klines1h)
    {
        if (klines1h.Count < 20)
        {
            return false;
        }

        var recent = klines1h.TakeLast(20).ToArray();
        var resistance = recent.Take(15).Max(x => x.High);
        var breakoutHappened = recent.Skip(15).Any(x => x.Close > resistance);
        var latest = recent[^1];
        var retest = latest.Low <= resistance * 1.01m && latest.Close >= resistance;
        return breakoutHappened && retest;
    }

    private string? DetectMainPattern(MarketSnapshot snapshot)
    {
        var pattern4h = _technicalAnalysisService.DetectCandlestickPattern(snapshot.Klines4H);
        if (!string.IsNullOrWhiteSpace(pattern4h))
        {
            return pattern4h;
        }

        return _technicalAnalysisService.DetectCandlestickPattern(snapshot.Klines1H);
    }

    private static void ApplyRegimeRiskNotes(MarketRegime regime, List<string> risks)
    {
        if (regime == MarketRegime.RiskOff)
        {
            risks.Add("BTC regime is RISK_OFF; thresholds become strict or signals blocked.");
        }
        else if (regime == MarketRegime.Neutral)
        {
            risks.Add("BTC regime is NEUTRAL; confirmation quality should be higher.");
        }
    }
}

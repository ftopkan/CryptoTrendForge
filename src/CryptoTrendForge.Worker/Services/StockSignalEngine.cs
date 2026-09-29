using CryptoTrendForge.Core.Domain.Enums;
using CryptoTrendForge.Core.Domain.Models;
using CryptoTrendForge.Worker.Configuration;
using Microsoft.Extensions.Options;

namespace CryptoTrendForge.Worker.Services;

public sealed class StockSignalEngine
{
    private readonly TechnicalAnalysisService _technicalAnalysisService;
    private readonly BotOptions _botOptions;
    private readonly StockOptions _stockOptions;

    public StockSignalEngine(
        TechnicalAnalysisService technicalAnalysisService,
        IOptions<BotOptions> botOptions,
        IOptions<StockOptions> stockOptions)
    {
        _technicalAnalysisService = technicalAnalysisService;
        _botOptions = botOptions.Value;
        _stockOptions = stockOptions.Value;
    }

    public ScoreResult CalculateScore(MarketSnapshot snapshot)
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
            risks.Add("4 saatlik ortalamalar düşüş sırasında.");
        }

        var rsiScore = ScoreRsi(snapshot, reasons, risks);
        breakdown["rsi"] = rsiScore;

        var volumeScore = ScoreVolume(snapshot, reasons, risks);
        breakdown["volume"] = volumeScore;

        var supportScore = ScoreSupport(snapshot, reasons, risks, out var supportDistancePct, out var supportLevel);
        breakdown["support"] = supportScore;

        var oiScore = ScoreOpenInterest(snapshot, reasons, risks);
        breakdown["oi"] = oiScore;

        var baseScore = Math.Clamp(trendScore + rsiScore + volumeScore + supportScore + oiScore, 0, 100);
        var patternMain = _technicalAnalysisService.DetectCandlestickPattern(snapshot.Klines4H);
        var pattern15m = _technicalAnalysisService.DetectCandlestickPattern(snapshot.Klines15M);
        var patternBonus = _botOptions.PatternBonusEnabled && patternMain is not null
            ? _botOptions.PatternBonusPoints
            : 0;
        breakdown["pattern_bonus"] = patternBonus;

        if (patternBonus > 0)
        {
            reasons.Add($"Mum yapısı: {patternMain}.");
            if (supportScore >= 13)
            {
                reasons.Add($"Destekte bu mum yapısı var: {patternMain}. Dönüş ihtimali artıyor.");
            }
        }

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
                supportDistancePct,
                asset = "stock"
            }
        };
    }

    private int ScoreTrend(MarketSnapshot snapshot, out bool isBearAligned, out string? reason)
    {
        isBearAligned = false;
        reason = null;
        if (snapshot.Klines4H.Count < 200)
        {
            return 0;
        }

        var ema4h20 = _technicalAnalysisService.CalculateEma(snapshot.Klines4H.Select(x => x.Close), 20)[^1];
        var ema4h50 = _technicalAnalysisService.CalculateEma(snapshot.Klines4H.Select(x => x.Close), 50)[^1];
        var ema4h200 = _technicalAnalysisService.CalculateEma(snapshot.Klines4H.Select(x => x.Close), 200)[^1];

        if (ema4h20 > ema4h50 && ema4h50 > ema4h200)
        {
            reason = "4 saatlikte kısa ortalama, uzun ortalamanın üstünde.";
            return 25;
        }

        if (ema4h20 > ema4h50 && ema4h50 < ema4h200)
        {
            reason = "4 saatlik ortalamalar henüz net bir yöne oturmamış.";
            return 12;
        }

        if (ema4h20 < ema4h50 && ema4h50 < ema4h200)
        {
            isBearAligned = true;
        }

        return 0;
    }

    private int ScoreRsi(MarketSnapshot snapshot, List<string> reasons, List<string> risks)
    {
        if (snapshot.Klines4H.Count < _botOptions.RsiPeriod * 2)
        {
            return 0;
        }

        var rsi4h = _technicalAnalysisService.CalculateRsi(snapshot.Klines4H, _botOptions.RsiPeriod);
        var score = ScoreRsiBucket(rsi4h);
        if (rsi4h <= 35m)
        {
            reasons.Add("RSI düşük; satış baskısı azalmış, toparlanma ihtimali var.");
        }

        if (rsi4h > 65m)
        {
            risks.Add("4 saatlik RSI yüksek; yükseliş yorulmuş olabilir.");
        }

        return score;
    }

    private int ScoreVolume(MarketSnapshot snapshot, List<string> reasons, List<string> risks)
    {
        var sessionCandles = snapshot.Klines1H
            .Where(x => UsEquitySession.IsOpen(x.OpenTime, _stockOptions))
            .ToArray();
        var needed = _botOptions.VolumeRecentCandles + _botOptions.VolumeBaselineCandles + _botOptions.VolumeRecentCandles;
        if (sessionCandles.Length < needed)
        {
            return 0;
        }

        var sentiment = _technicalAnalysisService.DetermineVolumeSentiment(
            sessionCandles,
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
            reasons.Add("Seans hacmi alıcıların tarafında.");
        }
        else if (sentiment == VolumeSentiment.Distribution)
        {
            risks.Add("Fiyat düşerken seans hacmi artıyor; satış baskısı güçlü.");
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
        var supports = swing4h
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
            risks.Add("Fiyat destek seviyesinin altında.");
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
            reasons.Add("Fiyat desteğe yakın.");
        }

        if (HasBreakoutRetest(snapshot.Klines4H))
        {
            score = Math.Min(20, score + 5);
            reasons.Add("Fiyat direnci kırıp geri test etmiş; bu seviye artık destek.");
        }

        return score;
    }

    private int ScoreOpenInterest(MarketSnapshot snapshot, List<string> reasons, List<string> risks)
    {
        if (snapshot.Klines4H.Count < 2)
        {
            return 0;
        }

        var latest = snapshot.Klines4H[^1].Close;
        var previous = snapshot.Klines4H[^2].Close;
        var priceUp = latest > previous;
        var priceDown = latest < previous;
        var oiUp = snapshot.OpenInterestChangePct4H > 0m;
        var oiDown = snapshot.OpenInterestChangePct4H < 0m;

        if (priceUp && oiUp)
        {
            reasons.Add("Fiyat ve açık işlem sayısı birlikte artıyor; yeni alım var.");
            return 15;
        }

        if (priceDown && oiDown)
        {
            return 8;
        }

        if (priceUp && oiDown)
        {
            return 5;
        }

        if (priceDown && oiUp)
        {
            risks.Add("Fiyat düşerken açık işlem artıyor; satış baskısı gelebilir.");
            return -5;
        }

        return 0;
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

    private static bool HasBreakoutRetest(IReadOnlyList<Kline> klines4h)
    {
        if (klines4h.Count < 15)
        {
            return false;
        }

        var recent = klines4h.TakeLast(15).ToArray();
        var resistance = recent.Take(10).Max(x => x.High);
        var breakoutHappened = recent.Skip(10).Any(x => x.Close > resistance);
        var latest = recent[^1];
        return breakoutHappened && latest.Low <= resistance * 1.01m && latest.Close >= resistance;
    }
}

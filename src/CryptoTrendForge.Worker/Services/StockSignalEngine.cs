using CryptoTrendForge.Core.Domain;
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

    public ScoreResult CalculateScore(MarketSnapshot snapshot, DateTimeOffset? asOf = null)
    {
        var now = asOf ?? DateTimeOffset.UtcNow;
        snapshot = UsEquitySession.WithSessionFourHourCandles(snapshot, _stockOptions);
        snapshot = WithSessionHourCandles(snapshot);
        snapshot = WithClosedStructure(snapshot, now);
        var breakdown = new Dictionary<string, int>();
        var reasons = new List<string>();
        var risks = new List<string>();

        var trendScore = ScoreTrend(snapshot, out var isBearAligned, out var trendReason, out var ema20ExtensionPct);
        breakdown["trend"] = trendScore;
        if (!string.IsNullOrWhiteSpace(trendReason))
        {
            reasons.Add(trendReason);
        }

        if (isBearAligned)
        {
            risks.Add("4 saatlik ortalamalar düşüş sırasında.");
        }

        var rsiScore = ScoreRsi(snapshot, reasons, risks, out var rsi1h);
        breakdown["rsi"] = rsiScore;

        var volumeScore = ScoreVolume(snapshot, now, reasons, risks);
        breakdown["volume"] = volumeScore;

        var supportScore = ScoreSupport(snapshot, reasons, risks, out var supportDistancePct, out var supportLevel);
        breakdown["support"] = supportScore;

        var baseScore = Math.Clamp(trendScore + rsiScore + volumeScore + supportScore, 0, 100);
        var patternMain = _technicalAnalysisService.DetectCandlestickPattern(snapshot.Klines4H);
        var pattern15m = _technicalAnalysisService.DetectCandlestickPattern(snapshot.Klines15M);
        var patternBonus = _botOptions.PatternBonusEnabled && patternMain is not null
            ? _botOptions.PatternBonusPoints
            : 0;
        breakdown["pattern_bonus"] = patternBonus;

        if (patternBonus > 0)
        {
            reasons.Add($"Mum yapısı: {patternMain}.");
            if (supportScore >= SupportNear)
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
            Rsi4H = rsi1h,
            Ema20ExtensionPct = ema20ExtensionPct,
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
                rsi1h,
                ema20ExtensionPct,
                asset = "stock"
            }
        };
    }

    private int ScoreTrend(MarketSnapshot snapshot, out bool isBearAligned, out string? reason, out decimal? ema20ExtensionPct)
    {
        var trend = _technicalAnalysisService.EvaluateTrend(snapshot.Klines4H.Select(x => x.Close).ToArray());
        isBearAligned = trend.IsBearAligned;
        reason = trend.Reason;
        ema20ExtensionPct = trend.Ema20ExtensionPct;
        return trend.Score;
    }

    private int ScoreRsi(MarketSnapshot snapshot, List<string> reasons, List<string> risks, out decimal? rsi1h)
    {
        rsi1h = null;
        if (snapshot.Klines1H.Count < _botOptions.RsiPeriod * 2)
        {
            return 0;
        }

        rsi1h = _technicalAnalysisService.CalculateRsi(snapshot.Klines1H, _botOptions.RsiPeriod);
        var score = ScoreRsiBucket(rsi1h.Value);
        if (rsi1h <= 35m)
        {
            reasons.Add("RSI düşük; satış baskısı azalmış, toparlanma ihtimali var.");
        }
        else if (rsi1h <= 55m)
        {
            reasons.Add("RSI orta bandda; trend içi sağlıklı geri çekilme bölgesi.");
        }

        if (rsi1h > 65m)
        {
            risks.Add("1 saatlik RSI yüksek; yükseliş yorulmuş olabilir.");
        }

        return score;
    }

    private int ScoreVolume(MarketSnapshot snapshot, DateTimeOffset now, List<string> reasons, List<string> risks)
    {
        var pace = _technicalAnalysisService.AssessHourVolumePace(
            snapshot.Klines1H,
            snapshot.Klines15M,
            _botOptions.VolumeBaselineCandles,
            now,
            candle => UsEquitySession.IsOpen(candle.OpenTime, _stockOptions));
        if (!pace.IsReady)
        {
            pace = AssessOpeningVolume(snapshot.Klines15M, now);
        }

        if (!pace.IsReady)
        {
            return 0;
        }

        var sentiment = pace.Sentiment;

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
            reasons.Add("Bu seans saatinde biriken hacim, geçen süreye düşen payı geçmiş.");
        }
        else if (sentiment == VolumeSentiment.Distribution)
        {
            risks.Add("Fiyat düşerken seans hacmi artıyor; satış baskısı güçlü.");
        }

        return score;
    }

    private HourVolumePace AssessOpeningVolume(IReadOnlyList<Kline> fifteenMinute, DateTimeOffset now)
    {
        if (!UsEquitySession.IsOpeningVolumeInterval(now, _stockOptions))
        {
            return new HourVolumePace(false, VolumeSentiment.WeakMove);
        }

        var windows = UsEquitySession.RecentOpeningWindows(now, _stockOptions, 30);
        if (windows.Count < 2)
        {
            return new HourVolumePace(false, VolumeSentiment.WeakMove);
        }

        var current = windows[^1];
        var prior = windows.Take(windows.Count - 1).Select(x => (x.StartUtc, x.EndUtc)).ToArray();
        return _technicalAnalysisService.AssessWindowVolumePace(
            fifteenMinute,
            current.StartUtc,
            current.EndUtc,
            prior,
            _botOptions.VolumeBaselineCandles);
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

        var swing1h = _technicalAnalysisService.FindSwingLows(snapshot.Klines1H, _botOptions.SwingLookbackCandles, _botOptions.SwingNeighborCount);
        supportLevel = TechnicalAnalysisService.NearestSupport(swing1h, snapshot.CurrentPrice);
        if (supportLevel == 0m)
        {
            return 0;
        }

        supportDistancePct = ((snapshot.CurrentPrice - supportLevel) / supportLevel) * 100m;

        int score;
        if (snapshot.CurrentPrice < supportLevel)
        {
            score = ScaleSupport(-15);
            risks.Add("Fiyat destek seviyesinin altında.");
        }
        else if (supportDistancePct <= 1.5m)
        {
            score = SupportMax;
        }
        else if (supportDistancePct <= 3m)
        {
            score = ScaleSupport(13);
        }
        else if (supportDistancePct <= 5m)
        {
            score = ScaleSupport(5);
        }
        else
        {
            score = 0;
        }

        if (score >= SupportNear)
        {
            reasons.Add("Fiyat desteğe yakın.");
        }

        if (HasBreakoutRetest(snapshot.Klines1H))
        {
            score = Math.Min(SupportMax, score + ScaleSupport(5));
            reasons.Add("Fiyat direnci kırıp geri test etmiş; bu seviye artık destek.");
        }

        return score;
    }

    private const int SupportMax = 35;
    private const int SupportNear = 23;

    private static int ScaleSupport(int cryptoPoints)
    {
        return (int)Math.Round(cryptoPoints * (SupportMax / 20m), MidpointRounding.AwayFromZero);
    }

    private static int ScoreRsiBucket(decimal rsi)
    {
        return rsi switch
        {
            <= 25m => 20,
            <= 35m => 18,
            <= 45m => 12,
            <= 55m => 5,
            <= 65m => 0,
            _ => -5
        };
    }

    private MarketSnapshot WithSessionHourCandles(MarketSnapshot snapshot)
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
            Klines1H = snapshot.Klines1H.Where(candle => UsEquitySession.IsOpen(candle.OpenTime, _stockOptions)).ToArray(),
            Klines4H = snapshot.Klines4H
        };
    }

    private static bool HasBreakoutRetest(IReadOnlyList<Kline> klines1h)
    {
        if (klines1h.Count < 15)
        {
            return false;
        }

        var recent = klines1h.TakeLast(15).ToArray();
        var resistance = recent.Take(10).Max(x => x.High);
        var breakoutHappened = recent.Skip(10).Any(x => x.Close > resistance);
        var latest = recent[^1];
        return breakoutHappened && latest.Low <= resistance * 1.01m && latest.Close >= resistance;
    }

    private static MarketSnapshot WithClosedStructure(MarketSnapshot snapshot, DateTimeOffset now)
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
            Klines15M = CandleClock.Closed(snapshot.Klines15M, TimeSpan.FromMinutes(15), now),
            Klines1H = CandleClock.Closed(snapshot.Klines1H, TimeSpan.FromHours(1), now),
            Klines4H = CandleClock.Closed(snapshot.Klines4H, TimeSpan.FromHours(4), now)
        };
    }
}

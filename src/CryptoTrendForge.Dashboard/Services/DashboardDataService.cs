using System.Text.Json;
using CryptoTrendForge.Core.Domain.Enums;
using CryptoTrendForge.Core.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace CryptoTrendForge.Dashboard.Services;

public sealed class DashboardDataService
{
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

    public DashboardDataService(IDbContextFactory<AppDbContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public async Task<IReadOnlyList<SignalListItem>> GetSignalsAsync(SignalFilter filter, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var query = db.Signals
            .AsNoTracking()
            .Include(x => x.Coin)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Symbol))
        {
            query = query.Where(x => x.Coin != null && x.Coin.Symbol == filter.Symbol.Trim().ToUpperInvariant());
        }

        if (filter.FromUtc.HasValue)
        {
            query = query.Where(x => x.CreatedAt >= filter.FromUtc.Value);
        }

        if (filter.ToUtc.HasValue)
        {
            query = query.Where(x => x.CreatedAt <= filter.ToUtc.Value);
        }

        if (filter.MinScore.HasValue)
        {
            query = query.Where(x => x.TotalScore >= filter.MinScore.Value);
        }

        if (filter.Status.HasValue)
        {
            query = query.Where(x => x.Status == filter.Status.Value);
        }

        if (filter.Regime.HasValue)
        {
            query = query.Where(x => x.MarketRegime == filter.Regime.Value);
        }

        if (filter.CoinType.HasValue)
        {
            query = query.Where(x => x.Coin != null && x.Coin.CoinType == filter.CoinType.Value);
        }

        if (filter.PatternBonusOnly)
        {
            query = query.Where(x => x.PatternBonus > 0);
        }

        return await query
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new SignalListItem
            {
                Id = x.Id,
                Symbol = x.Coin != null ? x.Coin.Symbol : "?",
                CoinType = x.Coin != null ? x.Coin.CoinType : CoinType.Crypto,
                TotalScore = x.TotalScore,
                BaseScore = x.Score,
                PatternBonus = x.PatternBonus,
                Status = x.Status,
                Regime = x.MarketRegime,
                SignalType = x.SignalType,
                CreatedAt = x.CreatedAt
            })
            .Take(500)
            .ToListAsync(cancellationToken);
    }

    public async Task<SignalDetailView?> GetSignalDetailAsync(int signalId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var signal = await db.Signals
            .AsNoTracking()
            .Include(x => x.Coin)
            .FirstOrDefaultAsync(x => x.Id == signalId, cancellationToken);

        if (signal is null)
        {
            return null;
        }

        var outcomes = await db.SignalOutcomes
            .AsNoTracking()
            .Where(x => x.SignalId == signalId)
            .OrderBy(x => x.MinutesElapsed)
            .ToListAsync(cancellationToken);

        return new SignalDetailView
        {
            Id = signal.Id,
            Symbol = signal.Coin?.Symbol ?? "?",
            CoinType = signal.Coin?.CoinType ?? CoinType.Crypto,
            BaseScore = signal.Score,
            PatternBonus = signal.PatternBonus,
            TotalScore = signal.TotalScore,
            Status = signal.Status,
            Regime = signal.MarketRegime,
            SignalType = signal.SignalType,
            SignalPrice = signal.SignalPrice,
            EntryPrice = signal.EntryPrice,
            StopPrice = signal.StopPrice,
            StopMinutes = signal.StopMinutes,
            CautiousExit = signal.CautiousExit,
            CautiousMinutes = signal.CautiousMinutes,
            BalancedExit = signal.BalancedExit,
            BalancedMinutes = signal.BalancedMinutes,
            WideExit = signal.WideExit,
            WideMinutes = signal.WideMinutes,
            TargetsClosedAt = signal.TargetsClosedAt,
            SupportLevel = signal.SupportLevel,
            SupportDistPct = signal.SupportDistPct,
            FundingRate = signal.FundingRate,
            PatternMain = signal.Pattern1H4H,
            Pattern15m = signal.Pattern15M,
            CreatedAt = signal.CreatedAt,
            ExpiresAt = signal.ExpiresAt,
            Breakdown = ParseBreakdown(signal.ScoreBreakdown),
            Reasons = ParseStringArray(signal.Reasons),
            Risks = ParseStringArray(signal.Risks),
            Outcomes = outcomes
                .Select(x => new OutcomePoint
                {
                    MinutesElapsed = x.MinutesElapsed,
                    PriceAt = x.PriceAt,
                    PriceChangePct = x.PriceChangePct,
                    SnapshotAt = x.SnapshotAt
                })
                .ToList()
        };
    }

    public async Task<IReadOnlyList<ExitTargetRow>> GetExitTargetsAsync(
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtc,
        CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var query = db.Signals
            .AsNoTracking()
            .Where(x => x.EntryPrice != null);

        if (fromUtc.HasValue)
        {
            query = query.Where(x => x.CreatedAt >= fromUtc.Value);
        }

        if (toUtc.HasValue)
        {
            query = query.Where(x => x.CreatedAt <= toUtc.Value);
        }

        return await query
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => new ExitTargetRow
            {
                Id = x.Id,
                Symbol = x.Coin != null ? x.Coin.Symbol : "?",
                CoinType = x.Coin != null ? x.Coin.CoinType : CoinType.Crypto,
                CreatedAt = x.CreatedAt,
                EntryPrice = x.EntryPrice,
                StopPrice = x.StopPrice,
                StopMinutes = x.StopMinutes,
                CautiousExit = x.CautiousExit,
                CautiousMinutes = x.CautiousMinutes,
                BalancedExit = x.BalancedExit,
                BalancedMinutes = x.BalancedMinutes,
                WideExit = x.WideExit,
                WideMinutes = x.WideMinutes,
                TargetsClosedAt = x.TargetsClosedAt
            })
            .Take(500)
            .ToListAsync(cancellationToken);
    }

    public async Task<PerformanceSnapshot> GetPerformanceAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var outcomes = await db.SignalOutcomes
            .AsNoTracking()
            .Include(x => x.Signal!)
            .ThenInclude(x => x.Coin)
            .Where(x => x.MinutesElapsed == 60)
            .Where(x => x.Signal != null && x.Signal.Coin != null)
            .ToListAsync(cancellationToken);

        var total = outcomes.Count;
        var success = outcomes.Count(x => x.PriceChangePct > 0m);
        var mapped = outcomes
            .Where(x => x.Signal is not null && x.Signal.Coin is not null)
            .Select(x => new PerformancePoint
            {
                Coin = x.Signal!.Coin!.Symbol,
                PatternBonus = x.Signal.PatternBonus,
                Regime = x.Signal.MarketRegime,
                PriceChangePct = x.PriceChangePct
            })
            .ToList();

        var byCoin = mapped
            .GroupBy(x => x.Coin)
            .Select(g => new PerformanceBucket
            {
                Key = g.Key,
                Total = g.Count(),
                SuccessRatePct = Math.Round((decimal)g.Count(x => x.PriceChangePct > 0m) * 100m / Math.Max(1, g.Count()), 2)
            })
            .OrderByDescending(x => x.SuccessRatePct)
            .ToList();

        var withPattern = mapped.Where(x => x.PatternBonus > 0).ToList();
        var withoutPattern = mapped.Where(x => x.PatternBonus == 0).ToList();

        return new PerformanceSnapshot
        {
            TotalSignalsAt1H = total,
            OverallSuccessRatePct = total == 0 ? 0m : Math.Round((decimal)success * 100m / total, 2),
            AvgChange1HPct = total == 0 ? 0m : Math.Round(outcomes.Average(x => x.PriceChangePct), 2),
            AvgChange4HPct = await CalculateAverageChangeAtMinutes(db, 240, cancellationToken),
            CoinBuckets = byCoin,
            PatternWithSuccessRate = ComputeSuccessRate(withPattern),
            PatternWithoutSuccessRate = ComputeSuccessRate(withoutPattern),
            RegimeBuckets = BuildRegimeBuckets(mapped),
            ExitTargets = await BuildExitTargetSummaryAsync(db, null, cancellationToken),
            CryptoExitTargets = await BuildExitTargetSummaryAsync(db, CoinType.Crypto, cancellationToken),
            StockExitTargets = await BuildExitTargetSummaryAsync(db, CoinType.Stock, cancellationToken)
        };
    }

    private static async Task<ExitTargetSummary> BuildExitTargetSummaryAsync(
        AppDbContext db,
        CoinType? coinType,
        CancellationToken cancellationToken)
    {
        var query = db.Signals
            .AsNoTracking()
            .Where(x => x.TargetsClosedAt != null && x.CautiousExit != null);

        if (coinType.HasValue)
        {
            query = query.Where(x => x.Coin != null && x.Coin.CoinType == coinType.Value);
        }

        var closed = await query
            .Select(x => new
            {
                x.CautiousReachedAt,
                x.BalancedReachedAt,
                x.WideReachedAt
            })
            .ToListAsync(cancellationToken);

        var total = closed.Count;
        return new ExitTargetSummary
        {
            ClosedSignals = total,
            CautiousHitRatePct = HitRate(closed.Count(x => x.CautiousReachedAt != null), total),
            BalancedHitRatePct = HitRate(closed.Count(x => x.BalancedReachedAt != null), total),
            WideHitRatePct = HitRate(closed.Count(x => x.WideReachedAt != null), total)
        };
    }

    private static decimal HitRate(int hits, int total)
    {
        if (total == 0)
        {
            return 0m;
        }

        return Math.Round((decimal)hits * 100m / total, 2);
    }

    private static decimal ComputeSuccessRate(IReadOnlyCollection<PerformancePoint> outcomes)
    {
        if (outcomes.Count == 0)
        {
            return 0m;
        }

        var success = outcomes.Count(x => x.PriceChangePct > 0m);
        return Math.Round((decimal)success * 100m / outcomes.Count, 2);
    }

    private static List<PerformanceBucket> BuildRegimeBuckets(IReadOnlyCollection<PerformancePoint> outcomes)
    {
        return outcomes
            .GroupBy(x => x.Regime)
            .Select(g => new PerformanceBucket
            {
                Key = g.Key.ToString(),
                Total = g.Count(),
                SuccessRatePct = Math.Round((decimal)g.Count(x => x.PriceChangePct > 0m) * 100m / Math.Max(1, g.Count()), 2)
            })
            .OrderByDescending(x => x.SuccessRatePct)
            .ToList();
    }

    private static async Task<decimal> CalculateAverageChangeAtMinutes(AppDbContext db, int minutes, CancellationToken cancellationToken)
    {
        var values = await db.SignalOutcomes
            .AsNoTracking()
            .Where(x => x.MinutesElapsed == minutes)
            .Select(x => x.PriceChangePct)
            .ToListAsync(cancellationToken);

        if (values.Count == 0)
        {
            return 0m;
        }

        return Math.Round(values.Average(), 2);
    }

    private static Dictionary<string, int> ParseBreakdown(JsonDocument? doc)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (doc is null)
        {
            return result;
        }

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt32(out var value))
            {
                result[prop.Name] = value;
            }
        }

        return result;
    }

    private static List<string> ParseStringArray(JsonDocument? doc)
    {
        var result = new List<string>();
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
            {
                result.Add(item.GetString()!);
            }
        }

        return result;
    }

    private sealed class PerformancePoint
    {
        public string Coin { get; set; } = string.Empty;
        public int PatternBonus { get; set; }
        public MarketRegime Regime { get; set; }
        public decimal PriceChangePct { get; set; }
    }
}

public sealed class SignalFilter
{
    public string? Symbol { get; set; }
    public DateTimeOffset? FromUtc { get; set; }
    public DateTimeOffset? ToUtc { get; set; }
    public decimal? MinScore { get; set; }
    public SignalStatus? Status { get; set; }
    public MarketRegime? Regime { get; set; }
    public CoinType? CoinType { get; set; }
    public bool PatternBonusOnly { get; set; }
}

public sealed class SignalListItem
{
    public int Id { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public CoinType CoinType { get; set; }
    public decimal BaseScore { get; set; }
    public int PatternBonus { get; set; }
    public decimal TotalScore { get; set; }
    public SignalStatus Status { get; set; }
    public MarketRegime Regime { get; set; }
    public SignalType SignalType { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class SignalDetailView
{
    public int Id { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public CoinType CoinType { get; set; }
    public decimal BaseScore { get; set; }
    public int PatternBonus { get; set; }
    public decimal TotalScore { get; set; }
    public SignalStatus Status { get; set; }
    public MarketRegime Regime { get; set; }
    public SignalType SignalType { get; set; }
    public decimal SignalPrice { get; set; }
    public decimal? EntryPrice { get; set; }
    public decimal? StopPrice { get; set; }
    public int? StopMinutes { get; set; }
    public decimal? CautiousExit { get; set; }
    public int? CautiousMinutes { get; set; }
    public decimal? BalancedExit { get; set; }
    public int? BalancedMinutes { get; set; }
    public decimal? WideExit { get; set; }
    public int? WideMinutes { get; set; }
    public DateTimeOffset? TargetsClosedAt { get; set; }
    public decimal SupportLevel { get; set; }
    public decimal SupportDistPct { get; set; }
    public decimal FundingRate { get; set; }
    public string? PatternMain { get; set; }
    public string? Pattern15m { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
    public Dictionary<string, int> Breakdown { get; set; } = new();
    public List<string> Reasons { get; set; } = [];
    public List<string> Risks { get; set; } = [];
    public List<OutcomePoint> Outcomes { get; set; } = [];
}

public sealed class OutcomePoint
{
    public int MinutesElapsed { get; set; }
    public decimal PriceAt { get; set; }
    public decimal PriceChangePct { get; set; }
    public DateTimeOffset SnapshotAt { get; set; }
}

public sealed class PerformanceSnapshot
{
    public int TotalSignalsAt1H { get; set; }
    public decimal OverallSuccessRatePct { get; set; }
    public decimal AvgChange1HPct { get; set; }
    public decimal AvgChange4HPct { get; set; }
    public decimal PatternWithSuccessRate { get; set; }
    public decimal PatternWithoutSuccessRate { get; set; }
    public List<PerformanceBucket> CoinBuckets { get; set; } = [];
    public List<PerformanceBucket> RegimeBuckets { get; set; } = [];
    public ExitTargetSummary ExitTargets { get; set; } = new();
    public ExitTargetSummary CryptoExitTargets { get; set; } = new();
    public ExitTargetSummary StockExitTargets { get; set; } = new();
}

public sealed class ExitTargetRow
{
    public int Id { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public CoinType CoinType { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public decimal? EntryPrice { get; set; }
    public decimal? StopPrice { get; set; }
    public int? StopMinutes { get; set; }
    public decimal? CautiousExit { get; set; }
    public int? CautiousMinutes { get; set; }
    public decimal? BalancedExit { get; set; }
    public int? BalancedMinutes { get; set; }
    public decimal? WideExit { get; set; }
    public int? WideMinutes { get; set; }
    public DateTimeOffset? TargetsClosedAt { get; set; }
}

public sealed class ExitTargetSummary
{
    public int ClosedSignals { get; set; }
    public decimal CautiousHitRatePct { get; set; }
    public decimal BalancedHitRatePct { get; set; }
    public decimal WideHitRatePct { get; set; }
}

public sealed class PerformanceBucket
{
    public string Key { get; set; } = string.Empty;
    public int Total { get; set; }
    public decimal SuccessRatePct { get; set; }
}

using System.Text.Json;
using CryptoTrendForge.Core.Domain.Enums;
using CryptoTrendForge.Core.Domain.Models;
using CryptoTrendForge.Core.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace CryptoTrendForge.Worker.Services;

public sealed class SignalRepository
{
    private readonly AppDbContext _dbContext;

    public SignalRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<Signal>> GetActiveSignalsAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Signals
            .Include(x => x.Coin)
            .Where(x => x.Status == SignalStatus.Active)
            .ToListAsync(cancellationToken);
    }

    public async Task<Signal?> GetActiveSignalByCoinIdAsync(int coinId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Signals
            .Where(x => x.CoinId == coinId && x.Status == SignalStatus.Active)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<Signal?> GetLatestSignalByCoinIdAsync(int coinId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Signals
            .Where(x => x.CoinId == coinId)
            .OrderByDescending(x => x.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task ExpireDueSignalsAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var dueSignals = await _dbContext.Signals
            .Where(x => x.Status == SignalStatus.Active && x.ExpiresAt.HasValue && x.ExpiresAt.Value <= now)
            .ToListAsync(cancellationToken);

        foreach (var signal in dueSignals)
        {
            signal.Status = SignalStatus.Expired;
        }

        if (dueSignals.Count > 0)
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task InvalidateIfBrokenSupportAsync(
        int coinId,
        decimal currentPrice,
        CancellationToken cancellationToken = default)
    {
        var activeSignal = await GetActiveSignalByCoinIdAsync(coinId, cancellationToken);
        if (activeSignal is null || activeSignal.SupportLevel <= 0m)
        {
            return;
        }

        if (currentPrice < activeSignal.SupportLevel * 0.97m)
        {
            activeSignal.Status = SignalStatus.Invalidated;
            activeSignal.InvalidatedAt = DateTimeOffset.UtcNow;
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task MarkSupersededAsync(int signalId, CancellationToken cancellationToken = default)
    {
        var signal = await _dbContext.Signals.FirstOrDefaultAsync(x => x.Id == signalId, cancellationToken);
        if (signal is null)
        {
            return;
        }

        signal.Status = SignalStatus.Superseded;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<Signal> CreateSignalAsync(
        Coin coin,
        ScoreResult scoreResult,
        SignalType signalType,
        MarketRegime marketRegime,
        decimal signalPrice,
        decimal supportLevel,
        decimal supportDistancePct,
        decimal fundingRate,
        string? btcTrend,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken = default)
    {
        var signal = new Signal
        {
            CoinId = coin.Id,
            Score = scoreResult.BaseScore,
            PatternBonus = scoreResult.PatternBonus,
            TotalScore = scoreResult.TotalScore,
            SignalType = signalType,
            Status = SignalStatus.Pending,
            MarketRegime = marketRegime,
            SignalPrice = signalPrice,
            SupportLevel = supportLevel,
            SupportDistPct = supportDistancePct,
            FundingRate = fundingRate,
            Pattern1H4H = scoreResult.PatternName,
            Pattern15M = scoreResult.PatternName15m,
            BtcTrend = btcTrend,
            ScoreBreakdown = JsonSerializer.SerializeToDocument(scoreResult.Breakdown),
            Reasons = JsonSerializer.SerializeToDocument(scoreResult.Reasons),
            Risks = JsonSerializer.SerializeToDocument(scoreResult.Risks),
            RawSnapshot = scoreResult.RawSnapshot is null
                ? null
                : JsonSerializer.SerializeToDocument(scoreResult.RawSnapshot),
            ExpiresAt = expiresAt
        };

        _dbContext.Signals.Add(signal);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return signal;
    }

    public async Task ActivateSignalAsync(int signalId, CancellationToken cancellationToken = default)
    {
        var signal = await _dbContext.Signals.FirstOrDefaultAsync(x => x.Id == signalId, cancellationToken);
        if (signal is null)
        {
            return;
        }

        signal.Status = SignalStatus.Active;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Signal>> GetActiveSignalsWithCoinAsync(CancellationToken cancellationToken = default)
    {
        return await _dbContext.Signals
            .Include(x => x.Coin)
            .Where(x => x.Status == SignalStatus.Active && x.Coin != null)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> OutcomeExistsAsync(int signalId, int minutesElapsed, CancellationToken cancellationToken = default)
    {
        return await _dbContext.SignalOutcomes
            .AnyAsync(x => x.SignalId == signalId && x.MinutesElapsed == minutesElapsed, cancellationToken);
    }

    public async Task AddOutcomeAsync(
        int signalId,
        int minutesElapsed,
        decimal priceAt,
        decimal signalPrice,
        DateTimeOffset snapshotAt,
        CancellationToken cancellationToken = default)
    {
        var priceChangePct = signalPrice == 0m
            ? 0m
            : Math.Round(((priceAt - signalPrice) / signalPrice) * 100m, 2);

        _dbContext.SignalOutcomes.Add(new SignalOutcome
        {
            SignalId = signalId,
            MinutesElapsed = minutesElapsed,
            PriceAt = priceAt,
            PriceChangePct = priceChangePct,
            SnapshotAt = snapshotAt
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}

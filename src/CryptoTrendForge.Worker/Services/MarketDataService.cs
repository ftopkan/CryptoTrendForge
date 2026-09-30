using CryptoTrendForge.Core.Domain;
using CryptoTrendForge.Core.Domain.Enums;
using CryptoTrendForge.Core.Domain.Models;
using CryptoTrendForge.Core.Infrastructure.Cache;
using CryptoTrendForge.Core.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace CryptoTrendForge.Worker.Services;

public sealed class MarketDataService
{
    private static readonly TimeSpan OpenInterestTtl = TimeSpan.FromMinutes(4);
    private static readonly TimeSpan FundingRateTtl = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan TickerTtl = TimeSpan.FromSeconds(30);

    private readonly BybitService _bybitService;
    private readonly MarketDataCache _cache;
    private readonly AppDbContext _dbContext;

    public MarketDataService(BybitService bybitService, MarketDataCache cache, AppDbContext dbContext)
    {
        _bybitService = bybitService;
        _cache = cache;
        _dbContext = dbContext;
    }

    public async Task<IReadOnlyList<Coin>> GetActiveCoinsAsync(CancellationToken cancellationToken = default)
    {
        return await ActiveCoins(CoinType.Crypto, cancellationToken);
    }

    public async Task<IReadOnlyList<Coin>> GetActiveStocksAsync(CancellationToken cancellationToken = default)
    {
        return await ActiveCoins(CoinType.Stock, cancellationToken);
    }

    private async Task<IReadOnlyList<Coin>> ActiveCoins(CoinType coinType, CancellationToken cancellationToken)
    {
        return await _dbContext.Coins
            .AsNoTracking()
            .Where(x => x.IsActive && x.CoinType == coinType)
            .OrderBy(x => x.Symbol)
            .ToListAsync(cancellationToken);
    }

    public Task<MarketSnapshot?> GetMarketSnapshotAsync(
        string symbol,
        CancellationToken cancellationToken = default)
    {
        // EMA200 needs 200 closed bars. Bybit includes the open 4h candle, so request one extra.
        return LoadSnapshotAsync(symbol, fourHourLimit: 201, cancellationToken);
    }

    public Task<MarketSnapshot?> GetStockMarketSnapshotAsync(
        string symbol,
        CancellationToken cancellationToken = default)
    {
        return LoadSnapshotAsync(symbol, fourHourLimit: 1000, fourHourPages: 3, cancellationToken);
    }

    private async Task<MarketSnapshot?> LoadSnapshotAsync(
        string symbol,
        int fourHourLimit,
        CancellationToken cancellationToken)
    {
        return await LoadSnapshotAsync(symbol, fourHourLimit, fourHourPages: 1, cancellationToken);
    }

    private async Task<MarketSnapshot?> LoadSnapshotAsync(
        string symbol,
        int fourHourLimit,
        int fourHourPages,
        CancellationToken cancellationToken)
    {
        var kline15Task = GetKlinesCachedAsync(symbol, "15", 200, 1, cancellationToken);
        var kline1hTask = GetKlinesCachedAsync(symbol, "60", 200, 1, cancellationToken);
        var kline4hTask = GetKlinesCachedAsync(symbol, "240", fourHourLimit, fourHourPages, cancellationToken);
        var oi1hTask = GetOpenInterestChangePctCachedAsync(symbol, "1h", cancellationToken);
        var oi4hTask = GetOpenInterestChangePctCachedAsync(symbol, "4h", cancellationToken);
        var fundingTask = GetFundingRateCachedAsync(symbol, cancellationToken);
        var tickerTask = GetTickerCachedAsync(symbol, cancellationToken);

        await Task.WhenAll(kline15Task, kline1hTask, kline4hTask, oi1hTask, oi4hTask, fundingTask, tickerTask);

        var ticker = await tickerTask;
        if (ticker is null)
        {
            return null;
        }

        return new MarketSnapshot
        {
            Symbol = symbol,
            CurrentPrice = ticker.LastPrice,
            FundingRate = await fundingTask,
            OpenInterestChangePct1H = await oi1hTask,
            OpenInterestChangePct4H = await oi4hTask,
            Volume24h = ticker.Volume24h,
            Turnover24h = ticker.Turnover24h,
            Klines15M = await kline15Task,
            Klines1H = await kline1hTask,
            Klines4H = await kline4hTask
        };
    }

    public async Task<BtcSnapshot?> GetBtcSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var symbol = "BTCUSDT";
        var kline15Task = GetKlinesCachedAsync(symbol, "15", 200, 1, cancellationToken);
        var kline1hTask = GetKlinesCachedAsync(symbol, "60", 200, 1, cancellationToken);
        var kline4hTask = GetKlinesCachedAsync(symbol, "240", 200, 1, cancellationToken);
        var tickerTask = GetTickerCachedAsync(symbol, cancellationToken);

        await Task.WhenAll(kline15Task, kline1hTask, kline4hTask, tickerTask);
        var ticker = await tickerTask;
        if (ticker is null)
        {
            return null;
        }

        return new BtcSnapshot
        {
            Symbol = symbol,
            CurrentPrice = ticker.LastPrice,
            Price24hChangePct = ticker.Price24hChangePct,
            Klines15M = await kline15Task,
            Klines1H = await kline1hTask,
            Klines4H = await kline4hTask
        };
    }

    private async Task<IReadOnlyList<Kline>> GetKlinesCachedAsync(
        string symbol,
        string interval,
        int limit,
        int pages,
        CancellationToken cancellationToken)
    {
        var key = BuildCacheKey(symbol, $"{interval}:{limit}:{pages}", "kline");
        if (_cache.TryGet<IReadOnlyList<Kline>>(key, out var cached) && cached is not null)
        {
            return cached;
        }

        var klines = await GetKlinePagesAsync(symbol, interval, limit, pages, cancellationToken);
        _cache.Set(key, klines, GetKlineTtl(interval, DateTimeOffset.UtcNow));
        return klines;
    }

    private async Task<IReadOnlyList<Kline>> GetKlinePagesAsync(
        string symbol,
        string interval,
        int limit,
        int pages,
        CancellationToken cancellationToken)
    {
        var merged = new List<Kline>();
        long? endTimeMs = null;
        for (var page = 0; page < pages; page++)
        {
            var batch = await _bybitService.GetKlinesAsync(symbol, interval, limit, endTimeMs, cancellationToken);
            if (batch.Count == 0)
            {
                break;
            }

            merged.AddRange(batch);
            if (batch.Count < limit)
            {
                break;
            }

            endTimeMs = batch.Min(x => x.OpenTime).ToUnixTimeMilliseconds() - 1;
        }

        return merged
            .GroupBy(x => x.OpenTime)
            .Select(x => x.First())
            .OrderBy(x => x.OpenTime)
            .ToArray();
    }

    private async Task<decimal> GetOpenInterestChangePctCachedAsync(
        string symbol,
        string intervalTime,
        CancellationToken cancellationToken)
    {
        var key = BuildCacheKey(symbol, intervalTime, "open-interest");
        if (_cache.TryGet<decimal>(key, out var cached))
        {
            return cached;
        }

        var value = await _bybitService.GetOpenInterestChangePctAsync(symbol, intervalTime, 2, cancellationToken);
        _cache.Set(key, value, OpenInterestTtl);
        return value;
    }

    private async Task<decimal> GetFundingRateCachedAsync(
        string symbol,
        CancellationToken cancellationToken)
    {
        var key = BuildCacheKey(symbol, "latest", "funding");
        if (_cache.TryGet<decimal>(key, out var cached))
        {
            return cached;
        }

        var value = await _bybitService.GetFundingRateAsync(symbol, cancellationToken);
        _cache.Set(key, value, FundingRateTtl);
        return value;
    }

    private async Task<TickerData?> GetTickerCachedAsync(
        string symbol,
        CancellationToken cancellationToken)
    {
        var key = BuildCacheKey(symbol, "spot", "ticker");
        if (_cache.TryGet<TickerData>(key, out var cached) && cached is not null)
        {
            return cached;
        }

        var value = await _bybitService.GetTickerAsync(symbol, cancellationToken);
        if (value is not null)
        {
            _cache.Set(key, value, TickerTtl);
        }

        return value;
    }

    private static string BuildCacheKey(string symbol, string interval, string dataType)
    {
        return $"{symbol}:{interval}:{dataType}".ToLowerInvariant();
    }

    private static TimeSpan GetKlineTtl(string interval, DateTimeOffset now)
    {
        var length = interval switch
        {
            "15" => TimeSpan.FromMinutes(15),
            "60" => TimeSpan.FromHours(1),
            "240" => TimeSpan.FromHours(4),
            _ => TimeSpan.FromMinutes(5)
        };

        return CandleClock.TimeUntilNextClose(now, length);
    }
}

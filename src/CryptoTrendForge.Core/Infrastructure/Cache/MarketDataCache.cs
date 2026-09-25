using Microsoft.Extensions.Caching.Memory;

namespace CryptoTrendForge.Core.Infrastructure.Cache;

public sealed class MarketDataCache
{
    private readonly IMemoryCache _cache;

    public MarketDataCache(IMemoryCache cache)
    {
        _cache = cache;
    }

    public bool TryGet<T>(string key, out T? value)
    {
        return _cache.TryGetValue(key, out value);
    }

    public void Set<T>(string key, T value, TimeSpan ttl)
    {
        _cache.Set(key, value, ttl);
    }
}

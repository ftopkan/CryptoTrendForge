using System.Globalization;
using System.Text.Json;
using CryptoTrendForge.Core.Domain.Models;
using CryptoTrendForge.Core.Infrastructure.Http;

namespace CryptoTrendForge.Worker.Services;

public sealed class BybitService
{
    private const string CategoryLinear = "linear";
    private readonly BybitHttpClient _httpClient;
    private readonly ILogger<BybitService> _logger;

    public BybitService(BybitHttpClient httpClient, ILogger<BybitService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IReadOnlyList<Kline>> GetKlinesAsync(
        string symbol,
        string interval,
        int limit = 200,
        CancellationToken cancellationToken = default)
    {
        var payload = await _httpClient.GetJsonAsync(
            "/v5/market/kline",
            new Dictionary<string, string?>
            {
                ["category"] = CategoryLinear,
                ["symbol"] = symbol,
                ["interval"] = interval,
                ["limit"] = limit.ToString(CultureInfo.InvariantCulture)
            },
            cancellationToken);

        if (payload is null)
        {
            return [];
        }

        if (!TryGetResultList(payload.RootElement, out var listElement))
        {
            _logger.LogWarning("Kline response did not contain expected list. Symbol: {Symbol}, Interval: {Interval}", symbol, interval);
            return [];
        }

        var items = new List<Kline>();
        foreach (var item in listElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() < 6)
            {
                continue;
            }

            var openTimeMs = ParseLong(item[0]);
            var open = ParseDecimal(item[1]);
            var high = ParseDecimal(item[2]);
            var low = ParseDecimal(item[3]);
            var close = ParseDecimal(item[4]);
            var volume = ParseDecimal(item[5]);

            if (openTimeMs == 0)
            {
                continue;
            }

            items.Add(new Kline
            {
                OpenTime = DateTimeOffset.FromUnixTimeMilliseconds(openTimeMs),
                Open = open,
                High = high,
                Low = low,
                Close = close,
                Volume = volume
            });
        }

        return items.OrderBy(x => x.OpenTime).ToArray();
    }

    public async Task<decimal> GetOpenInterestChangePctAsync(
        string symbol,
        string intervalTime = "4h",
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var payload = await _httpClient.GetJsonAsync(
            "/v5/market/open-interest",
            new Dictionary<string, string?>
            {
                ["category"] = CategoryLinear,
                ["symbol"] = symbol,
                ["intervalTime"] = intervalTime,
                ["limit"] = limit.ToString(CultureInfo.InvariantCulture)
            },
            cancellationToken);

        if (payload is null || !TryGetResultList(payload.RootElement, out var listElement))
        {
            return 0m;
        }

        var values = listElement
            .EnumerateArray()
            .Select(x => x.TryGetProperty("openInterest", out var oiElement) ? ParseDecimal(oiElement) : 0m)
            .Where(v => v > 0m)
            .ToArray();

        if (values.Length < 2)
        {
            return 0m;
        }

        var oldest = values[^1];
        var latest = values[0];

        if (oldest == 0m)
        {
            return 0m;
        }

        return Math.Round(((latest - oldest) / oldest) * 100m, 2);
    }

    public async Task<decimal> GetFundingRateAsync(
        string symbol,
        CancellationToken cancellationToken = default)
    {
        var payload = await _httpClient.GetJsonAsync(
            "/v5/market/funding/history",
            new Dictionary<string, string?>
            {
                ["category"] = CategoryLinear,
                ["symbol"] = symbol,
                ["limit"] = "1"
            },
            cancellationToken);

        if (payload is null || !TryGetResultList(payload.RootElement, out var listElement))
        {
            return 0m;
        }

        var first = listElement.EnumerateArray().FirstOrDefault();
        if (first.ValueKind == JsonValueKind.Undefined)
        {
            return 0m;
        }

        return first.TryGetProperty("fundingRate", out var fundingElement)
            ? ParseDecimal(fundingElement)
            : 0m;
    }

    public async Task<TickerData?> GetTickerAsync(
        string symbol,
        CancellationToken cancellationToken = default)
    {
        var payload = await _httpClient.GetJsonAsync(
            "/v5/market/tickers",
            new Dictionary<string, string?>
            {
                ["category"] = CategoryLinear,
                ["symbol"] = symbol
            },
            cancellationToken);

        if (payload is null || !TryGetResultList(payload.RootElement, out var listElement))
        {
            return null;
        }

        var first = listElement.EnumerateArray().FirstOrDefault();
        if (first.ValueKind == JsonValueKind.Undefined)
        {
            return null;
        }

        return new TickerData
        {
            LastPrice = first.TryGetProperty("lastPrice", out var lastPrice) ? ParseDecimal(lastPrice) : 0m,
            Volume24h = first.TryGetProperty("volume24h", out var volume24h) ? ParseDecimal(volume24h) : 0m,
            Turnover24h = first.TryGetProperty("turnover24h", out var turnover24h) ? ParseDecimal(turnover24h) : 0m,
            Price24hChangePct = first.TryGetProperty("price24hPcnt", out var changePct) ? ParseDecimal(changePct) : 0m
        };
    }

    private static bool TryGetResultList(JsonElement root, out JsonElement listElement)
    {
        listElement = default;
        if (!root.TryGetProperty("result", out var resultElement))
        {
            return false;
        }

        if (!resultElement.TryGetProperty("list", out listElement))
        {
            return false;
        }

        return listElement.ValueKind == JsonValueKind.Array;
    }

    private static decimal ParseDecimal(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number)
        {
            return element.GetDecimal();
        }

        if (element.ValueKind == JsonValueKind.String &&
            decimal.TryParse(element.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return 0m;
    }

    private static long ParseLong(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var value))
        {
            return value;
        }

        if (element.ValueKind == JsonValueKind.String &&
            long.TryParse(element.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return 0;
    }
}

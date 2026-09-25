using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CryptoTrendForge.Core.Infrastructure.Http;

public sealed class BybitHttpClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BybitHttpClient> _logger;
    private readonly BybitClientOptions _options;
    private int _consecutiveFailures;
    private DateTimeOffset? _pausedUntil;

    public BybitHttpClient(
        HttpClient httpClient,
        ILogger<BybitHttpClient> logger,
        IOptions<BybitClientOptions> options)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<JsonDocument?> GetJsonAsync(
        string relativePath,
        IDictionary<string, string?>? queryParams = null,
        CancellationToken cancellationToken = default)
    {
        if (_pausedUntil.HasValue && _pausedUntil.Value > DateTimeOffset.UtcNow)
        {
            _logger.LogWarning("Bybit client paused until {PausedUntil}", _pausedUntil.Value);
            return null;
        }

        var uri = BuildUri(relativePath, queryParams);
        var delaySeconds = 2;

        for (var attempt = 1; attempt <= _options.MaxRetries; attempt++)
        {
            try
            {
                using var response = await _httpClient.GetAsync(uri, cancellationToken);

                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    var retryDelay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(delaySeconds);
                    _logger.LogWarning("Bybit 429 received, waiting {Delay}s", retryDelay.TotalSeconds);
                    await Task.Delay(retryDelay, cancellationToken);
                    delaySeconds *= 2;
                    continue;
                }

                response.EnsureSuccessStatusCode();
                _consecutiveFailures = 0;
                _pausedUntil = null;

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < _options.MaxRetries)
            {
                _consecutiveFailures++;
                _logger.LogWarning(ex, "Bybit request failed attempt {Attempt}/{MaxRetries}", attempt, _options.MaxRetries);
                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
                delaySeconds *= 2;
            }
            catch (Exception ex)
            {
                _consecutiveFailures++;
                _logger.LogError(ex, "Bybit request permanently failed: {Uri}", uri);
                break;
            }
        }

        if (_consecutiveFailures >= _options.CircuitBreakerFailures)
        {
            _pausedUntil = DateTimeOffset.UtcNow.AddSeconds(_options.CircuitBreakerPauseSeconds);
            _logger.LogError(
                "Bybit circuit opened for {PauseSeconds}s after {Failures} failures",
                _options.CircuitBreakerPauseSeconds,
                _consecutiveFailures);
            _consecutiveFailures = 0;
        }

        return null;
    }

    private static string BuildUri(string relativePath, IDictionary<string, string?>? queryParams)
    {
        if (queryParams is null || queryParams.Count == 0)
        {
            return relativePath;
        }

        var encoded = queryParams
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value))
            .Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value!)}");

        return $"{relativePath}?{string.Join("&", encoded)}";
    }
}

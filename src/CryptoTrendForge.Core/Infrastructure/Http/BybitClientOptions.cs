namespace CryptoTrendForge.Core.Infrastructure.Http;

public sealed class BybitClientOptions
{
    public int MaxRetries { get; set; } = 3;
    public int CircuitBreakerFailures { get; set; } = 5;
    public int CircuitBreakerPauseSeconds { get; set; } = 60;
}

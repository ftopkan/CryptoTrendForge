namespace CryptoTrendForge.Worker.Configuration;

public sealed class BybitOptions
{
    public const string SectionName = "Bybit";

    public string BaseUrl { get; set; } = "https://api.bybit.com";
    public int TimeoutSeconds { get; set; } = 10;
    public int MaxRetries { get; set; } = 3;
}

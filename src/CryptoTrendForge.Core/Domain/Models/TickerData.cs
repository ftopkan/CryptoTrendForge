namespace CryptoTrendForge.Core.Domain.Models;

public sealed class TickerData
{
    public decimal LastPrice { get; set; }
    public decimal Volume24h { get; set; }
    public decimal Turnover24h { get; set; }
    public decimal Price24hChangePct { get; set; }
}

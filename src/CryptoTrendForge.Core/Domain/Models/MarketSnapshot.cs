namespace CryptoTrendForge.Core.Domain.Models;

public sealed class MarketSnapshot
{
    public string Symbol { get; set; } = string.Empty;
    public decimal CurrentPrice { get; set; }
    public decimal FundingRate { get; set; }
    public decimal OpenInterestChangePct1H { get; set; }
    public decimal OpenInterestChangePct4H { get; set; }
    public decimal Volume24h { get; set; }
    public decimal Turnover24h { get; set; }
    public IReadOnlyList<Kline> Klines15M { get; set; } = [];
    public IReadOnlyList<Kline> Klines1H { get; set; } = [];
    public IReadOnlyList<Kline> Klines4H { get; set; } = [];
}

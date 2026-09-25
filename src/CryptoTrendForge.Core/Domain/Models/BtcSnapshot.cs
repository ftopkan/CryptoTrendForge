namespace CryptoTrendForge.Core.Domain.Models;

public sealed class BtcSnapshot
{
    public string Symbol { get; set; } = "BTCUSDT";
    public decimal CurrentPrice { get; set; }
    public decimal Price24hChangePct { get; set; }
    public IReadOnlyList<Kline> Klines15M { get; set; } = [];
    public IReadOnlyList<Kline> Klines1H { get; set; } = [];
    public IReadOnlyList<Kline> Klines4H { get; set; } = [];
}

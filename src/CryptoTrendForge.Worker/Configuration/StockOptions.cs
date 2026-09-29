namespace CryptoTrendForge.Worker.Configuration;

public sealed class StockOptions
{
    public const string SectionName = "StockSettings";

    public int Candidate { get; set; } = 70;
    public int Strong { get; set; } = 85;
    public decimal FundingRateHardFilterPct { get; set; } = 1.5m;
    public decimal OiSpikeHardFilterPct { get; set; } = 20m;
    public decimal PriceDumpHardFilterPct { get; set; } = 10m;
    public int MarketOpenHour { get; set; } = 9;
    public int MarketOpenMinute { get; set; } = 30;
    public int MarketCloseHour { get; set; } = 16;
    public int MarketCloseMinute { get; set; } = 0;
    public bool ScanOnlyDuringMarketHours { get; set; } = true;
}

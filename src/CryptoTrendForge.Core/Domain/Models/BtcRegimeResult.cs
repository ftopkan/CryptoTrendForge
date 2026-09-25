using CryptoTrendForge.Core.Domain.Enums;

namespace CryptoTrendForge.Core.Domain.Models;

public sealed class BtcRegimeResult
{
    public MarketRegime Regime { get; set; } = MarketRegime.Neutral;
    public bool SkipScan { get; set; }
    public string? SkipReason { get; set; }
}

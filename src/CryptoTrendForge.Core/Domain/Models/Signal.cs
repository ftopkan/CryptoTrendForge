using System.Text.Json;
using CryptoTrendForge.Core.Domain.Enums;

namespace CryptoTrendForge.Core.Domain.Models;

public sealed class Signal
{
    public int Id { get; set; }
    public int CoinId { get; set; }
    public Coin? Coin { get; set; }
    public decimal Score { get; set; }
    public int PatternBonus { get; set; }
    public decimal TotalScore { get; set; }
    public SignalType SignalType { get; set; }
    public SignalStatus Status { get; set; }
    public MarketRegime MarketRegime { get; set; }
    public decimal SignalPrice { get; set; }
    public decimal SupportLevel { get; set; }
    public decimal SupportDistPct { get; set; }
    public decimal FundingRate { get; set; }
    public string? Pattern1H4H { get; set; }
    public string? Pattern15M { get; set; }
    public string? BtcTrend { get; set; }
    public JsonDocument? ScoreBreakdown { get; set; }
    public JsonDocument? Reasons { get; set; }
    public JsonDocument? Risks { get; set; }
    public JsonDocument? RawSnapshot { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? InvalidatedAt { get; set; }
    public DateTimeOffset? ExpiresAt { get; set; }
}

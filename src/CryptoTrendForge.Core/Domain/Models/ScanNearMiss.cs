using System.Text.Json;

namespace CryptoTrendForge.Core.Domain.Models;

public sealed class ScanNearMiss
{
    public int Id { get; set; }
    public int CoinId { get; set; }
    public Coin? Coin { get; set; }
    public int ScoreVersion { get; set; }
    public decimal TotalScore { get; set; }
    public decimal BaseScore { get; set; }
    public int PatternBonus { get; set; }
    public int CandidateThreshold { get; set; }
    public string? BlockReason { get; set; }
    public decimal? Rsi4H { get; set; }
    public decimal? Ema20ExtensionPct { get; set; }
    public JsonDocument? ScoreBreakdown { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

namespace CryptoTrendForge.Core.Domain.Models;

public sealed class ScoreResult
{
    public int BaseScore { get; set; }
    public int PatternBonus { get; set; }
    public int TotalScore { get; set; }
    public decimal SupportLevel { get; set; }
    public decimal SupportDistancePct { get; set; }
    public string? PatternName { get; set; }
    public string? PatternName15m { get; set; }
    public Dictionary<string, int> Breakdown { get; set; } = new();
    public List<string> Reasons { get; set; } = new();
    public List<string> Risks { get; set; } = new();
    public object? RawSnapshot { get; set; }
}

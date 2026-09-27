namespace CryptoTrendForge.Worker.Configuration;

public sealed class BotOptions
{
    public const string SectionName = "BotSettings";

    /// <summary>
    /// When true, each process runs one scan/outcome cycle and exits (for Plesk/cron).
    /// When false, workers loop until the host is stopped (Render/Docker).
    /// </summary>
    public bool RunOnce { get; set; }

    public int ScanIntervalSeconds { get; set; } = 300;
    public int CooldownHours { get; set; } = 4;
    public bool AllowCooldownBypass { get; set; } = true;
    public int CooldownBypassMinScoreDelta { get; set; } = 15;
    public int CooldownBypassMinElapsedMinutes { get; set; } = 60;
    public bool BtcRiskOffBlockEnabled { get; set; } = true;
    public decimal BtcAnomalousDumpPct { get; set; } = 5m;
    public decimal BtcEmaNeutralBandPct { get; set; } = 0.5m;
    public List<string> Timeframes { get; set; } = ["15", "60", "240"];
    public int RsiPeriod { get; set; } = 14;
    public int SwingLookbackCandles { get; set; } = 100;
    public int SwingNeighborCount { get; set; } = 3;
    public int VolumeRecentCandles { get; set; } = 3;
    public int VolumeBaselineCandles { get; set; } = 10;
    public decimal FundingRateHardFilterPct { get; set; } = 0.07m;
    public decimal OiSpikeHardFilterPct { get; set; } = 15m;
    public decimal PriceDumpHardFilterPct { get; set; } = 8m;
    public bool PatternBonusEnabled { get; set; } = true;
    public int PatternBonusPoints { get; set; } = 10;
    public ScoreThresholdOptions ScoreThresholds { get; set; } = new();
}

public sealed class ScoreThresholdOptions
{
    public ThresholdLevel RiskOn { get; set; } = new() { Candidate = 70, Strong = 80 };
    public ThresholdLevel Neutral { get; set; } = new() { Candidate = 75, Strong = 85 };
}

public sealed class ThresholdLevel
{
    public int Candidate { get; set; }
    public int Strong { get; set; }
}

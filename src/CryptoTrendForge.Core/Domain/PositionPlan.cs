namespace CryptoTrendForge.Core.Domain;

public sealed class PositionPlan
{
    private const decimal MarketEntryMaxDistancePct = 1.5m;
    private const decimal CautiousRewardMultiple = 0.5m;
    private const decimal BalancedRewardMultiple = 1m;
    private const decimal WideRewardMultiple = 1.5m;

    public decimal Entry { get; init; }
    public string EntryNote { get; init; } = string.Empty;
    public decimal Stop { get; init; }
    public decimal CautiousExit { get; init; }
    public decimal BalancedExit { get; init; }
    public decimal WideExit { get; init; }

    public static PositionPlan Create(decimal currentPrice, decimal supportLevel)
    {
        var entry = currentPrice;
        var entryNote = "güncel fiyat";

        var supportBelowPrice = supportLevel > 0m && supportLevel < currentPrice;
        if (supportBelowPrice)
        {
            var distancePct = ((currentPrice - supportLevel) / supportLevel) * 100m;
            var pullback = supportLevel * 1.01m;
            if (distancePct > MarketEntryMaxDistancePct && pullback < currentPrice)
            {
                entry = pullback;
                entryNote = "desteğe çekilince";
            }
        }

        var stop = supportBelowPrice && supportLevel * 0.97m < entry
            ? supportLevel * 0.97m
            : entry * 0.97m;

        var risk = entry - stop;
        if (risk <= 0m)
        {
            stop = entry * 0.97m;
            risk = entry - stop;
        }

        return new PositionPlan
        {
            Entry = entry,
            EntryNote = entryNote,
            Stop = stop,
            CautiousExit = entry + (risk * CautiousRewardMultiple),
            BalancedExit = entry + (risk * BalancedRewardMultiple),
            WideExit = entry + (risk * WideRewardMultiple)
        };
    }
}

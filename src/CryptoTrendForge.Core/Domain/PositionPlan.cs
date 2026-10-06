namespace CryptoTrendForge.Core.Domain;

public sealed class PositionPlan
{
    private const decimal MarketEntryMaxDistancePct = 1.5m;
    private const decimal StopCushion = 0.02m;
    private const decimal CautiousRewardMultiple = 0.5m;
    private const decimal BalancedRewardMultiple = 1m;
    private const decimal WideRewardMultiple = 1.5m;

    public decimal Entry { get; init; }
    public string EntryNote { get; init; } = string.Empty;
    public decimal Stop { get; init; }
    public decimal CautiousExit { get; init; }
    public decimal BalancedExit { get; init; }
    public decimal WideExit { get; init; }
    public decimal? BtcEntryPrice { get; init; }
    public decimal? CoinBtcRatio { get; init; }

    public static PositionPlan Create(decimal currentPrice, decimal supportLevel, decimal? btcPrice = null)
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

        var stopMultiplier = 1m - StopCushion;
        var stop = supportBelowPrice && supportLevel * stopMultiplier < entry
            ? supportLevel * stopMultiplier
            : entry * stopMultiplier;

        var risk = entry - stop;
        if (risk <= 0m)
        {
            stop = entry * stopMultiplier;
            risk = entry - stop;
        }

        return new PositionPlan
        {
            Entry = entry,
            EntryNote = entryNote,
            Stop = stop,
            CautiousExit = entry + (risk * CautiousRewardMultiple),
            BalancedExit = entry + (risk * BalancedRewardMultiple),
            WideExit = entry + (risk * WideRewardMultiple),
            BtcEntryPrice = btcPrice is > 0m ? btcPrice : null,
            CoinBtcRatio = btcPrice is > 0m && currentPrice > 0m ? currentPrice / btcPrice.Value : null
        };
    }
}

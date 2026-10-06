using CryptoTrendForge.Core.Domain.Enums;

namespace CryptoTrendForge.Worker.Services;

public static class CryptoSignalTypeRule
{
    public const string StrongDowngradeRisk = "Son 4 saatte Bitcoin'den önde değil; güçlü long etiketini taşımıyor.";

    public static SignalType Resolve(int totalScore, int strongThreshold, decimal? btcRelativePct, ICollection<string> risks)
    {
        var type = totalScore >= strongThreshold
            ? SignalType.StrongLongCandidate
            : SignalType.LongCandidate;
        if (type == SignalType.StrongLongCandidate && btcRelativePct is < 0m)
        {
            risks.Add(StrongDowngradeRisk);
            return SignalType.LongCandidate;
        }

        return type;
    }
}

namespace CryptoTrendForge.Core.Domain;

public static class NearMissRule
{
    public const int FormulaVersion = 3;
    public const int Band = 15;

    public static bool ShouldRecord(int totalScore, int candidateThreshold, bool blocked)
    {
        if (candidateThreshold >= int.MaxValue - Band)
        {
            return false;
        }

        if (totalScore < candidateThreshold - Band)
        {
            return false;
        }

        if (!blocked && totalScore >= candidateThreshold)
        {
            return false;
        }

        return true;
    }
}

namespace CryptoTrendForge.Worker.Services;

public static class OpenInterestChange
{
    public static decimal PercentFromNewestFirst(IReadOnlyList<decimal> newestFirst)
    {
        if (newestFirst.Count < 2)
        {
            return 0m;
        }

        var latest = newestFirst[0];
        var previous = newestFirst[1];
        if (previous == 0m)
        {
            return 0m;
        }

        return Math.Round(((latest - previous) / previous) * 100m, 2);
    }
}

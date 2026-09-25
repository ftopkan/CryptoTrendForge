namespace CryptoTrendForge.Core.Infrastructure.Database;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";
    public string Provider { get; set; } = "Postgres";
}

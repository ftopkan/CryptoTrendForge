namespace CryptoTrendForge.Dashboard.Configuration;

public sealed class DashboardAuthOptions
{
    public const string SectionName = "DashboardAuth";

    public string Username { get; set; } = "admin";
    public string Password { get; set; } = "changeme";
}

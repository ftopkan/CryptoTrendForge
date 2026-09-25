using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CryptoTrendForge.Core.Infrastructure.Database;

public static class DatabaseConfiguration
{
    public const string PostgresMigrationsAssembly = "CryptoTrendForge.Core.Migrations.Postgres";
    public const string SqlServerMigrationsAssembly = "CryptoTrendForge.Core.Migrations.SqlServer";

    public static void ConfigureAppDbContext(DbContextOptionsBuilder options, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        var provider = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>()?.Provider ?? "Postgres";

        if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            options.UseSqlServer(
                connectionString,
                sql => sql.MigrationsAssembly(SqlServerMigrationsAssembly));
        }
        else
        {
            options.UseNpgsql(
                connectionString,
                npgsql => npgsql.MigrationsAssembly(PostgresMigrationsAssembly));
        }
    }
}

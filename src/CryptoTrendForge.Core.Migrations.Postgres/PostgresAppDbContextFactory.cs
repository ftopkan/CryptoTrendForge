using CryptoTrendForge.Core.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CryptoTrendForge.Core.Migrations.Postgres;

public sealed class PostgresAppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(
            "Host=localhost;Port=5432;Database=cryptotrendforge;Username=postgres;Password=postgres",
            npgsql => npgsql.MigrationsAssembly(DatabaseConfiguration.PostgresMigrationsAssembly));

        return new AppDbContext(optionsBuilder.Options);
    }
}

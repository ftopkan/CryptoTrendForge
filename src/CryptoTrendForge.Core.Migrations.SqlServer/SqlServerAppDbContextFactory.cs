using CryptoTrendForge.Core.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace CryptoTrendForge.Core.Migrations.SqlServer;

public sealed class SqlServerAppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseSqlServer(
            "Server=localhost,1433;Database=CryptoTrendForge;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True",
            sql => sql.MigrationsAssembly(DatabaseConfiguration.SqlServerMigrationsAssembly));

        return new AppDbContext(optionsBuilder.Options);
    }
}

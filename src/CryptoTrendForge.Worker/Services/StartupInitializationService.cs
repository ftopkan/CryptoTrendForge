using CryptoTrendForge.Core.Domain.Models;
using CryptoTrendForge.Core.Infrastructure.Database;
using Microsoft.EntityFrameworkCore;

namespace CryptoTrendForge.Worker.Services;

public sealed class StartupInitializationService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<StartupInitializationService> _logger;

    public StartupInitializationService(
        IServiceProvider serviceProvider,
        ILogger<StartupInitializationService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var provider = dbContext.Database.IsSqlServer() ? "SQL Server" : "PostgreSQL";
        _logger.LogInformation("Applying pending {Provider} database migrations...", provider);
        await dbContext.Database.MigrateAsync(cancellationToken);
        _logger.LogInformation("{Provider} database migration check completed.", provider);

        if (!await dbContext.Coins.AnyAsync(cancellationToken))
        {
            dbContext.Coins.Add(new Coin
            {
                Symbol = "XRPUSDT",
                DisplayName = "XRP",
                IsActive = true
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Seeded initial coin: XRPUSDT.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

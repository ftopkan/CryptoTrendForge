using CryptoTrendForge.Worker.Services;

namespace CryptoTrendForge.Worker.Workers;

public sealed class SignalOutcomeWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SignalOutcomeWorker> _logger;

    public SignalOutcomeWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<SignalOutcomeWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await RunOutcomeIterationAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
        }
    }

    private async Task RunOutcomeIterationAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            await scope.ServiceProvider.GetRequiredService<SignalOutcomeRunner>().RunAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Signal outcome iteration failed.");
        }
    }
}

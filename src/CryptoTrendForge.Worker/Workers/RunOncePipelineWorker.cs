using CryptoTrendForge.Worker.Infrastructure;
using CryptoTrendForge.Worker.Services;
using Serilog;

namespace CryptoTrendForge.Worker.Workers;

public sealed class RunOncePipelineWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RunOnceOverlapGuard? _overlapGuard;
    private readonly ILogger<RunOncePipelineWorker> _logger;

    public RunOncePipelineWorker(
        IServiceScopeFactory scopeFactory,
        RunOnceOverlapGuard? overlapGuard,
        ILogger<RunOncePipelineWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _overlapGuard = overlapGuard;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(9));

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var services = scope.ServiceProvider;
            var token = timeoutCts.Token;

            _logger.LogInformation("RunOnce pipeline starting (crypto → stock → outcomes).");
            await services.GetRequiredService<CryptoEntryScanRunner>().RunAsync(token);
            await services.GetRequiredService<StockEntryScanRunner>().RunAsync(token);
            await services.GetRequiredService<SignalOutcomeRunner>().RunAsync(token);
            _logger.LogInformation("RunOnce pipeline finished successfully.");
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            _logger.LogWarning("RunOnce pipeline cancelled (host stop or 9-minute budget).");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RunOnce pipeline failed.");
        }
        finally
        {
            try
            {
                _overlapGuard?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to release RunOnce overlap marker.");
            }

            Log.Information("RunOnce pipeline exiting process.");
            Log.CloseAndFlush();
            Environment.Exit(0);
        }
    }
}

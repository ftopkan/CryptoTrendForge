using CryptoTrendForge.Worker.Configuration;
using Microsoft.Extensions.Options;

namespace CryptoTrendForge.Worker.Infrastructure;

public sealed class RunOnceCoordinator
{
    private int _completedWorkers;
    private readonly BotOptions _botOptions;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<RunOnceCoordinator> _logger;

    public RunOnceCoordinator(
        IOptions<BotOptions> botOptions,
        IHostApplicationLifetime lifetime,
        ILogger<RunOnceCoordinator> logger)
    {
        _botOptions = botOptions.Value;
        _lifetime = lifetime;
        _logger = logger;
    }

    public void NotifyWorkerCompleted(string workerName)
    {
        if (!_botOptions.RunOnce)
        {
            return;
        }

        var completed = Interlocked.Increment(ref _completedWorkers);
        _logger.LogInformation(
            "RunOnce mode: {WorkerName} completed ({Completed}/{Total}).",
            workerName,
            completed,
            WorkerCount);

        if (completed >= WorkerCount)
        {
            _logger.LogInformation("RunOnce mode: all worker cycles completed, stopping application.");
            _lifetime.StopApplication();
        }
    }

    private const int WorkerCount = 2;
}

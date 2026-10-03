using System.Diagnostics;
using CryptoTrendForge.Worker.Configuration;
using CryptoTrendForge.Worker.Infrastructure;
using CryptoTrendForge.Worker.Services;
using CryptoTrendForge.Worker.Workers;
using Microsoft.Extensions.Options;
using Serilog;

var logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
Directory.CreateDirectory(logDirectory);
Trace(logDirectory, "process entry");

var runOnce = WorkerHostSettings.IsRunOnce(AppContext.BaseDirectory, args);
var killedStuckWorkers = 0;
if (runOnce)
{
    killedStuckWorkers = StuckWorkerCleanup.KillOlderSiblingWorkers(TimeSpan.FromSeconds(20));
    if (killedStuckWorkers > 0)
    {
        Trace(logDirectory, $"killed {killedStuckWorkers} stuck sibling worker(s)");
        TryDeleteRunOnceMarker(logDirectory);
    }

    // Thread-pool timers can stall while HttpClient is shutting down, so this cannot be Task.Delay.
    var watchdog = new Thread(() =>
    {
        Thread.Sleep(TimeSpan.FromMinutes(12));
        Trace(logDirectory, "watchdog: forcing process kill");
        HardKill.Force();
    })
    {
        IsBackground = true,
        Name = "runonce-watchdog"
    };
    watchdog.Start();
}

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .WriteTo.File(
        Path.Combine(logDirectory, "worker-.txt"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        shared: true,
        flushToDiskInterval: TimeSpan.FromSeconds(1))
    .CreateLogger();

RunOnceOverlapGuard? runOnceOverlapGuard = null;

try
{
    AppendCronInvocation(logDirectory);
    Log.Information(
        "Worker process starting. BaseDirectory={BaseDirectory} RunOnce={RunOnce} KilledStuckWorkers={KilledStuckWorkers}",
        AppContext.BaseDirectory,
        runOnce,
        killedStuckWorkers);

    if (runOnce)
    {
        if (!RunOnceOverlapGuard.TryEnter(logDirectory, out runOnceOverlapGuard))
        {
            Log.Warning("RunOnce overlap guard blocked this invocation.");
            TryAppendSkippedRunMarker(logDirectory, "SKIPPED (previous RunOnce still active)");
            Trace(logDirectory, "skipped; previous run still active");
            return;
        }

        try
        {
            using var cycleTimeout = new CancellationTokenSource(TimeSpan.FromMinutes(12));
            await RunOnceCycle.ExecuteAsync(args, cycleTimeout.Token);
            Trace(logDirectory, "cycle finished");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "RunOnce cycle failed.");
            Trace(logDirectory, "cycle failed: " + ex.GetType().Name);
        }
        finally
        {
            runOnceOverlapGuard?.Dispose();
            runOnceOverlapGuard = null;
            Log.Information("RunOnce process exiting.");
            Trace(logDirectory, "forcing process exit");
            HardKill.Now();
        }

        return;
    }

    using var instanceLock = SingleInstanceLock.TryAcquire();
    if (instanceLock is null)
    {
        Log.Warning("Another CryptoTrendForge Worker instance is already running. Exiting.");
        TryAppendSkippedRunMarker(logDirectory, "SKIPPED (lock held by another instance)");
        return;
    }

    await RunHostAsync(args, logDirectory);
}
catch (Exception ex)
{
    Log.Fatal(ex, "Worker terminated unexpectedly.");
    throw;
}
finally
{
    runOnceOverlapGuard?.Dispose();
    Log.CloseAndFlush();
}

static async Task RunHostAsync(string[] args, string logDirectory)
{
    var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
    {
        Args = args,
        ContentRootPath = AppContext.BaseDirectory
    });
    builder.Configuration.AddJsonFile(
        $"appsettings.{builder.Environment.EnvironmentName}.local.json",
        optional: true,
        reloadOnChange: false);
    builder.Services.AddSerilog((services, config) => config
        .MinimumLevel.Information()
        .WriteTo.Console()
        .WriteTo.File(
            Path.Combine(logDirectory, "worker-.txt"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 14,
            shared: true,
            flushToDiskInterval: TimeSpan.FromSeconds(1)));

    WorkerServiceRegistration.AddWorkerServices(builder.Services, builder.Configuration);
    builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(20));
    builder.Services.AddHostedService(sp => sp.GetRequiredService<StartupInitializationService>());
    builder.Services.AddHostedService<SignalScanWorker>();
    builder.Services.AddHostedService<StockSignalScanWorker>();
    builder.Services.AddHostedService<SignalOutcomeWorker>();

    var app = builder.Build();

    var botOptions = app.Services.GetRequiredService<IOptions<BotOptions>>().Value;
    var telegramOptions = app.Services.GetRequiredService<IOptions<TelegramOptions>>().Value;
    Log.Information(
        "Config loaded. Environment={Environment} ContentRoot={ContentRoot} CurrentDirectory={CurrentDirectory} RunOnce={RunOnce} TelegramConfigured={TelegramConfigured} DbProvider={DbProvider}",
        builder.Environment.EnvironmentName,
        builder.Environment.ContentRootPath,
        Directory.GetCurrentDirectory(),
        botOptions.RunOnce,
        !string.IsNullOrWhiteSpace(telegramOptions.BotToken) && !string.IsNullOrWhiteSpace(telegramOptions.ChatId),
        builder.Configuration["Database:Provider"] ?? "(missing)");

    await app.RunAsync();
}

static void Trace(string logDirectory, string message)
{
    try
    {
        File.AppendAllText(
            Path.Combine(logDirectory, "run-trace.txt"),
            $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz} pid={Environment.ProcessId} {message}{Environment.NewLine}");
    }
    catch (IOException)
    {
    }
}

static void TryDeleteRunOnceMarker(string logDirectory)
{
    try
    {
        var path = Path.Combine(logDirectory, "runonce-active.marker");
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
    catch (IOException)
    {
    }
}

static void AppendCronInvocation(string logDirectory)
{
    try
    {
        var path = Path.Combine(logDirectory, "cron-invocations.txt");
        File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} pid={Environment.ProcessId}{Environment.NewLine}");
    }
    catch (IOException)
    {
    }
}

static void TryAppendSkippedRunMarker(string logDirectory, string reason)
{
    try
    {
        Directory.CreateDirectory(logDirectory);
        var path = Path.Combine(logDirectory, "last-run.txt");
        File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} | {reason}{Environment.NewLine}");
    }
    catch (IOException)
    {
    }
}

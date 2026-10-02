using CryptoTrendForge.Core.Infrastructure.Database;
using CryptoTrendForge.Core.Infrastructure.Http;
using CryptoTrendForge.Core.Infrastructure.Cache;
using CryptoTrendForge.Worker.Configuration;
using CryptoTrendForge.Worker.Infrastructure;
using CryptoTrendForge.Worker.Services;
using CryptoTrendForge.Worker.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;

var logDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
Directory.CreateDirectory(logDirectory);

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

try
{
    Log.Information("Worker process starting. BaseDirectory={BaseDirectory}", AppContext.BaseDirectory);

    using var instanceLock = SingleInstanceLock.TryAcquire();
    if (instanceLock is null)
    {
        Log.Warning("Another CryptoTrendForge Worker instance is already running. Exiting.");
        TryAppendSkippedRunMarker(logDirectory);
        return;
    }

    // Cron runs from an arbitrary working directory; always read appsettings beside the DLL.
    var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
    {
        Args = args,
        ContentRootPath = AppContext.BaseDirectory
    });
    builder.Configuration.AddJsonFile(
        $"appsettings.{builder.Environment.EnvironmentName}.local.json",
        optional: true,
        reloadOnChange: true);
    builder.Services.AddSerilog((services, config) => config
        .MinimumLevel.Information()
        .WriteTo.Console()
        .WriteTo.File(
            Path.Combine(logDirectory, "worker-.txt"),
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 14,
            shared: true,
            flushToDiskInterval: TimeSpan.FromSeconds(1)));

builder.Services.AddSingleton<IValidateOptions<BotOptions>, BotOptionsValidator>();
builder.Services.AddSingleton<IValidateOptions<StockOptions>, StockOptionsValidator>();
builder.Services.AddSingleton<IValidateOptions<BybitOptions>, BybitOptionsValidator>();
builder.Services
    .AddOptions<BotOptions>()
    .Bind(builder.Configuration.GetSection(BotOptions.SectionName))
    .ValidateOnStart();
builder.Services
    .AddOptions<BybitOptions>()
    .Bind(builder.Configuration.GetSection(BybitOptions.SectionName))
    .ValidateOnStart();
builder.Services
    .AddOptions<TelegramOptions>()
    .Bind(builder.Configuration.GetSection(TelegramOptions.SectionName));
builder.Services
    .AddOptions<StockOptions>()
    .Bind(builder.Configuration.GetSection(StockOptions.SectionName))
    .ValidateOnStart();
builder.Services.Configure<BybitClientOptions>(opt =>
{
    opt.MaxRetries = builder.Configuration.GetValue<int>("Bybit:MaxRetries", 3);
    opt.CircuitBreakerFailures = 5;
    opt.CircuitBreakerPauseSeconds = 60;
});

builder.Services.AddDbContext<AppDbContext>(options =>
    DatabaseConfiguration.ConfigureAppDbContext(options, builder.Configuration));

builder.Services.AddMemoryCache();
builder.Services.AddSingleton<MarketDataCache>();
builder.Services.AddScoped<TechnicalAnalysisService>();
builder.Services.AddScoped<BybitService>();
builder.Services.AddScoped<MarketDataService>();
builder.Services.AddScoped<BtcRegimeService>();
builder.Services.AddScoped<RiskFilterService>();
builder.Services.AddScoped<SignalEngine>();
builder.Services.AddScoped<StockSignalEngine>();
builder.Services.AddScoped<StockRiskFilterService>();
builder.Services.AddScoped<SignalRepository>();
builder.Services.AddScoped<TelegramService>();
builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(20));
builder.Services.AddSingleton<RunOnceCoordinator>();
builder.Services.AddHostedService<StartupInitializationService>();
builder.Services.AddHostedService<SignalScanWorker>();
builder.Services.AddHostedService<StockSignalScanWorker>();
builder.Services.AddHostedService<SignalOutcomeWorker>();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient<BybitHttpClient>((sp, client) =>
{
    var bybit = sp.GetRequiredService<IConfiguration>().GetSection(BybitOptions.SectionName).Get<BybitOptions>() ?? new BybitOptions();
    client.BaseAddress = new Uri(bybit.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(bybit.TimeoutSeconds);
});

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

    if (botOptions.RunOnce)
    {
        // Must stay below the Plesk cron interval (15 min) so a stuck run releases the lock.
        var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
        RunOnceForceExit.Register(lifetime, TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(25));
    }

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Worker terminated unexpectedly.");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

static void TryAppendSkippedRunMarker(string logDirectory)
{
    try
    {
        Directory.CreateDirectory(logDirectory);
        var path = Path.Combine(logDirectory, "last-run.txt");
        File.AppendAllText(
            path,
            $"{DateTimeOffset.UtcNow:O} | SKIPPED (lock held by another instance){Environment.NewLine}");
    }
    catch (IOException)
    {
    }
}

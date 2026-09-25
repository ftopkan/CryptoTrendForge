using CryptoTrendForge.Core.Infrastructure.Database;
using CryptoTrendForge.Core.Infrastructure.Http;
using CryptoTrendForge.Core.Infrastructure.Cache;
using CryptoTrendForge.Worker.Configuration;
using CryptoTrendForge.Worker.Services;
using CryptoTrendForge.Worker.Workers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);
builder.Configuration.AddJsonFile(
    $"appsettings.{builder.Environment.EnvironmentName}.local.json",
    optional: true,
    reloadOnChange: true);
builder.Services.AddSerilog(config => config.WriteTo.Console());

builder.Services.AddSingleton<IValidateOptions<BotOptions>, BotOptionsValidator>();
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
builder.Services.AddScoped<SignalRepository>();
builder.Services.AddScoped<TelegramService>();
builder.Services.AddHostedService<StartupInitializationService>();
builder.Services.AddHostedService<SignalScanWorker>();
builder.Services.AddHostedService<SignalOutcomeWorker>();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient<BybitHttpClient>((sp, client) =>
{
    var bybit = sp.GetRequiredService<IConfiguration>().GetSection(BybitOptions.SectionName).Get<BybitOptions>() ?? new BybitOptions();
    client.BaseAddress = new Uri(bybit.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(bybit.TimeoutSeconds);
});

var app = builder.Build();
await app.RunAsync();

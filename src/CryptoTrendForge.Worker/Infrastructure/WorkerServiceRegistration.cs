using CryptoTrendForge.Core.Infrastructure.Cache;
using CryptoTrendForge.Core.Infrastructure.Database;
using CryptoTrendForge.Core.Infrastructure.Http;
using CryptoTrendForge.Worker.Configuration;
using CryptoTrendForge.Worker.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CryptoTrendForge.Worker.Infrastructure;

internal static class WorkerServiceRegistration
{
    public static void AddWorkerServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<BotOptions>, BotOptionsValidator>();
        services.AddSingleton<IValidateOptions<StockOptions>, StockOptionsValidator>();
        services.AddSingleton<IValidateOptions<BybitOptions>, BybitOptionsValidator>();
        services
            .AddOptions<BotOptions>()
            .Bind(configuration.GetSection(BotOptions.SectionName))
            .ValidateOnStart();
        services
            .AddOptions<BybitOptions>()
            .Bind(configuration.GetSection(BybitOptions.SectionName))
            .ValidateOnStart();
        services
            .AddOptions<TelegramOptions>()
            .Bind(configuration.GetSection(TelegramOptions.SectionName));
        services
            .AddOptions<StockOptions>()
            .Bind(configuration.GetSection(StockOptions.SectionName))
            .ValidateOnStart();
        services.Configure<BybitClientOptions>(opt =>
        {
            opt.MaxRetries = configuration.GetValue<int>("Bybit:MaxRetries", 3);
            opt.CircuitBreakerFailures = 5;
            opt.CircuitBreakerPauseSeconds = 60;
        });

        services.AddDbContext<AppDbContext>(options =>
            DatabaseConfiguration.ConfigureAppDbContext(options, configuration));

        services.AddMemoryCache();
        services.AddSingleton<MarketDataCache>();
        services.AddScoped<TechnicalAnalysisService>();
        services.AddScoped<BybitService>();
        services.AddScoped<MarketDataService>();
        services.AddScoped<BtcRegimeService>();
        services.AddScoped<RiskFilterService>();
        services.AddScoped<SignalEngine>();
        services.AddScoped<StockSignalEngine>();
        services.AddScoped<StockRiskFilterService>();
        services.AddScoped<SignalRepository>();
        services.AddScoped<TelegramService>();
        services.AddScoped<CryptoEntryScanRunner>();
        services.AddScoped<StockEntryScanRunner>();
        services.AddScoped<SignalOutcomeRunner>();
        services.AddSingleton<StartupInitializationService>();
        services.AddHttpClient();
        services.AddHttpClient<BybitHttpClient>((sp, client) =>
        {
            var bybit = sp.GetRequiredService<IConfiguration>().GetSection(BybitOptions.SectionName).Get<BybitOptions>() ?? new BybitOptions();
            client.BaseAddress = new Uri(bybit.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(bybit.TimeoutSeconds);
        });
    }
}

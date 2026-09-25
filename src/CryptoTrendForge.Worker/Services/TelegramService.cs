using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using CryptoTrendForge.Core.Domain.Models;
using CryptoTrendForge.Worker.Configuration;
using Microsoft.Extensions.Options;

namespace CryptoTrendForge.Worker.Services;

public sealed class TelegramService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TelegramOptions _telegramOptions;
    private readonly TechnicalAnalysisService _technicalAnalysisService;
    private readonly ILogger<TelegramService> _logger;

    public TelegramService(
        IHttpClientFactory httpClientFactory,
        IOptions<TelegramOptions> telegramOptions,
        TechnicalAnalysisService technicalAnalysisService,
        ILogger<TelegramService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _telegramOptions = telegramOptions.Value;
        _technicalAnalysisService = technicalAnalysisService;
        _logger = logger;
    }

    public async Task<bool> SendSignalAsync(
        Signal signal,
        ScoreResult scoreResult,
        MarketSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_telegramOptions.BotToken) || string.IsNullOrWhiteSpace(_telegramOptions.ChatId))
        {
            _logger.LogWarning("Telegram is not configured. Signal {SignalId} stays pending.", signal.Id);
            return false;
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            var url = $"https://api.telegram.org/bot{_telegramOptions.BotToken}/sendMessage";
            var message = BuildMessage(signal, scoreResult, snapshot);
            var request = new
            {
                chat_id = _telegramOptions.ChatId,
                text = message
            };

            using var response = await client.PostAsJsonAsync(url, request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Telegram send failed for signal {SignalId}. Status: {StatusCode}", signal.Id, response.StatusCode);
                await SendAdminAlertAsync($"Signal notification failed for {snapshot.Symbol}. SignalId: {signal.Id}, Status: {response.StatusCode}", cancellationToken);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telegram send exception for signal {SignalId}", signal.Id);
            await SendAdminAlertAsync($"Signal notification exception for {snapshot.Symbol}. SignalId: {signal.Id}. {ex.Message}", cancellationToken);
            return false;
        }
    }

    public async Task SendAdminAlertAsync(string message, CancellationToken cancellationToken = default)
    {
        if (!_telegramOptions.AdminAlertsEnabled
            || string.IsNullOrWhiteSpace(_telegramOptions.BotToken)
            || string.IsNullOrWhiteSpace(_telegramOptions.AdminChatId))
        {
            return;
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            var url = $"https://api.telegram.org/bot{_telegramOptions.BotToken}/sendMessage";
            var request = new
            {
                chat_id = _telegramOptions.AdminChatId,
                text = $"ADMIN ALERT\n{DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC\n{message}"
            };

            using var response = await client.PostAsJsonAsync(url, request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Admin alert send failed. Status: {StatusCode}", response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Admin alert send threw exception.");
        }
    }

    private string BuildMessage(Signal signal, ScoreResult scoreResult, MarketSnapshot snapshot)
    {
        var sb = new StringBuilder();
        var typeText = signal.SignalType == Core.Domain.Enums.SignalType.StrongLongCandidate
            ? "STRONG LONG CANDIDATE"
            : "LONG CANDIDATE";

        var rsi4h = TryRsi(snapshot.Klines4H);
        var rsi1h = TryRsi(snapshot.Klines1H);
        var rsi15m = TryRsi(snapshot.Klines15M);
        var emaStructure = DescribeEmaStructure(snapshot.Klines4H);
        var volumeChange = CalculateVolumeChangePct(snapshot.Klines15M);

        sb.AppendLine(typeText);
        sb.AppendLine();
        sb.AppendLine($"{snapshot.Symbol} - ${snapshot.CurrentPrice.ToString("0.########", CultureInfo.InvariantCulture)}");
        sb.AppendLine($"Time: {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();
        sb.Append("Base Score: ")
            .Append(scoreResult.BaseScore.ToString(CultureInfo.InvariantCulture))
            .Append("/100");

        if (scoreResult.PatternBonus > 0)
        {
            sb.Append(" +")
                .Append(scoreResult.PatternBonus.ToString(CultureInfo.InvariantCulture))
                .Append(" pattern bonus");
        }

        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("Factor Breakdown:");
        sb.AppendLine($"Trend (EMA): +{scoreResult.Breakdown.GetValueOrDefault("trend")}/25");
        sb.AppendLine($"RSI: +{scoreResult.Breakdown.GetValueOrDefault("rsi")}/20");
        sb.AppendLine($"Volume: +{scoreResult.Breakdown.GetValueOrDefault("volume")}/20");
        sb.AppendLine($"Support: +{scoreResult.Breakdown.GetValueOrDefault("support")}/20");
        sb.AppendLine($"OI: +{scoreResult.Breakdown.GetValueOrDefault("oi")}/15");
        sb.AppendLine($"Pattern Bonus: +{scoreResult.PatternBonus}/10");
        sb.AppendLine();
        sb.AppendLine("Indicators:");
        sb.AppendLine($"RSI(14): 4H {rsi4h} | 1H {rsi1h} | 15M {rsi15m}");
        sb.AppendLine($"EMA Structure (4H): {emaStructure}");
        sb.AppendLine($"Support Distance: %{scoreResult.SupportDistancePct.ToString("0.##", CultureInfo.InvariantCulture)} (Level: ${scoreResult.SupportLevel.ToString("0.########", CultureInfo.InvariantCulture)})");
        sb.AppendLine($"Volume Change: {volumeChange:+0.##;-0.##;0}%");
        sb.AppendLine($"OI Change (4H): {snapshot.OpenInterestChangePct4H:+0.##;-0.##;0}%");
        sb.AppendLine($"Funding Rate: {(snapshot.FundingRate * 100m).ToString("+0.####;-0.####;0", CultureInfo.InvariantCulture)}%");
        sb.AppendLine($"Pattern (1H/4H): {scoreResult.PatternName ?? "None"}");
        sb.AppendLine($"Pattern (15M): {scoreResult.PatternName15m ?? "None"} (not scored)");
        sb.AppendLine($"BTC Regime: {signal.MarketRegime}");
        sb.AppendLine();
        sb.AppendLine("Reasons:");
        foreach (var reason in scoreResult.Reasons)
        {
            sb.AppendLine($"- {reason}");
        }

        if (scoreResult.Risks.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Risks:");
            foreach (var risk in scoreResult.Risks)
            {
                sb.AppendLine($"- {risk}");
            }
        }

        sb.AppendLine();
        sb.AppendLine($"SignalId: {signal.Id}");
        return sb.ToString();
    }

    private string DescribeEmaStructure(IReadOnlyList<Kline> klines4h)
    {
        if (klines4h.Count < 200)
        {
            return "Insufficient data";
        }

        var closes = klines4h.Select(x => x.Close);
        var ema20 = _technicalAnalysisService.CalculateEma(closes, 20)[^1];
        var ema50 = _technicalAnalysisService.CalculateEma(closes, 50)[^1];
        var ema200 = _technicalAnalysisService.CalculateEma(closes, 200)[^1];

        if (ema20 > ema50 && ema50 > ema200)
        {
            return "Bullish";
        }

        if (ema20 < ema50 && ema50 < ema200)
        {
            return "Bearish";
        }

        return "Neutral";
    }

    private decimal TryRsi(IReadOnlyList<Kline> klines)
    {
        if (klines.Count < 28)
        {
            return 0m;
        }

        return _technicalAnalysisService.CalculateRsi(klines, 14);
    }

    private static decimal CalculateVolumeChangePct(IReadOnlyList<Kline> klines)
    {
        if (klines.Count < 13)
        {
            return 0m;
        }

        var recent = klines.TakeLast(3).Average(x => x.Volume);
        var baseline = klines.Skip(klines.Count - 13).Take(10).Average(x => x.Volume);
        if (baseline == 0m)
        {
            return 0m;
        }

        return Math.Round(((recent / baseline) - 1m) * 100m, 2);
    }
}

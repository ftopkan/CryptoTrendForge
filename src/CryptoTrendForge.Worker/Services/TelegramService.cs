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
    private readonly ILogger<TelegramService> _logger;

    public TelegramService(
        IHttpClientFactory httpClientFactory,
        IOptions<TelegramOptions> telegramOptions,
        ILogger<TelegramService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _telegramOptions = telegramOptions.Value;
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
                await SendAdminAlertAsync($"Sinyal bildirimi gönderilemedi. {snapshot.Symbol}, sinyal no: {signal.Id}, durum: {response.StatusCode}", cancellationToken);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Telegram send exception for signal {SignalId}", signal.Id);
            await SendAdminAlertAsync($"Sinyal bildiriminde hata oluştu. {snapshot.Symbol}, sinyal no: {signal.Id}. {ex.Message}", cancellationToken);
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
                text = $"⚠️ Yönetici uyarısı\n{DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC\n{message}"
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

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private string BuildMessage(Signal signal, ScoreResult scoreResult, MarketSnapshot snapshot)
    {
        var sb = new StringBuilder();
        var typeText = signal.SignalType == Core.Domain.Enums.SignalType.StrongLongCandidate
            ? "🔥 GÜÇLÜ LONG ADAYI"
            : "🟢 LONG ADAYI";

        var plan = BuildPositionPlan(snapshot.CurrentPrice, scoreResult.SupportLevel);

        sb.AppendLine(typeText);
        sb.AppendLine();
        sb.AppendLine($"💎 {snapshot.Symbol} · ${FormatPrice(snapshot.CurrentPrice)}");
        sb.AppendLine($"🕒 {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        sb.AppendLine();
        sb.Append("📊 Temel skor: ")
            .Append(scoreResult.BaseScore.ToString(Turkish))
            .Append("/100");

        if (scoreResult.PatternBonus > 0)
        {
            sb.Append(" +")
                .Append(scoreResult.PatternBonus.ToString(Turkish))
                .Append(" mum yapısı bonusu");
        }

        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine($"📈 Trend (EMA): {FormatPoints(scoreResult.Breakdown.GetValueOrDefault("trend"))}/25");
        sb.AppendLine($"📉 RSI: {FormatPoints(scoreResult.Breakdown.GetValueOrDefault("rsi"))}/20");
        sb.AppendLine($"🔊 Hacim: {FormatPoints(scoreResult.Breakdown.GetValueOrDefault("volume"))}/20");
        sb.AppendLine($"🛡️ Destek: {FormatPoints(scoreResult.Breakdown.GetValueOrDefault("support"))}/20");
        sb.AppendLine($"📌 Açık işlem: {FormatPoints(scoreResult.Breakdown.GetValueOrDefault("oi"))}/15");
        sb.AppendLine($"✨ Mum yapısı bonusu: {FormatPoints(scoreResult.PatternBonus)}/10");
        sb.AppendLine();
        AppendPositionPlan(sb, plan);
        sb.AppendLine();
        sb.AppendLine("✅ Gerekçeler");
        foreach (var reason in scoreResult.Reasons)
        {
            sb.AppendLine($"• {reason}");
        }

        if (scoreResult.Risks.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("⚠️ Riskler");
            foreach (var risk in scoreResult.Risks)
            {
                sb.AppendLine($"• {risk}");
            }
        }

        sb.AppendLine();
        sb.AppendLine($"🆔 Sinyal no: {signal.Id}");
        return sb.ToString();
    }

    private readonly record struct PositionPlan(
        decimal Entry,
        string EntryNote,
        decimal Stop,
        decimal CautiousExit,
        decimal BalancedExit,
        decimal WideExit);

    private static PositionPlan BuildPositionPlan(decimal currentPrice, decimal supportLevel)
    {
        var entry = currentPrice;
        var entryNote = "güncel fiyat";

        var supportBelowPrice = supportLevel > 0m && supportLevel < currentPrice;
        if (supportBelowPrice)
        {
            var distancePct = ((currentPrice - supportLevel) / supportLevel) * 100m;
            var pullback = supportLevel * 1.01m;
            if (distancePct > 3m && pullback < currentPrice)
            {
                entry = pullback;
                entryNote = "desteğe çekilince";
            }
        }

        var stop = supportBelowPrice && supportLevel * 0.97m < entry
            ? supportLevel * 0.97m
            : entry * 0.97m;

        var risk = entry - stop;
        if (risk <= 0m)
        {
            stop = entry * 0.97m;
            risk = entry - stop;
        }

        return new PositionPlan(
            entry,
            entryNote,
            stop,
            entry + risk,
            entry + (risk * 2m),
            entry + (risk * 3m));
    }

    private static void AppendPositionPlan(StringBuilder sb, PositionPlan plan)
    {
        sb.AppendLine("🎯 Pozisyon");
        sb.AppendLine($"Giriş: ${FormatPrice(plan.Entry)} ({plan.EntryNote})");
        sb.AppendLine($"Zarar kes: ${FormatPrice(plan.Stop)} ({FormatMovePct(plan.Entry, plan.Stop)})");
        sb.AppendLine("Çıkışlar");
        sb.AppendLine($"• Temkinli: ${FormatPrice(plan.CautiousExit)} ({FormatMovePct(plan.Entry, plan.CautiousExit)})");
        sb.AppendLine($"• Dengeli: ${FormatPrice(plan.BalancedExit)} ({FormatMovePct(plan.Entry, plan.BalancedExit)})");
        sb.AppendLine($"• Geniş: ${FormatPrice(plan.WideExit)} ({FormatMovePct(plan.Entry, plan.WideExit)})");
    }

    private static string FormatPrice(decimal value)
    {
        return value.ToString("0.########", Turkish);
    }

    private static string FormatMovePct(decimal entry, decimal target)
    {
        if (entry <= 0m)
        {
            return "%0";
        }

        var pct = Math.Round(((target - entry) / entry) * 100m, 1);
        var sign = pct > 0m ? "+" : pct < 0m ? "-" : string.Empty;
        return sign + "%" + Math.Abs(pct).ToString("0.#", Turkish);
    }

    private static string FormatPoints(int value)
    {
        return value.ToString("+0;-0;0", Turkish);
    }
}

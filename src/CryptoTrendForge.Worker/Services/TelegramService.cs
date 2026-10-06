using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using CryptoTrendForge.Core.Domain;
using CryptoTrendForge.Core.Domain.Enums;
using CryptoTrendForge.Core.Domain.Models;
using CryptoTrendForge.Worker.Configuration;
using Microsoft.Extensions.Options;

namespace CryptoTrendForge.Worker.Services;

public sealed class TelegramService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TelegramOptions _telegramOptions;
    private readonly BotOptions _botOptions;
    private readonly ILogger<TelegramService> _logger;

    public TelegramService(
        IHttpClientFactory httpClientFactory,
        IOptions<TelegramOptions> telegramOptions,
        IOptions<BotOptions> botOptions,
        ILogger<TelegramService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _telegramOptions = telegramOptions.Value;
        _botOptions = botOptions.Value;
        _logger = logger;
    }

    public async Task<bool> SendSignalAsync(
        Signal signal,
        ScoreResult scoreResult,
        MarketSnapshot snapshot,
        CoinType coinType = CoinType.Crypto,
        decimal? btcPrice = null,
        IReadOnlyList<(string Symbol, int Score)>? leaders = null,
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
            var message = BuildMessage(signal, scoreResult, snapshot, coinType, btcPrice, leaders);
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

    /// <summary>
    /// Sends an admin alert. Returns null on success, or a short reason the personal chat did not receive it.
    /// </summary>
    public async Task<string?> SendAdminAlertAsync(string message, CancellationToken cancellationToken = default)
    {
        var adminChatId = _telegramOptions.AdminChatId?.Trim();
        if (!_telegramOptions.AdminAlertsEnabled)
        {
            return "AdminAlertsEnabled kapalı.";
        }

        if (string.IsNullOrWhiteSpace(_telegramOptions.BotToken))
        {
            return "BotToken boş.";
        }

        if (string.IsNullOrWhiteSpace(adminChatId))
        {
            return "Telegram__AdminChatId bu süreçte boş. Değişken worker servisinde kayıtlı değil.";
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            var url = $"https://api.telegram.org/bot{_telegramOptions.BotToken}/sendMessage";
            var request = new
            {
                chat_id = adminChatId,
                text = $"⚠️ Yönetici uyarısı\n{TurkeyTime.Format(DateTimeOffset.UtcNow)}\n{message}"
            };

            using var response = await client.PostAsJsonAsync(url, request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var description = ExtractTelegramDescription(body) ?? response.StatusCode.ToString();
            _logger.LogWarning(
                "Admin alert send failed. Status: {StatusCode}. ChatIdSuffix: {Suffix}. Description: {Description}",
                response.StatusCode,
                Tail(adminChatId),
                description);
            return $"Telegram kişisel sohbeti reddetti ({Tail(adminChatId)}): {description}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Admin alert send threw exception.");
            return $"Kişisel uyarı gönderilirken hata: {ex.Message}";
        }
    }

    public async Task SendChatNoticeAsync(string message, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_telegramOptions.BotToken) || string.IsNullOrWhiteSpace(_telegramOptions.ChatId))
        {
            return;
        }

        try
        {
            var client = _httpClientFactory.CreateClient();
            var url = $"https://api.telegram.org/bot{_telegramOptions.BotToken}/sendMessage";
            var request = new
            {
                chat_id = _telegramOptions.ChatId.Trim(),
                text = message
            };
            using var response = await client.PostAsJsonAsync(url, request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning("Chat notice failed. Status: {StatusCode}. Body: {Body}", response.StatusCode, body);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Chat notice threw exception.");
        }
    }

    private static string Tail(string value) => value.Length <= 4 ? value : value[^4..];

    private static string? ExtractTelegramDescription(string body)
    {
        const string marker = "\"description\":\"";
        var start = body.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += marker.Length;
        var end = body.IndexOf('"', start);
        return end > start ? body[start..end] : null;
    }

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private string BuildMessage(
        Signal signal,
        ScoreResult scoreResult,
        MarketSnapshot snapshot,
        CoinType coinType,
        decimal? btcPrice,
        IReadOnlyList<(string Symbol, int Score)>? leaders)
    {
        var sb = new StringBuilder();
        var isStock = coinType == CoinType.Stock;
        var typeText = signal.SignalType == SignalType.StrongLongCandidate
            ? isStock ? "📊 HİSSE GÜÇLÜ LONG ADAYI" : "🔥 GÜÇLÜ LONG ADAYI"
            : isStock ? "📊 HİSSE LONG ADAYI" : "🟢 LONG ADAYI";

        var plan = PositionPlan.Create(snapshot.CurrentPrice, scoreResult.SupportLevel, isStock ? null : btcPrice);

        sb.AppendLine(typeText);
        sb.AppendLine();
        sb.AppendLine($"💎 {snapshot.Symbol} · ${FormatPrice(snapshot.CurrentPrice)}");
        sb.AppendLine($"🕒 {TurkeyTime.Format(DateTimeOffset.UtcNow)}");
        if (isStock)
        {
            sb.AppendLine("🏛 Borsa: NYSE açık");
        }
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
        sb.AppendLine($"🛡️ Destek: {FormatPoints(scoreResult.Breakdown.GetValueOrDefault("support"))}/{(isStock ? 35 : 20)}");
        if (!isStock)
        {
            sb.AppendLine($"📌 Açık işlem: {FormatPoints(scoreResult.Breakdown.GetValueOrDefault("oi"))}/15");
            if (scoreResult.Breakdown.ContainsKey("btc_rs"))
            {
                sb.AppendLine($"🟠 Bitcoin'e göre: {FormatPoints(scoreResult.Breakdown.GetValueOrDefault("btc_rs"))}/8");
            }
        }
        sb.AppendLine($"✨ Mum yapısı bonusu: {FormatPoints(scoreResult.PatternBonus)}/{_botOptions.PatternBonusPoints.ToString(Turkish)}");
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

        AppendLeaders(sb, leaders, isStock);
        return sb.ToString();
    }

    private static void AppendLeaders(StringBuilder sb, IReadOnlyList<(string Symbol, int Score)>? leaders, bool isStock)
    {
        if (leaders is null || leaders.Count == 0)
        {
            return;
        }

        var title = isStock
            ? "Bu taramada en yüksek puanlı hisseler"
            : "Bu taramada en yüksek puanlı coinler";
        sb.AppendLine();
        sb.AppendLine($"🏆 {title}");
        for (var i = 0; i < leaders.Count; i++)
        {
            var item = leaders[i];
            sb.AppendLine($"{i + 1}. {item.Symbol} · {item.Score}");
        }
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
        if (plan.BtcEntryPrice is decimal btc && plan.CoinBtcRatio is decimal ratio)
        {
            sb.AppendLine($"BTC giriş: ${FormatPrice(btc)} · Coin/BTC: {ratio.ToString("0.##########", Turkish)}");
            sb.AppendLine("BTC yatay kalırsa hedefler dolar bazında geçerli.");
            sb.AppendLine("BTC %3'ten fazla düşerse temkinli hedef zor.");
        }
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

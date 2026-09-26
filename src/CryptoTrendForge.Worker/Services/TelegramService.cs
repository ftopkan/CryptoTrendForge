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

        var rsi4h = TryRsi(snapshot.Klines4H);
        var rsi1h = TryRsi(snapshot.Klines1H);
        var rsi15m = TryRsi(snapshot.Klines15M);
        var emaStructure = DescribeEmaStructure(snapshot.Klines4H);
        var volumeChange = CalculateVolumeChangePct(snapshot.Klines15M);
        var regimeText = signal.MarketRegime switch
        {
            Core.Domain.Enums.MarketRegime.RiskOn => "Bitcoin yükselişi destekliyor",
            Core.Domain.Enums.MarketRegime.RiskOff => "Bitcoin zayıf",
            _ => "Bitcoin yönsüz"
        };

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
        sb.AppendLine("🔎 Göstergeler");
        sb.AppendLine($"RSI(14): 4 saat {FormatDecimal(rsi4h)} · 1 saat {FormatDecimal(rsi1h)} · 15 dk {FormatDecimal(rsi15m)}");
        sb.AppendLine($"4 saatlik ortalama sırası: {emaStructure}");
        sb.AppendLine($"Desteğe uzaklık: %{FormatDecimal(scoreResult.SupportDistancePct)} (seviye ${FormatPrice(scoreResult.SupportLevel)})");
        sb.AppendLine($"Hacim değişimi: {volumeChange.ToString("+0.##;-0.##;0", Turkish)}%");
        sb.AppendLine($"Açık işlem değişimi (4 saat): {snapshot.OpenInterestChangePct4H.ToString("+0.##;-0.##;0", Turkish)}%");
        sb.AppendLine($"Fonlama oranı: {(snapshot.FundingRate * 100m).ToString("+0.####;-0.####;0", Turkish)}%");
        sb.AppendLine($"Mum yapısı (1 saat/4 saat): {scoreResult.PatternName ?? "Yok"}");
        sb.AppendLine($"Mum yapısı (15 dk): {scoreResult.PatternName15m ?? "Yok"} (puana eklenmedi)");
        sb.AppendLine($"Bitcoin durumu: {regimeText}");
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

    private static string FormatPrice(decimal value)
    {
        return value.ToString("0.########", Turkish);
    }

    private static string FormatDecimal(decimal value)
    {
        return value.ToString("0.##", Turkish);
    }

    private static string FormatPoints(int value)
    {
        return value.ToString("+0;-0;0", Turkish);
    }

    private string DescribeEmaStructure(IReadOnlyList<Kline> klines4h)
    {
        if (klines4h.Count < 200)
        {
            return "Yetersiz veri";
        }

        var closes = klines4h.Select(x => x.Close);
        var ema20 = _technicalAnalysisService.CalculateEma(closes, 20)[^1];
        var ema50 = _technicalAnalysisService.CalculateEma(closes, 50)[^1];
        var ema200 = _technicalAnalysisService.CalculateEma(closes, 200)[^1];

        if (ema20 > ema50 && ema50 > ema200)
        {
            return "Kısa ortalama üstte, yön yukarı";
        }

        if (ema20 < ema50 && ema50 < ema200)
        {
            return "Kısa ortalama altta, yön aşağı";
        }

        return "Ortalamalar karışık";
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

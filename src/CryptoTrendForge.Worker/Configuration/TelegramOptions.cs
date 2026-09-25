namespace CryptoTrendForge.Worker.Configuration;

public sealed class TelegramOptions
{
    public const string SectionName = "Telegram";

    public string BotToken { get; set; } = string.Empty;
    public string ChatId { get; set; } = string.Empty;
    public string? AdminChatId { get; set; }
    public bool AdminAlertsEnabled { get; set; } = false;
}

using CryptoTrendForge.Core.Domain.Enums;

namespace CryptoTrendForge.Core.Domain.Models;

public sealed class Coin
{
    public int Id { get; set; }
    public string Symbol { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public CoinType CoinType { get; set; } = CoinType.Crypto;
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

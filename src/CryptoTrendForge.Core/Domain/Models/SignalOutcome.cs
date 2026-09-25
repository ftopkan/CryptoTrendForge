namespace CryptoTrendForge.Core.Domain.Models;

public sealed class SignalOutcome
{
    public int Id { get; set; }
    public int SignalId { get; set; }
    public Signal? Signal { get; set; }
    public int MinutesElapsed { get; set; }
    public decimal PriceAt { get; set; }
    public decimal PriceChangePct { get; set; }
    public DateTimeOffset SnapshotAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

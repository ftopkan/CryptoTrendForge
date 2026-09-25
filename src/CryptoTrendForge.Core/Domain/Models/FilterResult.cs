namespace CryptoTrendForge.Core.Domain.Models;

public sealed class FilterResult
{
    public bool IsBlocked { get; set; }
    public string? Reason { get; set; }

    public static FilterResult Allowed() => new() { IsBlocked = false };

    public static FilterResult Blocked(string reason) => new()
    {
        IsBlocked = true,
        Reason = reason
    };
}

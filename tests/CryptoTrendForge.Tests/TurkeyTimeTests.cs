using CryptoTrendForge.Worker.Services;
using Xunit;

namespace CryptoTrendForge.Tests;

public sealed class TurkeyTimeTests
{
    [Fact]
    public void Format_ShiftsUtcByThreeHours()
    {
        var utc = new DateTimeOffset(2026, 9, 26, 14, 10, 24, TimeSpan.Zero);

        Assert.Equal("2026-09-26 17:10:24 TSİ", TurkeyTime.Format(utc));
    }
}

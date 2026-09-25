using CryptoTrendForge.Core.Domain.Models;
using CryptoTrendForge.Worker.Services;
using Xunit;

namespace CryptoTrendForge.Tests;

public sealed class TechnicalAnalysisTests
{
    private readonly TechnicalAnalysisService _service = new();

    [Fact]
    public void CalculateRsi_ReturnsHighValueForStrongUptrend()
    {
        var klines = Enumerable.Range(1, 40)
            .Select(i => new Kline
            {
                OpenTime = DateTimeOffset.UtcNow.AddMinutes(i),
                Close = 100 + i,
                Volume = 1000
            })
            .ToArray();

        var rsi = _service.CalculateRsi(klines, 14);

        Assert.True(rsi >= 90m);
    }

    [Fact]
    public void CalculateRsi_ThrowsWhenDataInsufficient()
    {
        var klines = Enumerable.Range(1, 20)
            .Select(i => new Kline { Close = 100 + i })
            .ToArray();

        Assert.Throws<ArgumentException>(() => _service.CalculateRsi(klines, 14));
    }

    [Fact]
    public void CalculateEma_ComputesExpectedLengthAndSeed()
    {
        var closes = new decimal[] { 10, 11, 12, 13, 14, 15, 16, 17, 18, 19 };
        var ema = _service.CalculateEma(closes, 5);

        Assert.Equal(closes.Length, ema.Length);
        Assert.Equal(12m, ema[4]); // Seed from SMA of first 5 values.
        Assert.True(ema[^1] > ema[4]);
    }

    [Fact]
    public void CalculateEma_ThrowsWhenDataInsufficient()
    {
        var closes = new decimal[] { 10, 11, 12 };

        Assert.Throws<ArgumentException>(() => _service.CalculateEma(closes, 5));
    }

    [Fact]
    public void CalculateRsi_ReturnsLowValueForStrongDowntrend()
    {
        var klines = Enumerable.Range(1, 40)
            .Select(i => new Kline
            {
                OpenTime = DateTimeOffset.UtcNow.AddMinutes(i),
                Close = 200 - i,
                Volume = 1000
            })
            .ToArray();

        var rsi = _service.CalculateRsi(klines, 14);

        Assert.True(rsi <= 10m);
    }
}

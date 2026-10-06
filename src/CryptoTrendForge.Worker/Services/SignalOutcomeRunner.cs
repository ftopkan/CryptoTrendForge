using CryptoTrendForge.Core.Domain;
using CryptoTrendForge.Core.Domain.Enums;
using CryptoTrendForge.Core.Domain.Models;
using CryptoTrendForge.Worker.Configuration;
using Microsoft.Extensions.Options;

namespace CryptoTrendForge.Worker.Services;

public sealed class SignalOutcomeRunner
{
    private static readonly int[] SnapshotMinutes = [15, 30, 60, 240];

    private readonly StockOptions _stockOptions;
    private readonly SignalRepository _signalRepository;
    private readonly BybitService _bybitService;
    private readonly ILogger<SignalOutcomeRunner> _logger;

    public SignalOutcomeRunner(
        IOptions<StockOptions> stockOptions,
        SignalRepository signalRepository,
        BybitService bybitService,
        ILogger<SignalOutcomeRunner> logger)
    {
        _stockOptions = stockOptions.Value;
        _signalRepository = signalRepository;
        _bybitService = bybitService;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await CaptureOutcomesAsync(cancellationToken);
        await EvaluateExitTargetsAsync(cancellationToken);
        _logger.LogInformation("Signal outcome cycle completed.");
    }

    private async Task CaptureOutcomesAsync(CancellationToken cancellationToken)
    {
        var activeSignals = await _signalRepository.GetActiveSignalsWithCoinAsync(cancellationToken);
        foreach (var signal in activeSignals)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (signal.Coin is null)
            {
                continue;
            }

            var ticker = await _bybitService.GetTickerAsync(signal.Coin.Symbol, cancellationToken);
            if (ticker is null)
            {
                continue;
            }

            await _signalRepository.InvalidateIfBrokenSupportAsync(signal.CoinId, ticker.LastPrice, cancellationToken);

            var elapsedMinutes = (int)Math.Floor((DateTimeOffset.UtcNow - signal.CreatedAt).TotalMinutes);
            foreach (var minuteMark in SnapshotMinutes)
            {
                if (elapsedMinutes < minuteMark)
                {
                    continue;
                }

                var exists = await _signalRepository.OutcomeExistsAsync(signal.Id, minuteMark, cancellationToken);
                if (exists)
                {
                    continue;
                }

                await _signalRepository.AddOutcomeAsync(
                    signal.Id,
                    minuteMark,
                    ticker.LastPrice,
                    signal.SignalPrice,
                    DateTimeOffset.UtcNow,
                    cancellationToken);
            }
        }

        await _signalRepository.ExpireDueSignalsAsync(DateTimeOffset.UtcNow, cancellationToken);
    }

    private async Task EvaluateExitTargetsAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var pending = await _signalRepository.GetSignalsPendingExitEvaluationAsync(now, cancellationToken);
        var changed = false;
        IReadOnlyList<Kline>? btcKlines = null;
        if (pending.Any(x => x.BtcEntryPrice is > 0m && x.Coin?.CoinType == CoinType.Crypto))
        {
            btcKlines = await _bybitService.GetKlinesAsync("BTCUSDT", "15", 200, cancellationToken);
        }

        foreach (var signal in pending)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (signal.Coin is null)
            {
                continue;
            }

            var klines = await _bybitService.GetKlinesAsync(signal.Coin.Symbol, "15", 200, cancellationToken);
            if (klines.Count == 0)
            {
                continue;
            }

            DateTimeOffset? countUntil = null;
            if (signal.Coin.CoinType == CoinType.Stock && signal.ExpiresAt is DateTimeOffset expiresAt)
            {
                countUntil = UsEquitySession.EvaluationEnd(signal.CreatedAt, expiresAt, _stockOptions);
            }

            var btcForSignal = signal.Coin.CoinType == CoinType.Crypto ? btcKlines : null;
            ExitTargetEvaluator.Apply(signal, klines, now, countUntil, btcForSignal);
            changed = true;
        }

        if (changed)
        {
            await _signalRepository.SaveChangesAsync(cancellationToken);
        }
    }
}

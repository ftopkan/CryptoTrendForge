using Microsoft.Extensions.Options;

namespace CryptoTrendForge.Worker.Configuration;

public sealed class BotOptionsValidator : IValidateOptions<BotOptions>
{
    public ValidateOptionsResult Validate(string? name, BotOptions options)
    {
        var errors = new List<string>();

        if (options.EntryCandleMinutes <= 0 || 60 % options.EntryCandleMinutes != 0)
        {
            errors.Add("BotSettings.EntryCandleMinutes must be a positive divisor of 60.");
        }

        if (options.CooldownHours <= 0)
        {
            errors.Add("BotSettings.CooldownHours must be greater than 0.");
        }

        if (options.CooldownBypassMinScoreDelta < 0)
        {
            errors.Add("BotSettings.CooldownBypassMinScoreDelta cannot be negative.");
        }

        if (options.CooldownBypassMinElapsedMinutes < 0)
        {
            errors.Add("BotSettings.CooldownBypassMinElapsedMinutes cannot be negative.");
        }

        if (options.Timeframes.Count == 0)
        {
            errors.Add("BotSettings.Timeframes must include at least one timeframe.");
        }

        if (options.RsiPeriod < 2)
        {
            errors.Add("BotSettings.RsiPeriod must be at least 2.");
        }

        if (options.SwingLookbackCandles <= 0 || options.SwingNeighborCount <= 0)
        {
            errors.Add("BotSettings swing settings must be greater than 0.");
        }

        if (options.ScoreThresholds.RiskOn.Candidate > options.ScoreThresholds.RiskOn.Strong)
        {
            errors.Add("BotSettings.ScoreThresholds.RiskOn.Candidate cannot exceed Strong.");
        }

        if (options.ScoreThresholds.Neutral.Candidate > options.ScoreThresholds.Neutral.Strong)
        {
            errors.Add("BotSettings.ScoreThresholds.Neutral.Candidate cannot exceed Strong.");
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}

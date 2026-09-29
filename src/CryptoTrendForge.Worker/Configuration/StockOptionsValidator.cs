using Microsoft.Extensions.Options;

namespace CryptoTrendForge.Worker.Configuration;

public sealed class StockOptionsValidator : IValidateOptions<StockOptions>
{
    public ValidateOptionsResult Validate(string? name, StockOptions options)
    {
        var errors = new List<string>();

        if (options.Candidate > options.Strong)
        {
            errors.Add("StockSettings.Candidate cannot exceed Strong.");
        }

        if (options.FundingRateHardFilterPct <= 0m)
        {
            errors.Add("StockSettings.FundingRateHardFilterPct must be greater than 0.");
        }

        var open = (options.MarketOpenHour * 60) + options.MarketOpenMinute;
        var close = (options.MarketCloseHour * 60) + options.MarketCloseMinute;
        if (open < 0 || close > (24 * 60) || open >= close)
        {
            errors.Add("StockSettings market hours must be a valid same-day US Eastern session.");
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}

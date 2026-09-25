using Microsoft.Extensions.Options;

namespace CryptoTrendForge.Worker.Configuration;

public sealed class BybitOptionsValidator : IValidateOptions<BybitOptions>
{
    public ValidateOptionsResult Validate(string? name, BybitOptions options)
    {
        var errors = new List<string>();

        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _))
        {
            errors.Add("Bybit.BaseUrl must be a valid absolute URL.");
        }

        if (options.TimeoutSeconds <= 0)
        {
            errors.Add("Bybit.TimeoutSeconds must be greater than 0.");
        }

        if (options.MaxRetries < 1)
        {
            errors.Add("Bybit.MaxRetries must be at least 1.");
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}

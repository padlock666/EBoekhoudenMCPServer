using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace EBoekhouden.Api.Configuration;

// A class rather than a record: generated ToString must never print the credential.
public sealed class EBoekhoudenOptions
{
    public const string SectionName = "EBoekhouden";
    public string AccessToken { get; set; } = "";
    public string Source { get; set; } = "";
    public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan RefreshBeforeExpiry { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromMilliseconds(200);
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(5);
    public int MaxReadAttempts { get; set; } = 3;
    public int MaxConcurrentReads { get; set; } = 4;
    public int CircuitFailureThreshold { get; set; } = 5;
    public TimeSpan CircuitBreakDuration { get; set; } = TimeSpan.FromSeconds(30);
}

internal sealed partial class EBoekhoudenOptionsValidator : IValidateOptions<EBoekhoudenOptions>
{
    public ValidateOptionsResult Validate(string? name, EBoekhoudenOptions options)
    {
        var failures = new List<string>();
        if (string.IsNullOrWhiteSpace(options.AccessToken)) failures.Add("EBoekhouden:AccessToken is required.");
        if (options.Source is null || !SourcePattern().IsMatch(options.Source))
            failures.Add("EBoekhouden:Source must match the documented 1..10 character pattern.");
        CheckDuration(options.SessionTimeout, TimeSpan.FromMinutes(2), "SessionTimeout", failures);
        CheckDuration(options.AttemptTimeout, TimeSpan.FromMinutes(2), "AttemptTimeout", failures);
        CheckDuration(options.OperationTimeout, TimeSpan.FromMinutes(5), "OperationTimeout", failures);
        CheckDuration(options.RefreshBeforeExpiry, TimeSpan.FromMinutes(5), "RefreshBeforeExpiry", failures);
        CheckDuration(options.RetryBaseDelay, TimeSpan.FromSeconds(30), "RetryBaseDelay", failures);
        CheckDuration(options.MaxRetryDelay, TimeSpan.FromSeconds(30), "MaxRetryDelay", failures);
        CheckDuration(options.CircuitBreakDuration, TimeSpan.FromMinutes(5), "CircuitBreakDuration", failures);
        if (options.RetryBaseDelay > options.MaxRetryDelay) failures.Add("RetryBaseDelay must not exceed MaxRetryDelay.");
        if (options.AttemptTimeout > options.OperationTimeout) failures.Add("AttemptTimeout must not exceed OperationTimeout.");
        if (options.MaxReadAttempts is < 1 or > 5) failures.Add("MaxReadAttempts must be between 1 and 5.");
        if (options.MaxConcurrentReads is < 1 or > 32) failures.Add("MaxConcurrentReads must be between 1 and 32.");
        if (options.CircuitFailureThreshold is < 1 or > 100) failures.Add("CircuitFailureThreshold must be between 1 and 100.");
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void CheckDuration(TimeSpan value, TimeSpan max, string name, List<string> failures)
    {
        if (value <= TimeSpan.Zero || value > max) failures.Add(name + " is outside the supported positive duration range.");
    }

    [GeneratedRegex(@"\A[\w_ ]{1,10}\z", RegexOptions.CultureInvariant)]
    private static partial Regex SourcePattern();
}

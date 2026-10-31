using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace EBoekhouden.Api.Configuration;

internal static class CredentialConfiguration
{
    internal static void Validate(IConfigurationSection section)
    {
        var allowed = typeof(EBoekhoudenOptions).GetProperties().Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var field in section.GetChildren())
        {
            if (!allowed.Contains(field.Key)) Fail();
            if (!field.Key.Equals(nameof(EBoekhoudenOptions.Keyring), StringComparison.OrdinalIgnoreCase))
            {
                if (field.Value is null || field.GetChildren().Any()) Fail();
                continue;
            }
            if (field.Value is not null || !field.GetChildren().Any()) Fail();
            foreach (var attribute in field.GetChildren())
                if (!(attribute.Key.Equals(nameof(KeyringOptions.Application), StringComparison.OrdinalIgnoreCase) ||
                      attribute.Key.Equals(nameof(KeyringOptions.Account), StringComparison.OrdinalIgnoreCase)) ||
                    string.IsNullOrWhiteSpace(attribute.Value) || attribute.GetChildren().Any()) Fail();
        }
    }

    private static void Fail() => throw new OptionsValidationException(nameof(EBoekhoudenOptions), typeof(EBoekhoudenOptions),
        ["Invalid e-Boekhouden configuration."]);
}

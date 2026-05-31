namespace EntraPimManager.Core.Configuration;

using Microsoft.Extensions.Options;

/// <summary>
/// Validates <see cref="EntraPimManagerOptions"/> shape — but is deliberately
/// lenient on <c>ClientId</c>: a missing or placeholder ClientId must NOT
/// crash startup. The first-run UI guides the user to enter a real one, so
/// the ClientId check is enforced softly in
/// <see cref="ViewModels.ShellViewModel"/> via the <c>NeedsConfiguration</c>
/// state instead.
/// </summary>
public sealed class EntraPimManagerOptionsValidator : IValidateOptions<EntraPimManagerOptions>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, EntraPimManagerOptions options)
    {
        var failures = new List<string>();

        if (options.Scopes is null || options.Scopes.Length == 0)
        {
            failures.Add($"{EntraPimManagerOptions.SectionName}:Scopes must contain at least one delegated Graph scope.");
        }

        if (options.AllowedTenants is { Length: > 0 } allowedTenants)
        {
            foreach (var tenant in allowedTenants)
            {
                if (!Guid.TryParse(tenant, out _))
                {
                    failures.Add(
                        $"{EntraPimManagerOptions.SectionName}:AllowedTenants contains a non-GUID value: '{tenant}'.");
                }
            }
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}

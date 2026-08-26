namespace EntraPimManager.Core.Configuration;

using EntraPimManager.Core.Auth;
using Microsoft.Extensions.Options;

/// <summary>
/// Validates <see cref="EntraPimManagerOptions"/> shape. An empty registration list
/// must NOT fail: the first-run UI guides the user to add one, so that state is
/// handled softly in <c>ShellViewModel.NeedsConfiguration</c>. Every entry that
/// does exist is checked strictly — there is no shipped placeholder, an entry only
/// exists because someone added it, and a malformed tenant id would silently never
/// match a sign-in while a misspelled cloud is invisible from the UI.
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

        var seenTenants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < options.TenantAppRegistrations.Count; i++)
        {
            var entry = options.TenantAppRegistrations[i];
            var prefix = $"{EntraPimManagerOptions.SectionName}:TenantAppRegistrations[{i}]";

            var cloudKnown = Enum.TryParse<EntraCloud>(entry.Cloud, ignoreCase: true, out var cloud);
            if (!cloudKnown)
            {
                var known = string.Join(", ", Enum.GetNames<EntraCloud>());
                failures.Add($"{prefix} has an unknown cloud '{entry.Cloud}'. Known clouds: {known}.");
            }

            var tenantKnown = Guid.TryParse(entry.TenantId, out var tenantId);
            if (!tenantKnown)
            {
                failures.Add($"{prefix} has a non-GUID TenantId: '{entry.TenantId}'.");
            }

            if (!Guid.TryParse(entry.ClientId, out _))
            {
                failures.Add($"{prefix} has a non-GUID ClientId: '{entry.ClientId}'.");
            }

            if (cloudKnown && tenantKnown && !seenTenants.Add($"{cloud}|{tenantId}"))
            {
                failures.Add($"{prefix} duplicates an earlier entry for tenant {tenantId} in {cloud}; only the first would ever be used.");
            }
        }

        return failures.Count > 0
            ? ValidateOptionsResult.Fail(failures)
            : ValidateOptionsResult.Success;
    }
}

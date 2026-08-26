namespace EntraPimManager.Core.Configuration;

using EntraPimManager.Core.Auth;
using Microsoft.Extensions.Options;

/// <summary>
/// Validates <see cref="EntraPimManagerOptions"/> shape — but is deliberately
/// lenient on the app registrations: a missing or placeholder client id must NOT
/// crash startup. The first-run UI guides the user to enter a real one, so
/// that check is enforced softly in
/// <see cref="ViewModels.ShellViewModel"/> via the <c>NeedsConfiguration</c>
/// state instead. The one thing rejected here is an unknown cloud name — that is
/// invisible from the UI, because the affected row silently never picks the value up.
/// <para/>
/// <see cref="EntraPimManagerOptions.TenantAppRegistrations"/> is held to a stricter
/// standard: an entry only exists because someone added it, there is no shipped
/// placeholder, and a malformed tenant id would silently never match — so every
/// field is checked and duplicates are rejected.
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

        // Only the cloud NAME is validated, never the client id. The shipped
        // appsettings.json carries a "YOUR-CLIENT-ID-HERE" placeholder, and the
        // Settings UI can leave a row blank — both must boot into the first-run CTA
        // rather than fail ValidateOnStart, which shuts the app down. Unusable ids
        // are filtered out by EntraPimManagerOptions.ConfiguredClouds instead.
        // A misspelled cloud, by contrast, is invisible from the UI: the row simply
        // never picks the value up. Fail loudly on that one.
        foreach (var cloudName in options.AppRegistrations.Keys)
        {
            if (!Enum.TryParse<EntraCloud>(cloudName, ignoreCase: true, out _))
            {
                var known = string.Join(", ", Enum.GetNames<EntraCloud>());
                failures.Add(
                    $"{EntraPimManagerOptions.SectionName}:AppRegistrations has an unknown cloud '{cloudName}'. Known clouds: {known}.");
            }
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

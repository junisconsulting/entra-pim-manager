namespace EntraPimManager.Core.Configuration;

using System.Text.Json;
using System.Text.Json.Nodes;
using EntraPimManager.Core.Auth;

/// <summary>
/// One-shot upgrade of a pre-0.7.0 per-user configuration. Those versions held one
/// multi-tenant client id per cloud (<c>AppRegistrations:{Cloud}</c>, before 0.4.2 a
/// bare <c>ClientId</c>) and enrolled any tenant through it, optionally limited by an
/// <c>AllowedTenants</c> whitelist. 0.7.0 knows only registrations pinned to a tenant,
/// so every tenant that was reachable — each enrolled account's tenant, plus every
/// whitelisted one — becomes an entry with that cloud's client id, the legacy keys are
/// removed, and the per-cloud token-cache files are renamed to the per-client-id names
/// so no account has to sign in again.
/// </summary>
/// <remarks>
/// Runs before the host is built, on the file the Settings UI writes. Synchronous file
/// I/O on purpose, like <see cref="AppPaths.MigrateLegacyDataDirectory"/>: it is a
/// startup step, not a service. A legacy client id whose cloud has neither an enrolled
/// account nor a whitelisted tenant cannot be migrated (there is no tenant to pin it
/// to) — it is reported as dropped so the caller can tell the user to add it again.
/// </remarks>
public static class LegacyRegistrationMigration
{
    /// <summary>
    /// Migrates <paramref name="configFilePath"/> using the enrollments in
    /// <paramref name="accountsFilePath"/> (<c>accounts.json</c>; missing or unreadable
    /// counts as empty) and renames the legacy cache files in <paramref name="cacheDirectory"/>.
    /// Returns <see cref="Result.Nothing"/> and leaves the file untouched when it carries
    /// no legacy keys.
    /// </summary>
    public static Result Run(string configFilePath, string accountsFilePath, string cacheDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(accountsFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);

        if (!File.Exists(configFilePath))
        {
            return Result.Nothing;
        }

        var root = LocalConfigStore.LoadRoot(configFilePath);
        if (root[EntraPimManagerOptions.SectionName] is not JsonObject section
            || !(section.ContainsKey("AppRegistrations") || section.ContainsKey("ClientId") || section.ContainsKey("AllowedTenants")))
        {
            return Result.Nothing;
        }

        var legacy = LegacyClientIds(section);
        var accounts = ReadAccounts(accountsFilePath);
        var list = LocalConfigStore.TenantRegistrationsOf(section);
        var migrated = new List<TenantAppRegistration>();
        var dropped = new List<(EntraCloud, string)>();

        foreach (var (cloud, clientId) in legacy)
        {
            var tenants = accounts
                .Where(a => a.Cloud == cloud)
                .Select(a => Guid.TryParse(a.TenantId, out var t) ? t : Guid.Empty)
                .Where(t => t != Guid.Empty)
                .ToHashSet();

            // A whitelisted tenant was reachable through the cloud-wide id even
            // without an enrollment. The whitelist never named a cloud; Global is
            // the only reading that cannot be wrong for a pre-China configuration,
            // and the first legacy cloud otherwise.
            if (cloud == legacy.Keys.First())
            {
                foreach (var allowed in WhitelistedTenants(section))
                {
                    tenants.Add(allowed);
                }
            }

            if (tenants.Count == 0)
            {
                dropped.Add((cloud, clientId));
            }

            // An entry the user already pinned before upgrading wins over the
            // legacy cloud-wide id — never overwrite it.
            foreach (var tenant in tenants.Where(t => LocalConfigStore.IndexOf(list, cloud, t) < 0))
            {
                LocalConfigStore.Upsert(list, cloud, tenant, clientId, label: null);
                migrated.Add(new TenantAppRegistration { TenantId = tenant.ToString(), ClientId = clientId, Cloud = cloud.ToString() });
            }

            RenameCache(cacheDirectory, cloud == EntraCloud.China ? "msal-china.cache" : "msal.cache", $"msal-{clientId}.cache");
            RenameCache(cacheDirectory, cloud == EntraCloud.China ? "msal-devicecode-china.cache" : "msal-devicecode.cache", $"msal-devicecode-{clientId}.cache");
        }

        section.Remove("AppRegistrations");
        section.Remove("ClientId");
        section.Remove("AllowedTenants");
        LocalConfigStore.WriteRoot(configFilePath, root);

        return new Result(migrated, dropped);
    }

    /// <summary>Usable (GUID) legacy client ids per cloud; Global first so the whitelist lands there.</summary>
    private static Dictionary<EntraCloud, string> LegacyClientIds(JsonObject section)
    {
        var result = new Dictionary<EntraCloud, string>();
        foreach (var cloud in Enum.GetValues<EntraCloud>())
        {
            var value = section["AppRegistrations"] is JsonObject registrations
                ? registrations.FirstOrDefault(p => string.Equals(p.Key, cloud.ToString(), StringComparison.OrdinalIgnoreCase)).Value
                : null;
            var clientId = StringOf(value);
            if (clientId is null && cloud == EntraCloud.Global)
            {
                clientId = StringOf(section["ClientId"]);
            }

            if (Guid.TryParse(clientId, out var parsed))
            {
                result[cloud] = parsed.ToString();
            }
        }

        return result;
    }

    private static IEnumerable<Guid> WhitelistedTenants(JsonObject section)
    {
        if (section["AllowedTenants"] is not JsonArray allowed)
        {
            yield break;
        }

        foreach (var node in allowed)
        {
            if (Guid.TryParse(StringOf(node), out var tenant))
            {
                yield return tenant;
            }
        }
    }

    private static string? StringOf(JsonNode? node)
        => node is JsonValue value && value.TryGetValue<string>(out var s) ? s.Trim() : null;

    private static IReadOnlyList<SignedInAccount> ReadAccounts(string accountsFilePath)
    {
        if (!File.Exists(accountsFilePath))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<SignedInAccount>>(File.ReadAllText(accountsFilePath)) ?? [];
        }
        catch (JsonException)
        {
            // The account store rebuilds an unreadable file empty at first use;
            // there is nothing to migrate for it either.
            return [];
        }
    }

    // DPAPI-encrypted caches are bound to the user, not the path, so a rename
    // keeps them readable. Never overwrite: a target that already exists belongs
    // to a registration the user added before upgrading.
    private static void RenameCache(string cacheDirectory, string legacyName, string newName)
    {
        var source = Path.Combine(cacheDirectory, legacyName);
        var target = Path.Combine(cacheDirectory, newName);
        if (File.Exists(source) && !File.Exists(target))
        {
            File.Move(source, target);
        }
    }

    /// <summary>What the migration did — for the startup log.</summary>
    /// <param name="Migrated">Entries created from legacy client ids.</param>
    /// <param name="Dropped">Legacy client ids without any tenant to pin them to.</param>
    public sealed record Result(
        IReadOnlyList<TenantAppRegistration> Migrated,
        IReadOnlyList<(EntraCloud Cloud, string ClientId)> Dropped)
    {
        public static Result Nothing { get; } = new([], []);
    }
}

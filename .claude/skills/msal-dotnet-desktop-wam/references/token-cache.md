# MSAL Token Cache — Persistence and Encryption

Default MSAL behaviour: tokens live in memory only. App restarts force a re-prompt. For a tray app that lives across sessions, this is unacceptable. The `Microsoft.Identity.Client.Extensions.Msal` package provides persistent, encrypted cache backed by DPAPI on Windows.

## Why DPAPI

DPAPI (Data Protection API) is built into Windows. Encryption keys are derived from the current Windows user's credentials. The encrypted cache file is:
- Unreadable by other users on the same machine
- Bound to the current user — copying the file to another machine/user won't work
- Survives reboots, password changes (Windows handles re-keying)

For a per-user-installed tray app, this is exactly the right model.

## Setup (Windows-only, minimal)

```csharp
using Microsoft.Identity.Client.Extensions.Msal;

var cacheDir = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "Entra PIM Manager");
Directory.CreateDirectory(cacheDir);

var storageProperties = new StorageCreationPropertiesBuilder(
        cacheFileName: "msal.cache",
        cacheDirectory: cacheDir)
    .Build();

var cacheHelper = await MsalCacheHelper.CreateAsync(storageProperties);
cacheHelper.RegisterCache(pca.UserTokenCache);
```

The cache file: `%LocalAppData%\Entra-PIM-Manager\msal.cache` — DPAPI-encrypted, user-scoped.

## Cross-platform variant (future-proofing)

If you ever need to ship on macOS or Linux:

```csharp
var storageProperties = new StorageCreationPropertiesBuilder(
        cacheFileName: "msal.cache",
        cacheDirectory: cacheDir)
    .WithMacKeyChain(
        serviceName: "Entra PIM Manager",
        accountName: "MSALCache")
    .WithLinuxKeyring(
        schemaName: "de.junis.Entra-PIM-Manager.tokencache",
        collection: MsalCacheHelper.LinuxKeyRingDefaultCollection,
        secretLabel: "MSAL token cache for Entra PIM Manager",
        attribute1: new KeyValuePair<string, string>("Version", "1"),
        attribute2: new KeyValuePair<string, string>("ProductGroup", "Entra PIM Manager"))
    .Build();
```

For Entra PIM Manager v1, Windows-only is fine — the cross-platform builder calls do nothing on Windows.

## Cache lifecycle

`MsalCacheHelper.RegisterCache(pca.UserTokenCache)` attaches the helper to MSAL's cache events. From that point:
- MSAL writes to memory cache; helper persists to disk
- On startup, helper hydrates memory cache from disk
- DPAPI handles encryption/decryption transparently

Don't manually call `ReadAsync`/`WriteAsync` — that's for low-level scenarios.

## Concurrent access

If two instances of Entra PIM Manager run for the same user (unlikely but possible — e.g., normal session + RDP), the cache file is locked during writes. `MsalCacheHelper` handles cross-process locking via a `.lockfile`. You don't need to do anything explicit.

For terminal server / multi-user-session scenarios, each Windows user gets their own `%LocalAppData%` and therefore their own cache file. No cross-user collisions.

## What's in the cache

The encrypted file contains:
- Access tokens (short-lived, ~1h)
- ID tokens (claims about the user)
- Refresh tokens (long-lived, used to get new access tokens)
- Account metadata (UPN, tenant ID, home account ID)

It does NOT contain:
- The user's Windows password
- The user's Entra password
- App secrets (Entra PIM Manager is a public client; no secrets exist)

## Cache eviction and reset

Sometimes you need to reset the cache — e.g., after a bug, after a config change, or on user request ("sign out"):

```csharp
public async Task ClearCacheAsync()
{
    var accounts = await pca.GetAccountsAsync();
    foreach (var account in accounts)
        await pca.RemoveAsync(account);
}
```

`RemoveAsync` clears the cache entries for that account, triggering re-auth on next call.

For a "fully forget me" UI button:
1. Call `ClearCacheAsync` above
2. Optionally delete the cache file: `File.Delete(Path.Combine(cacheDir, "msal.cache"))`
3. Reset MSAL state by recreating `IPublicClientApplication`

## Detecting cache corruption

Rarely, the cache file gets corrupted (disk issue, antivirus interference). Symptoms:
- `MsalCacheHelper.CreateAsync` throws
- `GetAccountsAsync` returns empty when it shouldn't
- Tokens fail validation

Recovery:
```csharp
try
{
    cacheHelper = await MsalCacheHelper.CreateAsync(storageProperties);
}
catch (Exception ex)
{
    Log.Warning(ex, "MSAL cache corrupted; rebuilding");
    var cacheFile = Path.Combine(cacheDir, "msal.cache");
    if (File.Exists(cacheFile)) File.Delete(cacheFile);
    cacheHelper = await MsalCacheHelper.CreateAsync(storageProperties);
}
```

## Cache verification

For diagnostics, verify the cache helper is functioning:

```csharp
cacheHelper.VerifyPersistence();
```

This writes a test value, reads it back, and throws if encryption/decryption fails. Useful as a startup health check.

## File path conventions

For a Per-User-installed tray app like Entra PIM Manager:

| File | Path | Purpose |
|---|---|---|
| Cache | `%LocalAppData%\Entra-PIM-Manager\msal.cache` | Token cache |
| Cache lock | `%LocalAppData%\Entra-PIM-Manager\msal.cache.lockfile` | Cross-process lock |
| Config | `%LocalAppData%\Entra-PIM-Manager\appsettings.json` | User-editable config |
| Logs | `%LocalAppData%\Entra-PIM-Manager\logs\Entra-PIM-Manager-YYYY-MM-DD.log` | Serilog rolling files |

All under `%LocalAppData%` because:
- Per-user (DPAPI works)
- User-writable (no UAC)
- Survives app uninstall (logs available for forensics)
- Does NOT roam (we don't want token cache on `%AppData%` roaming, that would break DPAPI binding)

## What NOT to do

- **Don't put the cache on a network share** — DPAPI keys are local; cache becomes unreadable
- **Don't put the cache in `%AppData%` (roaming)** — same issue + privacy implications
- **Don't share the cache between users** — DPAPI is per-user
- **Don't log cache contents** — these are bearer tokens; log only metadata (count, account UPN if non-sensitive, last refresh time)
- **Don't use a hardcoded cache filename** that's shared with other Anthropic-built / open-source MSAL apps on the same machine — keep Entra PIM Manager-specific so uninstall is clean

# MSAL WAM — Detailed Builder Configuration

This file goes deeper than the SKILL.md on `PublicClientApplicationBuilder` setup. Read it when you need to handle multi-tenant scenarios, national clouds, or special broker behaviour.

## Minimal builder

```csharp
var pca = PublicClientApplicationBuilder
    .Create(clientId)
    .WithAuthority($"https://login.microsoftonline.com/{tenantId}")
    .WithRedirectUri($"ms-appx-web://microsoft.aad.brokerplugin/{clientId}")
    .WithBroker(new BrokerOptions(BrokerOptions.OperatingSystems.Windows))
    .Build();
```

## Full production builder

```csharp
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Broker;

var pca = PublicClientApplicationBuilder
    .Create(clientId)
    .WithAuthority(AzureCloudInstance.AzurePublic, tenantId)
    .WithRedirectUri($"ms-appx-web://microsoft.aad.brokerplugin/{clientId}")
    .WithBroker(new BrokerOptions(BrokerOptions.OperatingSystems.Windows)
    {
        Title = "Entra PIM Manager",
        ListOperatingSystemAccounts = false,
        MsaPassthrough = false
    })
    .WithClientName("Entra PIM Manager")
    .WithClientVersion(typeof(Program).Assembly.GetName().Version?.ToString())
    .WithLogging(MsalLogCallback, LogLevel.Info, enablePiiLogging: false)
    .Build();
```

### BrokerOptions properties

| Property | Purpose | Default |
|---|---|---|
| `Title` | Window title shown in WAM prompt | App's executable name |
| `ListOperatingSystemAccounts` | Show Windows-joined account as sign-in option | `false` |
| `MsaPassthrough` | Allow personal Microsoft accounts (Outlook.com etc.) — usually `false` for enterprise apps | `false` |
| `HeaderText` | Extra header text in WAM dialog | none |

For Entra PIM Manager:
- `ListOperatingSystemAccounts = false` — **no Auto-SSO**. The Windows login on a typical Notebook is the regular Office-Worker account; PIM activation must run as a separate admin account. If WAM is allowed to surface the Windows account, it ends up as the only candidate (empty MSAL cache on first start) and the account picker collapses to the auth-method screen — the user cannot choose. See the `auth-no-sso-always-picker` memory.
- `MsaPassthrough = false` — admin accounts are not MSA.

## Authority variants

### Multi-tenant (what Entra PIM Manager uses)
```csharp
.WithAuthority(AzureCloudInstance.AzurePublic, AadAuthorityAudience.AzureAdMultipleOrgs)
```
One PCA per cloud, each serving every work-or-school tenant in that cloud. The
target tenant is selected per request with `.WithTenantId(...)`.

### Single-tenant
```csharp
.WithAuthority($"https://login.microsoftonline.com/{tenantId}")
// or equivalently
.WithAuthority(AzureCloudInstance.AzurePublic, tenantId)
```

### Common (allows MSA — generally avoid for privileged tools)
```csharp
.WithAuthority(AzureCloudInstance.AzurePublic, AadAuthorityAudience.AzureAdAndPersonalMicrosoftAccount)
```

### National clouds
```csharp
.WithAuthority(AzureCloudInstance.AzureUsGovernment, AadAuthorityAudience.AzureAdMultipleOrgs)  // GCC High
.WithAuthority(AzureCloudInstance.AzureChina, AadAuthorityAudience.AzureAdMultipleOrgs)         // 21Vianet
.WithAuthority(AzureCloudInstance.AzureGermany, tenantId)                                        // closed 2021, do not use
```

**A national cloud needs its own App Registration.** National clouds are physically
isolated instances of Entra with separate directories, authorities and Graph
endpoints. `AzureAdMultipleOrgs` means "every tenant *in this cloud*" — a client id
registered at `portal.azure.com` does not exist at `portal.azure.cn`, and sending it
there fails with `AADSTS700016`. Tokens are likewise not interchangeable between
clouds. Register separately in each cloud's portal
([Microsoft Learn](https://learn.microsoft.com/en-us/entra/identity-platform/authentication-national-cloud)).

Never hardcode the authority host: MSAL's `AzureCloudInstance.AzureChina` resolves
to `login.partner.microsoftonline.cn`, while some Microsoft docs still list the
legacy `login.chinacloudapi.cn`. Let the enum decide.

Entra PIM Manager keys everything off `EntraCloud` (`Core/Auth/EntraCloud.cs`): one
PCA, one token-cache file, one Graph base URL and one client id per cloud. See
`EntraCloudInfo` and `EntraPimManagerOptions.ClientIdFor`.

## Logging integration

```csharp
private void MsalLogCallback(LogLevel level, string message, bool containsPii)
{
    if (containsPii) return; // NEVER log PII

    var serilogLevel = level switch
    {
        LogLevel.Error => Serilog.Events.LogEventLevel.Error,
        LogLevel.Warning => Serilog.Events.LogEventLevel.Warning,
        LogLevel.Info => Serilog.Events.LogEventLevel.Information,
        LogLevel.Verbose => Serilog.Events.LogEventLevel.Debug,
        _ => Serilog.Events.LogEventLevel.Debug
    };

    Log.Write(serilogLevel, "[MSAL] {Message}", message);
}
```

`enablePiiLogging: false` is the default and correct setting. Only enable PII logging for local debugging by individual developers — never in CI, never in production builds.

## Diagnosing which auth flow was used

After acquiring a token:

```csharp
var result = await pca.AcquireTokenSilent(scopes, account).ExecuteAsync();

Log.Information("Token acquired via {Source}, scopes: {Scopes}, account: {Account}",
    result.AuthenticationResultMetadata.TokenSource,  // Cache / IdentityProvider / Broker
    string.Join(",", result.Scopes),
    result.Account.Username);
```

`TokenSource` values:
- `Cache` — silent from MSAL cache (zero network)
- `IdentityProvider` — fresh from Entra (network call)
- `Broker` — from WAM broker (this is the WAM happy path)

If you expect WAM but see `IdentityProvider`, your broker setup isn't engaging — check NuGet package, redirect URI, OS version.

## App registration requirements

For WAM to work end-to-end, the app registration must have:

1. **Platform type**: "Mobile and desktop applications"
2. **Redirect URIs** (add ALL of these):
   - `ms-appx-web://microsoft.aad.brokerplugin/{clientId}` — for WAM
   - `http://localhost` — for browser fallback (older OS, AAD B2C if ever)
3. **Allow public client flows**: Yes
4. **Implicit grant**: None
5. **Supported account types**: "Multitenant" for a tool that serves several tenants
   (Entra PIM Manager does); "Single tenant" for an in-house tool. Either way, one
   registration **per cloud** — see "National clouds" above.

PowerShell to verify:
```powershell
$app = Get-MgApplication -Filter "appId eq '$clientId'"
$app.PublicClient.RedirectUris
$app.IsFallbackPublicClient  # should be true
```

## Multi-instance and multi-thread

`IPublicClientApplication` is thread-safe for token operations. However, if multiple threads call `AcquireTokenSilent` simultaneously for the same scopes, they may all do a network call when one would have sufficed.

**Pattern**: Single PCA instance per app, single auth service wraps it with a semaphore for the silent-then-interactive logic:

```csharp
private readonly SemaphoreSlim _authLock = new(1, 1);

public async Task<AuthenticationResult> AcquireTokenAsync(string[] scopes, ...)
{
    await _authLock.WaitAsync();
    try
    {
        // silent → interactive logic
    }
    finally { _authLock.Release(); }
}
```

## When silent acquisition fails

`AcquireTokenSilent` throws `MsalUiRequiredException` in many situations:
- Token expired and refresh token also expired
- User signed out elsewhere (e.g., admin revoked session)
- New CA policy requires re-auth
- Account removed from cache
- CAE event invalidated token

In ALL these cases, the answer is the same: fall back to `AcquireTokenInteractive`. Don't try to handle subcategories — WAM will sort it.

Exceptions other than `MsalUiRequiredException` (e.g., `MsalServiceException` with non-null `Claims`) need claims-challenge handling — see `claims-challenge.md`.

## Stopping the silent loop

Avoid this pattern:
```csharp
// BAD — can loop forever
while (true)
{
    try { return await pca.AcquireTokenSilent(...).ExecuteAsync(); }
    catch { await Task.Delay(1000); }
}
```

Silent → interactive should be the only retry. After interactive fails, surface to user — don't retry indefinitely.

## Testing locally

For development, individual developers use their own admin account in a test tenant. For unit/integration tests, mock `IPublicClientApplication` directly:

```csharp
var mockPca = new Mock<IPublicClientApplication>();
mockPca.Setup(p => p.AcquireTokenSilent(It.IsAny<string[]>(), It.IsAny<IAccount>()))
       .Returns(Mock.Of<AcquireTokenSilentParameterBuilder>(...));
```

Use `MockedAuthenticationResult` patterns; `AuthenticationResult` has internal constructors that need workarounds (reflection or a test-only factory).

---
name: msal-dotnet-desktop-wam
description: Reference for integrating MSAL.NET with the Windows Web Account Manager (WAM) broker in desktop and tray applications. Use this skill whenever building or modifying authentication code in a Windows desktop .NET app — WPF, WinForms, Console, Tray, anything that needs to acquire tokens for Microsoft Graph or other Entra-protected resources. Critical when handling Conditional Access claims challenges, configuring the encrypted token cache, dealing with Windows Hello, FIDO, CAE, or migrating away from embedded browser auth. Load this skill whenever code uses Microsoft.Identity.Client, IPublicClientApplication, PublicClientApplicationBuilder, or any auth flow that targets the Microsoft identity platform from a Windows desktop app. Do not write MSAL desktop code from memory — older patterns (.WithBrokerPreview, embedded WebView) are obsolete and common in training data.
---

# MSAL.NET with WAM Broker — Desktop Apps

This skill captures the current (MSAL 4.66+) patterns for Windows desktop authentication using the WAM (Web Account Manager) broker. Older patterns from `.WithBrokerPreview()` or pre-broker MSAL are common in training data and obsolete. This skill exists to keep new code on the current path.

## When to use

Load this skill any time the work involves:

- Initializing `IPublicClientApplication` or `PublicClientApplicationBuilder` for a Windows desktop app
- Token cache configuration (especially DPAPI/encrypted persistence)
- Acquiring tokens silently with interactive fallback
- Handling Conditional Access claims challenges (CAE step-up, Auth Strength)
- Integrating with Microsoft.Graph SDK v5+ via custom `IAuthenticationProvider`
- App manifest configuration for per-user install
- Windows Hello, FIDO2, or device-compliance-aware auth flows

## Why WAM matters

The WAM broker is a Windows OS component that handles auth on behalf of apps. Using it gives you:

- **Conditional Access support** — including device compliance, auth strength, token protection
- **Windows Hello & FIDO2** — native, no extra code
- **Refresh token isolation** — your app never sees the RT, only access tokens
- **CAE (Continuous Access Evaluation)** support — token revocation propagates within minutes
- **Native Windows account picker** — clean enterprise UX

Without WAM (browser/embedded WebView fallback), CA policies often misfire or block silently. For privileged-access apps like a PIM tray tool, **WAM is non-negotiable**.

> **Entra PIM Manager note on SSO:** Even though WAM can offer "Sign in with the current Windows account" SSO via `ListOperatingSystemAccounts = true`, **Entra PIM Manager intentionally disables this**. The Windows login on user notebooks is the regular Office-Worker account; PIM activation has to happen with a dedicated admin account. Auto-SSO would silently bind the app to the wrong identity and there is no way back to an account picker once WAM has chosen. See the `auth-no-sso-always-picker` memory and the explicit-sign-in code pattern below.

## NuGet packages required

```xml
<PackageReference Include="Microsoft.Identity.Client" Version="4.66.0" />
<PackageReference Include="Microsoft.Identity.Client.Broker" Version="4.66.0" />
<PackageReference Include="Microsoft.Identity.Client.Extensions.Msal" Version="4.66.0" />
<PackageReference Include="Microsoft.Graph" Version="5.56.0" />
```

**`Microsoft.Identity.Client.Broker` is separate** — easy to miss. Without it, `.WithBroker(BrokerOptions)` won't resolve.

`Microsoft.Identity.Client.Extensions.Msal` provides the encrypted token cache.

## Builder pattern (current, verified)

```csharp
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Broker;

var pca = PublicClientApplicationBuilder
    .Create(clientId)
    .WithAuthority($"https://login.microsoftonline.com/{tenantId}")
    .WithRedirectUri($"ms-appx-web://microsoft.aad.brokerplugin/{clientId}")
    .WithBroker(new BrokerOptions(BrokerOptions.OperatingSystems.Windows)
    {
        Title = "Entra PIM Manager",

        // Entra PIM Manager: do NOT surface the Windows session account. With only
        // one candidate, the WAM picker collapses to the auth-method screen
        // and the user can no longer choose a different identity. The admin
        // account is typically NOT the Windows login account.
        ListOperatingSystemAccounts = false,
        MsaPassthrough = false,
    })
    .WithLogging((level, message, containsPii) =>
    {
        // Hand to Serilog at appropriate level; never log when containsPii=true
        if (!containsPii) Log.Debug("[MSAL {Level}] {Message}", level, message);
    })
    .Build();
```

Key points:
- `.WithBroker(new BrokerOptions(...))` — **not** the obsolete `.WithBrokerPreview()`
- `ListOperatingSystemAccounts = false` is the **Entra PIM Manager-correct** value — every other MSAL+WAM tutorial sets `true` for SSO ergonomics; do not "fix" it back to `true` (see `auth-no-sso-always-picker` memory)
- The redirect URI `ms-appx-web://microsoft.aad.brokerplugin/{clientId}` must also be registered on the app registration as a "Mobile and desktop applications" platform redirect
- `.WithLogging(...)` — pipe MSAL diagnostics into your logger. Never log when `containsPii=true`.

See `references/wam-setup.md` for additional builder options (national clouds, multi-tenant, etc.).

## Two distinct flows: explicit sign-in vs. silent renewal

Entra PIM Manager separates these explicitly. Conflating them is what causes the WAM "auto-picks the wrong account" bug.

### Explicit `SignInAsync` — user clicked "Sign in" in the tray

Always shows the WAM account picker. No silent attempt, no account hint, no `OperatingSystemAccount` fallback.

```csharp
public async Task<SignedInUser> SignInAsync(CancellationToken ct = default)
{
    var pca = await EnsurePcaAsync(ct);

    var interactive = pca.AcquireTokenInteractive(scopes)
        .WithPrompt(Prompt.SelectAccount)                          // force picker
        .WithParentActivityOrWindow(GetParentWindowHandle());      // no IntPtr.Zero

    var result = await interactive.ExecuteAsync(ct);
    return ToSignedInUser(result.Account, result);
}
```

`Prompt.SelectAccount` + no `.WithAccount(...)` + `ListOperatingSystemAccounts = false` together guarantee the account selection UX:
- ≥ 1 cached account → picker with cached accounts + "Use another account"
- 0 cached accounts → "Sign in with another account" / UPN entry screen

### Silent renewal `AcquireTokenAsync` — Graph calls after sign-in

Uses only the account from the persisted MSAL cache. If no cached account exists, signals "user must sign in" — does NOT fall back to the Windows session account.

```csharp
public async Task<AuthenticationResult> AcquireTokenAsync(
    string[] scopes,
    string? claimsChallenge = null,
    CancellationToken ct = default)
{
    var pca = await EnsurePcaAsync(ct);
    var accounts = await pca.GetAccountsAsync();
    var account = accounts.FirstOrDefault();

    // No cached account — the user must run SignInAsync explicitly. Falling
    // back to OperatingSystemAccount here would silently bind the app to
    // the Windows session identity and bypass the picker.
    if (account is null)
        throw new MsalUiRequiredException(
            MsalError.UserNullError, "Sign in via the tray menu first.");

    try
    {
        var silent = pca.AcquireTokenSilent(scopes, account);
        if (claimsChallenge is not null)
            silent = silent.WithClaims(claimsChallenge).WithForceRefresh(true);
        return await silent.ExecuteAsync(ct);
    }
    catch (MsalUiRequiredException)
    {
        // Re-auth (claims challenge / expired RT). Stay pinned to the same
        // account the user originally chose — never silently switch identity.
        var interactive = pca.AcquireTokenInteractive(scopes)
            .WithAccount(account)
            .WithParentActivityOrWindow(GetParentWindowHandle());
        if (claimsChallenge is not null)
            interactive = interactive.WithClaims(claimsChallenge);
        return await interactive.ExecuteAsync(ct);
    }
}
```

Why each piece matters:

- **`Prompt.SelectAccount`** — forces WAM to show the account picker. Without this, WAM defaults to whatever single candidate it knows about, and the picker collapses to the auth-method screen with no way back to account selection.
- **No `OperatingSystemAccount` fallback** — that sentinel tells WAM "use the Windows session account." For Entra PIM Manager that is by definition the *wrong* identity.
- **`WithAccount(account)` on the silent-retry interactive call** — pins to the account the user originally chose. Without this, WAM might switch accounts mid-session on a claims challenge.
- **`WithParentActivityOrWindow(...)`** — without this, the auth prompt can pop hidden behind your window or off-screen. For a tray app where no window is focused, see the section below.
- **`WithClaims()`** — re-requests token with a Conditional Access claims challenge embedded. See `references/claims-challenge.md`.

### Parent window for tray apps

Tray apps frequently have no foreground window. Strategies in order of preference:

1. **If a dialog is visible**: pass that window's handle.
   ```csharp
   var helper = new WindowInteropHelper(myDialog);
   var hwnd = helper.EnsureHandle();
   ```
2. **If no dialog visible**: track the last activated window in a hidden helper.
3. **Last resort**: `GetForegroundWindow()` — may pick a random window the user has focused, but prompt will at least be visible.

Never pass `IntPtr.Zero` — WAM will sometimes work, sometimes silently fail, sometimes show a prompt off-screen.

## Token cache persistence

Default in-memory cache is lost on app restart, forcing re-prompts. Use `Microsoft.Identity.Client.Extensions.Msal` for cross-restart persistence with platform-appropriate encryption (DPAPI on Windows):

```csharp
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

On Windows, DPAPI encrypts the cache per-user. Cache file at `%LocalAppData%\Entra-PIM-Manager\msal.cache` is unreadable by other users on the same machine.

See `references/token-cache.md` for cross-platform variants and concurrent-access locking.

## Conditional Access claims challenges

When a downstream API responds with `WWW-Authenticate: Bearer ... claims="<base64url>"`, the user's token doesn't satisfy a Conditional Access requirement (typically MFA, compliant device, or auth strength). The flow:

1. Parse the `claims` value from the `WWW-Authenticate` header
2. Base64URL-decode it (note: URL-safe variant, with `-_` and no padding)
3. Pass the decoded JSON to `AcquireTokenInteractive(...).WithClaims(claims)`
4. WAM prompts the user to satisfy the requirement (Hello, FIDO, MFA)
5. New token has the required claims
6. Retry the original API request

See `references/claims-challenge.md` for the full implementation including Graph SDK integration.

## Graph SDK v5 integration

Don't use the `TokenCredential`-based wiring; for desktop, the cleanest integration is a custom `IAuthenticationProvider`:

```csharp
public class MsalAuthProvider : IAuthenticationProvider
{
    private readonly MsalAuthService _auth;
    private readonly string[] _scopes;

    public MsalAuthProvider(MsalAuthService auth, string[] scopes)
    {
        _auth = auth;
        _scopes = scopes;
    }

    public async Task AuthenticateRequestAsync(
        RequestInformation request,
        Dictionary<string, object>? additionalAuthenticationContext = null,
        CancellationToken cancellationToken = default)
    {
        string? claims = null;
        if (additionalAuthenticationContext is not null
            && additionalAuthenticationContext.TryGetValue("claims", out var c))
            claims = c as string;

        var result = await _auth.AcquireTokenAsync(_scopes, claims, cancellationToken);
        request.Headers.Add("Authorization", $"Bearer {result.AccessToken}");
    }
}
```

Wire it up:
```csharp
var graph = new GraphServiceClient(new MsalAuthProvider(authService, scopes));
```

For Graph requests that return 401 with claims challenge, your HTTP middleware or service layer needs to catch, extract claims, and retry. See `references/claims-challenge.md`.

## App manifest — per-user install requirement

For a tray app that installs under `%LocalAppData%` without UAC, the embedded manifest must declare `asInvoker`:

```xml
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <trustInfo xmlns="urn:schemas-microsoft-com:asm.v3">
    <security>
      <requestedPrivileges xmlns="urn:schemas-microsoft-com:asm.v3">
        <requestedExecutionLevel level="asInvoker" uiAccess="false" />
      </requestedPrivileges>
    </security>
  </trustInfo>
  <compatibility xmlns="urn:schemas-microsoft-com:compatibility.v1">
    <application>
      <!-- Windows 10 / 11 -->
      <supportedOS Id="{8e0f7a12-bfb3-4fe8-b9a5-48fd50a15a9a}" />
    </application>
  </compatibility>
</assembly>
```

**Never** `requireAdministrator`. That cascades into install-path constraints, registry-write attempts, and AppLocker rejections.

Reference the manifest from `.csproj`:
```xml
<PropertyGroup>
  <ApplicationManifest>app.manifest</ApplicationManifest>
</PropertyGroup>
```

## Reference files

- **`references/wam-setup.md`** — Detailed builder configuration: regional clouds, multi-tenant, broker options, logging, diagnostics
- **`references/token-cache.md`** — Cache encryption setup, cross-platform variants, concurrent access, cache eviction
- **`references/claims-challenge.md`** — Full claims-challenge parsing, base64url decoding, retry pattern, Graph SDK middleware integration

## Things that will trip you up

1. **`Microsoft.Identity.Client.Broker` is a separate NuGet package** — listing only `Microsoft.Identity.Client` won't give you `.WithBroker(BrokerOptions)`. The compiler error is confusing ("BrokerOptions not found").
2. **`OperatingSystemAccount` is not in `GetAccountsAsync()` results** — it's a static sentinel property on `PublicClientApplication`, not an account in the cache. For Entra PIM Manager, **never** use this sentinel — see the SSO note at the top of this skill.
3. **WAM "auto-picks" the wrong account when only one candidate is known** — symptom: the dialog opens directly on the auth-method screen (PIN / FIDO / password) for one specific account, and "Back" loops to the same screen instead of an account picker. Cause: `ListOperatingSystemAccounts = true` plus an empty MSAL cache leaves WAM with exactly one candidate (the Windows session account); `Prompt.SelectAccount` is a no-op in that case. Fix: set `ListOperatingSystemAccounts = false`.
4. **WAM falls back to a browser silently on Windows < 10.0.17763** — log the auth flow used (`AuthenticationResult.AuthenticationResultMetadata.TokenSource`) to diagnose surprising prompts.
5. **Embedded browser is dead** — `.WithUseEmbeddedWebView(true)` doesn't combine with broker. Pick one. For desktop apps with Entra, always pick broker.
6. **`.WithBrokerPreview()` is obsolete** — references in blog posts pre-2022 predate the GA broker API. Don't paste them.
7. **AAD B2C and ADFS are NOT supported by WAM** — falls back to browser. If your app must support these, plan UX for it. Entra PIM Manager targets Entra-only tenants, so this isn't a concern here.
8. **For a tray app, foreground window may be null** — `GetForegroundWindow()` can return `IntPtr.Zero` if nothing is focused. Track the last interactive window or always use a hidden helper window as fallback.
9. **`IPublicClientApplication` is thread-safe**, but cache operations during concurrent token acquisition can race. Use a `SemaphoreSlim` around token acquisition if you call from multiple threads.
10. **Don't call `.GetAccountsAsync()` on the UI thread without await** — it does I/O and can block.
11. **CAE tokens are short-lived (5–15 min sometimes)** — frequent silent renewals are normal and cheap. Don't try to "save calls" by caching tokens longer than MSAL says.

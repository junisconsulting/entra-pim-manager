# MSAL Claims Challenge Handling

Conditional Access (CA) and Continuous Access Evaluation (CAE) can require step-up authentication for specific operations. The mechanism is the **claims challenge**: a downstream API returns 401 with a `WWW-Authenticate` header containing a `claims` parameter. MSAL re-acquires the token with those claims, WAM prompts the user to satisfy them (MFA, compliant device, Auth Strength, etc.), and the new token can access the protected resource.

This is critical for PIM activation flows because:
- Tenants running PIM commonly have a CA policy requiring strong auth for activation
- "Authentication Context" policies (used by PIM) emit claims challenges by design
- Without handling this, users get "Access Denied" instead of a step-up prompt

## The challenge flow

```
1. App calls Graph: POST /roleAssignmentScheduleRequests
2. Graph: 401 Unauthorized
   WWW-Authenticate: Bearer realm="", authorization_uri="...",
                     error="insufficient_claims",
                     claims="eyJhY2Nlc3NfdG9rZW4iOnsiYWNycyI6eyJlc3NlbnRpYWwiOnRydWUsInZhbHVlIjoiYzEifX19"
3. App parses claims, base64url-decodes it
4. App calls MSAL: AcquireTokenInteractive(scopes).WithClaims(decodedClaims)
5. WAM prompts user: "Verify your identity" (Hello / FIDO / MFA)
6. New token has the required acrs claim
7. App retries original Graph call → succeeds
```

## Parsing the WWW-Authenticate header

The header value is comma-separated key-value pairs. Real-world values are messy — they contain quotes, equals signs, and the claims value is itself base64url-encoded JSON.

```csharp
public static string? ExtractClaimsChallenge(HttpResponseMessage response)
{
    if (response.StatusCode != HttpStatusCode.Unauthorized)
        return null;

    foreach (var header in response.Headers.WwwAuthenticate)
    {
        if (!string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
            continue;

        var parameter = header.Parameter;
        if (string.IsNullOrEmpty(parameter))
            continue;

        // parameter is like: realm="", authorization_uri="...", error="insufficient_claims", claims="..."
        var claimsMatch = Regex.Match(parameter, @"claims=""([^""]+)""");
        if (claimsMatch.Success)
        {
            var base64Url = claimsMatch.Groups[1].Value;
            return DecodeBase64Url(base64Url);
        }
    }

    return null;
}

private static string DecodeBase64Url(string base64Url)
{
    // Base64url uses - and _ instead of + and / and omits padding
    var base64 = base64Url
        .Replace('-', '+')
        .Replace('_', '/');

    // Pad to multiple of 4
    var padding = base64.Length % 4;
    if (padding > 0) base64 += new string('=', 4 - padding);

    var bytes = Convert.FromBase64String(base64);
    return Encoding.UTF8.GetString(bytes);
}
```

Decoded claims look like:
```json
{
  "access_token": {
    "acrs": {
      "essential": true,
      "value": "c1"
    }
  }
}
```

`c1` is an Authentication Context Class Reference — defined per-tenant in the Conditional Access settings.

## Passing claims to MSAL

```csharp
var result = await pca
    .AcquireTokenInteractive(scopes)
    .WithAccount(account)
    .WithParentActivityOrWindow(GetForegroundWindow())
    .WithClaims(decodedClaims)  // pass the decoded JSON string
    .ExecuteAsync();
```

`.WithClaims()` takes the full JSON string, not a parsed object. MSAL forwards it to Entra; Entra reads it; WAM prompts the user to satisfy whatever claim is requested.

## Silent attempt first (CAE optimization)

CAE-issued tokens are revocable mid-lifetime. The Graph response might say "your token is technically valid but no longer satisfies our requirements". Often, the user can satisfy this without a UI prompt (e.g., Windows Hello is still verified). Try silent first:

```csharp
try
{
    var silent = pca.AcquireTokenSilent(scopes, account)
        .WithClaims(decodedClaims)
        .WithForceRefresh(true);  // important: force re-issue, don't read from cache
    return await silent.ExecuteAsync();
}
catch (MsalUiRequiredException)
{
    // Fall back to interactive
    return await pca.AcquireTokenInteractive(scopes)
        .WithAccount(account)
        .WithClaims(decodedClaims)
        .WithParentActivityOrWindow(GetForegroundWindow())
        .ExecuteAsync();
}
```

`.WithForceRefresh(true)` is essential when handling a claims challenge — otherwise MSAL returns the same cached (insufficient) token.

## Integration with Graph SDK

For Graph requests, intercept 401s in a custom `DelegatingHandler` or in your `IAuthenticationProvider`:

```csharp
public class ClaimsChallengeHandler : DelegatingHandler
{
    private readonly MsalAuthService _auth;
    private readonly string[] _scopes;

    public ClaimsChallengeHandler(MsalAuthService auth, string[] scopes)
    {
        _auth = auth;
        _scopes = scopes;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken);

        if (response.StatusCode != HttpStatusCode.Unauthorized)
            return response;

        var claims = ExtractClaimsChallenge(response);
        if (claims is null)
            return response; // 401 not due to claims — bubble up

        // Acquire new token with claims
        var result = await _auth.AcquireTokenAsync(_scopes, claims, cancellationToken);

        // Clone the original request (HttpRequestMessage can only be sent once)
        var retryRequest = await CloneRequestAsync(request);
        retryRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", result.AccessToken);

        // Dispose old response, send retry
        response.Dispose();
        return await base.SendAsync(retryRequest, cancellationToken);
    }
}

private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage original)
{
    var clone = new HttpRequestMessage(original.Method, original.RequestUri);

    foreach (var header in original.Headers)
        clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

    if (original.Content is not null)
    {
        var ms = new MemoryStream();
        await original.Content.CopyToAsync(ms);
        ms.Position = 0;
        clone.Content = new StreamContent(ms);
        foreach (var contentHeader in original.Content.Headers)
            clone.Content.Headers.TryAddWithoutValidation(contentHeader.Key, contentHeader.Value);
    }

    return clone;
}
```

Wire it into `GraphServiceClient`:

```csharp
var httpClient = GraphClientFactory.Create(new ClaimsChallengeHandler(authService, scopes)
{
    InnerHandler = new HttpClientHandler()
});

var graph = new GraphServiceClient(httpClient, authProvider);
```

## What NOT to do

### Don't retry indefinitely
After one claims-challenge retry, if the request fails again, surface to user. Loop limit = 1.

### Don't cache the claims
The claims string is one-shot per challenge. Caching it across requests can cause spurious prompts.

### Don't try to "pre-emptively" satisfy claims
Some apps try to embed claims in every request to avoid challenges. This is anti-pattern: it forces step-up auth even when not needed, degrading UX. Wait for the API to demand it.

### Don't expose claims to UI
The claims JSON is an implementation detail. Show user "Verification required" — never paste the JSON in a dialog.

### Don't log claims values
The claims string can contain ACR values that hint at internal policy structure. Log only "claims challenge received" at INFO level; log the full claims at DEBUG only when debugging an issue locally.

## Authentication Context as the canonical use case

For PIM specifically, a tenant may have:
1. A Conditional Access policy: "When activating PIM, require Authentication Context c1 (= phishing-resistant MFA)"
2. A PIM policy rule (`AuthenticationContext_EndUser_Assignment`) referencing `c1`

When the user clicks "Activate Global Admin":
1. Entra PIM Manager's existing token doesn't have the `acrs:c1` claim
2. Graph returns 401 with claims challenge
3. Handler extracts claims, calls MSAL with `.WithClaims(...)`
4. WAM prompts: "Use your security key" (FIDO2) or "Confirm via Authenticator"
5. User satisfies it
6. New token has `acrs:["c1"]`
7. Activation request retries successfully

This is the design — it's not an exception path, it's the main flow for high-privilege activations.

## Testing

Hard to unit-test end-to-end. Pragmatic approach:
- Unit-test `ExtractClaimsChallenge` and `DecodeBase64Url` with fixture headers
- Unit-test `ClaimsChallengeHandler` with a fake inner handler that returns 401-then-200
- Manual end-to-end test against a tenant with an Auth Context CA policy targeting the test user

Sample fixture for `ExtractClaimsChallenge`:
```
WWW-Authenticate: Bearer realm="", authorization_uri="https://login.microsoftonline.com/.../authorize", error="insufficient_claims", claims="eyJhY2Nlc3NfdG9rZW4iOnsiYWNycyI6eyJlc3NlbnRpYWwiOnRydWUsInZhbHVlIjoiYzEifX19"
```
Expected decoded:
```json
{"access_token":{"acrs":{"essential":true,"value":"c1"}}}
```

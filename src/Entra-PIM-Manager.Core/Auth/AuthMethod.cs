namespace EntraPimManager.Core.Auth;

/// <summary>
/// How an enrolled account authenticates. Persisted per <see cref="SignedInAccount"/>
/// so token renewal routes to the matching MSAL public-client instance and never
/// silently switches an account's auth path.
/// </summary>
public enum AuthMethod
{
    /// <summary>
    /// Windows WAM broker (the default). Refresh tokens live in WAM, not in the
    /// MSAL cache file; renewal and re-auth go through the broker.
    /// </summary>
    Broker = 0,

    /// <summary>
    /// OAuth 2.0 device-code flow on a broker-less public client. The user
    /// completes sign-in on a second device (typically a phone), which decouples
    /// the federated leg from the Windows machine's browser session — the escape
    /// hatch for tenants whose external IdP (e.g. Okta) does aggressive seamless
    /// SSO and binds the app to the wrong (Office) identity. See the
    /// <c>federated-idp-sso-wrong-account</c> memory.
    /// </summary>
    DeviceCode = 1,
}

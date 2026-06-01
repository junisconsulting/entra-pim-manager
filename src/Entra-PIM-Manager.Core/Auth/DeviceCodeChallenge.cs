namespace EntraPimManager.Core.Auth;

/// <summary>
/// The instructions MSAL returns when a device-code sign-in starts: the user must
/// open <see cref="VerificationUri"/> on any device and enter <see cref="UserCode"/>.
/// Surfaced to the UI so it can show the code and a copy/open affordance while the
/// auth call polls in the background.
/// </summary>
/// <remarks>
/// <see cref="Message"/> is MSAL's own human-readable instruction string; the UI
/// may show it verbatim instead of composing its own. None of these fields are
/// secret in the token sense, but the user code is single-use and short-lived
/// (<see cref="ExpiresOn"/>), so it should not be logged.
/// </remarks>
public sealed record DeviceCodeChallenge(
    string UserCode,
    string VerificationUri,
    string Message,
    DateTimeOffset ExpiresOn);

namespace EntraPimManager.Core.Models;

/// <summary>
/// A Graph error translated into something safe and useful to show the user.
/// Raw Graph messages and stack traces never reach the UI.
/// </summary>
/// <param name="Severity">How the UI should treat this error.</param>
/// <param name="Message">User-facing message.</param>
/// <param name="FieldHint">
/// Which input field the error relates to (<c>justification</c>, <c>ticket</c>,
/// <c>duration</c>), for inline validation — or <c>null</c>.
/// </param>
/// <param name="ConsentMissing">
/// True when the cause is a tenant that has not consented to the App Registration. The
/// add-account panel then shows the junis-hosted admin-consent link below the message —
/// the one thing the user can hand to an admin.
/// </param>
public sealed record UserFacingError(
    ErrorSeverity Severity,
    string Message,
    string? FieldHint,
    bool ConsentMissing = false);

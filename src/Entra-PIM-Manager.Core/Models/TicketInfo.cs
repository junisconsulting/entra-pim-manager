namespace EntraPimManager.Core.Models;

/// <summary>
/// A ticket reference attached to an activation when the policy requires ticketing.
/// </summary>
/// <param name="TicketNumber">The ticket number (safe to log — not sensitive).</param>
/// <param name="TicketSystem">
/// The ticketing system, e.g. "ServiceNow". <c>null</c> when the user gave none:
/// the ticketing rule makes only the *number* mandatory, so the field is omitted
/// from the request rather than sent as an empty string.
/// </param>
public sealed record TicketInfo(
    string TicketNumber,
    string? TicketSystem);

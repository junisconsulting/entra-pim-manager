namespace EntraPimManager.Core.Models;

/// <summary>
/// A ticket reference attached to an activation when the policy requires ticketing.
/// </summary>
/// <param name="TicketNumber">The ticket number (safe to log — not sensitive).</param>
/// <param name="TicketSystem">The ticketing system, e.g. "ServiceNow".</param>
public sealed record TicketInfo(
    string TicketNumber,
    string TicketSystem);

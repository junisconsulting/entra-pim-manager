namespace EntraPimManager.Core.Auth;

/// <summary>
/// An Entra identity the user has enrolled in Entra PIM Manager. Identified internally
/// by (<see cref="ObjectId"/>, <see cref="TenantId"/>) — the same home identity
/// can be enrolled in multiple tenants. <see cref="Cloud"/> distinguishes the
/// sovereign Entra cloud each enrollment lives in.
/// </summary>
/// <remarks>
/// <see cref="Username"/> and <see cref="DisplayName"/> are for UI display and must
/// never be written to logs in clear text. Only <see cref="ObjectId"/>,
/// <see cref="TenantId"/> and <see cref="Cloud"/> are safe for log output.
/// <para/>
/// <see cref="Cloud"/> defaults to <see cref="EntraCloud.Global"/> so persisted
/// enrollments written before this field existed deserialize correctly.
/// </remarks>
public sealed record SignedInAccount(
    string ObjectId,
    string TenantId,
    string Username,
    string? DisplayName,
    DateTimeOffset AddedAt,
    EntraCloud Cloud = EntraCloud.Global);

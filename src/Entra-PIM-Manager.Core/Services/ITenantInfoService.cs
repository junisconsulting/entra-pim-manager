namespace EntraPimManager.Core.Services;

using EntraPimManager.Core.Auth;

/// <summary>
/// Resolves tenant-level metadata via the Graph <c>/organization</c> endpoint.
/// Only the parts that need a Graph round-trip live here — the tenant <i>id</i>
/// is already carried on <see cref="SignedInAccount"/>.
/// </summary>
public interface ITenantInfoService
{
    /// <summary>
    /// Returns the display name of the tenant <paramref name="account"/> is signed
    /// into (e.g. <c>"Junis GmbH"</c>), or <c>null</c> when the call fails or the
    /// tenant has none configured.
    /// </summary>
    Task<string?> GetTenantDisplayNameAsync(SignedInAccount account, CancellationToken ct = default);
}

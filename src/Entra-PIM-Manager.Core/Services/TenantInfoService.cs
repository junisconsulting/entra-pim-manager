namespace EntraPimManager.Core.Services;

using EntraPimManager.Core.Auth;
using EntraPimManager.Core.Graph;
using Microsoft.Graph.Models.ODataErrors;

/// <summary>
/// Default <see cref="ITenantInfoService"/> implementation. The Graph
/// <c>/organization</c> collection always contains exactly one entry — the
/// tenant the caller is signed into — so we just take the first hit.
/// </summary>
public sealed class TenantInfoService : ITenantInfoService
{
    private readonly IGraphClientFactory _factory;

    public TenantInfoService(IGraphClientFactory factory)
    {
        _factory = factory;
    }

    /// <inheritdoc />
    public async Task<string?> GetTenantDisplayNameAsync(SignedInAccount account, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        var graph = _factory.CreateFor(account);
        try
        {
            var response = await graph.Organization.GetAsync(cancellationToken: ct);
            return response?.Value?.FirstOrDefault()?.DisplayName;
        }
        catch (ODataError)
        {
            // /organization is delegated-readable with User.Read in most tenants;
            // a denial here is rare and not actionable for the user — fall back to null.
            return null;
        }
    }
}

namespace EntraPimManager.Core.Auth;

/// <summary>
/// Persists the list of Entra accounts the user has enrolled in Entra PIM Manager.
/// This is metadata only — tokens stay in the encrypted MSAL cache. The store
/// lets the UI render the account list across restarts without prompting MSAL
/// (which is async and serialized through the auth lock).
/// </summary>
/// <remarks>
/// Enrollments are identified by the composite (<see cref="SignedInAccount.ObjectId"/>,
/// <see cref="SignedInAccount.TenantId"/>): the same Entra identity can be enrolled
/// in multiple tenants (home + guest tenants), which produces entries that share
/// an oid but differ in tenant id.
/// </remarks>
public interface IAccountStore
{
    /// <summary>Returns all enrolled accounts in stable insertion order.</summary>
    Task<IReadOnlyList<SignedInAccount>> GetAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Adds or updates <paramref name="account"/>. Matching is by the composite
    /// (oid, tenantId); an existing entry's <see cref="SignedInAccount.AddedAt"/>
    /// is preserved.
    /// </summary>
    Task UpsertAsync(SignedInAccount account, CancellationToken ct = default);

    /// <summary>Removes the enrollment matching the (oid, tenantId) pair. No-op if absent.</summary>
    Task RemoveAsync(string objectId, string tenantId, CancellationToken ct = default);

    /// <summary>
    /// Persists the enrollments in the order given. Matching is by (oid, tenantId)
    /// — enrollments in <paramref name="orderedAccounts"/> that aren't currently
    /// known are ignored; stored enrollments that aren't in
    /// <paramref name="orderedAccounts"/> are appended at the end so reorder
    /// cannot accidentally lose data when the caller's view of the world is stale.
    /// </summary>
    Task ReorderAsync(IReadOnlyList<SignedInAccount> orderedAccounts, CancellationToken ct = default);
}

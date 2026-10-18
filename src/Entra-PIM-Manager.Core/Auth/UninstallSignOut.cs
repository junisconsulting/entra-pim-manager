namespace EntraPimManager.Core.Auth;

/// <summary>
/// The part of an uninstall that deleting the data directory cannot do.
/// </summary>
public static class UninstallSignOut
{
    /// <summary>
    /// Removes every account enrolled through the WAM broker, the way Settings → Accounts →
    /// Remove does: <see cref="IAuthService.RemoveAccountAsync"/> signs it out of the app in
    /// Windows' token broker. Its refresh token lives there, outside the data directory, so
    /// without this the account stays signed in to the app after the app is gone.
    /// </summary>
    /// <remarks>
    /// Device-code accounts are skipped: their refresh tokens are in the data directory, which
    /// the uninstall deletes. Best effort per account, so one that fails does not keep the
    /// others signed in; only <paramref name="ct"/> ends the loop early.
    /// </remarks>
    public static async Task SignOutBrokerAccountsAsync(IAuthService auth, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(auth);

        foreach (var account in await auth.GetAllAccountsAsync(ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            if (account.AuthMethod != AuthMethod.Broker)
            {
                continue;
            }

            try
            {
                await auth.RemoveAccountAsync(account.ObjectId, account.TenantId, account.Cloud, ct).ConfigureAwait(false);
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // Includes a timeout inside MSAL or the broker that surfaces as a cancellation
                // nobody asked for: the next account still gets its sign-out.
            }
        }
    }
}

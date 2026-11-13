namespace EntraPimManager.Core.Auth;

using System.Buffers.Binary;
using System.Globalization;
using Microsoft.Identity.Client;

/// <summary>
/// Decides when a lost sign-in may be recovered through the Windows account: only for an
/// enrolled broker account that is provably the signed-in Windows user, and at most one
/// failed attempt per enrollment, scope set and process.
/// </summary>
/// <remarks>
/// Some hosts lose the token cache at every logon (issue #13: Entra joined pooled AVD with
/// FSLogix, where DPAPI cannot decrypt the previous logon's cache). When the enrolled account
/// is the Windows account, Windows still holds a sign-in for it. Asking Windows for the wrong
/// account is not harmless, though: it is a real sign-in of that account to this app, logged
/// in its tenant. Hence the exact local check first and the cap on failures, because MSAL
/// does not throttle broker requests. Kept out of <see cref="MsalAuthService"/>, which drives
/// the live broker and cannot be unit-tested. Token requests are serialized by the auth
/// service's lock, so the failure set needs no locking.
/// </remarks>
internal sealed class WindowsAccountSafetyNet
{
    /// <summary>
    /// Thrown by Microsoft.Identity.Client.Broker 4.66.0 when WAM no longer knows the account, as
    /// on a pooled host after every logon; MSAL has no constant for it.
    /// </summary>
    internal const string WamNoAccountForId = "wam_no_account_for_id";

    private readonly Guid? _sessionObjectId;
    private readonly HashSet<string> _failed = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="sessionSid">The Windows session user's SID, or null when it could not be read.</param>
    public WindowsAccountSafetyNet(string? sessionSid) => _sessionObjectId = ObjectIdFromSid(sessionSid);

    /// <summary>
    /// The Entra object id behind a Microsoft Entra SID (<c>S-1-12-1-a-b-c-d</c>), or null for
    /// any other SID. Windows forms such a SID from the object id's 16 bytes, read as four
    /// little-endian uint32; an AD or local SID carries no object id.
    /// </summary>
    public static Guid? ObjectIdFromSid(string? sid)
    {
        var parts = sid?.Split('-');
        if (parts is not { Length: 8 } || !string.Join('-', parts[..4]).Equals("S-1-12-1", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        Span<byte> bytes = stackalloc byte[16];
        for (var i = 0; i < 4; i++)
        {
            if (!uint.TryParse(parts[4 + i], NumberStyles.None, CultureInfo.InvariantCulture, out var value))
            {
                return null;
            }

            BinaryPrimitives.WriteUInt32LittleEndian(bytes[(i * 4)..], value);
        }

        return new Guid(bytes);
    }

    /// <summary>True when the result belongs to the enrolled account in the enrolled tenant.</summary>
    public static bool IsSameAccount(string? resultObjectId, string? resultTenantId, string objectId, string tenantId)
        => Guid.TryParse(resultObjectId, out var resultOid) && Guid.TryParse(objectId, out var oid) && resultOid == oid
            && Guid.TryParse(resultTenantId, out var resultTid) && Guid.TryParse(tenantId, out var tid) && resultTid == tid;

    /// <summary>
    /// True when the failed request may be retried silently as the Windows account: the
    /// sign-in was lost, the account is an enrolled broker account that is the Windows session
    /// user, no claims challenge is being answered, and no attempt for it and these scopes has
    /// failed yet.
    /// </summary>
    /// <param name="authMethod">The enrollment's sign-in method, or null when it is no longer enrolled.</param>
    public bool ShouldTry(string errorCode, AuthMethod? authMethod, string? claimsChallenge, string objectId, string tenantId, string[] scopes)
        => errorCode is MsalError.UserNullError or WamNoAccountForId
            && authMethod == AuthMethod.Broker
            && claimsChallenge is null
            && _sessionObjectId is { } session
            && Guid.TryParse(objectId, out var oid) && oid == session
            && !_failed.Contains(Key(objectId, tenantId, scopes));

    /// <summary>
    /// Records a failed attempt, so the enrollment is not retried for these scopes in this
    /// process. Scopes count separately: a tenant without Azure consent fails for Azure only.
    /// A network error does not count: the next request may try again.
    /// </summary>
    /// <param name="error">The request's exception, or null when Windows returned another account.</param>
    public void RecordFailure(string objectId, string tenantId, string[] scopes, MsalException? error = null)
    {
        if (error is { IsRetryable: true })
        {
            return;
        }

        _failed.Add(Key(objectId, tenantId, scopes));
    }

    private static string Key(string objectId, string tenantId, string[] scopes) => $"{objectId}/{tenantId}/{string.Join(' ', scopes)}";
}

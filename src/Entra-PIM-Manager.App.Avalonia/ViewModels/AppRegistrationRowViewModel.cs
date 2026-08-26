namespace EntraPimManager.AppAvalonia.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using EntraPimManager.Core.Auth;
using EntraPimManager.Core.Configuration;

/// <summary>
/// One row of the Settings → APP REGISTRATION list: a single App Registration,
/// read-only with a remove button. <see cref="TenantId"/> is <c>null</c> for the
/// cloud-wide (multi-tenant) registration of a cloud and the pinned tenant's GUID
/// for a single-tenant one. Rows are added through the form below the list;
/// re-adding the same cloud + tenant replaces the entry — there is no inline edit.
/// </summary>
/// <remarks>
/// The title uses the same wording as the "Sign in with" picker in the add-account
/// panel ("Entra Global — any tenant", "Contoso · Entra Global"), so the user meets
/// each registration under one name in both places.
/// </remarks>
public sealed class AppRegistrationRowViewModel : ObservableObject
{
    private readonly Func<string[]> _verifiedClientIds;

    public AppRegistrationRowViewModel(
        EntraCloud cloud,
        string? tenantId,
        string clientId,
        string? label,
        Func<string[]> verifiedClientIds)
    {
        Cloud = cloud;
        TenantId = tenantId;
        ClientId = clientId;
        Label = label;
        _verifiedClientIds = verifiedClientIds;
    }

    public EntraCloud Cloud { get; }

    /// <summary>Pinned tenant, or <c>null</c> for the cloud-wide registration.</summary>
    public string? TenantId { get; }

    public string ClientId { get; }

    public string? Label { get; }

    public bool IsCloudWide => TenantId is null;

    /// <summary>Row heading — identical to the entry's label in the "Sign in with" picker.</summary>
    public string Title => IsCloudWide
        ? $"{EntraCloudInfo.DisplayName(Cloud)} — any tenant"
        : $"{(string.IsNullOrWhiteSpace(Label) ? TenantId : Label)} · {EntraCloudInfo.DisplayName(Cloud)}";

    /// <summary>
    /// A registration only counts as verified once a sign-in through it succeeded
    /// (<see cref="UserSettings.VerifiedClientIds"/>) — a saved GUID proves nothing
    /// about public client flows, the redirect URI or admin consent.
    /// </summary>
    public bool IsVerified => _verifiedClientIds().Contains(ClientId, StringComparer.OrdinalIgnoreCase);

    public string Status => IsVerified
        ? "Verified — an account signed in successfully with this App Registration."
        : "Not verified yet — sign in with it to prove the setup.";

    /// <summary>Re-raises the derived verification properties after a sign-in.</summary>
    public void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(IsVerified));
        OnPropertyChanged(nameof(Status));
    }
}

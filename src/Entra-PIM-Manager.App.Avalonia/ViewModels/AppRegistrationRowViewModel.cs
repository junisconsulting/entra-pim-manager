namespace EntraPimManager.AppAvalonia.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using EntraPimManager.Core.Auth;
using EntraPimManager.Core.Configuration;

/// <summary>
/// One row of the Settings → APP REGISTRATION list: a single App Registration pinned
/// to its tenant, read-only with a remove button. Clicking the row loads it into the
/// form below for editing; saving replaces it.
/// </summary>
/// <remarks>
/// The title is the same name the "Sign in with" picker in the add-account panel
/// uses ("Contoso"), so the user meets each registration under one name in both
/// places; the cloud is shown as a chip next to it.
/// </remarks>
public sealed class AppRegistrationRowViewModel : ObservableObject
{
    private readonly Func<string[]> _verifiedClientIds;

    public AppRegistrationRowViewModel(
        EntraCloud cloud,
        string tenantId,
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

    public string TenantId { get; }

    public string ClientId { get; }

    public string? Label { get; }

    /// <summary>Row heading: the label, or the tenant id when none was given.</summary>
    public string Title => string.IsNullOrWhiteSpace(Label) ? TenantId : Label;

    /// <summary>Short cloud name for the chip ("Global", "China").</summary>
    public string CloudName => Cloud.ToString();

    /// <summary>Drives the chip colour — Global and China must be told apart at a glance.</summary>
    public bool IsChina => Cloud == EntraCloud.China;

    /// <summary>
    /// A registration only counts as verified once a sign-in through it succeeded
    /// (<see cref="UserSettings.VerifiedClientIds"/>) — a saved GUID proves nothing
    /// about public client flows, the redirect URI or admin consent.
    /// </summary>
    public bool IsVerified => _verifiedClientIds().Contains(ClientId, StringComparer.OrdinalIgnoreCase);

    public string Status => IsVerified ? "Verified" : "Not verified yet — sign in with it once";

    /// <summary>Re-raises the derived verification properties after a sign-in.</summary>
    public void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(IsVerified));
        OnPropertyChanged(nameof(Status));
    }
}

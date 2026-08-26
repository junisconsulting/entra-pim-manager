namespace EntraPimManager.AppAvalonia.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using EntraPimManager.Core.Auth;
using EntraPimManager.Core.Configuration;

/// <summary>
/// One row of the Settings → APP REGISTRATION → "Tenant-specific registrations" list:
/// a registration pinned to a single tenant, read-only with a remove button. Rows are
/// added through the form below the list and replaced by re-adding the same tenant —
/// there is no inline edit (mirrors the per-cloud rows' "Save per row" simplicity).
/// </summary>
public sealed class TenantRegistrationRowViewModel : ObservableObject
{
    private readonly TenantAppRegistration _registration;
    private readonly Func<string[]> _verifiedClientIds;

    public TenantRegistrationRowViewModel(TenantAppRegistration registration, Func<string[]> verifiedClientIds)
    {
        ArgumentNullException.ThrowIfNull(registration);
        _registration = registration;
        _verifiedClientIds = verifiedClientIds;

        // The validator rejects unknown cloud names at startup, and the add form
        // only offers known ones, so the fallback never shows in practice.
        Cloud = Enum.TryParse<EntraCloud>(registration.Cloud, ignoreCase: true, out var cloud) ? cloud : EntraCloud.Global;
    }

    /// <summary>The cloud the pinned tenant lives in.</summary>
    public EntraCloud Cloud { get; }

    /// <summary>Row heading: the label when one was given, else the tenant id.</summary>
    public string Title => string.IsNullOrWhiteSpace(_registration.Label) ? _registration.TenantId : _registration.Label;

    public string TenantId => _registration.TenantId;

    public string ClientId => _registration.ClientId;

    /// <summary>
    /// Second line: cloud plus verification state. Like the per-cloud rows, a
    /// registration only counts as verified once a sign-in through it succeeded
    /// (<see cref="UserSettings.VerifiedClientIds"/>).
    /// </summary>
    public string Status => IsVerified
        ? $"{EntraCloudInfo.DisplayName(Cloud)} · Verified"
        : $"{EntraCloudInfo.DisplayName(Cloud)} · Not verified yet — sign in with it to prove the setup";

    public bool IsVerified => _verifiedClientIds().Contains(_registration.ClientId, StringComparer.OrdinalIgnoreCase);

    /// <summary>Re-raises the derived verification properties after a sign-in.</summary>
    public void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(IsVerified));
        OnPropertyChanged(nameof(Status));
    }
}

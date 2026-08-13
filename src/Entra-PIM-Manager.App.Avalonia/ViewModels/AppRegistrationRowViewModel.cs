namespace EntraPimManager.AppAvalonia.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EntraPimManager.Core.Auth;
using EntraPimManager.Core.Configuration;

/// <summary>
/// One row of the Settings → APP REGISTRATION section: the client id for a single
/// sovereign cloud, with its own validation and verification state.
/// </summary>
/// <remarks>
/// There is one row per <see cref="EntraCloud"/> because national clouds are
/// physically isolated instances — an app registration lives in exactly one of them,
/// so each cloud needs its own client id and each must prove itself with its own
/// sign-in. See <c>docs/app-registration-setup.md</c>.
/// </remarks>
public sealed partial class AppRegistrationRowViewModel : ObservableObject
{
    private readonly EntraPimManagerOptions _options;
    private readonly Func<string[]> _verifiedClientIds;
    private readonly Action<EntraCloud, string> _save;

    /// <summary>
    /// Bound to the row's client id TextBox. Validated as a GUID before
    /// <see cref="SaveCommand"/> writes it to the local config file. Seeded from
    /// the current effective value when the panel opens.
    /// </summary>
    [ObservableProperty]
    private string _input = string.Empty;

    public AppRegistrationRowViewModel(
        EntraCloud cloud,
        EntraPimManagerOptions options,
        Func<string[]> verifiedClientIds,
        Action<EntraCloud, string> save)
    {
        Cloud = cloud;
        _options = options;
        _verifiedClientIds = verifiedClientIds;
        _save = save;
    }

    /// <summary>The cloud this row configures.</summary>
    public EntraCloud Cloud { get; }

    /// <summary>Row heading, e.g. <c>Entra China (21Vianet)</c>.</summary>
    public string DisplayName => EntraCloudInfo.DisplayName(Cloud);

    /// <summary>
    /// True when <see cref="Input"/> holds a syntactically valid GUID. Drives the
    /// enabled state of the Save button so the user gets feedback before submitting.
    /// </summary>
    public bool IsInputValid => Guid.TryParse(Input, out _);

    /// <summary>
    /// True when this cloud has no usable client id configured. Drives the inline
    /// "not configured" hint, and keeps the shipped placeholder out of the TextBox
    /// — <see cref="EntraPimManagerOptions.ClientIdFor"/> returns null rather than a
    /// non-GUID value.
    /// </summary>
    public bool IsMissing => ConfiguredClientId is null;

    /// <summary>
    /// True once a sign-in has actually succeeded against this cloud's client id
    /// (see <see cref="UserSettings.VerifiedClientIds"/>). Only then does the row
    /// show a green check — a saved GUID alone says nothing about whether the
    /// registration is usable.
    /// </summary>
    public bool IsVerified =>
        !IsMissing
        && _verifiedClientIds().Contains(ConfiguredClientId!, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// True when a client id is configured but no sign-in has proven it yet.
    /// Drives the neutral "sign in to verify" hint — deliberately not green.
    /// </summary>
    public bool IsUnverified => !IsMissing && !IsVerified;

    private string? ConfiguredClientId => _options.ClientIdFor(Cloud);

    /// <summary>Seeds <see cref="Input"/> from the active configuration.</summary>
    public void Seed()
    {
        Input = IsMissing ? string.Empty : ConfiguredClientId!;
        NotifyStateChanged();
    }

    /// <summary>Re-raises the derived verification properties.</summary>
    public void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(IsMissing));
        OnPropertyChanged(nameof(IsVerified));
        OnPropertyChanged(nameof(IsUnverified));
    }

    partial void OnInputChanged(string value)
    {
        OnPropertyChanged(nameof(IsInputValid));
        SaveCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(IsInputValid))]
    private void Save() => _save(Cloud, Input.Trim());
}

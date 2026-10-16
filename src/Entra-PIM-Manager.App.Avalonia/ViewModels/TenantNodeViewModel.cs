namespace EntraPimManager.AppAvalonia.ViewModels;

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EntraPimManager.Core.Auth;
using EntraPimManager.Core.Configuration;

/// <summary>
/// One tenant in the Settings tree: its App Registration, its ticketing system, and the
/// accounts signed into it.
/// </summary>
/// <remarks>
/// This is the shape the configuration actually has. One tenant carries many accounts —
/// a normal and an admin account in one customer directory are two enrollments sharing
/// one registration — so anything shared sits here, one level above the rows it applies
/// to, instead of being repeated in each of them.
/// <para/>
/// <see cref="Accounts"/> holds the very instances the shell owns, never copies of them:
/// the shell pushes resolved tenant names and alias changes into the rows it reaches
/// through its own collection, and a copy would quietly stop receiving them.
/// <para/>
/// Tenant id and cloud are deliberately not editable here. Changing either does not edit
/// this registration, it names a different one — so that is a remove and an add, and the
/// whole "the entry moved to another slot" case stops existing.
/// </remarks>
public sealed partial class TenantNodeViewModel : ObservableObject
{
    private readonly EntraPimManagerOptions _options;
    private readonly Func<UserSettings> _settings;
    private readonly Action<TenantNodeViewModel> _addAccount;

    /// <summary>True while the configuration disclosure is open. In memory only.</summary>
    [ObservableProperty]
    private bool _isConfigExpanded;

    /// <summary>
    /// Draft of the client id while the disclosure is open. Node-local on purpose: a
    /// single set of form fields on the panel would show — and edit — the same text in
    /// every open disclosure at once.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private string _clientIdDraft = string.Empty;

    /// <summary>
    /// The way chosen in the editor: junis-hosted, or the tenant's own registration. Seeded
    /// from the saved client id when the editor opens.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    private bool _isJunisHostedDraft;

    [ObservableProperty]
    private string _labelDraft = string.Empty;

    [ObservableProperty]
    private string _ticketSystemDraft = string.Empty;

    private string? _clientId;
    private string? _label;
    private string? _ticketSystem;
    private bool _canAddAccount;

    public TenantNodeViewModel(
        TenantSlot slot,
        EntraPimManagerOptions options,
        Func<UserSettings> settings,
        Func<TenantNodeViewModel, Task> save,
        Func<TenantNodeViewModel, Task> remove,
        Action<TenantNodeViewModel> addAccount,
        Func<bool> isBusy)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(remove);
        ArgumentNullException.ThrowIfNull(addAccount);
        ArgumentNullException.ThrowIfNull(isBusy);

        Slot = slot;
        _options = options;
        _settings = settings;
        _addAccount = addAccount;
        RemoveConfirmation = new ArmedConfirmation(() => remove(this), () => !isBusy());

        // Only a save that removes accounts asks twice, and only that one has to wait for
        // the shell: the removal is refused while it is busy.
        SaveConfirmation = new ArmedConfirmation(
            () => save(this),
            () => CanSave && !(SaveRemovesAccounts && isBusy()),
            () => SaveRemovesAccounts);

        Accounts.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(HasAccounts));
            OnPropertyChanged(nameof(ArmedSaveText));
        };
    }

    /// <summary>Identity of this tenant: cloud plus normalised tenant id.</summary>
    public TenantSlot Slot { get; }

    /// <summary>
    /// The accounts enrolled in this tenant, in the shell's own order. Filled by the
    /// panel's reconcile; the instances are shared with the shell, never cloned.
    /// </summary>
    public ObservableCollection<AccountListItemViewModel> Accounts { get; } = [];

    /// <summary>Sovereign cloud this tenant lives in.</summary>
    public EntraCloud Cloud => Slot.Cloud;

    /// <summary>Tenant id as configured, or as the enrolled account reports it.</summary>
    public string TenantId => Slot.TenantId;

    /// <summary>Stable key for the reconcile — see <see cref="TenantSlot.Key"/>.</summary>
    public string Key => Slot.Key;

    /// <summary>Header line: the admin's label when there is one, else the tenant id.</summary>
    public string Title => string.IsNullOrWhiteSpace(_label) ? TenantId : _label;

    /// <summary>Cloud chip text.</summary>
    public string CloudName => Cloud.ToString();

    /// <summary>Drives the warning colour of the cloud chip for the sovereign cloud.</summary>
    public bool IsChina => Cloud == EntraCloud.China;

    /// <summary>False for a tenant that still has accounts but whose registration was removed.</summary>
    public bool HasRegistration => !string.IsNullOrWhiteSpace(_clientId);

    /// <summary>True when at least one account is signed into this tenant.</summary>
    public bool HasAccounts => Accounts.Count > 0;

    /// <summary>
    /// A registration only counts as verified once a sign-in through it succeeded
    /// (<see cref="UserSettings.IsRegistrationVerified"/>) — a saved GUID proves nothing about
    /// public client flows, the redirect URI or admin consent.
    /// </summary>
    public bool IsVerified => _clientId is { } clientId
        && HasRegistration
        && _settings().IsRegistrationVerified(Cloud, TenantId, clientId, _options.HostingOf(Cloud, clientId));

    /// <summary>
    /// Whether the card shows whose registration it uses. Not without a registration, and
    /// not at all while this build ships no junis client id — then there is only one way.
    /// </summary>
    public bool ShowHostingChip => HasRegistration && _options.JunisHostedClientId is not null;

    /// <summary>Hosting chip text, from the saved entry — so it follows a save before the restart.</summary>
    public string HostingChipText => _options.HostingOf(Cloud, _clientId) == AppRegistrationHosting.JunisHosted
        ? "junis-hosted"
        : "self-hosted";

    /// <summary>The editor offers the way only where junis-hosted exists: Global, with a shipped junis id.</summary>
    public bool IsHostingChoiceVisible => _options.JunisHostedClientId is not null && Cloud == EntraCloud.Global;

    /// <summary>The client id Save would write, or <c>null</c> when the editor holds no valid one.</summary>
    public string? ResolvedClientIdDraft => _options.ResolveClientId(
        IsJunisHostedDraft ? AppRegistrationHosting.JunisHosted : AppRegistrationHosting.SelfHosted,
        Cloud,
        ClientIdDraft);

    /// <summary>
    /// True when Save would switch this tenant to another client id while accounts are
    /// signed into it. Their tokens belong to the old registration, so they go first.
    /// </summary>
    public bool SaveRemovesAccounts => HasAccounts
        && HasRegistration
        && ResolvedClientIdDraft is { } clientId
        && TenantSlot.NormaliseTenantId(clientId) != TenantSlot.NormaliseTenantId(_clientId);

    /// <summary>The armed Save button's text.</summary>
    public string ArmedSaveText => Accounts.Count == 1
        ? "Remove 1 account and save"
        : $"Remove {Accounts.Count} accounts and save";

    /// <summary>One-line state under the header.</summary>
    public string Status => !HasRegistration
        ? "No App Registration — sign-in for this tenant will fail"
        : IsVerified ? "Verified" : "Not verified yet — sign in with it once";

    /// <summary>
    /// False while the saved registration is not the one the app started with — none is
    /// configured, or one was just added or changed and the app has not restarted yet.
    /// Sign-in resolves its client id from the startup configuration, so it would fail or
    /// use the old registration.
    /// </summary>
    public bool CanAddAccount => _canAddAccount;

    /// <summary>Why the add-account button is disabled, or <c>null</c> when it is not.</summary>
    public string? AddAccountHint => CanAddAccount
        ? null
        : HasRegistration
            ? "Restart the app to sign in to this tenant."
            : "Add an App Registration for this tenant first.";

    /// <summary>Save is allowed once the chosen way yields a client id.</summary>
    public bool CanSave => ResolvedClientIdDraft is not null;

    /// <summary>
    /// The second click "Remove tenant" asks for. It sits inside the configuration rather
    /// than as an X in the header, where it was read as "cancel" — and removed the
    /// registration on the spot.
    /// </summary>
    public ArmedConfirmation RemoveConfirmation { get; }

    /// <summary>
    /// Save, which asks for a second click when it would remove the tenant's accounts —
    /// see <see cref="SaveRemovesAccounts"/>.
    /// </summary>
    public ArmedConfirmation SaveConfirmation { get; }

    /// <summary>
    /// Applies the current configuration to this node in place. Only raises change
    /// notifications for what actually changed — the reconcile runs on every account
    /// change, and a spurious notification costs a bound row its containers.
    /// </summary>
    /// <param name="clientId">Configured client id, or <c>null</c> when no registration exists.</param>
    /// <param name="label">Configured label, or <c>null</c>.</param>
    /// <param name="ticketSystem">The tenant's ticketing system, or <c>null</c>.</param>
    /// <param name="canAddAccount">Whether a sign-in to this tenant can resolve a client id.</param>
    public void UpdateRegistration(string? clientId, string? label, string? ticketSystem, bool canAddAccount)
    {
        var wasVerified = IsVerified;
        var clientIdChanged = !string.Equals(_clientId, clientId, StringComparison.OrdinalIgnoreCase);

        _clientId = clientId;

        if (!string.Equals(_label, label, StringComparison.Ordinal))
        {
            _label = label;
            OnPropertyChanged(nameof(Title));
        }

        if (_canAddAccount != canAddAccount)
        {
            _canAddAccount = canAddAccount;
            OnPropertyChanged(nameof(CanAddAccount));
            OnPropertyChanged(nameof(AddAccountHint));
        }

        if (clientIdChanged || wasVerified != IsVerified)
        {
            NotifyStateChanged();
        }

        // Not seeded here: the drafts are only ever read while the editor is open, and
        // opening it seeds them. Doing it here as well would be a second copy of that
        // rule — and one that fires while the user is typing.
        _ticketSystem = ticketSystem;
    }

    /// <summary>
    /// Re-reads the verified state, which lives in user settings rather than on this node
    /// and therefore changes without anything here being touched.
    /// </summary>
    public void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(HasRegistration));
        OnPropertyChanged(nameof(IsVerified));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(ShowHostingChip));
        OnPropertyChanged(nameof(HostingChipText));

        // Its wording depends on HasRegistration, so it goes stale here too — a removed
        // registration would otherwise keep saying "Restart the app to sign in".
        OnPropertyChanged(nameof(AddAccountHint));
    }

    private void SeedDrafts()
    {
        // A card without a registration preselects junis-hosted where it exists, as the add
        // form does. A junis-hosted card leaves the client id blank: the field only shows
        // for self-hosted, and the junis id is not a self-hosted value to start from.
        IsJunisHostedDraft = HasRegistration
            ? _options.HostingOf(Cloud, _clientId) == AppRegistrationHosting.JunisHosted
            : IsHostingChoiceVisible;
        ClientIdDraft = IsJunisHostedDraft ? string.Empty : _clientId ?? string.Empty;
        LabelDraft = _label ?? string.Empty;
        TicketSystemDraft = _ticketSystem ?? string.Empty;
    }

    [RelayCommand]
    private void ToggleConfig()
    {
        // Opening the editor always shows what is stored, so collapsing it discards an
        // unsaved edit rather than leaving it to reappear later.
        if (!IsConfigExpanded)
        {
            SeedDrafts();
        }

        IsConfigExpanded = !IsConfigExpanded;
    }

    /// <summary>Closes the configuration without saving, exactly like the gear does.</summary>
    [RelayCommand]
    private void CancelConfig() => IsConfigExpanded = false;

    // An armed Save confirms what was on screen when it was armed; any edit disarms it.
    partial void OnClientIdDraftChanged(string value) => DisarmSave();

    partial void OnIsJunisHostedDraftChanged(bool value) => DisarmSave();

    private void DisarmSave()
    {
        SaveConfirmation.Disarm();
        SaveConfirmation.Refresh();
    }

    // Cancel, the gear and a successful Save all collapse the editor; none of them may
    // leave an armed button behind for the next time it opens.
    partial void OnIsConfigExpandedChanged(bool value)
    {
        if (!value)
        {
            RemoveConfirmation.Disarm();
            SaveConfirmation.Disarm();
        }
    }

    [RelayCommand]
    private void AddAccount() => _addAccount(this);
}

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
    private readonly Func<string[]> _verifiedClientIds;
    private readonly Action<TenantNodeViewModel> _save;
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
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private string _clientIdDraft = string.Empty;

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
        Func<string[]> verifiedClientIds,
        Action<TenantNodeViewModel> save,
        Func<TenantNodeViewModel, Task> remove,
        Action<TenantNodeViewModel> addAccount,
        Func<bool> isBusy)
    {
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentNullException.ThrowIfNull(verifiedClientIds);
        ArgumentNullException.ThrowIfNull(save);
        ArgumentNullException.ThrowIfNull(remove);
        ArgumentNullException.ThrowIfNull(addAccount);
        ArgumentNullException.ThrowIfNull(isBusy);

        Slot = slot;
        _verifiedClientIds = verifiedClientIds;
        _save = save;
        _addAccount = addAccount;
        RemoveConfirmation = new ArmedConfirmation(() => remove(this), () => !isBusy());

        Accounts.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasAccounts));
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
    /// (<see cref="UserSettings.VerifiedClientIds"/>) — a saved GUID proves nothing about
    /// public client flows, the redirect URI or admin consent.
    /// </summary>
    public bool IsVerified => HasRegistration
        && _verifiedClientIds().Contains(_clientId, StringComparer.OrdinalIgnoreCase);

    /// <summary>One-line state under the header.</summary>
    public string Status => !HasRegistration
        ? "No App Registration — sign-in for this tenant will fail"
        : IsVerified ? "Verified" : "Not verified yet — sign in with it once";

    /// <summary>
    /// False while no registration is loaded for this tenant — either none is configured,
    /// or one was just added and the app has not restarted yet. Sign-in resolves its client
    /// id from the startup configuration, so it would fail in both cases.
    /// </summary>
    public bool CanAddAccount => _canAddAccount;

    /// <summary>Why the add-account button is disabled, or <c>null</c> when it is not.</summary>
    public string? AddAccountHint => CanAddAccount
        ? null
        : HasRegistration
            ? "Restart the app to sign in to this tenant."
            : "Add an App Registration for this tenant first.";

    /// <summary>Save is allowed once the client id parses as a GUID.</summary>
    public bool CanSave => Guid.TryParse(ClientIdDraft, out _);

    /// <summary>
    /// The second click "Remove tenant" asks for. It sits inside the configuration rather
    /// than as an X in the header, where it was read as "cancel" — and removed the
    /// registration on the spot.
    /// </summary>
    public ArmedConfirmation RemoveConfirmation { get; }

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
        var hadRegistration = HasRegistration;
        var wasVerified = IsVerified;

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

        if (hadRegistration != HasRegistration || wasVerified != IsVerified)
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

        // Its wording depends on HasRegistration, so it goes stale here too — a removed
        // registration would otherwise keep saying "Restart the app to sign in".
        OnPropertyChanged(nameof(AddAccountHint));
    }

    private void SeedDrafts()
    {
        ClientIdDraft = _clientId ?? string.Empty;
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

    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save() => _save(this);

    // Cancel, the gear and a successful Save all collapse the editor; none of them may
    // leave an armed remove button behind for the next time it opens.
    partial void OnIsConfigExpandedChanged(bool value)
    {
        if (!value)
        {
            RemoveConfirmation.Disarm();
        }
    }

    [RelayCommand]
    private void AddAccount() => _addAccount(this);
}

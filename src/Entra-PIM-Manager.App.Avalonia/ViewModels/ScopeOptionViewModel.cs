namespace EntraPimManager.AppAvalonia.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;
using EntraPimManager.Core.Models;

/// <summary>One management group or subscription in the activation panel's scope picker.</summary>
public sealed partial class ScopeOptionViewModel : ObservableObject
{
    private readonly Action<ScopeOptionViewModel> _selectionChanged;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>False while the search box filters this row out.</summary>
    [ObservableProperty]
    private bool _isVisible = true;

    public ScopeOptionViewModel(EligibleChildScope scope, Action<ScopeOptionViewModel> selectionChanged)
    {
        ArgumentNullException.ThrowIfNull(scope);
        Scope = scope;
        _selectionChanged = selectionChanged;
    }

    /// <summary>The scope behind the row.</summary>
    public EligibleChildScope Scope { get; }

    /// <summary>Display name of the management group or subscription.</summary>
    public string Name => Scope.Name;

    /// <summary>"Management group", or "Subscription · under Landing zones".</summary>
    public string Caption => Scope.ParentName is null
        ? Scope.KindLabel
        : $"{Scope.KindLabel} · under {Scope.ParentName}";

    /// <summary>Whether the search box text matches the name or the parent's name.</summary>
    public bool Matches(string filter)
        => Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || (Scope.ParentName?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);

    partial void OnIsSelectedChanged(bool value) => _selectionChanged(this);
}

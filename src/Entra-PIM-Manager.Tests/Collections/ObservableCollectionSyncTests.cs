namespace EntraPimManager.Tests.Collections;

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using EntraPimManager.Core.Collections;

/// <summary>
/// Covers the rebuild that keeps the Settings tenant tree in sync. The assertion that
/// matters is the first one: a rebuild which changes nothing must raise nothing, because
/// every change notification costs the bound control its containers — and with them the
/// keyboard focus of whatever text box the user is typing in.
/// </summary>
public sealed class ObservableCollectionSyncTests
{
    [Fact]
    public void Apply_UnchangedInput_RaisesNoEvent()
    {
        var a = new Row();
        var b = new Row();
        var target = new ObservableCollection<Row> { a, b };
        var events = Record(target);

        ObservableCollectionSync.Apply(target, [a, b]);

        Assert.Empty(events);
    }

    [Fact]
    public void Apply_Reorder_MovesInsteadOfRebuilding()
    {
        var a = new Row();
        var b = new Row();
        var c = new Row();
        var target = new ObservableCollection<Row> { a, b, c };
        var events = Record(target);

        ObservableCollectionSync.Apply(target, [c, a, b]);

        Assert.Equal([c, a, b], target);
        Assert.Equal(NotifyCollectionChangedAction.Move, Assert.Single(events).Action);
    }

    [Fact]
    public void Apply_AppendsANewItem()
    {
        var a = new Row();
        var b = new Row();
        var target = new ObservableCollection<Row> { a };

        ObservableCollectionSync.Apply(target, [a, b]);

        Assert.Equal([a, b], target);
    }

    [Fact]
    public void Apply_InsertsInTheMiddle()
    {
        var a = new Row();
        var b = new Row();
        var c = new Row();
        var target = new ObservableCollection<Row> { a, c };

        ObservableCollectionSync.Apply(target, [a, b, c]);

        Assert.Equal([a, b, c], target);
    }

    [Fact]
    public void Apply_RemovesFromTheMiddle()
    {
        var a = new Row();
        var b = new Row();
        var c = new Row();
        var target = new ObservableCollection<Row> { a, b, c };

        ObservableCollectionSync.Apply(target, [a, c]);

        Assert.Equal([a, c], target);
    }

    [Fact]
    public void Apply_ReplacesEverything()
    {
        var target = new ObservableCollection<Row> { new(), new() };
        var x = new Row();

        ObservableCollectionSync.Apply(target, [x]);

        Assert.Same(x, Assert.Single(target));
    }

    [Fact]
    public void Apply_EmptiesTheCollection()
    {
        var target = new ObservableCollection<Row> { new(), new() };

        ObservableCollectionSync.Apply(target, []);

        Assert.Empty(target);
    }

    [Fact]
    public void Apply_KeepsTheInstance_NotAnEqualCopy()
    {
        // The rows carry live state the shell pushes into them (tenant name, alias).
        // A sync that swapped in an equal copy would leave those updates going nowhere.
        var a = new Row();
        var target = new ObservableCollection<Row> { a };

        ObservableCollectionSync.Apply(target, [a]);

        Assert.Same(a, target[0]);
    }

    private static List<NotifyCollectionChangedEventArgs> Record(ObservableCollection<Row> target)
    {
        var events = new List<NotifyCollectionChangedEventArgs>();
        target.CollectionChanged += (_, e) => events.Add(e);
        return events;
    }

    /// <summary>Stand-in for a row view model: identity is the instance, nothing else.</summary>
    private sealed class Row;
}

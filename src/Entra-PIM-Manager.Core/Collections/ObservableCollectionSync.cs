namespace EntraPimManager.Core.Collections;

using System.Collections.ObjectModel;

/// <summary>
/// Brings a bound <see cref="ObservableCollection{T}"/> in line with a desired sequence
/// by adding, moving, inserting and trimming — never by clearing it first.
/// </summary>
/// <remarks>
/// The distinction is not an optimisation. Clearing a bound collection makes the items
/// control drop every container and build new ones, and a rebuilt container loses
/// keyboard focus and the caret. A list that is rebuilt on a timer would therefore throw
/// the user out of a text box mid-word. This helper raises **no** change notification at
/// all when the target already matches, so per-row editing state survives a rebuild by
/// construction rather than by care.
/// <para/>
/// Identity is reference identity: the desired sequence is expected to carry the very
/// instances the target should end up holding, not equal copies of them.
/// </remarks>
public static class ObservableCollectionSync
{
    /// <summary>
    /// Mutates <paramref name="target"/> until it holds exactly the items of
    /// <paramref name="desired"/>, in that order.
    /// </summary>
    /// <typeparam name="T">Item type; reference type, because identity is by reference.</typeparam>
    /// <param name="target">The bound collection to bring in line.</param>
    /// <param name="desired">The items the collection should hold, in display order.</param>
    public static void Apply<T>(ObservableCollection<T> target, IReadOnlyList<T> desired)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(desired);

        for (var i = 0; i < desired.Count; i++)
        {
            if (i >= target.Count)
            {
                target.Add(desired[i]);
                continue;
            }

            if (ReferenceEquals(target[i], desired[i]))
            {
                continue;
            }

            // Already present further down: move it rather than remove-and-add, so the
            // items control reuses the container instead of rebuilding it.
            var found = IndexOf(target, desired[i], i + 1);
            if (found >= 0)
            {
                target.Move(found, i);
            }
            else
            {
                target.Insert(i, desired[i]);
            }
        }

        while (target.Count > desired.Count)
        {
            target.RemoveAt(target.Count - 1);
        }
    }

    /// <summary>Reference-identity search, because <c>IndexOf</c> would use value equality.</summary>
    private static int IndexOf<T>(ObservableCollection<T> items, T item, int start)
        where T : class
    {
        for (var i = start; i < items.Count; i++)
        {
            if (ReferenceEquals(items[i], item))
            {
                return i;
            }
        }

        return -1;
    }
}

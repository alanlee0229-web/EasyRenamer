using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace BatchRenamer.App.ViewModels;

/// <summary>
/// Replaces a full ordering with one CollectionChanged.Reset notification.
/// Sorting 20k rows must not emit 40k individual remove/add notifications.
/// </summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> source)
    {
        var materialized = source as IReadOnlyCollection<T> ?? source.ToArray();
        CheckReentrancy();
        Items.Clear();
        foreach (var item in materialized) Items.Add(item);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    public void AddRange(IEnumerable<T> source)
    {
        var materialized = source.ToArray();
        if (materialized.Length == 0) return;
        CheckReentrancy();
        foreach (var item in materialized) Items.Add(item);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}

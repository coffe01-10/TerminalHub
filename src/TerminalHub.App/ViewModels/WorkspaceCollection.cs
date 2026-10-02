using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace TerminalHub.App.ViewModels;

/// <summary>Publish a workspace's complete list once instead of rebuilding the UI per session.</summary>
public sealed class WorkspaceCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IReadOnlyList<T> items)
    {
        CheckReentrancy();
        Items.Clear();
        foreach (var item in items) Items.Add(item);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}

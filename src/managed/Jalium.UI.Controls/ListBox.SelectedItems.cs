using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Jalium.UI.Controls;

public partial class ListBox
{
    protected override void OnSelectionChanged(SelectionChangedEventArgs e)
    {
        try { base.OnSelectionChanged(e); }
        finally { _selectedItemsView?.PublishCommittedSelection(); }
    }

    private void ApplyCollectionSelection(List<object> items)
    {
        if (_isApplyingSelectedItems)
            throw new InvalidOperationException("The selection transaction is still active.");
        if (SelectionMode == SelectionMode.Single && items.Count > 1)
            throw new InvalidOperationException("Single selection cannot contain multiple items.");
        int? preferred = SelectedItem is { } current && items.Any(item => SelectionValuesEqual(item, current))
            ? SelectedIndex : null;
        if (!ApplySelectedItems(items, preferred, publishToSource: true, preserveItemAliases: true))
            throw new InvalidOperationException("The selection could not be applied.");
    }

    // The raw list is owned exclusively by the existing selection transaction.
    // This stable view makes every public mutation use that same transaction.
    private sealed class SelectedItemsCollection(ListBox owner, List<object> storage) :
        IList, IList<object>, IReadOnlyList<object>, INotifyCollectionChanged, INotifyPropertyChanged
    {
        private object[] _published = [];
        public event NotifyCollectionChangedEventHandler? CollectionChanged;
        public event PropertyChangedEventHandler? PropertyChanged;
        public int Count => storage.Count;
        public bool IsReadOnly => false;
        public bool IsFixedSize => false;
        public bool IsSynchronized => false;
        public object SyncRoot => this;
        public object this[int index]
        {
            get => storage[index];
            set
            {
                if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
                Validate(value);
                var items = new List<object>(storage); items[index] = value;
                owner.ApplyCollectionSelection(items);
            }
        }
        object? IList.this[int index] { get => this[index]; set => this[index] = value!; }

        private void Validate(object? item)
        {
            if (item is null || !owner.TryResolveSelectionItem(item, out _, out _, out _))
                throw new ArgumentException("The item must belong to this ListBox.", nameof(item));
        }
        private int FindSelection(object value) => storage.FindIndex(item => SelectionValuesEqual(item, value));
        public void Add(object item) => AddCore(item);
        int IList.Add(object? item) => AddCore(item);
        private int AddCore(object? item)
        {
            Validate(item);
            int existing = FindSelection(item!);
            if (existing >= 0) return existing;
            var items = new List<object>(storage) { item! };
            owner.ApplyCollectionSelection(items);
            return FindSelection(item!);
        }
        public void Insert(int index, object item)
        {
            if ((uint)index > (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
            Validate(item);
            if (FindSelection(item) >= 0) return;
            var items = new List<object>(storage); items.Insert(index, item);
            owner.ApplyCollectionSelection(items);
        }
        void IList.Insert(int index, object? value) => Insert(index, value!);
        public bool Remove(object item)
        {
            int index = storage.IndexOf(item);
            if (index < 0) return false;
            RemoveAt(index); return true;
        }
        void IList.Remove(object? value) { if (value is not null) Remove(value); }
        public void RemoveAt(int index)
        {
            if ((uint)index >= (uint)Count) throw new ArgumentOutOfRangeException(nameof(index));
            var items = new List<object>(storage); items.RemoveAt(index);
            owner.ApplyCollectionSelection(items);
        }
        public void Clear() { if (Count > 0) owner.ApplyCollectionSelection([]); }
        public bool Contains(object item) => storage.Contains(item);
        bool IList.Contains(object? value) => value is not null && Contains(value);
        public int IndexOf(object item) => storage.IndexOf(item);
        int IList.IndexOf(object? value) => value is null ? -1 : IndexOf(value);
        public void CopyTo(object[] array, int arrayIndex) => storage.CopyTo(array, arrayIndex);
        void ICollection.CopyTo(Array array, int index) => ((ICollection)storage).CopyTo(array, index);
        public IEnumerator<object> GetEnumerator() => storage.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        internal void PublishCommittedSelection()
        {
            object[] current = storage.ToArray();
            bool same = _published.Length == current.Length;
            for (int i = 0; same && i < current.Length; i++)
                same = ReferenceEquals(_published[i], current[i]);
            if (same) return;
            bool countChanged = _published.Length != current.Length;
            _published = current;
            // Reset means observers reread the complete committed selection.
            // A callback can commit a newer choice without leaving stale indices
            // in later callbacks from the outer transaction.
            try
            {
                if (countChanged) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
            }
            finally
            {
                try { PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]")); }
                finally { CollectionChanged?.Invoke(this, new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset)); }
            }
        }
    }
}

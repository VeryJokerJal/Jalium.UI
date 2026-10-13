using System.Collections;

namespace Jalium.UI.Documents;

/// <summary>
/// Captures editable document state without replacing the content elements.
/// Keeping the original nodes preserves bindings, event handlers, CSS context,
/// and hosted UI elements when an edit is undone.
/// </summary>
internal sealed class DocumentSnapshot
{
    private readonly List<ElementState> _elements = new();
    private readonly List<CollectionState> _collections = new();
    private readonly Dictionary<DependencyObject, DependencyObject> _parents = new(ReferenceEqualityComparer.Instance);

    private DocumentSnapshot(FlowDocument document)
    {
        Document = document;
        Visit(document, new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance));
    }

    internal FlowDocument Document { get; }

    internal static DocumentSnapshot Capture(FlowDocument document) => new(document);

    internal bool HasChanges => _collections.Any(static state => !state.Matches()) ||
        _elements.Any(static state => !state.Matches());

    internal bool CanRestore(FlowDocument currentDocument, DependencyObject owner)
    {
        if (Document.Parent is { } documentParent && !ReferenceEquals(documentParent, owner))
            return false;
        var visited = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
        var owners = new Dictionary<DependencyObject, IList>(ReferenceEqualityComparer.Instance);
        FindOwners(currentDocument, owners, visited);
        FindOwners(Document, owners, visited);
        foreach (var (element, savedParent) in _parents)
        {
            if (element is FrameworkContentElement { Parent: { } parent } &&
                !ReferenceEquals(parent, savedParent) && !visited.Contains(parent))
                return false;
        }
        return true;
    }

    internal void Restore(FlowDocument currentDocument)
    {
        var owners = new Dictionary<DependencyObject, IList>(ReferenceEqualityComparer.Instance);
        var visited = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
        FindOwners(currentDocument, owners, visited);
        FindOwners(Document, owners, visited);

        var changed = _collections.Where(static state => !state.Matches()).ToArray();
        // Detach before attaching: a paragraph split can move an existing Run
        // to a new paragraph that is absent from the snapshot being restored.
        foreach (var state in changed)
            Clear(state.Collection);

        foreach (var state in changed)
        {
            foreach (var element in state.Items)
            {
                if (owners.TryGetValue(element, out var owner) &&
                    !ReferenceEquals(owner, state.Collection) && owner.Contains(element))
                    Remove(owner, element);
            }
        }

        foreach (var state in changed)
        {
            foreach (var element in state.Items)
                Add(state.Collection, element);
        }

        foreach (var state in _elements)
            state.Restore();

        Document.NotifyTextPresentationChanged();
    }

    private void Visit(DependencyObject element, HashSet<DependencyObject> visited)
    {
        if (!visited.Add(element)) return;
        _elements.Add(new ElementState(element));
        foreach (var collection in GetCollections(element))
        {
            var state = new CollectionState(collection);
            _collections.Add(state);
            foreach (var child in state.Items)
            {
                _parents[child] = element;
                Visit(child, visited);
            }
        }
    }

    private static void FindOwners(DependencyObject element,
        Dictionary<DependencyObject, IList> owners, HashSet<DependencyObject> visited)
    {
        if (!visited.Add(element)) return;
        foreach (var collection in GetCollections(element))
        {
            foreach (DependencyObject child in collection)
            {
                owners[child] = collection;
                FindOwners(child, owners, visited);
            }
        }
    }

    private static IEnumerable<IList> GetCollections(DependencyObject element)
    {
        switch (element)
        {
            case FlowDocument document: yield return document.Blocks; break;
            case Paragraph paragraph: yield return paragraph.Inlines; break;
            case Span span: yield return span.Inlines; break;
            case Section section: yield return section.Blocks; break;
            case List list: yield return list.ListItems; break;
            case ListItem item: yield return item.Blocks; break;
            case Table table:
                yield return table.Columns; yield return table.RowGroups; break;
            case TableRowGroup group: yield return group.Rows; break;
            case TableRow row: yield return row.Cells; break;
            case TableCell cell: yield return cell.Blocks; break;
            case AnchoredBlock anchor: yield return anchor.Blocks; break;
        }
    }

    // ListItemCollection's typed methods also maintain logical ownership.
    private static void Clear(IList collection)
    {
        if (collection is ListItemCollection items) items.Clear();
        else collection.Clear();
    }

    private static void Add(IList collection, DependencyObject element)
    {
        if (collection is ListItemCollection items) items.Add((ListItem)element);
        else collection.Add(element);
    }

    private static void Remove(IList collection, DependencyObject element)
    {
        if (collection is ListItemCollection items) items.Remove((ListItem)element);
        else collection.Remove(element);
    }

    private sealed class CollectionState(IList collection)
    {
        internal IList Collection { get; } = collection;
        internal DependencyObject[] Items { get; } = collection.Cast<DependencyObject>().ToArray();

        internal bool Matches()
        {
            if (Collection.Count != Items.Length) return false;
            for (int i = 0; i < Items.Length; i++)
                if (!ReferenceEquals(Collection[i], Items[i])) return false;
            return true;
        }
    }

    private sealed class ElementState(DependencyObject element)
    {
        private readonly KeyValuePair<DependencyProperty, object?>[] _values =
            element.GetLocalValueEntriesInternal().Where(static entry => !entry.Key.ReadOnly).ToArray();

        internal bool Matches()
        {
            var current = element.GetLocalValueEntriesInternal().Where(static entry => !entry.Key.ReadOnly).ToArray();
            return current.Length == _values.Length && _values.All(saved =>
                current.Any(entry => ReferenceEquals(entry.Key, saved.Key) && Equals(entry.Value, saved.Value)));
        }

        internal void Restore()
        {
            var properties = _values.Select(static entry => entry.Key).ToHashSet();
            foreach (var entry in element.GetLocalValueEntriesInternal())
            {
                if (!entry.Key.ReadOnly && !properties.Contains(entry.Key))
                    element.ClearValue(entry.Key);
            }
            foreach (var entry in _values)
            {
                if (!Equals(element.ReadLocalValue(entry.Key), entry.Value))
                    element.SetValue(entry.Key, entry.Value);
            }
        }
    }
}

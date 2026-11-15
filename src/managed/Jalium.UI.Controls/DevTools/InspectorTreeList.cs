using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Input;
using Jalium.UI.Threading;

namespace Jalium.UI.Controls.DevTools;

/// <summary>
/// Lightweight data model for one row of the Inspector's flattened visual-tree list.
/// Not a Visual — carries no layout/render cost, so the model can be O(total) while the
/// realized containers stay O(viewport) via the ListBox's VirtualizingStackPanel.
/// </summary>
internal sealed class InspectorRow
{
    public Visual Visual { get; }
    public int Depth { get; }
    public string DisplayName { get; }
    public bool HasChildren { get; set; }
    public bool IsExpanded { get; set; }

    /// <summary>Current expander chevron rotation in degrees (0 = expanded/down, -90 = collapsed/right).</summary>
    public double ChevronAngle;
    /// <summary>This row's position in the visible-row list (set during rebuild). Used to compute the
    /// expand/collapse reveal stagger without an O(n) IndexOf per realized container.</summary>
    public int Index = -1;

    public InspectorRow(Visual visual, int depth)
    {
        Visual = visual;
        Depth = depth;
        DisplayName = DevToolsWindow.GetVisualDisplayName(visual);
    }
}

/// <summary>
/// ObservableCollection that supports a single bulk replace raising one Reset notification,
/// instead of N per-item Add/Remove events. The ItemContainerGenerator handles Reset by
/// re-realizing only the viewport, so a whole-list rebuild stays O(viewport) on screen.
/// </summary>
internal sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void Reset(IReadOnlyList<T> newItems)
    {
        Items.Clear();
        for (int i = 0; i < newItems.Count; i++)
        {
            Items.Add(newItems[i]);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}

/// <summary>Connects the Inspector's visual model to the reusable, themed row control.</summary>
internal sealed class InspectorRowContainer : DevToolsTreeItem
{
    private Action<InspectorRow>? _onToggle;
    private bool _binding;
    public InspectorRow? Row { get; private set; }
    public InspectorRowContainer() => ExpansionChanged += OnExpansionChanged;
    public void Bind(InspectorRow row, Action<InspectorRow> onToggle)
    {
        _binding = true;
        try
        {
            Row = row;
            _onToggle = onToggle;
            SetCurrentValue(LevelProperty, row.Depth);
            SetCurrentValue(ContentProperty, row.DisplayName);
            SetCurrentValue(HasChildrenProperty, row.HasChildren);
            SetCurrentValue(IsExpandedProperty, row.IsExpanded);
            SetCurrentValue(ChevronAngleProperty, row.ChevronAngle);
        }
        finally { _binding = false; }
    }
    internal void Unbind()
    {
        _binding = true;
        try
        {
            Row = null;
            _onToggle = null;
            ClearValue(ContentProperty);
            ClearValue(LevelProperty);
            ClearValue(HasChildrenProperty);
            ClearValue(IsExpandedProperty);
            ClearValue(ChevronAngleProperty);
            ClearValue(RevealProgressProperty);
        }
        finally { _binding = false; }
    }
    internal void SetRevealProgress(double progress) => SetCurrentValue(RevealProgressProperty, progress);
    internal void ApplyChevron()
    {
        if (Row is not { } row) return;
        _binding = true;
        try
        {
            SetCurrentValue(IsExpandedProperty, row.IsExpanded);
            SetCurrentValue(ChevronAngleProperty, row.ChevronAngle);
        }
        finally { _binding = false; }
    }
    private void OnExpansionChanged(object sender, RoutedEventArgs e)
    {
        if (!_binding && Row is { HasChildren: true } row && row.IsExpanded != IsExpanded)
            _onToggle?.Invoke(row);
    }
}

/// <summary>
/// The Inspector's flat virtualized list. A standard <see cref="ListBox"/> (so virtualization,
/// selection, keyboard nav and selection highlight come for free) using a custom row container so
/// the expand/collapse reveal can enumerate the realized rows. Replaces the old nested,
/// non-virtualized TreeView.
/// </summary>
internal sealed class InspectorTreeList : ListBox
{
    /// <summary>Callback invoked when a row's expander chevron is clicked.</summary>
    internal Action<InspectorRow>? ToggleExpand;

    private Action<InspectorRow>? _onToggleRow;                       // cached Bind callback
    private readonly List<InspectorRowContainer> _realizedScratch = new();

    // Active expand/collapse reveal published by DevToolsWindow BEFORE the async layout realizes
    // containers, so rows bound during the animation (frame 0 after the Reset, or scrolled into
    // view mid-reveal) enter at their current staggered progress instead of flashing revealed.
    private int _activeRevealStart = -1;   // inclusive
    private int _activeRevealEnd = -1;     // exclusive
    private Func<int, double>? _activeRevealProgress;

    public InspectorTreeList()
    {
        SetCurrentValue(TransitionPropertyProperty, TransitionPropertyCollection.None());
    }

    protected override void OnItemsChanged(NotifyCollectionChangedEventArgs e)
    {
        // A flattened-tree rebuild retires every container, including the one
        // handling Left/Right. Recover focus by the inspected visual's identity
        // once the virtualizing panel has realized its replacement.
        var previousFocus = Keyboard.FocusedElement;
        Visual? target = null;
        for (var current = previousFocus as Visual; current != null; current = current.VisualParent)
        {
            if (current is InspectorRowContainer { Row: { } row }) { target = row.Visual; break; }
            if (ReferenceEquals(current, this)) break;
        }
        base.OnItemsChanged(e);
        if (target == null) return;
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
        {
            if (!IsLoaded) return;
            var focus = Keyboard.FocusedElement;
            if (focus != null && !ReferenceEquals(focus, previousFocus) && !ReferenceEquals(focus, this) && focus is not Window)
            {
                bool within = false;
                for (var current = focus as Visual; current != null; current = current.VisualParent)
                    if (ReferenceEquals(current, this)) { within = true; break; }
                if (!within) return;
            }
            for (int i = 0; i < Items.Count; i++)
            {
                if (Items[i] is not InspectorRow row || !ReferenceEquals(row.Visual, target)) continue;
                BringRowIntoView(i);
                UpdateLayout();
                (ItemContainerGenerator.ContainerFromIndex(i) as InspectorRowContainer)?.Focus();
                break;
            }
        });
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new InspectorListPeer(this);
    private sealed class InspectorListPeer(InspectorTreeList owner) : ListBoxAutomationPeer(owner)
    {
        protected override string GetClassNameCore() => nameof(InspectorTreeList);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Tree;
        protected override ItemAutomationPeer CreateItemAutomationPeer(object item) => item is InspectorRow row
            ? new InspectorItemPeer(row, this, owner) : base.CreateItemAutomationPeer(item);
    }
    private sealed class InspectorItemPeer(InspectorRow row, InspectorListPeer peer, InspectorTreeList list)
        : ListBoxItemAutomationPeer(row, peer), IExpandCollapseProvider
    {
        protected override string GetNameCore() => row.DisplayName;
        protected override string GetClassNameCore() => nameof(DevToolsTreeItem);
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.TreeItem;
        protected override object? GetPatternCore(PatternInterface pattern) => pattern == PatternInterface.ExpandCollapse ? this : base.GetPatternCore(pattern);
        public ExpandCollapseState ExpandCollapseState => !row.HasChildren ? ExpandCollapseState.LeafNode : row.IsExpanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;
        public void Expand() => ChangeExpansion(true);
        public void Collapse() => ChangeExpansion(false);
        private void ChangeExpansion(bool expanded)
        {
            ThrowIfRetired();
            if (!list.IsEnabled || !row.HasChildren) throw new InvalidOperationException("The Inspector row cannot change expansion.");
            if (row.IsExpanded != expanded) list.ToggleExpand?.Invoke(row);
        }
    }

    /// <summary>Publishes the reveal range and per-index progress function for the in-flight
    /// expand/collapse animation. Containers bound while active pick up their entry progress from
    /// it (see <see cref="PrepareContainerForItem"/>).</summary>
    internal void SetActiveReveal(int startInclusive, int endExclusive, Func<int, double> progressForIndex)
    {
        _activeRevealStart = startInclusive;
        _activeRevealEnd = endExclusive;
        _activeRevealProgress = progressForIndex;
    }

    /// <summary>Clears the active reveal; containers bound afterwards enter fully revealed.</summary>
    internal void ClearActiveReveal()
    {
        _activeRevealStart = -1;
        _activeRevealEnd = -1;
        _activeRevealProgress = null;
    }

    private double RevealProgressForIndex(int index)
        => _activeRevealProgress is { } progressForIndex && index >= _activeRevealStart && index < _activeRevealEnd
            ? progressForIndex(index)
            : 1.0;

    /// <inheritdoc />
    protected override FrameworkElement GetContainerForItem(object item)
    {
        // Containers are realized lazily during layout (outside the window ctor scope); wrap so
        // constructor-time InvalidateMeasure does not pollute diagnostics.
        using var __scope = Jalium.UI.Diagnostics.DiagnosticsScope.BeginIgnoredCreation();
        return new InspectorRowContainer();
    }

    /// <inheritdoc />
    protected override bool IsItemItsOwnContainerOverride(object item) => item is InspectorRowContainer;

    /// <inheritdoc />
    protected override void PrepareContainerForItem(FrameworkElement element, object item)
    {
        base.PrepareContainerForItem(element, item);
        if (element is InspectorRowContainer container && item is InspectorRow row)
        {
            container.Bind(row, _onToggleRow ??= r => ToggleExpand?.Invoke(r));
            container.SetRevealProgress(RevealProgressForIndex(row.Index));
        }
    }

    /// <inheritdoc />
    protected override void ClearContainerForItem(FrameworkElement element, object item)
    {
        if (element is InspectorRowContainer container) container.Unbind();
        base.ClearContainerForItem(element, item);
    }

    /// <summary>Brings the row at <paramref name="index"/> into view — virtualization-aware
    /// (works even when that row's container is not currently realized).</summary>
    internal void BringRowIntoView(int index)
    {
        if (index < 0)
        {
            return;
        }

        (ItemsHost as VirtualizingPanel)?.BringIndexIntoView(index);
    }

    /// <summary>
    /// After the bound row collection is replaced, the inner ScrollViewer still holds the previous
    /// extent and scroll offset — the VSP invalidates only itself and this framework has no
    /// IScrollInfo change notification. Re-measuring the ScrollViewer makes it re-read the new
    /// extent (SyncExtentFromScrollInfo) and clamp an out-of-range offset, so a shrunk list (e.g.
    /// Collapse All while scrolled down) refreshes immediately instead of only on a window resize.
    /// </summary>
    internal void SyncScrollAfterReset()
    {
        if (ItemsHost is IScrollInfo scrollInfo && scrollInfo.ScrollOwner is { } scrollViewer)
        {
            scrollViewer.InvalidateMeasure();
            scrollViewer.InvalidateArrange();
        }
        else
        {
            InvalidateMeasure();
        }
    }

    /// <summary>Currently-realized row containers (viewport + cache). Empty when the list has not
    /// been laid out yet (e.g. headless), which the animation driver uses to fall back to instant
    /// expand/collapse. Fills and returns a reused buffer (the reveal ticks every frame, so no
    /// per-call allocation) — valid only until the next call; iterate immediately, do not store.</summary>
    internal IReadOnlyList<InspectorRowContainer> GetRealizedContainers()
    {
        _realizedScratch.Clear();
        if (ItemsHost is { } panel)
        {
            var children = panel.Children;
            for (int i = 0; i < children.Count; i++)
            {
                if (children[i] is InspectorRowContainer container)
                    _realizedScratch.Add(container);
            }
        }

        return _realizedScratch;
    }
}

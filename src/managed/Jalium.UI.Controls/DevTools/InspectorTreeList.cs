using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Media;
using ShapePath = Jalium.UI.Controls.Shapes.Path;

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

/// <summary>
/// Row container for the inspector list. Builds the row visual (indent + chevron + label) directly,
/// so realized containers are enumerable for the expand/collapse reveal animation. Rows always keep
/// their full layout height (the VSP virtualizes normally); the reveal is render-only — a clipped
/// content slide via <see cref="SetRevealProgress"/> (no opacity) — so it never affects layout,
/// never stacks rows, never allocates per-row alpha layers, and stays O(viewport).
/// </summary>
internal sealed class InspectorRowContainer : ListBoxItem
{
    private const double IndentSize = 14;

    private readonly Grid _content;
    private readonly Border _indent;
    private readonly Border _expanderHit;
    private readonly ShapePath _arrow;
    private readonly TextBlock _label;
    private Action<InspectorRow>? _onToggle;
    private TranslateTransform? _revealTranslate;
    private double _lastRevealY = double.NaN;

    public InspectorRow? Row { get; private set; }

    public InspectorRowContainer()
    {
        SetCurrentValue(TransitionPropertyProperty, "None");
        MinHeight = 0;
        Padding = new Thickness(0);
        ClipToBounds = true; // clips the reveal slide (content translated up) to the row's slot

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _indent = new Border();
        Grid.SetColumn(_indent, 0);

        // Rounded down-triangle chevron — same geometry as the legacy TreeViewItem expander.
        _arrow = new ShapePath
        {
            Data = "M 733.87,841.90 L 1160.54,273.07 A 170.67,170.67,0,0,0,1024.00,0 H 170.67 A 170.67,170.67,0,0,0,34.14,273.07 L 460.80,841.90 A 170.67,170.67,0,0,0,733.87,841.90 Z",
            Fill = new SolidColorBrush(DevToolsTheme.TextPrimaryColor),
            Width = 8,
            Height = 8,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _expanderHit = new Border
        {
            Width = 16,
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)), // transparent yet hit-testable
            Child = _arrow,
        };
        Grid.SetColumn(_expanderHit, 1);
        _expanderHit.AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnExpanderMouseDown), handledEventsToo: true);

        _label = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = DevToolsTheme.UiFont,
            FontSize = DevToolsTheme.FontBase,
            Foreground = new SolidColorBrush(DevToolsTheme.TextPrimaryColor),
        };
        Grid.SetColumn(_label, 2);

        grid.Children.Add(_indent);
        grid.Children.Add(_expanderHit);
        grid.Children.Add(_label);

        _content = grid;
        Content = grid;
    }

    public void Bind(InspectorRow row, Action<InspectorRow> onToggle)
    {
        Row = row;
        _onToggle = onToggle;
        _indent.Width = row.Depth * IndentSize;
        _label.Text = row.DisplayName;
        _arrow.Visibility = row.HasChildren ? Visibility.Visible : Visibility.Hidden;
        SetChevronAngle(row.ChevronAngle);
        // The reveal progress is applied by PrepareContainerForItem right after Bind: 1.0 outside
        // an animation (normalizes a recycled container's stale transform), or the row's current
        // staggered progress while a reveal is active — an unconditional 1.0 here is exactly the
        // first-frame flash of rows realized mid-animation.
    }

    /// <summary>
    /// Drops the bound row model and toggle callback before the container is pooled, so a pooled
    /// container never pins the previous generation's row objects or the DevToolsWindow closure.
    /// </summary>
    internal void Unbind()
    {
        Row = null;
        _onToggle = null;
    }

    /// <summary>
    /// Render-only reveal: slides the row content down into its full-height, clipped slot.
    /// <paramref name="revealed"/> 0 = fully hidden (content translated up out of the clip), 1 =
    /// fully shown. Uses a translate (cheap matrix) + ClipToBounds (scissor), and deliberately NO
    /// opacity — animating per-row opacity forces offscreen alpha layers that on a large expand
    /// exhaust the render target (content goes black) and thrash (flicker). Never touches layout,
    /// so virtualization is unaffected.
    /// </summary>
    internal void SetRevealProgress(double revealed)
    {
        if (revealed >= 1.0)
        {
            if (_content.RenderTransform != null)
            {
                _content.RenderTransform = null;
                _lastRevealY = double.NaN;
                InvalidateComposition();
            }

            return;
        }

        // Fall back to an estimate before the row's first arrange (ActualHeight==0 then), so the
        // reveal isn't a silent no-op on frame 0.
        double h = ActualHeight > 0 ? ActualHeight : (DesiredSize.Height > 0 ? DesiredSize.Height : 28.0);
        double y = -(1.0 - revealed) * h;
        if (y == _lastRevealY && ReferenceEquals(_content.RenderTransform, _revealTranslate))
            return; // unchanged frame: no invalidation, no GPU submission

        var translate = _revealTranslate ??= new TranslateTransform();
        translate.Y = y;
        _lastRevealY = y;
        if (!ReferenceEquals(_content.RenderTransform, translate))
            _content.RenderTransform = translate;

        // Composition-only invalidation: the parent child-loop reads RenderTransform live each
        // frame, so the row's recorded content (and its retained layer) stays valid — only the
        // composite moves. InvalidateVisual here would re-record every animated row every frame.
        InvalidateComposition();
    }

    /// <summary>Re-applies the bound row's chevron angle (animated for the toggled row).</summary>
    internal void ApplyChevron()
    {
        if (Row != null) SetChevronAngle(Row.ChevronAngle);
    }

    private void OnExpanderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Row is { HasChildren: true } row)
        {
            _onToggle?.Invoke(row);
            e.Handled = true; // toggle expansion without also selecting the row
        }
    }

    private void SetChevronAngle(double angle)
    {
        var rotate = _arrow.RenderTransform as RotateTransform ?? new RotateTransform();
        rotate.CenterX = 4;
        rotate.CenterY = 4;
        rotate.Angle = angle;
        _arrow.RenderTransform = rotate;
        _arrow.InvalidateVisual();
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
        SetCurrentValue(TransitionPropertyProperty, "None");
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
    protected override bool IsItemItsOwnContainer(object item) => item is InspectorRowContainer;

    /// <inheritdoc />
    protected override void PrepareContainerForItem(FrameworkElement element, object item)
    {
        // Do NOT call base for our row container: the base would set Content = the InspectorRow
        // data item (clobbering the container's own row visual). Replicate only the bits ListBox
        // needs — owner back-reference + selection state — and bind our visual.
        if (element is InspectorRowContainer container && item is InspectorRow row)
        {
            container.ParentListBox = this;
            container.IsSelected = ReferenceEquals(item, SelectedItem);
            container.Bind(row, _onToggleRow ??= r => ToggleExpand?.Invoke(r));
            // Inside the active reveal range the row enters at its current staggered progress
            // (kills the first-frame flash and mid-animation scroll pop-in); outside it, 1.0
            // normalizes whatever stale reveal transform a recycled container may carry.
            container.SetRevealProgress(RevealProgressForIndex(row.Index));
            return;
        }

        base.PrepareContainerForItem(element, item);
    }

    /// <inheritdoc />
    protected override void ClearContainerForItem(FrameworkElement element, object item)
    {
        // Mirror PrepareContainerForItem's base skip: the row visual is the container's OWN
        // Content (not the data item), and its reveal clip is a constructor-set local
        // ClipToBounds — the base ContentControl branch would ClearValue the Content away
        // (every container popped from the recycle pool would come back blank) and the
        // visual-state hygiene would ClearValue the clip. Undo only what Prepare and the
        // reveal animation set.
        if (element is InspectorRowContainer container)
        {
            container.ParentListBox = null;
            container.ClearValue(ListBoxItem.IsSelectedProperty);
            container.SetRevealProgress(1.0);
            container.ApplyChevron();
            container.Unbind();
            return;
        }

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

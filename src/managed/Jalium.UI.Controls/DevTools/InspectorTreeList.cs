using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
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
        SetRevealProgress(1.0); // recycled containers may carry a stale reveal transform
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
                _content.RenderTransform = null;
        }
        else
        {
            // Fall back to an estimate before the row's first arrange (ActualHeight==0 then), so the
            // reveal isn't a silent no-op on frame 0.
            double h = ActualHeight > 0 ? ActualHeight : (DesiredSize.Height > 0 ? DesiredSize.Height : 28.0);
            _content.RenderTransform = new TranslateTransform { X = 0, Y = -(1.0 - revealed) * h };
        }
        InvalidateVisual();
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

    public InspectorTreeList()
    {
        SetCurrentValue(TransitionPropertyProperty, "None");
    }

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
            container.Bind(row, r => ToggleExpand?.Invoke(r));
            return;
        }

        base.PrepareContainerForItem(element, item);
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
    /// expand/collapse.</summary>
    internal IReadOnlyList<InspectorRowContainer> GetRealizedContainers()
    {
        if (ItemsHost is { } panel)
        {
            return panel.Children.OfType<InspectorRowContainer>().ToList();
        }

        return Array.Empty<InspectorRowContainer>();
    }

    /// <summary>Forces the realizing panel to re-measure/arrange so per-frame reveal updates are
    /// actually presented. A plain InvalidateVisual on a deeply-nested child does not reliably
    /// trigger a window present here; the measure/arrange path does (it is what the legacy tree
    /// animation used). Cheap — rows keep full height, so it is a stable viewport-sized layout pass.</summary>
    internal void InvalidateItemsHostMeasure()
    {
        ItemsHost?.InvalidateMeasure();
        ItemsHost?.InvalidateArrange();
    }
}

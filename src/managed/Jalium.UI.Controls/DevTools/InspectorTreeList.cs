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
/// Visual for a single inspector row: indent spacer + expander chevron + label.
/// Driven entirely by its <see cref="FrameworkElement.DataContext"/> (an <see cref="InspectorRow"/>),
/// so the ContentPresenter recycling fast-path can reuse one instance and just re-point DataContext.
/// </summary>
internal sealed class InspectorRowView : Grid
{
    private const double IndentSize = 14;

    private readonly Border _indent;
    private readonly Border _expanderHit;
    private readonly ShapePath _arrow;
    private readonly TextBlock _label;
    private readonly Action<InspectorRow> _onToggle;

    public InspectorRow? Row { get; private set; }

    public InspectorRowView(Action<InspectorRow> onToggle)
    {
        _onToggle = onToggle;
        SetCurrentValue(TransitionPropertyProperty, "None");

        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        _indent = new Border();
        SetColumn(_indent, 0);

        // Rounded down-triangle chevron — same geometry as the legacy TreeViewItem expander.
        // Natural orientation points down = expanded; collapsed rotates -90° to point right.
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
        SetColumn(_expanderHit, 1);
        _expanderHit.AddHandler(MouseDownEvent, new MouseButtonEventHandler(OnExpanderMouseDown), handledEventsToo: true);

        _label = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            FontFamily = DevToolsTheme.UiFont,
            FontSize = DevToolsTheme.FontBase,
            Foreground = new SolidColorBrush(DevToolsTheme.TextPrimaryColor),
        };
        SetColumn(_label, 2);

        Children.Add(_indent);
        Children.Add(_expanderHit);
        Children.Add(_label);

        DataContextChanged += OnRowDataContextChanged;
    }

    private void OnRowDataContextChanged(object? sender, DependencyPropertyChangedEventArgs e)
    {
        Row = DataContext as InspectorRow;
        if (Row == null)
        {
            return;
        }

        _indent.Width = Row.Depth * IndentSize;
        _label.Text = Row.DisplayName;

        if (Row.HasChildren)
        {
            _arrow.Visibility = Visibility.Visible;
            SetArrowAngle(Row.IsExpanded ? 0 : -90);
        }
        else
        {
            _arrow.Visibility = Visibility.Hidden;
        }
    }

    private void OnExpanderMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Row is { HasChildren: true } row)
        {
            _onToggle(row);
            e.Handled = true; // clicking the chevron toggles expansion without also selecting the row
        }
    }

    private void SetArrowAngle(double angle)
    {
        var rotate = _arrow.RenderTransform as RotateTransform ?? new RotateTransform();
        rotate.CenterX = 4;
        rotate.CenterY = 4;
        rotate.Angle = angle;
        _arrow.RenderTransform = rotate;
    }
}

/// <summary>
/// The Inspector's flat virtualized list. A standard <see cref="ListBox"/> (so virtualization,
/// selection, keyboard nav and selection highlight come for free) wired with a row template and
/// an index-based scroll helper. Replaces the old nested, non-virtualized TreeView.
/// </summary>
internal sealed class InspectorTreeList : ListBox
{
    /// <summary>Callback invoked when a row's expander chevron is clicked.</summary>
    internal Action<InspectorRow>? ToggleExpand;

    public InspectorTreeList()
    {
        SetCurrentValue(TransitionPropertyProperty, "None");

        var template = new DataTemplate();
        template.SetVisualTree(() =>
        {
            using var __scope = Jalium.UI.Diagnostics.DiagnosticsScope.BeginIgnoredCreation();
            return new InspectorRowView(row => ToggleExpand?.Invoke(row));
        });
        ItemTemplate = template;
    }

    /// <inheritdoc />
    protected override FrameworkElement GetContainerForItem(object item)
    {
        // Containers are realized lazily during layout (outside the window's ctor scope);
        // wrap creation so constructor-time InvalidateMeasure does not pollute diagnostics.
        using var __scope = Jalium.UI.Diagnostics.DiagnosticsScope.BeginIgnoredCreation();
        return base.GetContainerForItem(item);
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
    /// After the bound row collection is replaced, the inner ScrollViewer still holds the
    /// previous extent and scroll offset — the VSP invalidates only itself and this framework has
    /// no IScrollInfo change notification. Re-measuring the ScrollViewer makes it re-read the new
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
}

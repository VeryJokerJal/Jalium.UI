using System.Collections.ObjectModel;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Provides a container for a group of commands or controls.
/// </summary>
public sealed class ToolBar : HeaderedItemsControl
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the Band dependency property.
    /// </summary>
    public static readonly DependencyProperty BandProperty =
        DependencyProperty.Register(nameof(Band), typeof(int), typeof(ToolBar),
            new PropertyMetadata(0));

    /// <summary>
    /// Identifies the BandIndex dependency property.
    /// </summary>
    public static readonly DependencyProperty BandIndexProperty =
        DependencyProperty.Register(nameof(BandIndex), typeof(int), typeof(ToolBar),
            new PropertyMetadata(0));

    /// <summary>
    /// Identifies the IsOverflowOpen dependency property.
    /// </summary>
    public static readonly DependencyProperty IsOverflowOpenProperty =
        DependencyProperty.Register(nameof(IsOverflowOpen), typeof(bool), typeof(ToolBar),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the Orientation dependency property.
    /// </summary>
    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(ToolBar),
            new PropertyMetadata(Orientation.Horizontal));

    /// <summary>
    /// Identifies the OverflowMode attached property.
    /// </summary>
    public static readonly DependencyProperty OverflowModeProperty =
        DependencyProperty.RegisterAttached("OverflowMode", typeof(OverflowMode), typeof(ToolBar),
            new PropertyMetadata(OverflowMode.AsNeeded));

    /// <summary>
    /// Identifies the IsOverflowItem attached property.
    /// </summary>
    public static readonly DependencyProperty IsOverflowItemProperty =
        DependencyProperty.RegisterAttached("IsOverflowItem", typeof(bool), typeof(ToolBar),
            new PropertyMetadata(false));

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets a value that indicates where the toolbar should be located in the ToolBarTray.
    /// </summary>
    public int Band
    {
        get => (int)GetValue(BandProperty)!;
        set => SetValue(BandProperty, value);
    }

    /// <summary>
    /// Gets or sets the band index number that indicates the position of the toolbar on the band.
    /// </summary>
    public int BandIndex
    {
        get => (int)GetValue(BandIndexProperty)!;
        set => SetValue(BandIndexProperty, value);
    }

    /// <summary>
    /// Gets or sets a value that indicates whether the ToolBar overflow area is currently visible.
    /// </summary>
    public bool IsOverflowOpen
    {
        get => (bool)GetValue(IsOverflowOpenProperty)!;
        set => SetValue(IsOverflowOpenProperty, value);
    }

    /// <summary>
    /// Gets a value that indicates whether the toolbar has items that are not visible.
    /// </summary>
    public bool HasOverflowItems { get; private set; }

    /// <summary>
    /// Gets or sets the orientation of the ToolBar.
    /// </summary>
    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty)!;
        set => SetValue(OrientationProperty, value);
    }

    #endregion

    #region Attached Properties

    /// <summary>
    /// Gets the value of the OverflowMode attached property for an object.
    /// </summary>
    public static OverflowMode GetOverflowMode(DependencyObject element)
    {
        return (OverflowMode)(element.GetValue(OverflowModeProperty) ?? OverflowMode.AsNeeded);
    }

    /// <summary>
    /// Sets the value of the OverflowMode attached property for an object.
    /// </summary>
    public static void SetOverflowMode(DependencyObject element, OverflowMode mode)
    {
        element.SetValue(OverflowModeProperty, mode);
    }

    /// <summary>
    /// Gets the value of the IsOverflowItem attached property for an object.
    /// </summary>
    public static bool GetIsOverflowItem(DependencyObject element)
    {
        return (bool)(element.GetValue(IsOverflowItemProperty) ?? false);
    }

    internal static void SetIsOverflowItem(DependencyObject element, bool value)
    {
        element.SetValue(IsOverflowItemProperty, value);
    }

    #endregion

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolBar"/> class.
    /// </summary>
    public ToolBar()
    {
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var result = base.ArrangeOverride(finalSize);
        UpdateOverflowItems();
        return result;
    }

    private void UpdateOverflowItems()
    {
        // In a real implementation, this would move items to/from the overflow panel
        // based on available space
        HasOverflowItems = false;
    }
}

/// <summary>
/// Represents a container that handles the layout of a ToolBar.
/// </summary>
public sealed class ToolBarTray : FrameworkElement
{
    private readonly ObservableCollection<ToolBar> _toolBars;

    #region Dependency Properties

    /// <summary>
    /// Identifies the Orientation dependency property.
    /// </summary>
    public static readonly DependencyProperty OrientationProperty =
        DependencyProperty.Register(nameof(Orientation), typeof(Orientation), typeof(ToolBarTray),
            new PropertyMetadata(Orientation.Horizontal));

    /// <summary>
    /// Identifies the IsLocked dependency property.
    /// </summary>
    public static readonly DependencyProperty IsLockedProperty =
        DependencyProperty.Register(nameof(IsLocked), typeof(bool), typeof(ToolBarTray),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the Background dependency property.
    /// </summary>
    public static readonly DependencyProperty BackgroundProperty =
        DependencyProperty.Register(nameof(Background), typeof(Brush), typeof(ToolBarTray),
            new PropertyMetadata(null));

    #endregion

    #region Properties

    /// <summary>
    /// Gets the collection of ToolBar elements inside a ToolBarTray.
    /// </summary>
    public Collection<ToolBar> ToolBars => _toolBars;

    /// <summary>
    /// Gets or sets a value that specifies the orientation of a ToolBarTray.
    /// </summary>
    public Orientation Orientation
    {
        get => (Orientation)GetValue(OrientationProperty)!;
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>
    /// Gets or sets a value that indicates whether ToolBar elements can be moved inside the ToolBarTray.
    /// </summary>
    public bool IsLocked
    {
        get => (bool)GetValue(IsLockedProperty)!;
        set => SetValue(IsLockedProperty, value);
    }

    /// <summary>
    /// Gets or sets a brush to use for the background color of the ToolBarTray.
    /// </summary>
    public Brush? Background
    {
        get => (Brush?)GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    #endregion

    /// <summary>
    /// Initializes a new instance of the <see cref="ToolBarTray"/> class.
    /// </summary>
    public ToolBarTray()
    {
        _toolBars = new ObservableCollection<ToolBar>();
        _toolBars.CollectionChanged += OnToolBarsChanged;
    }

    private void OnToolBarsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        InvalidateMeasure();
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var totalWidth = 0.0;
        var totalHeight = 0.0;
        var maxBand = 0;

        foreach (var toolBar in _toolBars)
        {
            maxBand = Math.Max(maxBand, toolBar.Band);
        }

        // Group toolbars by band
        for (var band = 0; band <= maxBand; band++)
        {
            var bandToolBars = _toolBars.Where(t => t.Band == band).OrderBy(t => t.BandIndex).ToList();
            var bandWidth = 0.0;
            var bandHeight = 0.0;

            foreach (var toolBar in bandToolBars)
            {
                toolBar.Measure(availableSize);
                if (Orientation == Orientation.Horizontal)
                {
                    bandWidth += toolBar.DesiredSize.Width;
                    bandHeight = Math.Max(bandHeight, toolBar.DesiredSize.Height);
                }
                else
                {
                    bandHeight += toolBar.DesiredSize.Height;
                    bandWidth = Math.Max(bandWidth, toolBar.DesiredSize.Width);
                }
            }

            if (Orientation == Orientation.Horizontal)
            {
                totalWidth = Math.Max(totalWidth, bandWidth);
                totalHeight += bandHeight;
            }
            else
            {
                totalWidth += bandWidth;
                totalHeight = Math.Max(totalHeight, bandHeight);
            }
        }

        return new Size(totalWidth, totalHeight);
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var maxBand = _toolBars.Count > 0 ? _toolBars.Max(t => t.Band) : 0;
        var offset = 0.0;

        for (var band = 0; band <= maxBand; band++)
        {
            var bandToolBars = _toolBars.Where(t => t.Band == band).OrderBy(t => t.BandIndex).ToList();
            var bandSize = 0.0;
            var bandOffset = 0.0;

            foreach (var toolBar in bandToolBars)
            {
                Rect rect;
                if (Orientation == Orientation.Horizontal)
                {
                    rect = new Rect(bandOffset, offset, toolBar.DesiredSize.Width, toolBar.DesiredSize.Height);
                    bandOffset += toolBar.DesiredSize.Width;
                    bandSize = Math.Max(bandSize, toolBar.DesiredSize.Height);
                }
                else
                {
                    rect = new Rect(offset, bandOffset, toolBar.DesiredSize.Width, toolBar.DesiredSize.Height);
                    bandOffset += toolBar.DesiredSize.Height;
                    bandSize = Math.Max(bandSize, toolBar.DesiredSize.Width);
                }
                toolBar.Arrange(rect);
            }

            if (Orientation == Orientation.Horizontal)
                offset += bandSize;
            else
                offset += bandSize;
        }

        return finalSize;
    }
}

/// <summary>
/// Specifies how a ToolBar item is placed in the main ToolBar panel and in the overflow panel.
/// </summary>
public enum OverflowMode
{
    /// <summary>
    /// Item moves between the main panel and overflow panel, depending on the available space.
    /// </summary>
    AsNeeded,

    /// <summary>
    /// Item is permanently placed in the overflow panel.
    /// </summary>
    Always,

    /// <summary>
    /// Item is never placed in the overflow panel.
    /// </summary>
    Never
}

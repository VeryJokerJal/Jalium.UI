using Jalium.UI;

namespace Jalium.UI.Controls;

/// <summary>
/// Defines row-specific properties that apply to Grid elements.
/// </summary>
public sealed class RowDefinition : DefinitionBase
{
    /// <summary>
    /// Identifies the Height dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty HeightProperty =
        DependencyProperty.Register(nameof(Height), typeof(GridLength), typeof(RowDefinition),
            new PropertyMetadata(new GridLength(1.0, GridUnitType.Star)));

    /// <summary>
    /// Identifies the MinHeight dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MinHeightProperty =
        DependencyProperty.Register(nameof(MinHeight), typeof(double), typeof(RowDefinition),
            new PropertyMetadata(0.0));

    /// <summary>
    /// Identifies the MaxHeight dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MaxHeightProperty =
        DependencyProperty.Register(nameof(MaxHeight), typeof(double), typeof(RowDefinition),
            new PropertyMetadata(double.PositiveInfinity));

    /// <summary>
    /// Gets or sets the height of the row.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public GridLength Height
    {
        get => (GridLength)GetValue(HeightProperty)!;
        set => SetValue(HeightProperty, value);
    }

    /// <summary>
    /// Gets or sets the minimum height of the row.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double MinHeight
    {
        get => (double)GetValue(MinHeightProperty)!;
        set => SetValue(MinHeightProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum height of the row.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double MaxHeight
    {
        get => (double)GetValue(MaxHeightProperty)!;
        set => SetValue(MaxHeightProperty, value);
    }

    /// <summary>
    /// Gets the actual height of the row after layout.
    /// </summary>
    public double ActualHeight { get; internal set; }

    /// <summary>
    /// Gets the offset of the row from the top of the grid.
    /// </summary>
    public double Offset { get; internal set; }
}

/// <summary>
/// Defines column-specific properties that apply to Grid elements.
/// </summary>
public sealed class ColumnDefinition : DefinitionBase
{
    /// <summary>
    /// Identifies the Width dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty WidthProperty =
        DependencyProperty.Register(nameof(Width), typeof(GridLength), typeof(ColumnDefinition),
            new PropertyMetadata(new GridLength(1.0, GridUnitType.Star)));

    /// <summary>
    /// Identifies the MinWidth dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MinWidthProperty =
        DependencyProperty.Register(nameof(MinWidth), typeof(double), typeof(ColumnDefinition),
            new PropertyMetadata(0.0));

    /// <summary>
    /// Identifies the MaxWidth dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty MaxWidthProperty =
        DependencyProperty.Register(nameof(MaxWidth), typeof(double), typeof(ColumnDefinition),
            new PropertyMetadata(double.PositiveInfinity));

    /// <summary>
    /// Gets or sets the width of the column.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public GridLength Width
    {
        get => (GridLength)GetValue(WidthProperty)!;
        set => SetValue(WidthProperty, value);
    }

    /// <summary>
    /// Gets or sets the minimum width of the column.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double MinWidth
    {
        get => (double)GetValue(MinWidthProperty)!;
        set => SetValue(MinWidthProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum width of the column.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double MaxWidth
    {
        get => (double)GetValue(MaxWidthProperty)!;
        set => SetValue(MaxWidthProperty, value);
    }

    /// <summary>
    /// Gets the actual width of the column after layout.
    /// </summary>
    public double ActualWidth { get; internal set; }

    /// <summary>
    /// Gets the offset of the column from the left of the grid.
    /// </summary>
    public double Offset { get; internal set; }
}

/// <summary>
/// A collection of <see cref="RowDefinition"/> objects.
/// </summary>
public sealed class RowDefinitionCollection : List<RowDefinition>
{
}

/// <summary>
/// A collection of <see cref="ColumnDefinition"/> objects.
/// </summary>
public sealed class ColumnDefinitionCollection : List<ColumnDefinition>
{
}

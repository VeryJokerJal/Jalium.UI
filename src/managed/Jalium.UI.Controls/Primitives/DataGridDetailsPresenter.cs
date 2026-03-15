namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Represents a control that displays the details of a row in a DataGrid.
/// </summary>
public class DataGridDetailsPresenter : ContentPresenter
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the ContentHeight dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty ContentHeightProperty =
        DependencyProperty.Register(nameof(ContentHeight), typeof(double), typeof(DataGridDetailsPresenter),
            new PropertyMetadata(double.NaN, OnLayoutPropertyChanged));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the height of the details content.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public double ContentHeight
    {
        get => (double)GetValue(ContentHeightProperty)!;
        set => SetValue(ContentHeightProperty, value);
    }

    /// <summary>
    /// Gets or sets the DataGrid that owns this presenter.
    /// </summary>
    public DataGrid? DataGridOwner { get; internal set; }

    /// <summary>
    /// Gets or sets the row item associated with these details.
    /// </summary>
    public object? RowItem { get; internal set; }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var baseSize = base.MeasureOverride(availableSize);

        if (!double.IsNaN(ContentHeight) && ContentHeight > 0)
        {
            return new Size(baseSize.Width, ContentHeight);
        }

        return baseSize;
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is DataGridDetailsPresenter presenter)
        {
            presenter.InvalidateMeasure();
        }
    }

    #endregion
}

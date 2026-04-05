namespace Jalium.UI.Controls.Charts;

/// <summary>
/// A category (string label) axis.
/// </summary>
public class CategoryAxis : ChartAxis
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the Categories dependency property.
    /// </summary>
    public static readonly DependencyProperty CategoriesProperty =
        DependencyProperty.Register(nameof(Categories), typeof(IList<string>), typeof(CategoryAxis),
            new PropertyMetadata(null));

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the category labels.
    /// </summary>
    public IList<string>? Categories
    {
        get => (IList<string>?)GetValue(CategoriesProperty);
        set => SetValue(CategoriesProperty, value);
    }

    #endregion

    #region Methods

    /// <inheritdoc />
    public override double[] GenerateTicks(double min, double max, double availablePixels)
    {
        var categories = Categories;
        if (categories == null || categories.Count == 0)
            return Array.Empty<double>();

        var ticks = new double[categories.Count];
        for (int i = 0; i < categories.Count; i++)
        {
            ticks[i] = i;
        }
        return ticks;
    }

    /// <inheritdoc />
    public override string FormatLabel(double value)
    {
        var categories = Categories;
        if (categories != null)
        {
            var index = (int)Math.Round(value);
            if (index >= 0 && index < categories.Count)
                return categories[index];
        }

        return base.FormatLabel(value);
    }

    /// <summary>
    /// Converts a category index to a pixel position, centering each category in its band.
    /// </summary>
    public override double ValueToPixel(double value, double min, double max, double pixelRange)
    {
        var categories = Categories;
        if (categories == null || categories.Count == 0)
            return base.ValueToPixel(value, min, max, pixelRange);

        var count = categories.Count;
        if (count == 1)
            return pixelRange / 2.0;

        // Each category gets an equal band width
        var bandWidth = pixelRange / count;
        return value * bandWidth + bandWidth / 2.0;
    }

    /// <summary>
    /// Converts a pixel position to a category index.
    /// </summary>
    public override double PixelToValue(double pixel, double min, double max, double pixelRange)
    {
        var categories = Categories;
        if (categories == null || categories.Count == 0)
            return base.PixelToValue(pixel, min, max, pixelRange);

        var count = categories.Count;
        var bandWidth = pixelRange / count;
        return Math.Clamp(Math.Floor(pixel / bandWidth), 0, count - 1);
    }

    #endregion
}

namespace Jalium.UI.Media;

/// <summary>
/// Represents an ordered collection of Double values.
/// Used for StrokeDashArray and other properties requiring a list of doubles.
/// </summary>
public sealed class DoubleCollection : List<double>
{
    /// <summary>
    /// Initializes a new instance of the DoubleCollection class.
    /// </summary>
    public DoubleCollection()
    {
    }

    /// <summary>
    /// Initializes a new instance with the specified capacity.
    /// </summary>
    public DoubleCollection(int capacity) : base(capacity)
    {
    }

    /// <summary>
    /// Initializes a new instance with the specified values.
    /// </summary>
    public DoubleCollection(IEnumerable<double> collection) : base(collection)
    {
    }

    /// <summary>
    /// Parses a string of whitespace/comma-separated doubles into a DoubleCollection.
    /// </summary>
    public static DoubleCollection Parse(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            return new DoubleCollection();

        var collection = new DoubleCollection();
        var parts = source.Split(new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            collection.Add(double.Parse(part, System.Globalization.CultureInfo.InvariantCulture));
        }
        return collection;
    }
}

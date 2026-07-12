namespace Jalium.UI;

/// <summary>
/// Represents a named data-transfer format and its numeric identifier.
/// </summary>
public sealed class DataFormat
{
    /// <summary>
    /// Initializes a new data format with the specified name and identifier.
    /// </summary>
    public DataFormat(string name, int id)
    {
        ArgumentNullException.ThrowIfNull(name);

        if (name.Length == 0)
        {
            throw new ArgumentException("Empty string is not a valid value for parameter 'format'.");
        }

        Name = name;
        Id = id;
    }

    /// <summary>
    /// Gets the registered format name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the numeric format identifier.
    /// </summary>
    public int Id { get; }
}

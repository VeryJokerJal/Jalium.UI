namespace Jalium.UI.Controls.Primitives;

/// <summary>
/// Represents a text box used in DatePicker controls with placeholder support.
/// </summary>
public sealed class DatePickerTextBox : TextBox
{
    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="DatePickerTextBox"/> class.
    /// </summary>
    public DatePickerTextBox()
    {
        PlaceholderText = "Select a date";
    }

    #endregion

    #region Date Validation

    /// <summary>
    /// Validates that the text represents a valid date.
    /// </summary>
    /// <returns>True if the text is a valid date; otherwise, false.</returns>
    public bool ValidateDate()
    {
        return DateTime.TryParse(Text, out _);
    }

    /// <summary>
    /// Gets the date value if the text is a valid date.
    /// </summary>
    /// <returns>The parsed date, or null if invalid.</returns>
    public DateTime? GetDate()
    {
        if (DateTime.TryParse(Text, out var date))
        {
            return date;
        }
        return null;
    }

    /// <summary>
    /// Sets the text to represent the specified date.
    /// </summary>
    /// <param name="date">The date to set.</param>
    /// <param name="format">The format string to use.</param>
    public void SetDate(DateTime? date, string format = "d")
    {
        Text = date?.ToString(format) ?? string.Empty;
    }

    #endregion
}

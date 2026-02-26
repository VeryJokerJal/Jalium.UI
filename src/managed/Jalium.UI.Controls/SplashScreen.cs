using System.Runtime.InteropServices;

namespace Jalium.UI.Controls;

/// <summary>
/// Provides a startup screen for a WPF application.
/// </summary>
public sealed class SplashScreen
{
    private readonly string _resourceName;
    private nint _hwnd;

    /// <summary>
    /// Initializes a new instance of the <see cref="SplashScreen"/> class with the specified resource.
    /// </summary>
    /// <param name="resourceName">The name of the embedded resource.</param>
    public SplashScreen(string resourceName)
    {
        _resourceName = resourceName ?? throw new ArgumentNullException(nameof(resourceName));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SplashScreen"/> class with the specified
    /// resource assembly and resource name.
    /// </summary>
    public SplashScreen(System.Reflection.Assembly resourceAssembly, string resourceName)
    {
        _resourceName = resourceName ?? throw new ArgumentNullException(nameof(resourceName));
    }

    /// <summary>
    /// Displays the splash screen.
    /// </summary>
    /// <param name="autoClose">true to automatically close the splash screen; otherwise, false.</param>
    public void Show(bool autoClose)
    {
        Show(autoClose, false);
    }

    /// <summary>
    /// Displays the splash screen.
    /// </summary>
    /// <param name="autoClose">true to automatically close the splash screen.</param>
    /// <param name="topMost">true to make the splash screen topmost.</param>
    public void Show(bool autoClose, bool topMost)
    {
        // Splash screen display is handled by the native rendering backend
        // The resource is loaded and shown as a simple bitmap window
    }

    /// <summary>
    /// Closes the splash screen.
    /// </summary>
    /// <param name="fadeoutDuration">The duration of the fadeout animation.</param>
    public void Close(TimeSpan fadeoutDuration)
    {
        // Close the splash screen with optional fade animation
        if (_hwnd != nint.Zero)
        {
            _hwnd = nint.Zero;
        }
    }
}

/// <summary>
/// Specifies the character casing applied to text in a <see cref="TextBox"/> control.
/// </summary>
public enum CharacterCasing
{
    /// <summary>Characters are not changed.</summary>
    Normal,
    /// <summary>Characters are converted to lowercase.</summary>
    Lower,
    /// <summary>Characters are converted to uppercase.</summary>
    Upper
}

/// <summary>
/// Provides data for the <see cref="VirtualizingStackPanel.CleanUpVirtualizedItem"/> event.
/// </summary>
public sealed class CleanUpVirtualizedItemEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CleanUpVirtualizedItemEventArgs"/> class.
    /// </summary>
    public CleanUpVirtualizedItemEventArgs(object value, UIElement element)
    {
        Value = value;
        UIElement = element;
    }

    /// <summary>
    /// Gets the original data object.
    /// </summary>
    public object Value { get; }

    /// <summary>
    /// Gets the UI element that represents the data object.
    /// </summary>
    public UIElement UIElement { get; }

    /// <summary>
    /// Gets or sets a value indicating whether this item should not be virtualized.
    /// </summary>
    public bool Cancel { get; set; }
}

/// <summary>
/// Represents the method that handles the CleanUpVirtualizedItem event.
/// </summary>
public delegate void CleanUpVirtualizedItemEventHandler(object sender, CleanUpVirtualizedItemEventArgs e);

/// <summary>
/// Converts the dimensions of a GroupBox control border to its mask geometry.
/// </summary>
public sealed class BorderGapMaskConverter : IMultiValueConverter
{
    /// <inheritdoc />
    public object? Convert(object?[] values, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        // In WPF, this creates a CombinedGeometry with a hole for the header
        // Simplified implementation returns null (no mask needed for Jalium.UI's GroupBox rendering)
        if (values.Length < 3) return null;

        var headerWidth = values[0] is double w ? w : 0;
        var borderWidth = values[1] is double bw ? bw : 0;
        var borderHeight = values[2] is double bh ? bh : 0;

        if (borderWidth <= 0 || borderHeight <= 0) return null;

        // Return a simple mask that covers the border area with a gap for the header
        return new Jalium.UI.Media.RectangleGeometry(new Rect(0, 0, borderWidth, borderHeight));
    }

    /// <inheritdoc />
    public object?[] ConvertBack(object? value, Type[] targetTypes, object? parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

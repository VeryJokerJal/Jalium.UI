using System.Runtime.InteropServices;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Provides a startup screen for a WPF application.
/// </summary>
public sealed class SplashScreen
{
    private readonly System.Reflection.Assembly _resourceAssembly;
    private readonly string _resourceName;
    private nint _hwnd;
    private Window? _window;
    private Application? _autoCloseApplication;
    private Window? _autoCloseTargetWindow;

    /// <summary>
    /// Initializes a new instance of the <see cref="SplashScreen"/> class with the specified resource.
    /// </summary>
    /// <param name="resourceName">The name of the embedded resource.</param>
    public SplashScreen(string resourceName)
    {
        _resourceAssembly = System.Reflection.Assembly.GetEntryAssembly()
            ?? System.Reflection.Assembly.GetExecutingAssembly();
        _resourceName = resourceName ?? throw new ArgumentNullException(nameof(resourceName));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SplashScreen"/> class with the specified
    /// resource assembly and resource name.
    /// </summary>
    public SplashScreen(System.Reflection.Assembly resourceAssembly, string resourceName)
    {
        _resourceAssembly = resourceAssembly ?? throw new ArgumentNullException(nameof(resourceAssembly));
        _resourceName = resourceName ?? throw new ArgumentNullException(nameof(resourceName));
    }

    /// <summary>
    /// Gets the resource name used by the splash screen.
    /// </summary>
    public string ResourceName => _resourceName;

    /// <summary>
    /// Gets whether the splash screen is currently shown.
    /// </summary>
    public bool IsVisible => _window != null;

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
        if (_window != null)
        {
            return;
        }

        var image = TryCreateSplashImage();
        if (image == null)
        {
            return;
        }

        _window = new Window
        {
            Width = image.Width,
            Height = image.Height,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            Background = Brushes.Transparent,
            Topmost = topMost,
            Content = new Border
            {
                Background = Brushes.Transparent,
                Child = new Image
                {
                    Source = image,
                    Stretch = Stretch.None
                }
            }
        };

        _window.Closed += (_, _) =>
        {
            DetachAutoClose();
            _window = null;
            _hwnd = nint.Zero;
        };

        _window.Show();
        _hwnd = _window.Handle;

        if (autoClose)
        {
            ArmAutoClose();
        }
    }

    /// <summary>
    /// Closes the splash screen.
    /// </summary>
    /// <param name="fadeoutDuration">The duration of the fadeout animation.</param>
    public void Close(TimeSpan fadeoutDuration)
    {
        DetachAutoClose();

        var window = _window;
        if (window == null)
        {
            _hwnd = nint.Zero;
            return;
        }

        _window = null;
        _hwnd = nint.Zero;
        window.Close();
    }

    private void ArmAutoClose()
    {
        if (TryAttachAutoCloseApplication(Application.Current))
        {
            return;
        }

        Application.CurrentChanged += OnApplicationCurrentChanged;
    }

    private void OnApplicationCurrentChanged(object? sender, EventArgs e)
    {
        if (!TryAttachAutoCloseApplication(Application.Current))
        {
            return;
        }

        Application.CurrentChanged -= OnApplicationCurrentChanged;
    }

    private bool TryAttachAutoCloseApplication(Application? app)
    {
        if (app == null)
        {
            return false;
        }

        if (!ReferenceEquals(_autoCloseApplication, app))
        {
            if (_autoCloseApplication != null)
            {
                _autoCloseApplication.MainWindowChanged -= OnApplicationMainWindowChanged;
            }

            _autoCloseApplication = app;
            _autoCloseApplication.MainWindowChanged += OnApplicationMainWindowChanged;
        }

        TryAttachAutoCloseTarget(app.MainWindow);
        return true;
    }

    private void OnApplicationMainWindowChanged(object? sender, EventArgs e)
    {
        if (sender is not Application app)
        {
            return;
        }

        TryAttachAutoCloseTarget(app.MainWindow);
    }

    private bool TryAttachAutoCloseTarget(Window? targetWindow)
    {
        if (targetWindow == null || ReferenceEquals(targetWindow, _window))
        {
            return false;
        }

        if (!ReferenceEquals(_autoCloseTargetWindow, targetWindow))
        {
            if (_autoCloseTargetWindow != null)
            {
                _autoCloseTargetWindow.Loaded -= OnAutoCloseTargetLoaded;
            }

            _autoCloseTargetWindow = targetWindow;
            _autoCloseTargetWindow.Loaded += OnAutoCloseTargetLoaded;
        }

        if (targetWindow.Handle != nint.Zero)
        {
            Close(TimeSpan.Zero);
        }

        return true;
    }

    private void OnAutoCloseTargetLoaded(object? sender, EventArgs e)
    {
        if (sender is Window targetWindow)
        {
            targetWindow.Loaded -= OnAutoCloseTargetLoaded;
            if (ReferenceEquals(_autoCloseTargetWindow, targetWindow))
            {
                _autoCloseTargetWindow = null;
            }
        }

        Close(TimeSpan.Zero);
    }

    private void DetachAutoClose()
    {
        Application.CurrentChanged -= OnApplicationCurrentChanged;

        if (_autoCloseApplication != null)
        {
            _autoCloseApplication.MainWindowChanged -= OnApplicationMainWindowChanged;
            _autoCloseApplication = null;
        }

        if (_autoCloseTargetWindow != null)
        {
            _autoCloseTargetWindow.Loaded -= OnAutoCloseTargetLoaded;
            _autoCloseTargetWindow = null;
        }
    }

    private BitmapImage? TryCreateSplashImage()
    {
        var resourceName = ResolveResourceName(_resourceAssembly, _resourceName);
        if (resourceName == null)
        {
            return null;
        }

        using var stream = _resourceAssembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            return null;
        }

        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return BitmapImage.FromBytes(memory.ToArray());
    }

    private static string? ResolveResourceName(System.Reflection.Assembly assembly, string resourceName)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);

        var manifestNames = assembly.GetManifestResourceNames();
        return manifestNames.FirstOrDefault(name => string.Equals(name, resourceName, StringComparison.Ordinal))
            ?? manifestNames.FirstOrDefault(name => name.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));
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

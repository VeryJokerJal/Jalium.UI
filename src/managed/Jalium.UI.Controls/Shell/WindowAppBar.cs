namespace Jalium.UI.Shell;

/// <summary>Specifies the monitor edge occupied by an application desktop toolbar.</summary>
public enum AppBarEdge
{
    /// <summary>The left edge.</summary>
    Left,
    /// <summary>The top edge.</summary>
    Top,
    /// <summary>The right edge.</summary>
    Right,
    /// <summary>The bottom edge.</summary>
    Bottom
}

/// <summary>
/// Adds Windows Shell appbar registration and work-area reservation to a window.
/// The window retains its own chrome, resize mode, taskbar visibility and topmost settings.
/// </summary>
public static class WindowAppBar
{
    /// <summary>Identifies the IsEnabled attached property.</summary>
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(WindowAppBar),
            new PropertyMetadata(false, OnConfigurationChanged));

    /// <summary>Identifies the Edge attached property.</summary>
    public static readonly DependencyProperty EdgeProperty =
        DependencyProperty.RegisterAttached("Edge", typeof(AppBarEdge), typeof(WindowAppBar),
            new PropertyMetadata(AppBarEdge.Top, OnConfigurationChanged),
            static value => value is AppBarEdge edge && edge is >= AppBarEdge.Left and <= AppBarEdge.Bottom);

    /// <summary>Identifies the Thickness attached property, measured in device-independent pixels.</summary>
    public static readonly DependencyProperty ThicknessProperty =
        DependencyProperty.RegisterAttached("Thickness", typeof(double), typeof(WindowAppBar),
            new PropertyMetadata(48.0, OnConfigurationChanged),
            static value => value is double thickness && double.IsFinite(thickness) && thickness > 0);

    /// <summary>Gets whether this platform supports Windows Shell appbars.</summary>
    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>Gets whether the window should act as an appbar when visible in its normal state.</summary>
    public static bool GetIsEnabled(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return (bool)window.GetValue(IsEnabledProperty)!;
    }

    /// <summary>
    /// Enables or disables appbar registration. Hidden, minimized, maximized and fullscreen
    /// windows release their reservation until they return to a visible normal state.
    /// </summary>
    /// <exception cref="PlatformNotSupportedException">Appbars are enabled on a platform other than Windows.</exception>
    public static void SetIsEnabled(Window window, bool value)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (value && !IsSupported)
            throw new PlatformNotSupportedException("Application desktop toolbars require the Windows Shell.");
        window.SetValue(IsEnabledProperty, value);
    }

    /// <summary>Gets the monitor edge to which the window is docked.</summary>
    public static AppBarEdge GetEdge(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return (AppBarEdge)window.GetValue(EdgeProperty)!;
    }

    /// <summary>Sets the monitor edge to which the window is docked.</summary>
    public static void SetEdge(Window window, AppBarEdge value)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.SetValue(EdgeProperty, value);
    }

    /// <summary>Gets the appbar's thickness in device-independent pixels.</summary>
    public static double GetThickness(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return (double)window.GetValue(ThicknessProperty)!;
    }

    /// <summary>Sets the positive, finite appbar thickness in device-independent pixels.</summary>
    public static void SetThickness(Window window, double value)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.SetValue(ThicknessProperty, value);
    }

    /// <summary>Gets whether the window currently has an active Shell appbar registration.</summary>
    public static bool GetIsRegistered(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return window.IsAppBarRegistered;
    }

    private static void OnConfigurationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Window window)
            throw new ArgumentException("WindowAppBar properties can only be attached to a Window.");
        if (GetIsEnabled(window) && !IsSupported)
            throw new PlatformNotSupportedException("Application desktop toolbars require the Windows Shell.");
        window.UpdateAppBarConfiguration(GetIsEnabled(window));
    }
}

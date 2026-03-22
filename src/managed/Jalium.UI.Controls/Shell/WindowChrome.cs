using Jalium.UI.Media;

namespace Jalium.UI.Controls.Shell;

/// <summary>
/// Represents an object that describes the customizations to the non-client area of a window.
/// </summary>
public sealed class WindowChrome : DependencyObject
{
    #region Dependency Properties

    /// <summary>
    /// Identifies the WindowChrome attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty WindowChromeProperty =
        DependencyProperty.RegisterAttached("WindowChrome", typeof(WindowChrome), typeof(WindowChrome),
            new PropertyMetadata(null, OnWindowChromeChanged));

    /// <summary>
    /// Identifies the IsHitTestVisibleInChrome attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty IsHitTestVisibleInChromeProperty =
        DependencyProperty.RegisterAttached("IsHitTestVisibleInChrome", typeof(bool), typeof(WindowChrome),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the ResizeGripDirection attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty ResizeGripDirectionProperty =
        DependencyProperty.RegisterAttached("ResizeGripDirection", typeof(ResizeGripDirection), typeof(WindowChrome),
            new PropertyMetadata(ResizeGripDirection.None));

    /// <summary>
    /// Identifies the CaptionHeight dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty CaptionHeightProperty =
        DependencyProperty.Register(nameof(CaptionHeight), typeof(double), typeof(WindowChrome),
            new PropertyMetadata(20.0, OnChromePropertyChanged));

    /// <summary>
    /// Identifies the ResizeBorderThickness dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty ResizeBorderThicknessProperty =
        DependencyProperty.Register(nameof(ResizeBorderThickness), typeof(Thickness), typeof(WindowChrome),
            new PropertyMetadata(new Thickness(4), OnChromePropertyChanged));

    /// <summary>
    /// Identifies the GlassFrameThickness dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty GlassFrameThicknessProperty =
        DependencyProperty.Register(nameof(GlassFrameThickness), typeof(Thickness), typeof(WindowChrome),
            new PropertyMetadata(Thickness.Zero, OnChromePropertyChanged));

    /// <summary>
    /// Identifies the CornerRadius dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty CornerRadiusProperty =
        DependencyProperty.Register(nameof(CornerRadius), typeof(CornerRadius), typeof(WindowChrome),
            new PropertyMetadata(default(CornerRadius), OnChromePropertyChanged));

    /// <summary>
    /// Identifies the UseAeroCaptionButtons dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty UseAeroCaptionButtonsProperty =
        DependencyProperty.Register(nameof(UseAeroCaptionButtons), typeof(bool), typeof(WindowChrome),
            new PropertyMetadata(true, OnChromePropertyChanged));

    /// <summary>
    /// Identifies the NonClientFrameEdges dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty NonClientFrameEdgesProperty =
        DependencyProperty.Register(nameof(NonClientFrameEdges), typeof(NonClientFrameEdges), typeof(WindowChrome),
            new PropertyMetadata(NonClientFrameEdges.None, OnChromePropertyChanged));

    #endregion

    #region Attached Property Accessors

    /// <summary>
    /// Gets the WindowChrome attached to the specified window.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static WindowChrome? GetWindowChrome(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return (WindowChrome?)window.GetValue(WindowChromeProperty);
    }

    /// <summary>
    /// Sets the WindowChrome attached to the specified window.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static void SetWindowChrome(Window window, WindowChrome? chrome)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.SetValue(WindowChromeProperty, chrome);
    }

    /// <summary>
    /// Gets a value indicating whether the element is hit test visible in the non-client area.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static bool GetIsHitTestVisibleInChrome(IInputElement inputElement)
    {
        if (inputElement is not DependencyObject d)
            throw new ArgumentException("Element must be a DependencyObject", nameof(inputElement));
        return (bool)(d.GetValue(IsHitTestVisibleInChromeProperty) ?? false);
    }

    /// <summary>
    /// Sets a value indicating whether the element is hit test visible in the non-client area.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static void SetIsHitTestVisibleInChrome(IInputElement inputElement, bool hitTestVisible)
    {
        if (inputElement is not DependencyObject d)
            throw new ArgumentException("Element must be a DependencyObject", nameof(inputElement));
        d.SetValue(IsHitTestVisibleInChromeProperty, hitTestVisible);
    }

    /// <summary>
    /// Gets the resize grip direction for the element.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static ResizeGripDirection GetResizeGripDirection(IInputElement inputElement)
    {
        if (inputElement is not DependencyObject d)
            throw new ArgumentException("Element must be a DependencyObject", nameof(inputElement));
        return (ResizeGripDirection)(d.GetValue(ResizeGripDirectionProperty) ?? ResizeGripDirection.None);
    }

    /// <summary>
    /// Sets the resize grip direction for the element.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static void SetResizeGripDirection(IInputElement inputElement, ResizeGripDirection direction)
    {
        if (inputElement is not DependencyObject d)
            throw new ArgumentException("Element must be a DependencyObject", nameof(inputElement));
        d.SetValue(ResizeGripDirectionProperty, direction);
    }

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the height of the caption area at the top of the window.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public double CaptionHeight
    {
        get => (double)GetValue(CaptionHeightProperty)!;
        set => SetValue(CaptionHeightProperty, value);
    }

    /// <summary>
    /// Gets or sets the width of the window's resize border.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public Thickness ResizeBorderThickness
    {
        get => (Thickness)GetValue(ResizeBorderThicknessProperty)!;
        set => SetValue(ResizeBorderThicknessProperty, value);
    }

    /// <summary>
    /// Gets or sets the thickness of the glass frame around the window.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public Thickness GlassFrameThickness
    {
        get => (Thickness)GetValue(GlassFrameThicknessProperty)!;
        set => SetValue(GlassFrameThicknessProperty, value);
    }

    /// <summary>
    /// Gets or sets the corner radius for the window.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty)!;
        set => SetValue(CornerRadiusProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the Aero caption buttons are visible.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public bool UseAeroCaptionButtons
    {
        get => (bool)GetValue(UseAeroCaptionButtonsProperty)!;
        set => SetValue(UseAeroCaptionButtonsProperty, value);
    }

    /// <summary>
    /// Gets or sets the edges of the window that draw the system frame.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public NonClientFrameEdges NonClientFrameEdges
    {
        get => (NonClientFrameEdges)(GetValue(NonClientFrameEdgesProperty) ?? NonClientFrameEdges.None);
        set => SetValue(NonClientFrameEdgesProperty, value);
    }

    #endregion

    #region Static Properties

    /// <summary>
    /// Gets a uniform glass frame thickness that completely fills the window with glass.
    /// </summary>
    public static Thickness GlassFrameCompleteThickness { get; } = new Thickness(-1);

    #endregion

    #region Property Changed Handlers

    private static void OnWindowChromeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Window window)
        {
            if (e.OldValue is WindowChrome oldChrome)
            {
                oldChrome.DetachFromWindow(window);
            }

            if (e.NewValue is WindowChrome newChrome)
            {
                newChrome.AttachToWindow(window);
            }
        }
    }

    private static void OnChromePropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is WindowChrome chrome)
        {
            chrome.OnChromePropertyChangedInternal();
        }
    }

    #endregion

    #region Internal Methods

    private Window? _attachedWindow;

    /// <summary>
    /// Attaches this WindowChrome to the specified window.
    /// </summary>
    private void AttachToWindow(Window window)
    {
        _attachedWindow = window;
        ApplyChromeToWindow(window);
    }

    /// <summary>
    /// Detaches this WindowChrome from the specified window.
    /// </summary>
    private void DetachFromWindow(Window window)
    {
        if (_attachedWindow == window)
        {
            RemoveChromeFromWindow(window);
            _attachedWindow = null;
        }
    }

    /// <summary>
    /// Called when a chrome property changes.
    /// </summary>
    private void OnChromePropertyChangedInternal()
    {
        if (_attachedWindow != null)
        {
            ApplyChromeToWindow(_attachedWindow);
        }
    }

    /// <summary>
    /// Applies the chrome settings to the window.
    /// </summary>
    private void ApplyChromeToWindow(Window window)
    {
        // Platform-specific implementation would use DWM APIs
        // to customize the window frame
        UpdateWindowStyleInternal(window);
    }

    /// <summary>
    /// Removes the chrome settings from the window.
    /// </summary>
    private void RemoveChromeFromWindow(Window window)
    {
        // Platform-specific implementation would restore default window frame
        RestoreWindowStyleInternal(window);
    }

    /// <summary>
    /// Updates the window style based on chrome settings.
    /// </summary>
    private void UpdateWindowStyleInternal(Window window)
    {
        // Platform-specific implementation using DwmExtendFrameIntoClientArea
        // and WM_NCCALCSIZE handling
    }

    /// <summary>
    /// Restores the original window style.
    /// </summary>
    private void RestoreWindowStyleInternal(Window window)
    {
        // Platform-specific implementation
    }

    #endregion
}

/// <summary>
/// Specifies the edges of a window's frame that should remain non-client.
/// </summary>
[Flags]
public enum NonClientFrameEdges
{
    /// <summary>
    /// No edges are non-client.
    /// </summary>
    None = 0,

    /// <summary>
    /// The left edge is non-client.
    /// </summary>
    Left = 1,

    /// <summary>
    /// The top edge is non-client.
    /// </summary>
    Top = 2,

    /// <summary>
    /// The right edge is non-client.
    /// </summary>
    Right = 4,

    /// <summary>
    /// The bottom edge is non-client.
    /// </summary>
    Bottom = 8
}

/// <summary>
/// Specifies the direction of a resize grip.
/// </summary>
public enum ResizeGripDirection
{
    /// <summary>
    /// No resize grip.
    /// </summary>
    None,

    /// <summary>
    /// Resize from the top-left corner.
    /// </summary>
    TopLeft,

    /// <summary>
    /// Resize from the top edge.
    /// </summary>
    Top,

    /// <summary>
    /// Resize from the top-right corner.
    /// </summary>
    TopRight,

    /// <summary>
    /// Resize from the left edge.
    /// </summary>
    Left,

    /// <summary>
    /// Resize from the right edge.
    /// </summary>
    Right,

    /// <summary>
    /// Resize from the bottom-left corner.
    /// </summary>
    BottomLeft,

    /// <summary>
    /// Resize from the bottom edge.
    /// </summary>
    Bottom,

    /// <summary>
    /// Resize from the bottom-right corner.
    /// </summary>
    BottomRight,

    /// <summary>
    /// Move the window (caption area).
    /// </summary>
    Caption
}


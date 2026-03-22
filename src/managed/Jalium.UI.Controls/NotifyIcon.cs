using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// Represents a Windows notification area (system tray) icon.
/// </summary>
public class NotifyIcon : FrameworkElement, IDisposable
{
    private bool _disposed;
    private bool _isVisible;
    private ContextMenu? _contextMenu;
    private IntPtr _iconHandle;

    #region Dependency Properties

    /// <summary>
    /// Identifies the Text dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(NotifyIcon),
            new PropertyMetadata(string.Empty, OnTextChanged));

    /// <summary>
    /// Identifies the Icon dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(nameof(Icon), typeof(ImageSource), typeof(NotifyIcon),
            new PropertyMetadata(null, OnIconChanged));

    /// <summary>
    /// Identifies the Visible dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty VisibleProperty =
        DependencyProperty.Register(nameof(Visible), typeof(bool), typeof(NotifyIcon),
            new PropertyMetadata(false, OnVisibleChanged));

    /// <summary>
    /// Identifies the BalloonTipIcon dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty BalloonTipIconProperty =
        DependencyProperty.Register(nameof(BalloonTipIcon), typeof(BalloonTipIcon), typeof(NotifyIcon),
            new PropertyMetadata(BalloonTipIcon.None));

    /// <summary>
    /// Identifies the BalloonTipTitle dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty BalloonTipTitleProperty =
        DependencyProperty.Register(nameof(BalloonTipTitle), typeof(string), typeof(NotifyIcon),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// Identifies the BalloonTipText dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty BalloonTipTextProperty =
        DependencyProperty.Register(nameof(BalloonTipText), typeof(string), typeof(NotifyIcon),
            new PropertyMetadata(string.Empty));

    #endregion

    #region Events

    /// <summary>
    /// Occurs when the balloon tip is clicked.
    /// </summary>
    public event EventHandler? BalloonTipClicked;

    /// <summary>
    /// Occurs when the balloon tip is closed.
    /// </summary>
    public event EventHandler? BalloonTipClosed;

    /// <summary>
    /// Occurs when the balloon tip is shown.
    /// </summary>
    public event EventHandler? BalloonTipShown;

    /// <summary>
    /// Occurs when the icon is clicked.
    /// </summary>
    public event EventHandler? Click;

    /// <summary>
    /// Occurs when the icon is double-clicked.
    /// </summary>
    public event EventHandler? DoubleClick;

    /// <summary>
    /// Occurs when the mouse button is pressed while the pointer is over the icon.
    /// </summary>
    public new event MouseButtonEventHandler? MouseDown;

    /// <summary>
    /// Occurs when the mouse button is released while the pointer is over the icon.
    /// </summary>
    public new event MouseButtonEventHandler? MouseUp;

    /// <summary>
    /// Occurs when the mouse pointer moves while it is over the icon.
    /// </summary>
    public new event MouseEventHandler? MouseMove;

    #endregion

    #region CLR Properties

    /// <summary>
    /// Gets or sets the ToolTip text displayed when the mouse pointer is over the icon.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public string Text
    {
        get => (string)(GetValue(TextProperty) ?? string.Empty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>
    /// Gets or sets the icon displayed in the system tray.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public ImageSource? Icon
    {
        get => (ImageSource?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the icon is visible in the system tray.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public bool Visible
    {
        get => (bool)GetValue(VisibleProperty)!;
        set => SetValue(VisibleProperty, value);
    }

    /// <summary>
    /// Gets or sets the icon for the balloon tip.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public BalloonTipIcon BalloonTipIcon
    {
        get => (BalloonTipIcon)(GetValue(BalloonTipIconProperty) ?? BalloonTipIcon.None);
        set => SetValue(BalloonTipIconProperty, value);
    }

    /// <summary>
    /// Gets or sets the title of the balloon tip.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public string BalloonTipTitle
    {
        get => (string)(GetValue(BalloonTipTitleProperty) ?? string.Empty);
        set => SetValue(BalloonTipTitleProperty, value);
    }

    /// <summary>
    /// Gets or sets the text of the balloon tip.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public string BalloonTipText
    {
        get => (string)(GetValue(BalloonTipTextProperty) ?? string.Empty);
        set => SetValue(BalloonTipTextProperty, value);
    }

    /// <summary>
    /// Gets or sets the context menu for the icon.
    /// </summary>
    public ContextMenu? ContextMenu
    {
        get => _contextMenu;
        set => _contextMenu = value;
    }

    #endregion

    #region Methods

    /// <summary>
    /// Shows a balloon tip with the current BalloonTip properties.
    /// </summary>
    public void ShowBalloonTip(int timeout)
    {
        ShowBalloonTip(timeout, BalloonTipTitle, BalloonTipText, BalloonTipIcon);
    }

    /// <summary>
    /// Shows a balloon tip with the specified properties.
    /// </summary>
    /// <param name="timeout">The timeout in milliseconds.</param>
    /// <param name="title">The title of the balloon tip.</param>
    /// <param name="text">The text of the balloon tip.</param>
    /// <param name="icon">The icon for the balloon tip.</param>
    public void ShowBalloonTip(int timeout, string title, string text, BalloonTipIcon icon)
    {
        if (!_isVisible)
            return;

        ShowBalloonTipInternal(timeout, title, text, icon);
        OnBalloonTipShown();
    }

    #endregion

    #region Property Changed

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NotifyIcon notifyIcon && notifyIcon._isVisible)
        {
            notifyIcon.UpdateTooltipInternal((string)e.NewValue!);
        }
    }

    private static void OnIconChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NotifyIcon notifyIcon)
        {
            notifyIcon.UpdateIconInternal((ImageSource?)e.NewValue);
        }
    }

    private static void OnVisibleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is NotifyIcon notifyIcon)
        {
            var visible = (bool)e.NewValue!;
            if (visible)
            {
                notifyIcon.ShowInternal();
            }
            else
            {
                notifyIcon.HideInternal();
            }
            notifyIcon._isVisible = visible;
        }
    }

    #endregion

    #region Internal Methods (Platform Implementation Hooks)

    /// <summary>
    /// Shows the notify icon.
    /// </summary>
    protected void ShowInternal()
    {
        // Platform-specific implementation using Shell_NotifyIcon
    }

    /// <summary>
    /// Hides the notify icon.
    /// </summary>
    protected void HideInternal()
    {
        // Platform-specific implementation
    }

    /// <summary>
    /// Updates the icon.
    /// </summary>
    protected void UpdateIconInternal(ImageSource? icon)
    {
        // Platform-specific implementation
    }

    /// <summary>
    /// Updates the tooltip text.
    /// </summary>
    protected void UpdateTooltipInternal(string text)
    {
        // Platform-specific implementation
    }

    /// <summary>
    /// Shows a balloon tip.
    /// </summary>
    protected void ShowBalloonTipInternal(int timeout, string title, string text, BalloonTipIcon icon)
    {
        // Platform-specific implementation
    }

    #endregion

    #region Event Raising

    /// <summary>
    /// Raises the Click event.
    /// </summary>
    protected void OnClick()
    {
        Click?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Raises the DoubleClick event.
    /// </summary>
    protected void OnDoubleClick()
    {
        DoubleClick?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Raises the BalloonTipClicked event.
    /// </summary>
    protected void OnBalloonTipClicked()
    {
        BalloonTipClicked?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Raises the BalloonTipClosed event.
    /// </summary>
    protected void OnBalloonTipClosed()
    {
        BalloonTipClosed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Raises the BalloonTipShown event.
    /// </summary>
    protected void OnBalloonTipShown()
    {
        BalloonTipShown?.Invoke(this, EventArgs.Empty);
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// Releases all resources used by the NotifyIcon.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the unmanaged resources used by the NotifyIcon.
    /// </summary>
    protected void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            Visible = false;
        }

        _disposed = true;
    }

    /// <summary>
    /// Finalizer.
    /// </summary>
    ~NotifyIcon()
    {
        Dispose(false);
    }

    #endregion
}

/// <summary>
/// Specifies the icon displayed in a balloon tip.
/// </summary>
public enum BalloonTipIcon
{
    /// <summary>
    /// No icon.
    /// </summary>
    None,

    /// <summary>
    /// Information icon.
    /// </summary>
    Info,

    /// <summary>
    /// Warning icon.
    /// </summary>
    Warning,

    /// <summary>
    /// Error icon.
    /// </summary>
    Error
}

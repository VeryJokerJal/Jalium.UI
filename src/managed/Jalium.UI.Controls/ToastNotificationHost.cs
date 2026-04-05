using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Controls;

/// <summary>
/// A container panel that hosts and manages in-app toast notifications.
/// Supports positioning, stacking, and maximum visible toast count.
/// Place this control as an overlay in your window's root layout.
/// </summary>
public class ToastNotificationHost : Panel
{
    /// <inheritdoc />
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new Jalium.UI.Controls.Automation.ToastNotificationHostAutomationPeer(this);
    }

    #region Dependency Properties

    /// <summary>
    /// Identifies the Position dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty PositionProperty =
        DependencyProperty.Register(nameof(Position), typeof(ToastPosition), typeof(ToastNotificationHost),
            new PropertyMetadata(ToastPosition.TopRight, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the MaxVisibleToasts dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Behavior)]
    public static readonly DependencyProperty MaxVisibleToastsProperty =
        DependencyProperty.Register(nameof(MaxVisibleToasts), typeof(int), typeof(ToastNotificationHost),
            new PropertyMetadata(5));

    /// <summary>
    /// Identifies the Spacing dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty SpacingProperty =
        DependencyProperty.Register(nameof(Spacing), typeof(double), typeof(ToastNotificationHost),
            new PropertyMetadata(8.0, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the ToastWidth dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Layout)]
    public static readonly DependencyProperty ToastWidthProperty =
        DependencyProperty.Register(nameof(ToastWidth), typeof(double), typeof(ToastNotificationHost),
            new PropertyMetadata(400.0, OnLayoutPropertyChanged));

    /// <summary>
    /// Gets or sets the position of the toast stack within the host.
    /// </summary>
    public ToastPosition Position
    {
        get => (ToastPosition)GetValue(PositionProperty)!;
        set => SetValue(PositionProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum number of visible toasts.
    /// </summary>
    public int MaxVisibleToasts
    {
        get => (int)GetValue(MaxVisibleToastsProperty)!;
        set => SetValue(MaxVisibleToastsProperty, value);
    }

    /// <summary>
    /// Gets or sets the spacing between toasts.
    /// </summary>
    public double Spacing
    {
        get => (double)GetValue(SpacingProperty)!;
        set => SetValue(SpacingProperty, value);
    }

    /// <summary>
    /// Gets or sets the width of each toast notification.
    /// </summary>
    public double ToastWidth
    {
        get => (double)GetValue(ToastWidthProperty)!;
        set => SetValue(ToastWidthProperty, value);
    }

    #endregion

    #region Constructor

    public ToastNotificationHost()
    {
        IsHitTestVisible = true;
        ClipToBounds = true;
    }

    #endregion

    #region Show Methods

    /// <summary>
    /// Shows a toast notification with the specified severity, title, and message.
    /// </summary>
    /// <param name="severity">The severity level.</param>
    /// <param name="title">The title text.</param>
    /// <param name="message">The message text.</param>
    /// <param name="duration">Optional auto-dismiss duration. Defaults to 5 seconds.</param>
    /// <returns>The created toast notification item.</returns>
    public ToastNotificationItem Show(ToastSeverity severity, string title, string? message = null, TimeSpan? duration = null)
    {
        var toast = new ToastNotificationItem
        {
            Severity = severity,
            Title = title,
            Message = message,
            IsAutoDismissEnabled = true,
            Duration = duration ?? TimeSpan.FromSeconds(5)
        };

        ShowToast(toast);
        return toast;
    }

    /// <summary>
    /// Shows an informational toast notification.
    /// </summary>
    public ToastNotificationItem ShowInformation(string title, string? message = null, TimeSpan? duration = null)
        => Show(ToastSeverity.Information, title, message, duration);

    /// <summary>
    /// Shows a success toast notification.
    /// </summary>
    public ToastNotificationItem ShowSuccess(string title, string? message = null, TimeSpan? duration = null)
        => Show(ToastSeverity.Success, title, message, duration);

    /// <summary>
    /// Shows a warning toast notification.
    /// </summary>
    public ToastNotificationItem ShowWarning(string title, string? message = null, TimeSpan? duration = null)
        => Show(ToastSeverity.Warning, title, message, duration);

    /// <summary>
    /// Shows an error toast notification.
    /// </summary>
    public ToastNotificationItem ShowError(string title, string? message = null, TimeSpan? duration = null)
        => Show(ToastSeverity.Error, title, message, duration);

    /// <summary>
    /// Shows a pre-configured toast notification item.
    /// </summary>
    public void ShowToast(ToastNotificationItem toast)
    {
        // Wire up removal on close
        toast.Closed += OnToastClosed;

        // Enforce max visible count
        while (Children.Count >= MaxVisibleToasts)
        {
            var oldest = Children[0];
            if (oldest is ToastNotificationItem oldToast)
            {
                oldToast.Closed -= OnToastClosed;
                oldToast.IsOpen = false;
            }
            Children.RemoveAt(0);
        }

        Children.Add(toast);
        InvalidateMeasure();
        InvalidateVisual();
    }

    /// <summary>
    /// Dismisses all visible toasts.
    /// </summary>
    public void DismissAll()
    {
        for (int i = Children.Count - 1; i >= 0; i--)
        {
            if (Children[i] is ToastNotificationItem toast)
            {
                toast.Dismiss();
            }
        }
    }

    private void OnToastClosed(object sender, RoutedEventArgs e)
    {
        if (sender is ToastNotificationItem toast)
        {
            toast.Closed -= OnToastClosed;
            Children.Remove(toast);
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    #endregion

    #region Layout

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        var toastWidth = Math.Min(ToastWidth, availableSize.Width);
        var spacing = Spacing;
        var totalHeight = 0.0;

        foreach (UIElement child in Children)
        {
            child.Measure(new Size(toastWidth, double.PositiveInfinity));
            if (totalHeight > 0)
                totalHeight += spacing;
            totalHeight += child.DesiredSize.Height;
        }

        return availableSize;
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        var toastWidth = Math.Min(ToastWidth, finalSize.Width);
        var spacing = Spacing;
        var margin = 16.0;
        var position = Position;

        // Calculate X position
        double x = position switch
        {
            ToastPosition.TopLeft or ToastPosition.BottomLeft => margin,
            ToastPosition.TopCenter or ToastPosition.BottomCenter => (finalSize.Width - toastWidth) / 2,
            _ => finalSize.Width - toastWidth - margin // TopRight, BottomRight
        };

        bool isBottom = position is ToastPosition.BottomLeft or ToastPosition.BottomRight or ToastPosition.BottomCenter;

        if (isBottom)
        {
            // Stack from bottom up
            double y = finalSize.Height - margin;
            for (int i = Children.Count - 1; i >= 0; i--)
            {
                var child = Children[i];
                var desiredHeight = child.DesiredSize.Height;
                y -= desiredHeight;
                child.Arrange(new Rect(x, y, toastWidth, desiredHeight));
                y -= spacing;
            }
        }
        else
        {
            // Stack from top down
            double y = margin;
            foreach (UIElement child in Children)
            {
                var desiredHeight = child.DesiredSize.Height;
                child.Arrange(new Rect(x, y, toastWidth, desiredHeight));
                y += desiredHeight + spacing;
            }
        }

        return finalSize;
    }

    #endregion

    #region Property Changed

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ToastNotificationHost host)
        {
            host.InvalidateMeasure();
        }
    }

    #endregion
}

/// <summary>
/// Provides a global static API for showing in-app toast notifications.
/// Requires a <see cref="ToastNotificationHost"/> to be registered via <see cref="SetHost"/>.
/// </summary>
public static class ToastService
{
    private static ToastNotificationHost? s_host;

    /// <summary>
    /// Sets the global toast notification host.
    /// </summary>
    public static void SetHost(ToastNotificationHost host)
    {
        s_host = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <summary>
    /// Gets the current global toast notification host.
    /// </summary>
    public static ToastNotificationHost? Host => s_host;

    /// <summary>
    /// Shows an informational toast notification.
    /// </summary>
    public static ToastNotificationItem? ShowInformation(string title, string? message = null, TimeSpan? duration = null)
    {
        return s_host?.ShowInformation(title, message, duration);
    }

    /// <summary>
    /// Shows a success toast notification.
    /// </summary>
    public static ToastNotificationItem? ShowSuccess(string title, string? message = null, TimeSpan? duration = null)
    {
        return s_host?.ShowSuccess(title, message, duration);
    }

    /// <summary>
    /// Shows a warning toast notification.
    /// </summary>
    public static ToastNotificationItem? ShowWarning(string title, string? message = null, TimeSpan? duration = null)
    {
        return s_host?.ShowWarning(title, message, duration);
    }

    /// <summary>
    /// Shows an error toast notification.
    /// </summary>
    public static ToastNotificationItem? ShowError(string title, string? message = null, TimeSpan? duration = null)
    {
        return s_host?.ShowError(title, message, duration);
    }

    /// <summary>
    /// Shows a toast notification with the specified severity.
    /// </summary>
    public static ToastNotificationItem? Show(ToastSeverity severity, string title, string? message = null, TimeSpan? duration = null)
    {
        return s_host?.Show(severity, title, message, duration);
    }

    /// <summary>
    /// Dismisses all visible toast notifications.
    /// </summary>
    public static void DismissAll()
    {
        s_host?.DismissAll();
    }
}

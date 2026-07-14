using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Gallery;

/// <summary>
/// Gallery section for modal dialogs and notification surfaces. Follows the
/// per-category section pattern established by <c>GalleryWindow.Buttons.cs</c>.
///
/// Dialogs are inherently modal/transient, so they cannot be shown in a static
/// screenshot: <see cref="ContentDialog"/>, <see cref="MessageBox"/> and the file
/// dialogs are represented by trigger <see cref="Button"/>s (with NO click
/// handlers — nothing is ever shown). <see cref="ToastNotificationItem"/> renders
/// itself inline via OnRender, so a couple of real toasts are shown directly with
/// auto-dismiss disabled so they persist for the capture. <see cref="NotifyIcon"/>
/// lives in the OS system tray and has no in-window visual, so it uses a
/// <c>Placeholder</c>.
/// </summary>
internal static partial class GalleryWindow
{
    public static UIElement BuildDialogsSection() => Section(
        "Dialogs & Notifications",
        "Modal dialogs and toasts, shown via trigger buttons and inline previews.",
        Card("ContentDialog", new Button
        {
            Content = "Show ContentDialog",
            Padding = new Thickness(16, 8, 16, 8),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        }),
        Card("MessageBox", new Button
        {
            Content = "Show MessageBox",
            Padding = new Thickness(16, 8, 16, 8),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        }),
        Card("ToastNotificationItem", DialogsToastPreview(), width: 320),
        Card("OpenFileDialog / SaveFileDialog", new Button
        {
            Content = "Open file dialog",
            Padding = new Thickness(16, 8, 16, 8),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        }),
        Card("NotifyIcon", Placeholder("NotifyIcon", "system tray icon + balloon tips")));

    /// <summary>
    /// Two stacked in-app toasts (Success + Information) shown inline. Auto-dismiss
    /// is disabled so they remain visible in the static catalog screenshot; the
    /// toasts paint their own per-severity background, so they are not recolored.
    /// </summary>
    private static UIElement DialogsToastPreview()
    {
        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        stack.Children.Add(new ToastNotificationItem
        {
            Severity = ToastSeverity.Success,
            Title = "Saved",
            Message = "Your changes were saved successfully.",
            IsAutoDismissEnabled = false,
            Width = 280,
        });

        stack.Children.Add(new ToastNotificationItem
        {
            Severity = ToastSeverity.Information,
            Title = "Sync complete",
            Message = "12 items updated from the server.",
            IsAutoDismissEnabled = false,
            Width = 280,
        });

        return stack;
    }
}

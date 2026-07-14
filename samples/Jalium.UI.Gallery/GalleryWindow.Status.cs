using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Gallery;

/// <summary>
/// Gallery section for status / feedback controls: progress indicators, inline
/// notification banners and separators. Follows the per-category section pattern
/// established by <c>GalleryWindow.Buttons.cs</c>. All demos are static (no event
/// handlers, popups or timers are driven): each control is shown in a representative
/// resting state with sample content.
/// </summary>
internal static partial class GalleryWindow
{
    public static UIElement BuildStatusSection() => Section(
        "Status & Feedback",
        "Progress, inline notifications and separators.",
        Card("ProgressBar", BuildProgressBarDemo(), width: 300),
        Card("InfoBar", BuildInfoBarDemo(), width: 0),
        Card("Separator", BuildSeparatorDemo(), width: 300),
        Card("ToolTip", Placeholder("ToolTip", "hover tooltip")));

    // Two bars stacked: a ~65% determinate bar and an indeterminate bar.
    private static UIElement BuildProgressBarDemo()
    {
        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 16,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        stack.Children.Add(new TextBlock
        {
            Text = "Determinate — 65%",
            FontSize = 12,
            Foreground = TextSecondary,
        });
        stack.Children.Add(new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Value = 65,
            Width = 240,
            HorizontalAlignment = HorizontalAlignment.Left,
        });

        stack.Children.Add(new TextBlock
        {
            Text = "Indeterminate",
            FontSize = 12,
            Foreground = TextSecondary,
        });
        stack.Children.Add(new ProgressBar
        {
            IsIndeterminate = true,
            Width = 240,
            HorizontalAlignment = HorizontalAlignment.Left,
        });

        return stack;
    }

    // Three severities stacked, each a fixed-width banner with a title + message.
    private static UIElement BuildInfoBarDemo()
    {
        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 10,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        stack.Children.Add(new InfoBar
        {
            Severity = InfoBarSeverity.Informational,
            Title = "Heads up",
            Message = "A new version of the app is available.",
            IsOpen = true,
            Width = 360,
        });
        stack.Children.Add(new InfoBar
        {
            Severity = InfoBarSeverity.Success,
            Title = "Saved",
            Message = "Your changes were saved successfully.",
            IsOpen = true,
            Width = 360,
        });
        stack.Children.Add(new InfoBar
        {
            Severity = InfoBarSeverity.Warning,
            Title = "Low disk space",
            Message = "Less than 2 GB remaining on this drive.",
            IsOpen = true,
            Width = 360,
        });

        return stack;
    }

    // A horizontal separator dividing two short labels.
    private static UIElement BuildSeparatorDemo()
    {
        var stack = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Spacing = 10,
        };

        stack.Children.Add(new TextBlock
        {
            Text = "Recent files",
            FontSize = 13,
            Foreground = TextPrimary,
        });
        stack.Children.Add(new Separator());
        stack.Children.Add(new TextBlock
        {
            Text = "Shared with me",
            FontSize = 13,
            Foreground = TextPrimary,
        });

        return stack;
    }
}

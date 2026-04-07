using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>
/// A framework-rendered message box dialog for cross-platform use.
/// Uses Jalium.UI controls to render the dialog instead of native platform dialogs.
/// </summary>
internal sealed class MessageBoxDialog : Window
{
    private MessageBoxResult _result;

    internal MessageBoxResult Result => _result;

    internal MessageBoxDialog(
        string messageText,
        string caption,
        MessageBoxButton button,
        MessageBoxImage icon,
        MessageBoxResult defaultResult)
    {
        Title = caption ?? string.Empty;
        Width = 400;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;

        _result = defaultResult != MessageBoxResult.None ? defaultResult : MessageBoxResult.OK;

        Content = BuildContent(messageText, button, icon);
    }

    private UIElement BuildContent(string messageText, MessageBoxButton button, MessageBoxImage icon)
    {
        var rootPanel = new StackPanel { Margin = new Thickness(20) };

        // Icon + message row
        var messageRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 20)
        };

        // Icon text (using Unicode symbols)
        string iconChar = GetIconCharacter(icon);
        if (!string.IsNullOrEmpty(iconChar))
        {
            var iconText = new TextBlock
            {
                Text = iconChar,
                FontSize = 32,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 16, 0)
            };
            messageRow.Children.Add(iconText);
        }

        // Message text
        var textBlock = new TextBlock
        {
            Text = messageText ?? string.Empty,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 320
        };
        messageRow.Children.Add(textBlock);

        rootPanel.Children.Add(messageRow);

        // Buttons
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        foreach (var (label, result) in GetButtons(button))
        {
            var btn = new Button
            {
                Content = label,
                MinWidth = 80,
                Margin = new Thickness(4, 0, 0, 0),
                Padding = new Thickness(16, 6, 16, 6)
            };

            var capturedResult = result;
            btn.Click += (_, _) =>
            {
                _result = capturedResult;
                DialogResult = true;
                Close();
            };

            buttonPanel.Children.Add(btn);
        }

        rootPanel.Children.Add(buttonPanel);
        return rootPanel;
    }

    private static string GetIconCharacter(MessageBoxImage icon) => icon switch
    {
        MessageBoxImage.Hand => "\u26D4",      // Error/Stop
        MessageBoxImage.Question => "\u2753",  // Question
        MessageBoxImage.Exclamation => "\u26A0", // Warning
        MessageBoxImage.Asterisk => "\u2139",  // Information
        _ => string.Empty
    };

    private static (string Label, MessageBoxResult Result)[] GetButtons(MessageBoxButton button) => button switch
    {
        MessageBoxButton.OK => [("OK", MessageBoxResult.OK)],
        MessageBoxButton.OKCancel => [("OK", MessageBoxResult.OK), ("Cancel", MessageBoxResult.Cancel)],
        MessageBoxButton.YesNo => [("Yes", MessageBoxResult.Yes), ("No", MessageBoxResult.No)],
        MessageBoxButton.YesNoCancel => [("Yes", MessageBoxResult.Yes), ("No", MessageBoxResult.No), ("Cancel", MessageBoxResult.Cancel)],
        MessageBoxButton.AbortRetryIgnore => [("Abort", MessageBoxResult.Abort), ("Retry", MessageBoxResult.Retry), ("Ignore", MessageBoxResult.Ignore)],
        MessageBoxButton.RetryCancel => [("Retry", MessageBoxResult.Retry), ("Cancel", MessageBoxResult.Cancel)],
        MessageBoxButton.CancelTryContinue => [("Cancel", MessageBoxResult.Cancel), ("Try Again", MessageBoxResult.TryAgain), ("Continue", MessageBoxResult.Continue)],
        _ => [("OK", MessageBoxResult.OK)]
    };
}

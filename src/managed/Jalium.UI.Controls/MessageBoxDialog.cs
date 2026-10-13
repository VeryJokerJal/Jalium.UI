using Jalium.UI.Media;
using System.ComponentModel;

namespace Jalium.UI.Controls;

/// <summary>
/// A framework-rendered message box dialog for cross-platform use.
/// Uses Jalium.UI controls to render the dialog instead of native platform dialogs.
/// </summary>
internal sealed class MessageBoxDialog : Window
{
    private MessageBoxResult _result;
    private readonly bool _requiresSelection;
    private bool _hasSelection;

    internal MessageBoxResult Result => _result;

    internal MessageBoxDialog(
        string messageText,
        string caption,
        MessageBoxButton button,
        MessageBoxImage icon,
        MessageBoxResult defaultResult,
        MessageBoxOptions options = MessageBoxOptions.None)
    {
        Title = caption ?? string.Empty;
        Width = Math.Min(400, Math.Max(1, SystemParameters.WorkArea.Width - 40));
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        _requiresSelection = button == MessageBoxButton.YesNo;
        IsShowCloseButton = !_requiresSelection;

        // Dismissal is not an implicit click on the default. Yes/No requires an
        // explicit choice; No is only the fallback for accepted owner teardown.
        _result = button switch
        {
            MessageBoxButton.OKCancel or MessageBoxButton.YesNoCancel => MessageBoxResult.Cancel,
            MessageBoxButton.YesNo => MessageBoxResult.No,
            _ => MessageBoxResult.OK,
        };

        Content = BuildContent(messageText, button, icon, defaultResult, options);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (_requiresSelection && !_hasSelection &&
            OwnerForPlatformTermination?.IsCloseRequestedForPlatformTermination != true)
            e.Cancel = true;
    }

    private UIElement BuildContent(string messageText, MessageBoxButton button, MessageBoxImage icon,
        MessageBoxResult defaultResult, MessageBoxOptions options)
    {
        var rootPanel = new StackPanel { Margin = new Thickness(20) };

        // Icon + message row
        var messageRow = new Grid
        {
            Margin = new Thickness(0, 0, 0, 20)
        };
        messageRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        messageRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

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
            VerticalAlignment = VerticalAlignment.Top,
            TextAlignment = options.HasFlag(MessageBoxOptions.RightAlign) ? TextAlignment.Right : TextAlignment.Left,
            FlowDirection = options.HasFlag(MessageBoxOptions.RtlReading) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight
        };
        var messageScroll = new ScrollViewer
        {
            Content = textBlock,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = Math.Max(80, SystemParameters.WorkArea.Height * 0.6),
            Focusable = false
        };
        // Short messages should not add an empty Tab stop. Long messages still
        // expose the viewer for keyboard scrolling after layout is available.
        messageScroll.Loaded += (_, _) => messageScroll.Focusable = messageScroll.CanScrollVertically;
        Grid.SetColumn(messageScroll, 1);
        messageRow.Children.Add(messageScroll);

        rootPanel.Children.Add(messageRow);

        // Buttons
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var buttons = GetButtons(button);
        var defaultButton = buttons.Any(candidate => candidate.Result == defaultResult)
            ? defaultResult : buttons[0].Result;
        foreach (var (label, result) in buttons)
        {
            var btn = new Button
            {
                Content = label,
                IsDefault = result == defaultButton,
                IsCancel = result == MessageBoxResult.Cancel || buttons.Length == 1,
                MinWidth = 80,
                Margin = new Thickness(4, 0, 0, 0),
                Padding = new Thickness(16, 6, 16, 6)
            };

            var capturedResult = result;
            btn.Click += (_, _) =>
            {
                _hasSelection = true;
                _result = capturedResult;
                bool wasModal = IsModal;
                DialogResult = true;
                if (!wasModal) Close();
            };

            if (btn.IsDefault)
                Loaded += (_, _) => btn.Focus();

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
        _ => [("OK", MessageBoxResult.OK)]
    };
}

using System.ComponentModel;

namespace Jalium.UI.Controls;

/// <summary>A framework message box with a replaceable, theme-defined visual tree.</summary>
[TemplatePart(Name = "PART_MessageScrollViewer", Type = typeof(ScrollViewer))]
[TemplatePart(Name = "PART_OkButton", Type = typeof(Button))]
[TemplatePart(Name = "PART_CancelButton", Type = typeof(Button))]
[TemplatePart(Name = "PART_YesButton", Type = typeof(Button))]
[TemplatePart(Name = "PART_NoButton", Type = typeof(Button))]
[TemplateVisualState(Name = "OkOnly", GroupName = "ResponseStates")]
[TemplateVisualState(Name = "OkCancel", GroupName = "ResponseStates")]
[TemplateVisualState(Name = "YesNo", GroupName = "ResponseStates")]
[TemplateVisualState(Name = "YesNoCancel", GroupName = "ResponseStates")]
[TemplateVisualState(Name = "WithIcon", GroupName = "IconStates")]
[TemplateVisualState(Name = "WithoutIcon", GroupName = "IconStates")]
internal sealed class MessageBoxDialog : Window
{
    public static readonly DependencyProperty MessageTextProperty = DependencyProperty.Register(
        nameof(MessageText), typeof(string), typeof(MessageBoxDialog), new PropertyMetadata(string.Empty));
    public static readonly DependencyProperty ButtonsProperty = DependencyProperty.Register(
        nameof(Buttons), typeof(MessageBoxButton), typeof(MessageBoxDialog), new PropertyMetadata(MessageBoxButton.OK, OnConfigurationChanged));
    public static readonly DependencyProperty IconTextProperty = DependencyProperty.Register(
        nameof(IconText), typeof(string), typeof(MessageBoxDialog), new PropertyMetadata(string.Empty, OnConfigurationChanged));
    public static readonly DependencyProperty DefaultResponseProperty = DependencyProperty.Register(
        nameof(DefaultResponse), typeof(MessageBoxResult), typeof(MessageBoxDialog), new PropertyMetadata(MessageBoxResult.None, OnConfigurationChanged));
    public static readonly DependencyProperty MessageAlignmentProperty = DependencyProperty.Register(
        nameof(MessageAlignment), typeof(TextAlignment), typeof(MessageBoxDialog), new PropertyMetadata(TextAlignment.Left));
    public static readonly DependencyProperty MessageFlowDirectionProperty = DependencyProperty.Register(
        nameof(MessageFlowDirection), typeof(FlowDirection), typeof(MessageBoxDialog), new PropertyMetadata(FlowDirection.LeftToRight));
    public static readonly DependencyProperty MaxMessageHeightProperty = DependencyProperty.Register(
        nameof(MaxMessageHeight), typeof(double), typeof(MessageBoxDialog), new PropertyMetadata(600d));

    private MessageBoxResult _result;
    private bool _hasSelection;
    private ScrollViewer? _messageScroll;
    private readonly Dictionary<Button, MessageBoxResult> _responses = new();

    internal MessageBoxResult Result => _result;
    public string MessageText { get => (string)GetValue(MessageTextProperty)!; set => SetValue(MessageTextProperty, value); }
    public MessageBoxButton Buttons { get => (MessageBoxButton)GetValue(ButtonsProperty)!; set => SetValue(ButtonsProperty, value); }
    public string IconText { get => (string)GetValue(IconTextProperty)!; set => SetValue(IconTextProperty, value); }
    public MessageBoxResult DefaultResponse { get => (MessageBoxResult)GetValue(DefaultResponseProperty)!; set => SetValue(DefaultResponseProperty, value); }
    public TextAlignment MessageAlignment { get => (TextAlignment)GetValue(MessageAlignmentProperty)!; set => SetValue(MessageAlignmentProperty, value); }
    public FlowDirection MessageFlowDirection { get => (FlowDirection)GetValue(MessageFlowDirectionProperty)!; set => SetValue(MessageFlowDirectionProperty, value); }
    public double MaxMessageHeight { get => (double)GetValue(MaxMessageHeightProperty)!; set => SetValue(MaxMessageHeightProperty, value); }

    internal MessageBoxDialog(string messageText, string caption, MessageBoxButton button,
        MessageBoxImage icon, MessageBoxResult defaultResult, MessageBoxOptions options = MessageBoxOptions.None)
    {
        DefaultStyleKey = typeof(MessageBoxDialog);
        Title = caption ?? string.Empty;
        Width = Math.Min(400, Math.Max(1, SystemParameters.WorkArea.Width - 40));
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.NoResize;
        MessageText = messageText ?? string.Empty;
        Buttons = button;
        DefaultResponse = defaultResult;
        IconText = icon switch
        {
            MessageBoxImage.Hand => "⛔", MessageBoxImage.Question => "❓",
            MessageBoxImage.Exclamation => "⚠", MessageBoxImage.Asterisk => "ℹ", _ => string.Empty
        };
        MessageAlignment = options.HasFlag(MessageBoxOptions.RightAlign) ? TextAlignment.Right : TextAlignment.Left;
        MessageFlowDirection = options.HasFlag(MessageBoxOptions.RtlReading) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        MaxMessageHeight = Math.Max(80, SystemParameters.WorkArea.Height * .6);
        _result = button switch
        {
            MessageBoxButton.OKCancel or MessageBoxButton.YesNoCancel => MessageBoxResult.Cancel,
            MessageBoxButton.YesNo => MessageBoxResult.No, _ => MessageBoxResult.OK
        };
        Loaded += OnLoaded;
        UpdateDefaultStyle();
        ApplyTemplate();
        UpdateConfiguration();
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        AddResponse("PART_OkButton", MessageBoxResult.OK);
        AddResponse("PART_YesButton", MessageBoxResult.Yes);
        AddResponse("PART_NoButton", MessageBoxResult.No);
        AddResponse("PART_CancelButton", MessageBoxResult.Cancel);
        _messageScroll = GetTemplateChild("PART_MessageScrollViewer") as ScrollViewer;
        if (_messageScroll != null) _messageScroll.ScrollChanged += OnScrollChanged;
        UpdateConfiguration();
    }

    protected override void OnTemplateChanging()
    {
        foreach (var response in _responses.Keys) response.Click -= OnResponse;
        _responses.Clear();
        if (_messageScroll != null) _messageScroll.ScrollChanged -= OnScrollChanged;
        _messageScroll = null;
        base.OnTemplateChanging();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        // Dismissal is not an implicit click. Yes/No requires an explicit choice,
        // unless the owner is already ending the modal session.
        if (Buttons == MessageBoxButton.YesNo && !_hasSelection &&
            OwnerForPlatformTermination?.IsCloseRequestedForPlatformTermination != true) e.Cancel = true;
    }

    private void AddResponse(string name, MessageBoxResult result)
    {
        if (GetTemplateChild(name) is not Button button) return;
        _responses[button] = result;
        button.Click += OnResponse;
    }
    private static void OnConfigurationChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var dialog = (MessageBoxDialog)d;
        if (dialog.TemplateRootInternal != null) dialog.UpdateConfiguration();
    }
    private bool Includes(MessageBoxResult result) => result switch
    {
        MessageBoxResult.OK => Buttons is MessageBoxButton.OK or MessageBoxButton.OKCancel,
        MessageBoxResult.Yes or MessageBoxResult.No => Buttons is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel,
        MessageBoxResult.Cancel => Buttons is MessageBoxButton.OKCancel or MessageBoxButton.YesNoCancel,
        _ => false
    };
    private void UpdateConfiguration()
    {
        SetCurrentValue(IsShowCloseButtonProperty, Buttons != MessageBoxButton.YesNo);
        var first = Buttons is MessageBoxButton.YesNo or MessageBoxButton.YesNoCancel ? MessageBoxResult.Yes : MessageBoxResult.OK;
        var defaultResponse = Includes(DefaultResponse) ? DefaultResponse : first;
        foreach (var (button, result) in _responses)
        {
            button.SetCurrentValue(Button.IsDefaultProperty, result == defaultResponse && Includes(result));
            button.SetCurrentValue(Button.IsCancelProperty, result == MessageBoxResult.Cancel || Buttons == MessageBoxButton.OK && result == MessageBoxResult.OK);
        }
        if (TemplateRootInternal == null) return;
        VisualStateManager.GoToState(this, Buttons switch
        {
            MessageBoxButton.OKCancel => "OkCancel", MessageBoxButton.YesNo => "YesNo",
            MessageBoxButton.YesNoCancel => "YesNoCancel", _ => "OkOnly"
        }, false);
        VisualStateManager.GoToState(this, string.IsNullOrEmpty(IconText) ? "WithoutIcon" : "WithIcon", false);
    }
    private void OnResponse(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || !_responses.TryGetValue(button, out var result) || !Includes(result)) return;
        _hasSelection = true;
        _result = result;
        bool wasModal = IsModal;
        DialogResult = true;
        if (!wasModal) Close();
    }
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateScrollFocus();
        _responses.Keys.FirstOrDefault(button => button.IsDefault)?.Focus();
    }
    private void OnScrollChanged(object sender, ScrollChangedEventArgs e) => UpdateScrollFocus();
    private void UpdateScrollFocus()
    {
        if (_messageScroll != null) _messageScroll.SetCurrentValue(FocusableProperty, _messageScroll.CanScrollVertically);
    }
}

using Jalium.UI.Controls;
using Jalium.UI.AndroidDemo.Themes;

namespace Jalium.UI.AndroidDemo;

/// <summary>Portable keyboard form; native input hints and its default layout live in the theme.</summary>
[TemplatePart(Name = "PART_Password", Type = typeof(PasswordBox))]
[TemplatePart(Name = "PART_Submit", Type = typeof(Button))]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
public class KeyboardDemoSurface : Control
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(KeyboardDemoSurface), new PropertyMetadata("系统键盘适配演示"));
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(nameof(Description), typeof(string), typeof(KeyboardDemoSurface), new PropertyMetadata("点击任意输入框弹出对应的系统键盘；回车在字段间跳转。"));
    public static readonly DependencyProperty SubmitTextProperty = DependencyProperty.Register(nameof(SubmitText), typeof(string), typeof(KeyboardDemoSurface), new PropertyMetadata("提交"));
    public static readonly DependencyProperty NormalTextProperty = DependencyProperty.Register(nameof(NormalText), typeof(string), typeof(KeyboardDemoSurface), new PropertyMetadata(""));
    public static readonly DependencyProperty MultilineTextProperty = DependencyProperty.Register(nameof(MultilineText), typeof(string), typeof(KeyboardDemoSurface), new PropertyMetadata(""));
    public static readonly DependencyProperty DigitsTextProperty = DependencyProperty.Register(nameof(DigitsText), typeof(string), typeof(KeyboardDemoSurface), new PropertyMetadata(""));
    public static readonly DependencyProperty AmountTextProperty = DependencyProperty.Register(nameof(AmountText), typeof(string), typeof(KeyboardDemoSurface), new PropertyMetadata(""));
    public static readonly DependencyProperty TelephoneTextProperty = DependencyProperty.Register(nameof(TelephoneText), typeof(string), typeof(KeyboardDemoSurface), new PropertyMetadata(""));
    public static readonly DependencyProperty EmailTextProperty = DependencyProperty.Register(nameof(EmailText), typeof(string), typeof(KeyboardDemoSurface), new PropertyMetadata(""));
    public static readonly DependencyProperty SearchTextProperty = DependencyProperty.Register(nameof(SearchText), typeof(string), typeof(KeyboardDemoSurface), new PropertyMetadata(""));
    public static readonly DependencyProperty NameTextProperty = DependencyProperty.Register(nameof(NameText), typeof(string), typeof(KeyboardDemoSurface), new PropertyMetadata(""));
    public static readonly DependencyProperty NoteTextProperty = DependencyProperty.Register(nameof(NoteText), typeof(string), typeof(KeyboardDemoSurface), new PropertyMetadata(""));
    public string Title { get => (string)GetValue(TitleProperty)!; set => SetValue(TitleProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty)!; set => SetValue(DescriptionProperty, value); }
    public string SubmitText { get => (string)GetValue(SubmitTextProperty)!; set => SetValue(SubmitTextProperty, value); }
    public string NormalText { get => (string)GetValue(NormalTextProperty)!; set => SetValue(NormalTextProperty, value); }
    public string MultilineText { get => (string)GetValue(MultilineTextProperty)!; set => SetValue(MultilineTextProperty, value); }
    public string DigitsText { get => (string)GetValue(DigitsTextProperty)!; set => SetValue(DigitsTextProperty, value); }
    public string AmountText { get => (string)GetValue(AmountTextProperty)!; set => SetValue(AmountTextProperty, value); }
    public string TelephoneText { get => (string)GetValue(TelephoneTextProperty)!; set => SetValue(TelephoneTextProperty, value); }
    public string EmailText { get => (string)GetValue(EmailTextProperty)!; set => SetValue(EmailTextProperty, value); }
    public string SearchText { get => (string)GetValue(SearchTextProperty)!; set => SetValue(SearchTextProperty, value); }
    public string NameText { get => (string)GetValue(NameTextProperty)!; set => SetValue(NameTextProperty, value); }
    public string NoteText { get => (string)GetValue(NoteTextProperty)!; set => SetValue(NoteTextProperty, value); }
    public static readonly RoutedEvent SubmittedEvent = EventManager.RegisterRoutedEvent(nameof(Submitted), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(KeyboardDemoSurface));
    public event RoutedEventHandler Submitted { add => AddHandler(SubmittedEvent, value); remove => RemoveHandler(SubmittedEvent, value); }
    private PasswordBox? _passwordBox;
    private Button? _submit;
    private string _password = string.Empty;
    // Keep the native PasswordBox contract: passwords are not dependency properties.
    public string Password
    {
        get => _passwordBox?.Password ?? _password;
        set { _password = value ?? string.Empty; if (_passwordBox is not null) _passwordBox.Password = _password; }
    }
    public KeyboardDemoSurface() { KeyboardDemoTheme.Register(); DefaultStyleKey = typeof(KeyboardDemoSurface); }
    public override void OnApplyTemplate()
    {
        ReleaseParts(); base.OnApplyTemplate();
        _passwordBox = GetTemplateChild("PART_Password") as PasswordBox;
        if (_passwordBox is not null) _passwordBox.Password = _password;
        _submit = GetTemplateChild("PART_Submit") as Button;
        if (_submit is not null) _submit.Click += OnSubmit;
        UpdateStates();
    }
    protected override void OnTemplateChanging() { ReleaseParts(); base.OnTemplateChanging(); }
    protected override void OnIsEnabledChanged(bool oldValue, bool newValue) { base.OnIsEnabledChanged(oldValue, newValue); UpdateStates(); }
    private void OnSubmit(object sender, RoutedEventArgs e) { if (IsEnabled) RaiseEvent(new RoutedEventArgs(SubmittedEvent, this)); }
    private void ReleaseParts()
    {
        if (_passwordBox is not null) { _password = _passwordBox.Password; _passwordBox.Password = string.Empty; _passwordBox = null; }
        if (_submit is not null) { _submit.Click -= OnSubmit; _submit = null; }
    }
    private void UpdateStates() => VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
}

/// <summary>A reusable label/content pair for the keyboard form.</summary>
public class KeyboardDemoField : HeaderedContentControl
{
    public KeyboardDemoField() { KeyboardDemoTheme.Register(); DefaultStyleKey = typeof(KeyboardDemoField); }
}

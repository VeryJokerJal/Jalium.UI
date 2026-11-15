using Jalium.UI.Controls;
using Jalium.UI.AotWindowDemo.Themes;

namespace Jalium.UI.AotWindowDemo;

/// <summary>A reusable counter surface whose default tree is compiled from the AOT demo theme.</summary>
[TemplatePart(Name = "PART_IncrementButton", Type = typeof(Button))]
[TemplatePart(Name = "PART_StatusText", Type = typeof(TextBlock))]
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Idle", GroupName = "CountStates")]
[TemplateVisualState(Name = "Active", GroupName = "CountStates")]
public class AotCounterSurface : Control
{
    public static readonly DependencyProperty TitleProperty = DependencyProperty.Register(nameof(Title), typeof(string), typeof(AotCounterSurface), new PropertyMetadata("Jalium.UI"));
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(nameof(Description), typeof(string), typeof(AotCounterSurface), new PropertyMetadata("Native code linked statically. No DLLs alongside the exe."));
    public static readonly DependencyProperty HintProperty = DependencyProperty.Register(nameof(Hint), typeof(string), typeof(AotCounterSurface), new PropertyMetadata("Click the button to confirm the message loop is alive."));
    public static readonly DependencyProperty ButtonTextProperty = DependencyProperty.Register(nameof(ButtonText), typeof(string), typeof(AotCounterSurface), new PropertyMetadata("Click me"));
    private static readonly DependencyPropertyKey ClickCountPropertyKey = DependencyProperty.RegisterReadOnly(nameof(ClickCount), typeof(int), typeof(AotCounterSurface), new PropertyMetadata(0, OnCountChanged));
    public static readonly DependencyProperty ClickCountProperty = ClickCountPropertyKey.DependencyProperty;
    private static readonly DependencyPropertyKey StatusTextPropertyKey = DependencyProperty.RegisterReadOnly(nameof(StatusText), typeof(string), typeof(AotCounterSurface), new PropertyMetadata("(idle)"));
    public static readonly DependencyProperty StatusTextProperty = StatusTextPropertyKey.DependencyProperty;
    public static readonly RoutedEvent CountChangedEvent = EventManager.RegisterRoutedEvent(nameof(CountChanged), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(AotCounterSurface));

    public string Title { get => (string)GetValue(TitleProperty)!; set => SetValue(TitleProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty)!; set => SetValue(DescriptionProperty, value); }
    public string Hint { get => (string)GetValue(HintProperty)!; set => SetValue(HintProperty, value); }
    public string ButtonText { get => (string)GetValue(ButtonTextProperty)!; set => SetValue(ButtonTextProperty, value); }
    public int ClickCount => (int)GetValue(ClickCountProperty)!;
    public string StatusText => (string)GetValue(StatusTextProperty)!;
    public event RoutedEventHandler CountChanged { add => AddHandler(CountChangedEvent, value); remove => RemoveHandler(CountChangedEvent, value); }

    private Button? _incrementButton;

    public AotCounterSurface()
    {
        AotDemoTheme.Register(); DefaultStyleKey = typeof(AotCounterSurface);
    }

    public override void OnApplyTemplate()
    {
        ReleasePart(); base.OnApplyTemplate();
        _incrementButton = GetTemplateChild("PART_IncrementButton") as Button;
        if (_incrementButton is not null) _incrementButton.Click += OnIncrementClick;
        UpdateStates();
    }

    protected override void OnTemplateChanging() { ReleasePart(); base.OnTemplateChanging(); }
    protected override void OnIsEnabledChanged(bool oldValue, bool newValue) { base.OnIsEnabledChanged(oldValue, newValue); UpdateStates(); }
    public void Increment() => SetValue(ClickCountPropertyKey, checked(ClickCount + 1));
    public void Reset() => SetValue(ClickCountPropertyKey, 0);
    private void OnIncrementClick(object sender, RoutedEventArgs e) { if (IsEnabled) Increment(); }
    private void ReleasePart() { if (_incrementButton is not null) _incrementButton.Click -= OnIncrementClick; _incrementButton = null; }

    private static void OnCountChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var surface = (AotCounterSurface)d;
        surface.SetValue(StatusTextPropertyKey, surface.ClickCount switch { 0 => "(idle)", 1 => "1 click — UI thread is alive.", _ => $"{surface.ClickCount} clicks" });
        surface.UpdateStates(); surface.RaiseEvent(new RoutedEventArgs(CountChangedEvent, surface));
    }
    private void UpdateStates()
    {
        VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
        VisualStateManager.GoToState(this, ClickCount == 0 ? "Idle" : "Active", false);
    }
}

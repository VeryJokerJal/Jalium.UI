using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;

namespace Jalium.UI.Gallery;

internal static partial class GalleryWindow
{
    public static UIElement BuildTextInputSection() => SectionFromCards(
        "Text Input", "Single/multi-line, masked, numeric, auto-complete and combo entry.", CreateTextInputCards());
    private static DeferredCard[] CreateTextInputCards() =>
    [
        new("Gallery.Card.TextInput.TextBox", "TextBox", static () => new GallerySampleTextBoxes()),
        new("Gallery.Card.TextInput.PasswordBox", "PasswordBox", static () => new GallerySamplePasswordDemo()),
        new("Gallery.Card.TextInput.NumberBox", "NumberBox", static () => new GallerySampleNumberDemo()),
        new("Gallery.Card.TextInput.AutoCompleteBox", "AutoCompleteBox", static () => new GallerySampleAutoCompleteDemo()),
        new("Gallery.Card.TextInput.ComboBox", "ComboBox", static () => new GallerySampleComboDemo()),
    ];
}

/// <summary>Base for independently replaceable deferred examples.</summary>
[TemplateVisualState(Name = "Normal", GroupName = "CommonStates")]
[TemplateVisualState(Name = "Disabled", GroupName = "CommonStates")]
public abstract class GallerySampleDemo : Control
{
    protected GallerySampleDemo() => GallerySampleTheme.EnsureRegistered();
    public override void OnApplyTemplate() { base.OnApplyTemplate(); UpdateState(); }
    protected override void OnIsEnabledChanged(bool oldValue, bool newValue) { base.OnIsEnabledChanged(oldValue, newValue); UpdateState(); }
    private void UpdateState() => VisualStateManager.GoToState(this, IsEnabled ? "Normal" : "Disabled", false);
}

[TemplatePart(Name = "PART_SingleLine", Type = typeof(TextBox))]
[TemplatePart(Name = "PART_MultiLine", Type = typeof(TextBox))]
public class GallerySampleTextBoxes : GallerySampleDemo
{
    public static readonly DependencyProperty SingleLineTextProperty = DependencyProperty.Register(nameof(SingleLineText), typeof(string), typeof(GallerySampleTextBoxes), new PropertyMetadata("Single line entry"));
    public string SingleLineText { get => (string)GetValue(SingleLineTextProperty)!; set => SetValue(SingleLineTextProperty, value); }
    public static readonly DependencyProperty MultiLineTextProperty = DependencyProperty.Register(nameof(MultiLineText), typeof(string), typeof(GallerySampleTextBoxes), new PropertyMetadata("Multi-line text box.\nIt accepts Enter for new lines\nand wraps long paragraphs onto the next visual row."));
    public string MultiLineText { get => (string)GetValue(MultiLineTextProperty)!; set => SetValue(MultiLineTextProperty, value); }

    public GallerySampleTextBoxes() => DefaultStyleKey = typeof(GallerySampleTextBoxes);
}
[TemplatePart(Name = "PART_Password", Type = typeof(PasswordBox))]
public class GallerySamplePasswordDemo : GallerySampleDemo
{
    private string _password = "S3cr3t!";
    private PasswordBox? _passwordBox;
    // Match PasswordBox: password values deliberately stay outside the property system.
    public string Password
    {
        get => _passwordBox?.Password ?? _password;
        set { _password = value ?? string.Empty; if (_passwordBox is not null) _passwordBox.Password = _password; }
    }
    public GallerySamplePasswordDemo() => DefaultStyleKey = typeof(GallerySamplePasswordDemo);
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate(); _passwordBox = GetTemplateChild("PART_Password") as PasswordBox;
        if (_passwordBox is not null) _passwordBox.Password = _password;
    }
    protected override void OnTemplateChanging()
    {
        if (_passwordBox is not null) { _password = _passwordBox.Password; _passwordBox.Password = string.Empty; }
        _passwordBox = null; base.OnTemplateChanging();
    }
}
[TemplatePart(Name = "PART_Number", Type = typeof(NumberBox))]
public class GallerySampleNumberDemo : GallerySampleDemo
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(nameof(Value), typeof(double), typeof(GallerySampleNumberDemo), new PropertyMetadata(42d));
    public double Value { get => (double)GetValue(ValueProperty)!; set => SetValue(ValueProperty, value); }

    public GallerySampleNumberDemo() => DefaultStyleKey = typeof(GallerySampleNumberDemo);
}
[TemplatePart(Name = "PART_AutoComplete", Type = typeof(AutoCompleteBox))]
public class GallerySampleAutoCompleteDemo : GallerySampleDemo
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(GallerySampleAutoCompleteDemo), new PropertyMetadata("Cascadia"));
    public string Text { get => (string)GetValue(TextProperty)!; set => SetValue(TextProperty, value); }

    public GallerySampleAutoCompleteDemo() => DefaultStyleKey = typeof(GallerySampleAutoCompleteDemo);
}
[TemplatePart(Name = "PART_Combo", Type = typeof(ComboBox))]
public class GallerySampleComboDemo : GallerySampleDemo
{
    public static readonly DependencyProperty SelectedIndexProperty = DependencyProperty.Register(nameof(SelectedIndex), typeof(int), typeof(GallerySampleComboDemo), new PropertyMetadata(1));
    public int SelectedIndex { get => (int)GetValue(SelectedIndexProperty)!; set => SetValue(SelectedIndexProperty, value); }

    public static readonly DependencyProperty ChoicesProperty = DependencyProperty.Register(nameof(Choices), typeof(System.Collections.IEnumerable), typeof(GallerySampleComboDemo), new PropertyMetadata(null));
    public System.Collections.IEnumerable? Choices { get => (System.Collections.IEnumerable?)GetValue(ChoicesProperty); set => SetValue(ChoicesProperty, value); }
    public GallerySampleComboDemo()
    {
        DefaultStyleKey = typeof(GallerySampleComboDemo);
        SetCurrentValue(ChoicesProperty, new[] { "Apple", "Banana", "Cherry", "Dragon fruit" });
    }
}

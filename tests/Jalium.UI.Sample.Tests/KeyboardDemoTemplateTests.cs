using Jalium.UI.AndroidDemo;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Input.TextInput;

namespace Jalium.UI.Sample.Tests;

public sealed class KeyboardDemoTemplateTests
{
    private static readonly Lazy<ResourceDictionary> Defaults = new(() =>
    {
        Jalium.UI.Markup.ThemeLoader.Initialize(); using var stream = new MemoryStream();
        return ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
    });
    [Fact]
    public void NativeInputHintsAndTwoWayValuesSurviveTheCompiledTemplate()
    {
        var control = Create(); control.EmailText = "hello@example.com"; Layout(control);
        foreach (var (name, type, key) in new[]
        {
            ("Normal", TextInputContentType.Normal, TextInputReturnKeyType.Next),
            ("Multiline", TextInputContentType.Normal, TextInputReturnKeyType.Default),
            ("Digits", TextInputContentType.Digits, TextInputReturnKeyType.Next),
            ("Amount", TextInputContentType.Number, TextInputReturnKeyType.Next),
            ("Telephone", TextInputContentType.Telephone, TextInputReturnKeyType.Next),
            ("Email", TextInputContentType.Email, TextInputReturnKeyType.Next),
            ("Search", TextInputContentType.Search, TextInputReturnKeyType.Search),
            ("Name", TextInputContentType.Name, TextInputReturnKeyType.Next),
            ("Note", TextInputContentType.Normal, TextInputReturnKeyType.Done)
        })
        {
            var field = Assert.IsType<TextBox>(control.FindName("PART_" + name));
            Assert.Equal(type, TextInputOptions.GetContentType(field)); Assert.Equal(key, TextInputOptions.GetReturnKeyType(field));
            Assert.True(field.RenderSize.Width <= 272);
            Assert.False(string.IsNullOrWhiteSpace(Jalium.UI.Automation.AutomationProperties.GetName(field)));
        }
        Assert.True(TextInputOptions.GetMultiline(Assert.IsType<TextBox>(control.FindName("PART_Multiline"))));
        var email = Assert.IsType<TextBox>(control.FindName("PART_Email")); Assert.Equal("hello@example.com", email.Text);
        email.Text = "updated@example.com"; Assert.Equal("updated@example.com", control.EmailText);
        Assert.True(Assert.IsType<TextBox>(control.FindName("PART_ReadOnly")).IsReadOnly);
        Assert.False(Assert.IsType<TextBox>(control.FindName("PART_Disabled")).IsEnabled);
    }
    [Fact]
    public void ReplacementPreservesPasswordPrivatelyAndDisconnectsOldSubmit()
    {
        var control = Create(); control.Password = "private"; Layout(control);
        var password = Assert.IsType<PasswordBox>(control.FindName("PART_Password")); Assert.Equal("private", password.Password);
        password.Password = "updated";
        var old = Assert.IsType<Button>(control.FindName("PART_Submit")); var submissions = 0; control.Submitted += (_, _) => submissions++;
        old.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, old)); Assert.Equal(1, submissions);
        var blank = new ControlTemplate(typeof(KeyboardDemoSurface)); blank.SetVisualTree(() => new Border());
        control.Template = blank; Layout(control); Assert.Equal(string.Empty, password.Password);
        Assert.Equal("updated", control.Password); old.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, old)); Assert.Equal(1, submissions);
        Assert.Null(DependencyProperty.FromName(typeof(KeyboardDemoSurface), "Password"));
        control.ClearValue(Control.TemplateProperty); Layout(control);
        Assert.Equal("updated", Assert.IsType<PasswordBox>(control.FindName("PART_Password")).Password);
        var submit = Assert.IsType<Button>(control.FindName("PART_Submit")); submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, submit)); Assert.Equal(2, submissions);
        control.IsEnabled = false; submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, submit)); Assert.Equal(2, submissions);
    }
    private static KeyboardDemoSurface Create()
    {
        var control = new KeyboardDemoSurface(); control.Resources.MergedDictionaries.Add(Defaults.Value); return control;
    }
    private static void Layout(FrameworkElement element)
    {
        element.InvalidateMeasure(); element.Measure(new Size(320, 640)); element.Arrange(new Rect(0, 0, 320, 640)); element.UpdateLayout();
    }
}

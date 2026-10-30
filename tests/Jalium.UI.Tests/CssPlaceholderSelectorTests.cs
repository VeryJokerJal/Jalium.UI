using Jalium.UI.Controls;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public sealed class CssPlaceholderSelectorTests
{
    static CssPlaceholderSelectorTests() => System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(
        typeof(Jalium.UI.Markup.TypeConverterRegistry).Module.ModuleHandle);

    [Theory]
    [InlineData(":placeholder-shown", true)]
    [InlineData(":PLACEHOLDER-SHOWN", true)]
    [InlineData(":placeholder-shown()", false)]
    public void SelectorSupport_UsesSimplePseudoClass(string selector, bool expected)
        => Assert.Equal(expected, Css.Supports($"selector({selector})"));

    [Fact]
    public void PlaceholderState_FollowsTheHostControlsDisplayConditions()
    {
        var selector = Assert.Single(CssSelectorParser.ParseGroup(":placeholder-shown", out _)!);
        Assert.False(CssMatcher.Matches(new Border(), selector, evaluateStates: true));
        Assert.False(CssMatcher.Matches(new TextBox(), selector, evaluateStates: true));
        Assert.True(CssMatcher.Matches(new TextBox { PlaceholderText = "Hint" }, selector, evaluateStates: true));
        Assert.True(CssMatcher.Matches(new PasswordBox { PlaceholderText = "Hint" }, selector, evaluateStates: true));
        Assert.True(CssMatcher.Matches(new AutoCompleteBox { PlaceholderText = "Hint" }, selector, evaluateStates: true));
        Assert.True(CssMatcher.Matches(new NumberBox { PlaceholderText = "Hint", Text = "" }, selector, evaluateStates: true));
        Assert.True(CssMatcher.Matches(new DatePicker(), selector, evaluateStates: true));
        Assert.True(CssMatcher.Matches(new TimePicker(), selector, evaluateStates: true));
        Assert.True(CssMatcher.Matches(new ComboBox(), selector, evaluateStates: true));
        Assert.True(CssMatcher.Matches(new TreeSelector { PlaceholderText = "Hint" }, selector, evaluateStates: true));
        Assert.False(CssMatcher.Matches(new DatePicker { PlaceholderText = "" }, selector, evaluateStates: true));
        Assert.False(CssMatcher.Matches(new TimePicker { PlaceholderText = "" }, selector, evaluateStates: true));
    }

    [Fact]
    public void PlaceholderState_UpdatesWhenDisplayedContentChanges()
    {
        var text = new TextBox { PlaceholderText = "Hint" };
        var password = new PasswordBox { PlaceholderText = "Hint" };
        var auto = new AutoCompleteBox { PlaceholderText = "Hint" };
        var number = new NumberBox { PlaceholderText = "Hint", Text = "" };
        var date = new DatePicker();
        var time = new TimePicker();
        var combo = new ComboBox();
        var tree = new TreeSelector { PlaceholderText = "Hint" };
        var root = new StackPanel();
        FrameworkElement[] controls = [text, password, auto, number, date, time, combo, tree];
        foreach (var control in controls)
            root.Children.Add(control);
        Css.SetStyleSheet(root, ":placeholder-shown { opacity:.4 }");
        Flush(root);
        foreach (var control in controls)
            Assert.Equal(.4, control.Opacity);

        text.Text = "text";
        password.Password = "secret";
        auto.Text = "text";
        number.Text = "2";
        date.Text = "2026-09-26";
        time.SelectedTime = new TimeSpan(12, 30, 0);
        combo.IsEditable = true;
        combo.Text = "text";
        tree.SearchText = "text";
        Flush(root);
        foreach (var control in controls)
            Assert.Equal(1, control.Opacity);

        text.Text = "";
        password.Password = "";
        auto.Text = "";
        number.Text = "";
        date.Text = "";
        time.SelectedTime = null;
        combo.Text = "";
        tree.SearchText = "";
        Flush(root);
        foreach (var control in controls)
            Assert.Equal(.4, control.Opacity);

        text.PlaceholderText = "";
        Flush(root);
        Assert.Equal(1, text.Opacity);
        text.PlaceholderText = "Hint";
        Flush(root);
        Assert.Equal(.4, text.Opacity);
    }

    [Fact]
    public void PlaceholderState_UpdatesRelationalSelectorOnAnotherElement()
    {
        var text = new TextBox { PlaceholderText = "Hint" };
        var root = new StackPanel();
        root.Children.Add(text);
        Css.SetStyleSheet(root, "StackPanel:has(> TextBox:placeholder-shown) { opacity:.4 }");
        Flush(root);
        Assert.Equal(.4, root.Opacity);

        text.Text = "entered";
        Flush(root);
        Assert.Equal(1, root.Opacity);

        text.Text = "";
        Flush(root);
        Assert.Equal(.4, root.Opacity);
    }

    private static void Flush(FrameworkElement element) => CssEvaluationScheduler.FlushIfPending(element.Dispatcher);
}

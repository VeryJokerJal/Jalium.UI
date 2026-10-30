using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public sealed class CssValiditySelectorTests
{
    static CssValiditySelectorTests() => System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(
        typeof(Jalium.UI.Markup.TypeConverterRegistry).Module.ModuleHandle);

    [Theory]
    [InlineData(":valid", true)]
    [InlineData(":invalid", true)]
    [InlineData(":VALID", true)]
    [InlineData(":valid()", false)]
    [InlineData(":invalid()", false)]
    public void SelectorSupport_UsesSimplePseudoClasses(string selector, bool expected)
        => Assert.Equal(expected, Css.Supports($"selector({selector})"));

    [Fact]
    public void Validity_AppliesOnlyToValueControlsOrNativeValidationParticipants()
    {
        var valid = Assert.Single(CssSelectorParser.ParseGroup(":valid", out _)!);
        var invalid = Assert.Single(CssSelectorParser.ParseGroup(":invalid", out _)!);
        FrameworkElement[] inputs =
        [
            new TextBox(), new RichTextBox(), new AutoCompleteBox(), new NumberBox(),
            new PasswordBox(), new ComboBox(), new DatePicker(), new TimePicker(),
            new CheckBox(), new Slider(), new DataGridRow(), new DataGridCell(),
        ];
        foreach (var input in inputs)
        {
            Assert.True(CssMatcher.Matches(input, valid, evaluateStates: true));
            Assert.False(CssMatcher.Matches(input, invalid, evaluateStates: true));
        }

        FrameworkElement[] ordinary = [new Border(), new StackPanel(), new ProgressBar()];
        foreach (var element in ordinary)
        {
            Assert.False(CssMatcher.Matches(element, valid, evaluateStates: true));
            Assert.False(CssMatcher.Matches(element, invalid, evaluateStates: true));
        }

        var border = ordinary[0];
        Validation.MarkInvalid(border, new ValidationError("Invalid bound value"));
        Assert.False(CssMatcher.Matches(border, valid, evaluateStates: true));
        Assert.True(CssMatcher.Matches(border, invalid, evaluateStates: true));
        Validation.ClearInvalid(border);
        Assert.True(CssMatcher.Matches(border, valid, evaluateStates: true));
        Assert.False(CssMatcher.Matches(border, invalid, evaluateStates: true));
    }

    [Fact]
    public void NativeValidationChanges_UpdateInputAndExplicitValidationParticipant()
    {
        var text = new TextBox();
        var border = new Border();
        var root = new StackPanel();
        root.Children.Add(text);
        root.Children.Add(border);
        Css.SetStyleSheet(root, "TextBox:valid { opacity:.4 } TextBox:invalid { opacity:.8 } Border:valid { opacity:.5 } Border:invalid { opacity:.2 }");
        Flush(root);
        Assert.Equal(.4, text.Opacity);
        Assert.Equal(1, border.Opacity);

        Validation.MarkInvalid(text, new ValidationError("Required"));
        Validation.MarkInvalid(border, new ValidationError("Invalid bound value"));
        Flush(root);
        Assert.Equal(.8, text.Opacity);
        Assert.Equal(.2, border.Opacity);

        Validation.ClearInvalid(text);
        Validation.ClearInvalid(border);
        Flush(root);
        Assert.Equal(.4, text.Opacity);
        Assert.Equal(.5, border.Opacity);
    }

    [Fact]
    public void AncestorAndSiblingSelectors_TrackNativeValidationChanges()
    {
        var text = new TextBox();
        var sibling = new Border();
        var root = new StackPanel();
        root.Children.Add(text);
        root.Children.Add(sibling);
        Css.SetStyleSheet(root, "StackPanel:has(> TextBox:invalid) { opacity:.6 } TextBox:invalid + Border { opacity:.4 }");
        Flush(root);
        Assert.Equal(1, root.Opacity);
        Assert.Equal(1, sibling.Opacity);

        Validation.MarkInvalid(text, new ValidationError("Required"));
        Flush(root);
        Assert.Equal(.6, root.Opacity);
        Assert.Equal(.4, sibling.Opacity);

        Validation.ClearInvalid(text);
        Flush(root);
        Assert.Equal(1, root.Opacity);
        Assert.Equal(1, sibling.Opacity);
    }

    [Fact]
    public void BindingRuleErrors_ChangeValidityWithoutManualCssState()
    {
        var source = new InputModel { Value = "ready" };
        var text = new TextBox { DataContext = source };
        var root = new StackPanel();
        root.Children.Add(text);
        var binding = new Binding(nameof(InputModel.Value)) { Mode = BindingMode.TwoWay };
        binding.ValidationRules.Add(new RequiredValidationRule());
        BindingOperations.SetBinding(text, TextBox.TextProperty, binding);
        Css.SetStyleSheet(root, "TextBox:valid { opacity:.4 } TextBox:invalid { opacity:.8 }");
        Flush(root);
        Assert.Equal(.4, text.Opacity);

        text.SetCurrentValue(TextBox.TextProperty, "");
        text.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        Flush(root);
        Assert.True(Validation.GetHasError(text));
        Assert.Equal(.8, text.Opacity);

        text.SetCurrentValue(TextBox.TextProperty, "fixed");
        text.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
        Flush(root);
        Assert.False(Validation.GetHasError(text));
        Assert.Equal(.4, text.Opacity);
    }

    private sealed class InputModel
    {
        public string Value { get; set; } = "";
    }

    private static void Flush(FrameworkElement element) => CssEvaluationScheduler.FlushIfPending(element.Dispatcher);
}

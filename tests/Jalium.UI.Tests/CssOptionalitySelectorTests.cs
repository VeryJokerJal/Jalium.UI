using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public sealed class CssOptionalitySelectorTests
{
    static CssOptionalitySelectorTests() => System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(
        typeof(Jalium.UI.Markup.TypeConverterRegistry).Module.ModuleHandle);

    [Theory]
    [InlineData(":required", true)]
    [InlineData(":OPTIONAL", true)]
    [InlineData(":required()", false)]
    [InlineData(":optional()", false)]
    public void SelectorSupport_UsesSimplePseudoClasses(string selector, bool expected)
        => Assert.Equal(expected, Css.Supports($"selector({selector})"));

    [Fact]
    public void Optionality_UsesNativeRequiredMetadataOnlyOnValueControls()
    {
        var required = Assert.Single(CssSelectorParser.ParseGroup(":required", out _)!);
        var optional = Assert.Single(CssSelectorParser.ParseGroup(":optional", out _)!);
        FrameworkElement[] inputs =
        [
            new TextBox(), new RichTextBox(), new AutoCompleteBox(), new NumberBox(),
            new PasswordBox(), new ComboBox(), new DatePicker(), new TimePicker(),
            new CheckBox(), new Slider(), new DataGridCell(),
        ];
        foreach (var input in inputs)
        {
            Assert.False(CssMatcher.Matches(input, required, evaluateStates: true));
            Assert.True(CssMatcher.Matches(input, optional, evaluateStates: true));
            AutomationProperties.SetIsRequiredForForm(input, true);
            Assert.True(CssMatcher.Matches(input, required, evaluateStates: true));
            Assert.False(CssMatcher.Matches(input, optional, evaluateStates: true));
        }

        var border = new Border();
        AutomationProperties.SetIsRequiredForForm(border, true);
        Assert.False(CssMatcher.Matches(border, required, evaluateStates: true));
        Assert.False(CssMatcher.Matches(border, optional, evaluateStates: true));

        var gridRow = new DataGridRow();
        AutomationProperties.SetIsRequiredForForm(gridRow, true);
        Assert.False(CssMatcher.Matches(gridRow, required, evaluateStates: true));
        Assert.False(CssMatcher.Matches(gridRow, optional, evaluateStates: true));

        var disabled = new TextBox { IsEnabled = false };
        AutomationProperties.SetIsRequiredForForm(disabled, true);
        Assert.True(CssMatcher.Matches(disabled, required, evaluateStates: true));
    }

    [Fact]
    public void RequiredChanges_UpdateSelfSiblingAndHasSelectors()
    {
        var input = new TextBox();
        var sibling = new Border();
        var root = new StackPanel();
        root.Children.Add(input);
        root.Children.Add(sibling);
        Css.SetStyleSheet(root,
            "TextBox:optional { opacity:.4 } TextBox:required { opacity:.6 } " +
            "TextBox:required + Border { opacity:.7 } " +
            "StackPanel:has(> TextBox:required) { opacity:.8 }");
        Flush(root);
        Assert.Equal(.4, input.Opacity);
        Assert.Equal(1, sibling.Opacity);
        Assert.Equal(1, root.Opacity);

        AutomationProperties.SetIsRequiredForForm(input, true);
        Flush(root);
        Assert.Equal(.6, input.Opacity);
        Assert.Equal(.7, sibling.Opacity);
        Assert.Equal(.8, root.Opacity);

        AutomationProperties.SetIsRequiredForForm(input, false);
        Flush(root);
        Assert.Equal(.4, input.Opacity);
        Assert.Equal(1, sibling.Opacity);
        Assert.Equal(1, root.Opacity);
    }

    [Fact]
    public void RequiredValueBinding_UpdatesSelectorsWhenAttachedReplacedAndCleared()
    {
        var source = new InputModel { Value = "ready" };
        var input = new TextBox { Text = "ready" };
        var sibling = new Border();
        var root = new StackPanel();
        root.Children.Add(input);
        root.Children.Add(sibling);
        Css.SetStyleSheet(root,
            "TextBox:optional { opacity:.4 } TextBox:required { opacity:.6 } " +
            "TextBox:required + Border { opacity:.7 } " +
            "StackPanel:has(> TextBox:required) { opacity:.8 }");
        Flush(root);
        Assert.Equal(.4, input.Opacity);

        var required = new Binding(nameof(InputModel.Value)) { Source = source };
        required.ValidationRules.Add(new RequiredValidationRule());
        input.SetBinding(TextBox.TextProperty, required);
        Flush(root);
        Assert.Equal("ready", input.Text);
        Assert.Equal(.6, input.Opacity);
        Assert.Equal(.7, sibling.Opacity);
        Assert.Equal(.8, root.Opacity);

        input.SetBinding(TextBox.TextProperty, new Binding(nameof(InputModel.Value)) { Source = source });
        Flush(root);
        Assert.Equal(.4, input.Opacity);
        Assert.Equal(1, sibling.Opacity);
        Assert.Equal(1, root.Opacity);

        input.SetBinding(TextBox.TextProperty, required);
        Flush(root);
        Assert.Equal(.6, input.Opacity);
        input.ClearBinding(TextBox.TextProperty);
        Flush(root);
        Assert.Equal(.4, input.Opacity);

        input.SetBinding(TextBox.TextProperty, required);
        Flush(root);
        Assert.Equal(.6, input.Opacity);
        input.ClearAllBindings();
        Flush(root);
        Assert.Equal(.4, input.Opacity);
    }

    [Fact]
    public void UnrelatedBindingRulesDoNotMakeAnInputRequired()
    {
        var required = Assert.Single(CssSelectorParser.ParseGroup(":required", out _)!);
        var optional = Assert.Single(CssSelectorParser.ParseGroup(":optional", out _)!);
        var source = new InputModel { Width = 50 };
        var input = new TextBox();
        var width = new Binding(nameof(InputModel.Width)) { Source = source };
        width.ValidationRules.Add(new RequiredValidationRule());
        input.SetBinding(FrameworkElement.WidthProperty, width);
        Assert.False(CssMatcher.Matches(input, required, evaluateStates: true));
        Assert.True(CssMatcher.Matches(input, optional, evaluateStates: true));

        var value = new Binding(nameof(InputModel.Value)) { Source = source };
        value.ValidationRules.Add(new RequiredValidationRule());
        input.SetBinding(TextBox.TextProperty, value);
        Assert.True(CssMatcher.Matches(input, required, evaluateStates: true));

        AutomationProperties.SetIsRequiredForForm(input, true);
        input.ClearAllBindings();
        Assert.True(CssMatcher.Matches(input, required, evaluateStates: true));
        Assert.False(CssMatcher.Matches(input, optional, evaluateStates: true));
    }

    [Fact]
    public void MultiBindingRequiredRule_AppliesToTheInputValue()
    {
        var required = Assert.Single(CssSelectorParser.ParseGroup(":required", out _)!);
        var input = new TextBox { DataContext = new InputModel { Value = "ready" } };
        var multi = new MultiBinding();
        multi.Bindings.Add(new Binding(nameof(InputModel.Value)));
        multi.ValidationRules.Add(new RequiredValidationRule());
        input.SetBinding(TextBox.TextProperty, multi);
        Assert.True(CssMatcher.Matches(input, required, evaluateStates: true));

        input.ClearBinding(TextBox.TextProperty);
        Assert.False(CssMatcher.Matches(input, required, evaluateStates: true));
    }

    [Fact]
    public void MultiBindingChildRequiredRule_AppliesToTheInputValue()
    {
        var required = Assert.Single(CssSelectorParser.ParseGroup(":required", out _)!);
        var input = new TextBox { DataContext = new InputModel() };
        var child = new Binding(nameof(InputModel.Value));
        child.ValidationRules.Add(new RequiredValidationRule());
        var multi = new MultiBinding();
        multi.Bindings.Add(child);

        input.SetBinding(TextBox.TextProperty, multi);
        Assert.True(CssMatcher.Matches(input, required, evaluateStates: true));

        input.ClearBinding(TextBox.TextProperty);
        Assert.False(CssMatcher.Matches(input, required, evaluateStates: true));
    }

    [Fact]
    public void AttachedBindingRuleMutations_UpdateSelfSiblingAndHasSelectors()
    {
        var input = new TextBox();
        var sibling = new Border();
        var root = new StackPanel();
        root.Children.Add(input);
        root.Children.Add(sibling);
        Css.SetStyleSheet(root,
            "TextBox:optional { opacity:.4 } TextBox:required { opacity:.6 } " +
            "TextBox:required + Border { opacity:.7 } " +
            "StackPanel:has(> TextBox:required) { opacity:.8 }");

        var binding = new Binding(nameof(InputModel.Value)) { Source = new InputModel() };
        input.SetBinding(TextBox.TextProperty, binding);
        Flush(root);
        Assert.Equal(.4, input.Opacity);

        var rule = new RequiredValidationRule();
        binding.ValidationRules.Add(rule);
        Flush(root);
        Assert.Equal(.6, input.Opacity);
        Assert.Equal(.7, sibling.Opacity);
        Assert.Equal(.8, root.Opacity);

        binding.ValidationRules[0] = new RangeValidationRule();
        Flush(root);
        Assert.Equal(.4, input.Opacity);
        Assert.Equal(1, sibling.Opacity);
        Assert.Equal(1, root.Opacity);

        binding.ValidationRules[0] = rule;
        Flush(root);
        Assert.Equal(.6, input.Opacity);
        binding.ValidationRules.Remove(rule);
        Flush(root);
        Assert.Equal(.4, input.Opacity);

        binding.ValidationRules.Add(rule);
        Flush(root);
        Assert.Equal(.6, input.Opacity);
        binding.ValidationRules.Clear();
        Flush(root);
        Assert.Equal(.4, input.Opacity);
    }

    [Fact]
    public void AttachedCompositeRuleMutations_FollowTheActiveValueBinding()
    {
        var required = Assert.Single(CssSelectorParser.ParseGroup(":required", out _)!);
        var model = new PreferredInputModel();
        var input = new TextBox { DataContext = model };
        var root = new StackPanel();
        root.Children.Add(input);
        Css.SetStyleSheet(root, "TextBox:optional { opacity:.4 } TextBox:required { opacity:.6 }");
        var preferred = new Binding(nameof(PreferredInputModel.Preferred));
        var fallback = new Binding(nameof(PreferredInputModel.Value));
        var priority = new PriorityBinding();
        priority.Bindings.Add(preferred);
        priority.Bindings.Add(fallback);
        var multi = new MultiBinding();
        multi.Bindings.Add(priority);
        input.SetBinding(TextBox.TextProperty, multi);
        Flush(root);
        Assert.Equal(.4, input.Opacity);

        var rule = new RequiredValidationRule();
        preferred.ValidationRules.Add(rule);
        Flush(root);
        Assert.Equal(.6, input.Opacity);
        Assert.True(CssMatcher.Matches(input, required, evaluateStates: true));

        model.Preferred = DependencyProperty.UnsetValue;
        Flush(root);
        Assert.Equal(.4, input.Opacity);
        Assert.False(CssMatcher.Matches(input, required, evaluateStates: true));
        preferred.ValidationRules.Clear();
        Flush(root);
        Assert.False(CssMatcher.Matches(input, required, evaluateStates: true));

        fallback.ValidationRules.Add(rule);
        Flush(root);
        Assert.Equal(.6, input.Opacity);
        Assert.True(CssMatcher.Matches(input, required, evaluateStates: true));
        fallback.ValidationRules.Clear();
        Flush(root);
        Assert.Equal(.4, input.Opacity);
        Assert.False(CssMatcher.Matches(input, required, evaluateStates: true));

        multi.ValidationRules.Add(rule);
        Flush(root);
        Assert.Equal(.6, input.Opacity);
        Assert.True(CssMatcher.Matches(input, required, evaluateStates: true));
        multi.ValidationRules.Clear();
        Flush(root);
        Assert.Equal(.4, input.Opacity);
        Assert.False(CssMatcher.Matches(input, required, evaluateStates: true));
    }

    [Fact]
    public void SharedBindingRuleMutation_UpdatesEveryAttachedInput()
    {
        var binding = new Binding(nameof(InputModel.Value)) { Source = new InputModel() };
        var first = new TextBox();
        var second = new TextBox();
        var root = new StackPanel();
        root.Children.Add(first);
        root.Children.Add(second);
        Css.SetStyleSheet(root, "TextBox:optional { opacity:.4 } TextBox:required { opacity:.6 }");
        first.SetBinding(TextBox.TextProperty, binding);
        second.SetBinding(TextBox.TextProperty, binding);
        Flush(root);
        Assert.Equal(.4, first.Opacity);
        Assert.Equal(.4, second.Opacity);

        binding.ValidationRules.Add(new RequiredValidationRule());
        Flush(root);
        Assert.Equal(.6, first.Opacity);
        Assert.Equal(.6, second.Opacity);
    }

    [Fact]
    public void PriorityBinding_UsesOnlyTheActiveChildRuleAndRefreshesStyles()
    {
        var model = new PreferredInputModel();
        var input = new TextBox { DataContext = model };
        var root = new StackPanel();
        root.Children.Add(input);
        Css.SetStyleSheet(root, "TextBox:optional { opacity:.4 } TextBox:required { opacity:.6 }");

        var preferred = new Binding(nameof(PreferredInputModel.Preferred));
        preferred.ValidationRules.Add(new RequiredValidationRule());
        var priority = new PriorityBinding();
        priority.Bindings.Add(preferred);
        priority.Bindings.Add(new Binding(nameof(InputModel.Value)));
        input.SetBinding(TextBox.TextProperty, priority);
        Flush(root);
        Assert.Equal("ready", input.Text);
        Assert.Equal(.6, input.Opacity);

        model.Preferred = DependencyProperty.UnsetValue;
        Flush(root);
        Assert.Equal("ready", input.Text);
        Assert.Equal(.4, input.Opacity);

        model.Preferred = "ready";
        Flush(root);
        Assert.Equal("ready", input.Text);
        Assert.Equal(.6, input.Opacity);
    }

    private sealed class InputModel
    {
        public string Value { get; set; } = "ready";
        public double Width { get; set; }
    }

    private sealed class PreferredInputModel : System.ComponentModel.INotifyPropertyChanged
    {
        private object _preferred = "ready";

        public object Preferred
        {
            get => _preferred;
            set
            {
                _preferred = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Preferred)));
            }
        }

        public string Value { get; set; } = "ready";
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }

    private static void Flush(FrameworkElement element) => CssEvaluationScheduler.FlushIfPending(element.Dispatcher);
}

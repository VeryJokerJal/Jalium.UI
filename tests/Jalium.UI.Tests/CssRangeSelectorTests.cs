using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public sealed class CssRangeSelectorTests
{
    static CssRangeSelectorTests() => System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(
        typeof(Jalium.UI.Markup.TypeConverterRegistry).Module.ModuleHandle);

    [Theory]
    [InlineData(":in-range", true)]
    [InlineData(":OUT-OF-RANGE", true)]
    [InlineData(":in-range()", false)]
    [InlineData(":out-of-range()", false)]
    public void SelectorSupport_UsesSimplePseudoClasses(string selector, bool expected)
        => Assert.Equal(expected, Css.Supports($"selector({selector})"));

    [Fact]
    public void RangeStates_OnlyMatchInputsWithRangeLimits()
    {
        var inRange = Assert.Single(CssSelectorParser.ParseGroup(":in-range", out _)!);
        var outOfRange = Assert.Single(CssSelectorParser.ParseGroup(":out-of-range", out _)!);

        var plain = new TextBox { Text = "20" };
        Assert.False(CssMatcher.Matches(plain, inRange, evaluateStates: true));
        Assert.False(CssMatcher.Matches(plain, outOfRange, evaluateStates: true));

        var slider = new Slider { Minimum = 1, Maximum = 10, Value = 5 };
        Assert.True(CssMatcher.Matches(slider, inRange, evaluateStates: true));
        Assert.False(CssMatcher.Matches(slider, outOfRange, evaluateStates: true));

        var rangeSlider = new RangeSlider { Minimum = 1, Maximum = 10, RangeStart = 2, RangeEnd = 8 };
        Assert.True(CssMatcher.Matches(rangeSlider, inRange, evaluateStates: true));
        Assert.False(CssMatcher.Matches(rangeSlider, outOfRange, evaluateStates: true));

        var date = new DatePicker();
        Assert.False(CssMatcher.Matches(date, inRange, evaluateStates: true));
        date.DisplayDateStart = new DateTime(2026, 1, 1);
        date.SelectedDate = new DateTime(2026, 1, 2);
        Assert.True(CssMatcher.Matches(date, inRange, evaluateStates: true));

        var number = new NumberBox { Minimum = 1, Maximum = 10, Value = 5 };
        number.Text = "20";
        Assert.False(CssMatcher.Matches(number, inRange, evaluateStates: true));
        Assert.True(CssMatcher.Matches(number, outOfRange, evaluateStates: true));

        var border = new Border();
        Assert.False(CssMatcher.Matches(border, inRange, evaluateStates: true));
        Assert.False(CssMatcher.Matches(border, outOfRange, evaluateStates: true));

        var widthBinding = new Binding(nameof(WidthModel.Width)) { Source = new WidthModel() };
        widthBinding.ValidationRules.Add(new RangeValidationRule { Maximum = 10 });
        plain.SetBinding(FrameworkElement.WidthProperty, widthBinding);
        Assert.False(CssMatcher.Matches(plain, inRange, evaluateStates: true));
        Assert.False(CssMatcher.Matches(plain, outOfRange, evaluateStates: true));
    }

    [Fact]
    public void NumberBoxRangeChanges_UpdateSelfSiblingAndHasSelectors()
    {
        var number = new NumberBox { Minimum = 1, Maximum = 10, Value = 5 };
        var sibling = new Border();
        var root = new StackPanel();
        root.Children.Add(number);
        root.Children.Add(sibling);
        Css.SetStyleSheet(root,
            "NumberBox:in-range { opacity:.4 } NumberBox:out-of-range { opacity:.6 } " +
            "NumberBox:out-of-range + Border { opacity:.7 } " +
            "StackPanel:has(> NumberBox:out-of-range) { opacity:.8 }");
        Flush(root);
        Assert.Equal(.4, number.Opacity);

        number.Text = "11";
        Flush(root);
        Assert.Equal(.6, number.Opacity);
        Assert.Equal(.7, sibling.Opacity);
        Assert.Equal(.8, root.Opacity);

        number.Maximum = 12;
        Flush(root);
        Assert.Equal(.4, number.Opacity);
        Assert.Equal(1, sibling.Opacity);
        Assert.Equal(1, root.Opacity);

        number.Text = "not a number";
        Flush(root);
        Assert.Equal(.4, number.Opacity);
    }

    [Fact]
    public void BoundRangeRule_TracksTextAndRuleCollectionChanges()
    {
        var input = new TextBox();
        var sibling = new Border();
        var root = new StackPanel();
        root.Children.Add(input);
        root.Children.Add(sibling);
        Css.SetStyleSheet(root,
            "TextBox:in-range { opacity:.4 } TextBox:out-of-range { opacity:.6 } " +
            "TextBox:out-of-range + Border { opacity:.7 } " +
            "StackPanel:has(> TextBox:out-of-range) { opacity:.8 }");

        var binding = new Binding(nameof(InputModel.Value)) { Source = new InputModel() };
        var rule = new RangeValidationRule { Minimum = 1, Maximum = 10 };
        binding.ValidationRules.Add(rule);
        input.SetBinding(TextBox.TextProperty, binding);
        Flush(root);
        Assert.Equal(.4, input.Opacity);

        input.Text = "11";
        Flush(root);
        Assert.Equal(.6, input.Opacity);
        Assert.Equal(.7, sibling.Opacity);
        Assert.Equal(.8, root.Opacity);

        rule.Maximum = 12;
        Flush(root);
        Assert.Equal(.4, input.Opacity);
        Assert.Equal(1, sibling.Opacity);
        Assert.Equal(1, root.Opacity);

        rule.Maximum = 10;
        Flush(root);
        Assert.Equal(.6, input.Opacity);
        Assert.Equal(.7, sibling.Opacity);
        Assert.Equal(.8, root.Opacity);

        binding.ValidationRules.Clear();
        Flush(root);
        Assert.Equal(1, input.Opacity);

        binding.ValidationRules.Add(rule);
        Flush(root);
        Assert.Equal(.6, input.Opacity);

        input.ClearBinding(TextBox.TextProperty);
        Flush(root);
        Assert.Equal(1, input.Opacity);
    }

    [Fact]
    public void PriorityBindingRangeRule_RefreshesWhenActiveChildChangesWithoutValueChange()
    {
        var model = new PriorityInputModel();
        var input = new TextBox { DataContext = model };
        var root = new StackPanel();
        root.Children.Add(input);
        Css.SetStyleSheet(root, "TextBox:in-range { opacity:.4 } TextBox:out-of-range { opacity:.6 }");

        var preferred = new Binding(nameof(PriorityInputModel.Preferred));
        preferred.ValidationRules.Add(new RangeValidationRule { Minimum = 1, Maximum = 10 });
        var fallback = new Binding(nameof(PriorityInputModel.Fallback));
        fallback.ValidationRules.Add(new RangeValidationRule { Minimum = 6, Maximum = 10 });
        var priority = new PriorityBinding();
        priority.Bindings.Add(preferred);
        priority.Bindings.Add(fallback);
        input.SetBinding(TextBox.TextProperty, priority);
        Flush(root);
        Assert.Equal("5", input.Text);
        Assert.Equal(.4, input.Opacity);

        model.Preferred = DependencyProperty.UnsetValue;
        Flush(root);
        Assert.Equal("5", input.Text);
        Assert.Equal(.6, input.Opacity);

        model.Preferred = "5";
        Flush(root);
        Assert.Equal("5", input.Text);
        Assert.Equal(.4, input.Opacity);
    }

    [Fact]
    public void BoundRuleBecomesRangeLimitedWhenItsLimitIsSet()
    {
        var input = new TextBox();
        var root = new StackPanel();
        root.Children.Add(input);
        Css.SetStyleSheet(root, "TextBox:out-of-range { opacity:.6 }");
        var binding = new Binding(nameof(InputModel.Value)) { Source = new InputModel { Value = "11" } };
        var rule = new RangeValidationRule();
        binding.ValidationRules.Add(rule);
        input.SetBinding(TextBox.TextProperty, binding);
        Flush(root);
        Assert.Equal(1, input.Opacity);

        rule.Maximum = 10;
        Flush(root);
        Assert.Equal(.6, input.Opacity);

        rule.Maximum = null;
        Flush(root);
        Assert.Equal(1, input.Opacity);
    }

    private sealed class InputModel
    {
        public string Value { get; set; } = "5";
    }

    private sealed class WidthModel
    {
        public double Width { get; set; } = 20;
    }

    private sealed class PriorityInputModel : System.ComponentModel.INotifyPropertyChanged
    {
        private object _preferred = "5";

        public object Preferred
        {
            get => _preferred;
            set
            {
                _preferred = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Preferred)));
            }
        }

        public string Fallback { get; set; } = "5";
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }

    private static void Flush(FrameworkElement element) => CssEvaluationScheduler.FlushIfPending(element.Dispatcher);
}

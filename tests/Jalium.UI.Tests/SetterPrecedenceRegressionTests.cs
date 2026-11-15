using System.ComponentModel;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Data;

namespace Jalium.UI.Tests;

/// <summary>
/// Setter / trigger 优先级的回归护栏。每条对应一个真实缺陷：
///  • style setter 因为"目标已有 local 值"被整条丢弃 → ClearValue 回不到 style 值；
///  • Setter.Value 是 {Binding} 时绑定落进 local 层 → 压过所有 trigger，且换 style 后不拆；
///  • 模板对控件自身的 trigger 压过用户 Style.Triggers（与 WPF 相反）；
///  • 共享 ControlTemplate 被任一宿主 teardown 后，其它宿主的 trigger 归属被清空
///    → 写/清落到不同值层 → 值被永久钉死。
/// </summary>
public class SetterPrecedenceRegressionTests
{
    [Fact]
    public void StyleSetter_ShouldStillPopulateStyleLayer_WhenLocalValueWasSetFirst()
    {
        // 顺序刻意与标记一致：属性在标记里直接写（local 值先到），Style 在入树时才应用。
        var element = new SetterProbeElement { Token = "Local" };

        var style = new Style(typeof(SetterProbeElement));
        style.Setters.Add(new Setter(SetterProbeElement.TokenProperty, "Style"));
        element.Style = style;

        Assert.Equal("Local", element.Token);
        Assert.Equal(
            BaseValueSource.Local,
            DependencyPropertyHelper.GetValueSource(element, SetterProbeElement.TokenProperty).BaseValueSource);

        // 关键：清掉 local 之后必须回落到 style 值，而不是 DP 默认值。
        element.ClearValue(SetterProbeElement.TokenProperty);
        Assert.Equal("Style", element.Token);
        Assert.Equal(
            BaseValueSource.Style,
            DependencyPropertyHelper.GetValueSource(element, SetterProbeElement.TokenProperty).BaseValueSource);
    }

    [Fact]
    public void StyleSetterBinding_ShouldLandInStyleLayer_AndStayBelowTriggers()
    {
        var source = new TokenSource { Token = "Bound" };
        var element = new SetterProbeElement { DataContext = source };

        var style = new Style(typeof(SetterProbeElement));
        style.Setters.Add(new Setter(
            SetterProbeElement.TokenProperty,
            new Binding(nameof(TokenSource.Token))));

        var trigger = new Trigger
        {
            Property = SetterProbeElement.FlagProperty,
            Value = true
        };
        trigger.Setters.Add(new Setter(SetterProbeElement.TokenProperty, "Trigger"));
        style.Triggers.Add(trigger);

        element.Style = style;

        Assert.Equal("Bound", element.Token);
        Assert.False(element.HasLocalValue(SetterProbeElement.TokenProperty));
        Assert.Equal(
            BaseValueSource.Style,
            DependencyPropertyHelper.GetValueSource(element, SetterProbeElement.TokenProperty).BaseValueSource);

        // 绑定仍然是活的：源变了，style 层跟着变。
        source.Token = "Bound2";
        Assert.Equal("Bound2", element.Token);

        // trigger 必须能盖过带绑定的 style setter。
        element.SetValue(SetterProbeElement.FlagProperty, true);
        Assert.Equal("Trigger", element.Token);

        element.SetValue(SetterProbeElement.FlagProperty, false);
        Assert.Equal("Bound2", element.Token);

        // Style 被换掉后绑定必须拆干净，源再变也不能把值灌回来。
        element.Style = null;
        source.Token = "Changed";
        Assert.NotEqual("Changed", element.Token);
    }

    [Fact]
    public void DerivedConstantSetterRetiresBaseBindingAndRestoresItWhenRemoved()
    {
        var source = new TokenSource { Token = "Base" };
        var element = new SetterProbeElement { DataContext = source };
        var baseline = new Style(typeof(SetterProbeElement));
        baseline.Setters.Add(new Setter(SetterProbeElement.TokenProperty, new Binding(nameof(TokenSource.Token))));
        var derived = new Style(typeof(SetterProbeElement)) { BasedOn = baseline };
        derived.Setters.Add(new Setter(SetterProbeElement.TokenProperty, "Custom"));
        element.Style = derived;
        source.Token = "Changed base";
        element.DataContext = new TokenSource { Token = "Other base" };
        Assert.Equal("Custom", element.Token);
        element.Style = baseline;
        Assert.Equal("Other base", element.Token);
        element.DataContext = source;
        Assert.Equal("Changed base", element.Token);
        source.Token = "Restored live binding";
        Assert.Equal("Restored live binding", element.Token);
    }

    [Fact]
    public void DerivedBindingSetterRetiresBaseSubscriptionAndSwitchesSourcesCleanly()
    {
        var baselineSource = new TokenSource { Token = "Base" };
        var overrideSource = new TokenSource { Token = "Override" };
        var element = new SetterProbeElement();
        var baseline = new Style(typeof(SetterProbeElement));
        baseline.Setters.Add(new Setter(SetterProbeElement.TokenProperty,
            new Binding(nameof(TokenSource.Token)) { Source = baselineSource }));
        var derived = new Style(typeof(SetterProbeElement)) { BasedOn = baseline };
        derived.Setters.Add(new Setter(SetterProbeElement.TokenProperty,
            new Binding(nameof(TokenSource.Token)) { Source = overrideSource }));
        element.Style = derived;
        baselineSource.Token = "Changed base";
        Assert.Equal("Override", element.Token);
        overrideSource.Token = "Changed override";
        Assert.Equal("Changed override", element.Token);
        element.Style = baseline;
        overrideSource.Token = "Retired override";
        Assert.Equal("Changed base", element.Token);
        baselineSource.Token = "Restored base";
        Assert.Equal("Restored base", element.Token);
    }

    [Fact]
    public void StyleTrigger_ShouldOutrank_TemplateTriggerOnTemplatedControlItself()
    {
        var template = new ControlTemplate(typeof(SetterProbeControl));
        template.SetVisualTree(static () => new Border());

        // 模板对被模板化控件**自身**下的 trigger（无 TargetName）—— WPF 的 TemplateTrigger 级。
        var templateTrigger = new Trigger
        {
            Property = SetterProbeControl.FlagProperty,
            Value = true
        };
        templateTrigger.Setters.Add(new Setter(SetterProbeControl.TokenProperty, "FromTemplate"));
        template.Triggers.Add(templateTrigger);

        var style = new Style(typeof(SetterProbeControl));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        var styleTrigger = new Trigger
        {
            Property = SetterProbeControl.FlagProperty,
            Value = true
        };
        styleTrigger.Setters.Add(new Setter(SetterProbeControl.TokenProperty, "FromStyle"));
        style.Triggers.Add(styleTrigger);

        var control = new SetterProbeControl { Style = style };
        control.ApplyTemplate();

        control.SetValue(SetterProbeControl.FlagProperty, true);

        // WPF 优先级：StyleTrigger(7) > TemplateTrigger(6)。用户写的 Style.Triggers 必须赢。
        Assert.Equal("FromStyle", control.GetValue(SetterProbeControl.TokenProperty));
        Assert.Equal(
            BaseValueSource.StyleTrigger,
            DependencyPropertyHelper.GetValueSource(control, SetterProbeControl.TokenProperty).BaseValueSource);

        control.SetValue(SetterProbeControl.FlagProperty, false);
        Assert.Equal("Default", control.GetValue(SetterProbeControl.TokenProperty));
    }

    [Fact]
    public void TemplateTrigger_OnNamedPart_ShouldOutrankTemplateLiteralValue()
    {
        var control = new SetterProbeControl { Template = BuildNamedPartTemplate() };
        control.ApplyTemplate();
        var part = FindNamedPart(control);

        Assert.Equal("Base", part.Token);
        Assert.Equal(
            BaseValueSource.ParentTemplate,
            DependencyPropertyHelper.GetValueSource(part, SetterProbeElement.TokenProperty).BaseValueSource);

        control.SetValue(SetterProbeControl.FlagProperty, true);
        Assert.Equal("Triggered", part.Token);
        Assert.Equal(
            BaseValueSource.ParentTemplateTrigger,
            DependencyPropertyHelper.GetValueSource(part, SetterProbeElement.TokenProperty).BaseValueSource);
    }

    [Fact]
    public void SharedTemplate_TeardownOnOneHost_ShouldNotBreakTriggerLayerOnAnother()
    {
        // 一个 ControlTemplate 被两个控件共享（主题模板的常态）。
        var template = BuildNamedPartTemplate();

        var first = new SetterProbeControl { Template = template };
        var second = new SetterProbeControl { Template = template };
        first.ApplyTemplate();
        second.ApplyTemplate();

        var secondPart = FindNamedPart(second);

        second.SetValue(SetterProbeControl.FlagProperty, true);
        Assert.Equal("Triggered", secondPart.Token);

        // first 换模板 → teardown。共享 trigger 实例的模板归属绝不能因此被清掉，
        // 否则 second 的 trigger 之后会写/清另一个值层，值被永久钉死。
        first.Template = null;

        second.SetValue(SetterProbeControl.FlagProperty, false);
        Assert.Equal("Base", secondPart.Token);
        Assert.Equal(
            BaseValueSource.ParentTemplate,
            DependencyPropertyHelper.GetValueSource(secondPart, SetterProbeElement.TokenProperty).BaseValueSource);

        second.SetValue(SetterProbeControl.FlagProperty, true);
        Assert.Equal("Triggered", secondPart.Token);
        Assert.Equal(
            BaseValueSource.ParentTemplateTrigger,
            DependencyPropertyHelper.GetValueSource(secondPart, SetterProbeElement.TokenProperty).BaseValueSource);
    }

    [Fact]
    public void RepeatedStyleAttach_ShouldNotPinTriggerValues()
    {
        var element = new SetterProbeElement();
        var style = new Style(typeof(SetterProbeElement));
        var trigger = new Trigger
        {
            Property = SetterProbeElement.FlagProperty,
            Value = true
        };
        trigger.Setters.Add(new Setter(SetterProbeElement.TokenProperty, "Trigger"));
        style.Triggers.Add(trigger);

        element.Style = style;
        element.SetValue(SetterProbeElement.FlagProperty, true);
        Assert.Equal("Trigger", element.Token);

        // 同一个 style 再挂一遍（模板重建 / 样式重算路径会走到）。
        element.Style = null;
        element.Style = style;
        Assert.Equal("Trigger", element.Token);

        element.SetValue(SetterProbeElement.FlagProperty, false);
        Assert.Equal("Default", element.Token);
    }

    private static ControlTemplate BuildNamedPartTemplate()
    {
        var template = new ControlTemplate(typeof(SetterProbeControl));
        template.SetVisualTree(static () => new SetterProbeElement
        {
            Name = "PART_Token",
            Token = "Base" // 模板字面值 → ParentTemplate 层
        });

        var trigger = new Trigger
        {
            Property = SetterProbeControl.FlagProperty,
            Value = true
        };
        trigger.Setters.Add(new Setter(
            SetterProbeElement.TokenProperty,
            "Triggered",
            "PART_Token"));
        template.Triggers.Add(trigger);
        return template;
    }

    private static SetterProbeElement FindNamedPart(SetterProbeControl control)
        => Assert.IsType<SetterProbeElement>(control.GetVisualChild(0));

    private sealed class TokenSource : INotifyPropertyChanged
    {
        private string? _token;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string? Token
        {
            get => _token;
            set
            {
                if (_token == value)
                    return;
                _token = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Token)));
            }
        }
    }

    private sealed class SetterProbeControl : Control
    {
        public static readonly DependencyProperty TokenProperty =
            DependencyProperty.Register(
                "Token",
                typeof(string),
                typeof(SetterProbeControl),
                new PropertyMetadata("Default"));

        public static readonly DependencyProperty FlagProperty =
            DependencyProperty.Register(
                "Flag",
                typeof(bool),
                typeof(SetterProbeControl),
                new PropertyMetadata(false));
    }

    private sealed class SetterProbeElement : FrameworkElement
    {
        public static readonly DependencyProperty TokenProperty =
            DependencyProperty.Register(
                "Token",
                typeof(string),
                typeof(SetterProbeElement),
                new PropertyMetadata("Default"));

        public static readonly DependencyProperty FlagProperty =
            DependencyProperty.Register(
                "Flag",
                typeof(bool),
                typeof(SetterProbeElement),
                new PropertyMetadata(false));

        public string? Token
        {
            get => (string?)GetValue(TokenProperty);
            set => SetValue(TokenProperty, value);
        }
    }
}

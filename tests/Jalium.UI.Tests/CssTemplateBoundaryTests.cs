using Jalium.UI.Controls;
using Jalium.UI.Styling;
using Xunit;

namespace Jalium.UI.Tests;

/// <summary>
/// Template boundary semantics: template-generated parts neither match page-level rules nor
/// appear on the CSS ancestor axis, while user content inside a templated control does.
/// </summary>
public sealed class CssTemplateBoundaryTests
{
    static CssTemplateBoundaryTests()
    {
        System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(
            typeof(Jalium.UI.Markup.TypeConverterRegistry).Module.ModuleHandle);
    }

    private static void Flush(FrameworkElement element)
        => CssEvaluationScheduler.FlushIfPending(element.Dispatcher);

    private static Button MakeTemplatedButton(out Border templateBorder)
    {
        Border? captured = null;
        var template = new ControlTemplate(typeof(Button));
        template.SetVisualTree(() => captured = new Border { Child = new ContentPresenter() });
        var button = new Button { Template = template };
        button.ApplyTemplate();
        Assert.NotNull(captured);
        templateBorder = captured!;
        return button;
    }

    [Fact]
    public void TemplateParts_DoNotMatchPageRules()
    {
        var button = MakeTemplatedButton(out var templateBorder);
        var panel = new StackPanel();
        panel.Children.Add(button);
        Css.GetStyleSheets(panel).Add(CssStyleSheet.Parse("Border { opacity: 0.5 }"));
        Flush(panel);

        Assert.NotNull(templateBorder.TemplatedParent);
        Assert.Equal(1.0, templateBorder.Opacity);
    }

    [Fact]
    public void UserContent_InsideTemplatedControl_IsMatchable()
    {
        var content = new TextBlock { Text = "hi" };
        var button = MakeTemplatedButton(out _);
        button.Content = content;
        var panel = new StackPanel();
        Css.SetClass(panel, "host");
        panel.Children.Add(button);
        button.Measure(new Size(100, 40));

        Css.GetStyleSheets(panel).Add(CssStyleSheet.Parse(".host TextBlock { opacity: 0.5 }"));
        Flush(panel);

        Assert.Equal(0.5, content.Opacity);
    }

    [Fact]
    public void AncestorAxis_SkipsTemplateParts_ForChildCombinator()
    {
        // Visually the content sits under Button → Border → ContentPresenter → TextBlock,
        // but the CSS ancestor axis sees Button > TextBlock (template parts are transparent).
        var content = new TextBlock { Text = "hi" };
        var button = MakeTemplatedButton(out _);
        button.Content = content;
        var panel = new StackPanel();
        panel.Children.Add(button);
        button.Measure(new Size(100, 40));

        Css.GetStyleSheets(panel).Add(CssStyleSheet.Parse("Button > TextBlock { opacity: 0.5 }"));
        Flush(panel);

        Assert.Equal(0.5, content.Opacity);
    }

    [Fact]
    public void InlineStyle_StillWorksOnTemplateParts()
    {
        // Template authors may use Css.Style inside templates; only selector matching is gated.
        var button = MakeTemplatedButton(out var templateBorder);
        Css.SetStyle(templateBorder, "opacity: 0.5");
        Assert.Equal(0.5, templateBorder.Opacity);
    }
}

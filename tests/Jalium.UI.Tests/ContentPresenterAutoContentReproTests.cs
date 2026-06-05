using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Media;
using Xunit;

namespace Jalium.UI.Tests;

/// <summary>
/// Repro for the Jalium.One toolbar "empty IdeIconButton" bug.
///
/// Two button templates render content through a templated ContentPresenter that does NOT
/// explicitly bind Content; both rely on ContentPresenter.ApplyTemplateBindings() auto-adding
/// a Content&lt;-Content template binding. Their only structural difference:
///   - IdeChromeTextButton's CP carries explicit {TemplateBinding} (Margin/H/V align)
///   - IdeIconButton's CP carries ONLY literal HorizontalAlignment/VerticalAlignment, zero
///     template bindings.
/// IdeChromeTextButton renders content; IdeIconButton renders nothing in the real app.
/// These tests isolate whether the templated CP actually presents content in each shape.
/// </summary>
public class ContentPresenterAutoContentReproTests
{
    // Precondition for the auto-Content binding.
    [Fact]
    public void FromName_Button_Content_resolves()
    {
        Assert.NotNull(DependencyProperty.FromName(typeof(Button), "Content"));
    }

    // Scenario A — mimics IdeIconButton: literal alignment, no template bindings, no explicit
    // Content binding. Content set AFTER ApplyTemplate.
    [Fact]
    public void LiteralAlignmentCP_ContentAfterApply_PresentsContent()
    {
        ContentPresenter? cp = null;
        var template = new ControlTemplate(typeof(Button));
        template.SetVisualTree(() =>
        {
            cp = new ContentPresenter
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            return new Border { Child = cp };
        });

        var button = new Button { Width = 30, Height = 28, Template = template };
        button.ApplyTemplate();
        button.Content = new Border { Background = Brushes.Red, Width = 14, Height = 14 };

        button.Measure(new Size(30, 28));
        button.Arrange(new Rect(0, 0, 30, 28));

        Assert.NotNull(cp);
        Assert.Equal(1, cp!.VisualChildrenCount);
    }

    // Scenario A2 — mimics IdeIconButton with Content set BEFORE ApplyTemplate (attribute timing,
    // i.e. Content="−" in XAML before the template is instantiated).
    [Fact]
    public void LiteralAlignmentCP_ContentBeforeApply_PresentsContent()
    {
        ContentPresenter? cp = null;
        var template = new ControlTemplate(typeof(Button));
        template.SetVisualTree(() =>
        {
            cp = new ContentPresenter
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            return new Border { Child = cp };
        });

        var button = new Button { Width = 30, Height = 28, Template = template };
        button.Content = "X";
        button.ApplyTemplate();

        button.Measure(new Size(30, 28));
        button.Arrange(new Rect(0, 0, 30, 28));

        Assert.NotNull(cp);
        Assert.Equal(1, cp!.VisualChildrenCount);
    }

    // Scenario B — mimics IdeChromeTextButton: explicit alignment template bindings, no explicit
    // Content binding. Expected to render (the known-good shape).
    [Fact]
    public void TemplateBoundAlignmentCP_ContentBeforeApply_PresentsContent()
    {
        ContentPresenter? cp = null;
        var template = new ControlTemplate(typeof(Button));
        template.SetVisualTree(() =>
        {
            cp = new ContentPresenter();
            cp.SetTemplateBinding(FrameworkElement.HorizontalAlignmentProperty, Control.HorizontalContentAlignmentProperty);
            cp.SetTemplateBinding(FrameworkElement.VerticalAlignmentProperty, Control.VerticalContentAlignmentProperty);
            return new Border { Child = cp };
        });

        var button = new Button { Width = 60, Height = 28, Template = template };
        button.Content = "X";
        button.ApplyTemplate();

        button.Measure(new Size(60, 28));
        button.Arrange(new Rect(0, 0, 60, 28));

        Assert.NotNull(cp);
        Assert.Equal(1, cp!.VisualChildrenCount);
    }

    // Direct probe: after ApplyTemplate, does the IdeIconButton-shaped CP carry an active
    // Content binding at all?
    [Fact]
    public void LiteralAlignmentCP_HasContentBindingAfterApply()
    {
        ContentPresenter? cp = null;
        var template = new ControlTemplate(typeof(Button));
        template.SetVisualTree(() =>
        {
            cp = new ContentPresenter
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            return new Border { Child = cp };
        });

        var button = new Button { Width = 30, Height = 28, Template = template };
        button.Content = "X";
        button.ApplyTemplate();

        Assert.NotNull(cp);
        Assert.NotNull(cp!.GetBindingExpression(ContentPresenter.ContentProperty));
        Assert.Equal("X", cp.Content);
    }
}

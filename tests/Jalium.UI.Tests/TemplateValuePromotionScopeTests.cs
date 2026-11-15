using System.ComponentModel;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

/// <summary>
/// Template expansion moves the literal values of the elements a template instance created into the
/// ParentTemplate layer, so the template's triggers can override them. Content the application hands
/// to the templated control is not part of that instance, however the template projects it: it keeps
/// its local values (and with them ClearValue and local-value precedence checks), gets no templated
/// parent, and stays visible to page CSS. Regression: the promotion walk ran after the ContentPresenter
/// had already pulled the host's Content in, and the TemplatedParent walk stamped that content too.
/// </summary>
public sealed class TemplateValuePromotionScopeTests
{
    public enum ContentProjection
    {
        /// <summary>A bare ContentPresenter: its implicit TemplateBinding binds during the TemplatedParent walk.</summary>
        ImplicitPresenter,

        /// <summary>A ContentPresenter with an explicit TemplateBinding: the content arrives with AddVisualChild.</summary>
        ExplicitTemplateBinding,

        /// <summary>A nested ContentControl taking over the host's Content (SplitButton's PrimaryButton shape).</summary>
        NestedContentControl,
    }

    [Theory]
    [InlineData(ContentProjection.ImplicitPresenter)]
    [InlineData(ContentProjection.ExplicitTemplateBinding)]
    [InlineData(ContentProjection.NestedContentControl)]
    public void ContentSuppliedBeforeTemplateExpansion_KeepsItsLocalValues(ContentProjection projection)
    {
        var text = new TextBlock { Text = "centred", TextAlignment = TextAlignment.Center };
        var grid = new Grid { Name = "AppGrid" };
        grid.Children.Add(text);
        var scroll = new ScrollViewer { Content = grid };
        var host = new PanelHost { Content = scroll };

        host.Template = BuildHostTemplate(projection);
        Layout(host);

        Assert.True(text.HasLocalValue(TextBlock.TextAlignmentProperty));
        Assert.Equal(BaseValueSource.Local, SourceOf(text, TextBlock.TextAlignmentProperty));
        Assert.Null(text.TemplatedParent);
        Assert.Null(grid.TemplatedParent);
        Assert.Null(scroll.TemplatedParent);
        Assert.Null(host.FindName("AppGrid"));

        // ClearValue reverts to the default instead of uncovering a template-layer copy.
        text.ClearValue(TextBlock.TextAlignmentProperty);
        Assert.Equal(TextAlignment.Left, text.TextAlignment);
        Assert.Equal(BaseValueSource.Default, SourceOf(text, TextBlock.TextAlignmentProperty));

        // The template's own literal is still a template value.
        var chrome = Assert.IsType<Border>(host.FindName("PART_Chrome"));
        Assert.Same(host, chrome.TemplatedParent);
        Assert.Equal(BaseValueSource.ParentTemplate, SourceOf(chrome, FrameworkElement.WidthProperty));
    }

    [Fact]
    public void PageTemplate_KeepsPageContentLocalValues()
    {
        var text = new TextBlock { Text = "page", TextAlignment = TextAlignment.Center };
        var page = new Page { Content = text };
        var template = new ControlTemplate(typeof(Page));
        template.SetVisualTree(static () => new Border
        {
            Name = "PART_PageChrome",
            Width = 300,
            Child = new ContentPresenter()
        });

        page.Template = template;
        Layout(page);

        Assert.Same(text, FindNamed<TextBlock>(page, null, static t => t.Text == "page"));
        Assert.Equal(BaseValueSource.Local, SourceOf(text, TextBlock.TextAlignmentProperty));
        Assert.Null(text.TemplatedParent);
        var chrome = Assert.IsType<Border>(page.FindName("PART_PageChrome"));
        Assert.Equal(BaseValueSource.ParentTemplate, SourceOf(chrome, FrameworkElement.WidthProperty));
    }

    [Fact]
    public void TemplateLiterals_RemainOverridableByTemplateTriggers()
    {
        var text = new TextBlock { Text = "app", TextAlignment = TextAlignment.Center };
        var host = new PanelHost { Content = text, Template = BuildTriggeredHostTemplate() };
        Layout(host);

        var chrome = Assert.IsType<Border>(host.FindName("PART_Chrome"));
        var inner = Assert.IsType<Border>(host.FindName("PART_Inner"));
        Assert.Equal(40.0, chrome.Width);
        Assert.Equal(BaseValueSource.ParentTemplate, SourceOf(chrome, FrameworkElement.WidthProperty));

        // Content this template wrote on a nested control that had already expanded its own template
        // is still a part of this template, not of the nested one.
        Assert.Same(host, inner.TemplatedParent);
        Assert.Equal(10.0, inner.Width);
        Assert.Equal(BaseValueSource.ParentTemplate, SourceOf(inner, FrameworkElement.WidthProperty));

        host.IsHighlighted = true;
        Assert.Equal(80.0, chrome.Width);
        Assert.Equal(20.0, inner.Width);
        Assert.Equal(BaseValueSource.ParentTemplateTrigger, SourceOf(chrome, FrameworkElement.WidthProperty));
        Assert.Equal(BaseValueSource.ParentTemplateTrigger, SourceOf(inner, FrameworkElement.WidthProperty));
        Assert.Equal(BaseValueSource.Local, SourceOf(text, TextBlock.TextAlignmentProperty));

        host.IsHighlighted = false;
        Assert.Equal(40.0, chrome.Width);
        Assert.Equal(10.0, inner.Width);
        Assert.Equal(BaseValueSource.ParentTemplate, SourceOf(chrome, FrameworkElement.WidthProperty));
        Assert.Equal(BaseValueSource.ParentTemplate, SourceOf(inner, FrameworkElement.WidthProperty));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DataTemplateTriggers_OverrideTheTemplatesLiterals(bool contentBeforeTemplateExpansion)
    {
        var item = new BadgeItem();
        var host = new PanelHost { ContentTemplate = BuildBadgeTemplate() };
        if (contentBeforeTemplateExpansion)
        {
            host.Content = item;
            host.Template = BuildHostTemplate(ContentProjection.ImplicitPresenter);
        }
        else
        {
            host.Template = BuildHostTemplate(ContentProjection.ImplicitPresenter);
            Layout(host);
            host.Content = item;
        }

        Layout(host);
        AssertBadgeFollowsTrigger(FindNamed<Border>(host, "ItemBadge"), item);
    }

    [Fact]
    public void DataTemplateTriggers_OverrideTheTemplatesLiterals_InDirectContentMode()
    {
        var item = new BadgeItem();
        var host = new ContentControl { ContentTemplate = BuildBadgeTemplate(), Content = item };
        Layout(host);

        AssertBadgeFollowsTrigger(FindNamed<Border>(host, "ItemBadge"), item);
    }

    [Fact]
    public void TriggerlessDataTemplateLiterals_AreNotTakenOverByTheHostTemplate()
    {
        var dataTemplate = new DataTemplate();
        dataTemplate.SetVisualTree(static () => new Border { Name = "PlainBadge", Width = 30 });
        var host = new PanelHost { ContentTemplate = dataTemplate, Content = new BadgeItem() };

        host.Template = BuildHostTemplate(ContentProjection.ImplicitPresenter);
        Layout(host);

        var badge = FindNamed<Border>(host, "PlainBadge");
        Assert.NotSame(host, badge.TemplatedParent);
        Assert.Equal(BaseValueSource.Local, SourceOf(badge, FrameworkElement.WidthProperty));
    }

    [Fact]
    public void PageCss_MatchesProjectedContent_ButNotTemplateParts()
    {
        var root = new StackPanel();
        Css.SetStyleSheet(root, ".probe { opacity: 0.5 }");
        var text = new TextBlock { Text = "app" };
        Css.SetClass(text, "probe");
        var host = new PanelHost
        {
            Content = text,
            Template = BuildHostTemplate(ContentProjection.ImplicitPresenter, chromeClass: "probe")
        };
        root.Children.Add(host);

        Layout(root);
        CssEvaluationScheduler.FlushIfPending(root.Dispatcher);

        Assert.Equal(0.5, text.Opacity);
        var chrome = Assert.IsType<Border>(host.FindName("PART_Chrome"));
        Assert.Equal(1.0, chrome.Opacity);
    }

    private static void AssertBadgeFollowsTrigger(Border badge, BadgeItem item)
    {
        Assert.Equal(30.0, badge.Width);
        Assert.Equal(BaseValueSource.ParentTemplate, SourceOf(badge, FrameworkElement.WidthProperty));

        item.IsActive = true;
        Assert.Equal(60.0, badge.Width);
        Assert.Equal(BaseValueSource.ParentTemplateTrigger, SourceOf(badge, FrameworkElement.WidthProperty));

        item.IsActive = false;
        Assert.Equal(30.0, badge.Width);
        Assert.Equal(BaseValueSource.ParentTemplate, SourceOf(badge, FrameworkElement.WidthProperty));
    }

    private static ControlTemplate BuildHostTemplate(ContentProjection projection, string? chromeClass = null)
    {
        var template = new ControlTemplate(typeof(PanelHost));
        template.SetVisualTree(() =>
        {
            var chrome = new Border { Name = "PART_Chrome", Width = 400 };
            if (chromeClass != null)
            {
                Css.SetClass(chrome, chromeClass);
            }

            chrome.Child = projection switch
            {
                ContentProjection.ImplicitPresenter => new ContentPresenter(),
                ContentProjection.ExplicitTemplateBinding =>
                    BindContent(new ContentPresenter(), ContentPresenter.ContentProperty),
                _ => BindContent(new ContentControl(), ContentControl.ContentProperty),
            };
            return chrome;
        });
        return template;
    }

    private static FrameworkElement BindContent(FrameworkElement element, DependencyProperty contentProperty)
    {
        element.SetTemplateBinding(contentProperty, ContentControl.ContentProperty);
        return element;
    }

    private static ControlTemplate BuildTriggeredHostTemplate()
    {
        var template = new ControlTemplate(typeof(PanelHost));
        template.SetVisualTree(static () =>
        {
            var root = new StackPanel();
            root.Children.Add(new Border { Name = "PART_Chrome", Width = 40 });

            // Expands its own template while the outer template is still being loaded.
            var innerTemplate = new ControlTemplate(typeof(InnerHost));
            innerTemplate.SetVisualTree(static () => new Border { Child = new ContentPresenter() });
            var nested = new InnerHost { Content = new Border { Name = "PART_Inner", Width = 10 } };
            nested.Template = innerTemplate;
            root.Children.Add(nested);

            root.Children.Add(new ContentPresenter());
            return root;
        });

        var trigger = new Trigger { Property = PanelHost.IsHighlightedProperty, Value = true };
        trigger.Setters.Add(new Setter(FrameworkElement.WidthProperty, 80.0, "PART_Chrome"));
        trigger.Setters.Add(new Setter(FrameworkElement.WidthProperty, 20.0, "PART_Inner"));
        template.Triggers.Add(trigger);
        return template;
    }

    private static DataTemplate BuildBadgeTemplate()
    {
        var template = new DataTemplate();
        template.SetVisualTree(static () =>
        {
            var root = new StackPanel();
            root.Children.Add(new Border { Name = "ItemBadge", Width = 30 });
            return root;
        });

        var trigger = new DataTrigger { Binding = new Binding(nameof(BadgeItem.IsActive)), Value = true };
        trigger.Setters.Add(new Setter(FrameworkElement.WidthProperty, 60.0, "ItemBadge"));
        template.Triggers.Add(trigger);
        return template;
    }

    private static T FindNamed<T>(Visual root, string? name, Func<T, bool>? match = null)
        where T : FrameworkElement
    {
        var pending = new Queue<Visual>();
        pending.Enqueue(root);
        while (pending.TryDequeue(out var visual))
        {
            if (visual is T candidate &&
                (name is null || candidate.Name == name) &&
                (match is null || match(candidate)))
            {
                return candidate;
            }

            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(visual); i++)
            {
                if (VisualTreeHelper.GetChild(visual, i) is Visual child)
                {
                    pending.Enqueue(child);
                }
            }
        }

        throw new Xunit.Sdk.XunitException($"No {typeof(T).Name} named '{name}' under {root.GetType().Name}.");
    }

    private static BaseValueSource SourceOf(DependencyObject element, DependencyProperty property) =>
        DependencyPropertyHelper.GetValueSource(element, property).BaseValueSource;

    private static void Layout(UIElement root)
    {
        root.Measure(new Size(600, 400));
        root.Arrange(new Rect(0, 0, 600, 400));
    }

    private sealed class PanelHost : ContentControl
    {
        public static readonly DependencyProperty IsHighlightedProperty =
            DependencyProperty.Register(
                nameof(IsHighlighted),
                typeof(bool),
                typeof(PanelHost),
                new PropertyMetadata(false));

        public PanelHost() => UseTemplateContentManagement();

        public bool IsHighlighted
        {
            get => (bool)GetValue(IsHighlightedProperty)!;
            set => SetValue(IsHighlightedProperty, value);
        }
    }

    private sealed class InnerHost : ContentControl
    {
        public InnerHost() => UseTemplateContentManagement();
    }

    private sealed class BadgeItem : INotifyPropertyChanged
    {
        private bool _isActive;

        public event PropertyChangedEventHandler? PropertyChanged;

        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (_isActive == value)
                    return;
                _isActive = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));
            }
        }
    }
}

using System.ComponentModel;
using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public sealed class TemplateValuePromotionBoundaryTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void BoundContentAlreadyResolvedDuringDataTemplateLoad_KeepsLocalValues(bool directContent, bool nestedControl)
    {
        var text = new TextBlock { TextAlignment = TextAlignment.Center };
        var item = new Item { Visual = text };
        var template = TriggeredTemplate(() =>
        {
            FrameworkElement presenter = nestedControl ? new ContentControl() : new ContentPresenter();
            presenter.SetBinding(nestedControl ? ContentControl.ContentProperty : ContentPresenter.ContentProperty,
                new Binding(nameof(Item.Visual)) { Source = item });
            return new Border { Width = 30, Child = presenter };
        });

        FrameworkElement host = directContent
            ? new ContentControl { ContentTemplate = template, Content = item }
            : new ContentPresenter { ContentTemplate = template, Content = item };
        var root = Assert.IsType<Border>(VisualTreeHelper.GetChild(host, 0));

        AssertApplicationValue(text);
        AssertTriggerCycle(root, item);
        AssertApplicationValue(text);
    }

    [Fact]
    public void NestedDataTemplateContent_IsNotPromotedByTheOuterDataTemplate()
    {
        var innerTemplate = new DataTemplate();
        innerTemplate.SetVisualTree(static () => new Border { Width = 20 });
        var outerTemplate = TriggeredTemplate(() => new Border
        {
            Width = 30,
            Child = new ContentPresenter { ContentTemplate = innerTemplate, Content = new Item() }
        });
        var item = new Item();
        var presenter = new ContentPresenter { ContentTemplate = outerTemplate, Content = item };
        var outerRoot = Assert.IsType<Border>(VisualTreeHelper.GetChild(presenter, 0));
        var innerPresenter = Assert.IsType<ContentPresenter>(outerRoot.Child);
        var innerRoot = Assert.IsType<Border>(VisualTreeHelper.GetChild(innerPresenter, 0));

        Assert.True(innerRoot.HasLocalValue(FrameworkElement.WidthProperty));
        Assert.Equal(BaseValueSource.Local, SourceOf(innerRoot, FrameworkElement.WidthProperty));
        AssertTriggerCycle(outerRoot, item);
        Assert.Equal(20.0, innerRoot.Width);
    }

    [Fact]
    public void DataTemplateLoadedInsideControlTemplate_KeepsItsOwnScopeAndTriggers()
    {
        var item = new Item();
        var dataTemplate = TriggeredTemplate(static () => new Border { Name = "DataRoot", Width = 30 });
        var template = new ControlTemplate(typeof(TemplateHost));
        template.SetVisualTree(() => new ContentPresenter { ContentTemplate = dataTemplate, Content = item });
        var host = new TemplateHost { Template = template };
        var presenter = Assert.IsType<ContentPresenter>(VisualTreeHelper.GetChild(host, 0));
        var root = Assert.IsType<Border>(VisualTreeHelper.GetChild(presenter, 0));

        Assert.Null(root.TemplatedParent);
        Assert.Null(host.FindName("DataRoot"));
        Assert.Same(item, root.DataContext);
        Assert.True(root.HasLocalValue(FrameworkElement.DataContextProperty));
        AssertTriggerCycle(root, item);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BoundContentAlreadyResolvedDuringControlTemplateLoad_KeepsLocalValues(bool nestedControl)
    {
        var text = new TextBlock { TextAlignment = TextAlignment.Center };
        var item = new Item { Visual = text };
        var template = new ControlTemplate(typeof(TemplateHost));
        template.SetVisualTree(() =>
        {
            FrameworkElement presenter = nestedControl ? new ContentControl() : new ContentPresenter();
            presenter.SetBinding(nestedControl ? ContentControl.ContentProperty : ContentPresenter.ContentProperty,
                new Binding(nameof(Item.Visual)) { Source = item });
            return new Border { Child = presenter };
        });

        var host = new TemplateHost { Template = template };

        AssertApplicationValue(text);
        text.ClearValue(TextBlock.TextAlignmentProperty);
        Assert.Equal(TextAlignment.Left, text.TextAlignment);
        Assert.Equal(BaseValueSource.Default, SourceOf(text, TextBlock.TextAlignmentProperty));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResourceContentAlreadyResolvedDuringTemplateLoad_KeepsLocalValues(bool dataTemplate)
    {
        var text = new TextBlock { TextAlignment = TextAlignment.Center };
        ContentPresenter? contentPresenter = null;
        FrameworkElement Factory()
        {
            contentPresenter = new ContentPresenter();
            contentPresenter.Resources["ApplicationContent"] = text;
            contentPresenter.SetResourceReference(ContentPresenter.ContentProperty, "ApplicationContent");
            return new Border { Width = 30, Child = contentPresenter };
        }

        FrameworkElement host;
        if (dataTemplate)
        {
            var item = new Item();
            host = new ContentPresenter { ContentTemplate = TriggeredTemplate(Factory), Content = item };
            AssertTriggerCycle(Assert.IsType<Border>(VisualTreeHelper.GetChild(host, 0)), item);
        }
        else
        {
            var template = new ControlTemplate(typeof(TemplateHost));
            template.SetVisualTree(Factory);
            host = new TemplateHost { Template = template };
        }

        AssertApplicationValue(text);
        Assert.NotNull(contentPresenter);
        Assert.Same(text, contentPresenter.Content);
        var replacement = new TextBlock { TextAlignment = TextAlignment.Center };
        contentPresenter.Resources["ApplicationContent"] = replacement;
        Assert.Same(replacement, contentPresenter.Content);
        AssertApplicationValue(replacement);
        replacement.ClearValue(TextBlock.TextAlignmentProperty);
        Assert.Equal(TextAlignment.Left, replacement.TextAlignment);
    }

    [Fact]
    public void LiteralContentPresenterContent_StillBelongsToTheControlTemplate()
    {
        var template = new ControlTemplate(typeof(ContentControl));
        template.SetVisualTree(static () => new ContentPresenter
        {
            Content = new Border { Name = "LiteralContent", Width = 30 }
        });
        var trigger = new Trigger { Property = FrameworkElement.TagProperty, Value = true };
        trigger.Setters.Add(new Setter(FrameworkElement.WidthProperty, 60.0, "LiteralContent"));
        template.Triggers.Add(trigger);
        var host = new ContentControl { Template = template };
        var content = Assert.IsType<Border>(host.FindName("LiteralContent"));

        Assert.Same(host, content.TemplatedParent);
        Assert.Equal(BaseValueSource.ParentTemplate, SourceOf(content, FrameworkElement.WidthProperty));
        host.Tag = true;
        Assert.Equal(60.0, content.Width);
        host.Tag = false;
        Assert.Equal(30.0, content.Width);
    }

    [Fact]
    public void ReapplyingTemplateAndReplacingApplicationContent_PreservesLocalBindingsAndClearValue()
    {
        var style = new Style(typeof(TextBlock));
        style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
        var first = new TextBlock { TextAlignment = TextAlignment.Center, Style = style };
        var model = new Item { Label = "bound" };
        first.SetBinding(TextBlock.TextProperty, new Binding(nameof(Item.Label)) { Source = model });
        var host = new TemplateHost { Content = first, Template = PresenterTemplate() };

        host.Template = PresenterTemplate();
        AssertApplicationValue(first);
        Assert.NotNull(first.GetBindingExpression(TextBlock.TextProperty));
        Assert.True(first.HasLocalOrAnimatedValue(TextBlock.TextProperty));
        Assert.Equal("bound", first.Text);
        model.Label = "updated";
        Assert.Equal("updated", first.Text);
        Assert.True(first.HasLocalValue(TextBlock.TextProperty));

        var replacement = new TextBlock { TextAlignment = TextAlignment.Center, Style = style };
        host.Content = replacement;
        host.Template = PresenterTemplate();
        AssertApplicationValue(replacement);
        replacement.ClearValue(TextBlock.TextAlignmentProperty);
        Assert.Equal(TextAlignment.Right, replacement.TextAlignment);
        Assert.Equal(BaseValueSource.Style, SourceOf(replacement, TextBlock.TextAlignmentProperty));
    }

    private static ControlTemplate PresenterTemplate()
    {
        var template = new ControlTemplate(typeof(TemplateHost));
        template.SetVisualTree(static () => new ContentPresenter());
        return template;
    }

    private static DataTemplate TriggeredTemplate(Func<FrameworkElement> factory)
    {
        var template = new DataTemplate();
        template.SetVisualTree(factory);
        var trigger = new DataTrigger { Binding = new Binding(nameof(Item.IsActive)), Value = true };
        trigger.Setters.Add(new Setter(FrameworkElement.WidthProperty, 60.0));
        template.Triggers.Add(trigger);
        return template;
    }

    private static void AssertApplicationValue(TextBlock text)
    {
        Assert.True(text.HasLocalValue(TextBlock.TextAlignmentProperty));
        Assert.True(text.HasLocalOrAnimatedValue(TextBlock.TextAlignmentProperty));
        Assert.Equal(BaseValueSource.Local, SourceOf(text, TextBlock.TextAlignmentProperty));
        Assert.Equal(TextAlignment.Center, text.TextAlignment);
        Assert.Null(text.TemplatedParent);
    }

    private static void AssertTriggerCycle(Border root, Item item)
    {
        Assert.Equal(30.0, root.Width);
        Assert.Equal(BaseValueSource.ParentTemplate, SourceOf(root, FrameworkElement.WidthProperty));
        for (var i = 0; i < 2; i++)
        {
            item.IsActive = true;
            Assert.Equal(60.0, root.Width);
            Assert.Equal(BaseValueSource.ParentTemplateTrigger, SourceOf(root, FrameworkElement.WidthProperty));
            item.IsActive = false;
            Assert.Equal(30.0, root.Width);
            Assert.Equal(BaseValueSource.ParentTemplate, SourceOf(root, FrameworkElement.WidthProperty));
        }

        root.Width = 90;
        item.IsActive = true;
        Assert.Equal(90.0, root.Width);
        Assert.Equal(BaseValueSource.Local, SourceOf(root, FrameworkElement.WidthProperty));
        root.ClearValue(FrameworkElement.WidthProperty);
        Assert.Equal(60.0, root.Width);
        item.IsActive = false;
        Assert.Equal(30.0, root.Width);
    }

    private static BaseValueSource SourceOf(DependencyObject element, DependencyProperty property) =>
        DependencyPropertyHelper.GetValueSource(element, property).BaseValueSource;

    private sealed class TemplateHost : ContentControl
    {
        public TemplateHost() => UseTemplateContentManagement();
    }

    private sealed class Item : INotifyPropertyChanged
    {
        private bool _isActive;
        private string? _label;
        public FrameworkElement? Visual { get; init; }
        public event PropertyChangedEventHandler? PropertyChanged;

        public string? Label
        {
            get => _label;
            set
            {
                if (_label == value) return;
                _label = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
            }
        }

        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (_isActive == value) return;
                _isActive = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));
            }
        }
    }
}

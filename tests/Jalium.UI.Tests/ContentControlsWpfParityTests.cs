using System.Collections;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

public sealed class ContentControlsWpfParityTests
{
    [Fact]
    public void ContentControl_ImplementsMarkupFormattingLogicalContentAndVirtualHooks()
    {
        Assert.True(typeof(IAddChild).IsAssignableFrom(typeof(ContentControl)));
        AssertField<ContentControl>(nameof(ContentControl.ContentStringFormatProperty));
        AssertField<ContentControl>(nameof(ContentControl.HasContentProperty));
        AssertVirtual<ContentControl>("AddChild", typeof(object));
        AssertVirtual<ContentControl>("AddText", typeof(string));
        AssertVirtual<ContentControl>("OnContentStringFormatChanged", typeof(string), typeof(string));
        AssertVirtual<ContentControl>("OnContentTemplateChanged", typeof(DataTemplate), typeof(DataTemplate));
        AssertVirtual<ContentControl>("OnContentTemplateSelectorChanged", typeof(Controls.DataTemplateSelector), typeof(Controls.DataTemplateSelector));

        var control = new TrackingContentControl { ContentStringFormat = "value={0:0.0}" };
        control.Content = 12.34;

        Assert.True(control.HasContent);
        Assert.True(control.ShouldSerializeContent());
        Assert.Contains(control.Content, control.GetLogicalChildren());
        Assert.Equal("value=12.3", Assert.IsType<TextBlock>(control.ExposedContentElement).Text);

        control.ContentTemplate = new DataTemplate();
        control.ContentTemplateSelector = new Controls.DataTemplateSelector();
        Assert.Equal(1, control.TemplateChangedCalls);
        Assert.Equal(1, control.SelectorChangedCalls);
        Assert.Equal(1, control.StringFormatChangedCalls);
        Assert.Equal(1, control.ContentChangedCalls);

        var markupControl = new TrackingContentControl();
        var addChild = (IAddChild)markupControl;
        addChild.AddText("one");
        addChild.AddText(" two");
        Assert.Equal("one two", markupControl.Content);
        Assert.Throws<InvalidOperationException>(() => addChild.AddChild(new object()));

        control.Content = null;
        Assert.False(control.HasContent);
        Assert.Equal(2, control.ContentChangedCalls);
    }

    [Fact]
    public void ContentPresenter_ChoosesTemplatesFormatsTextAndRecognizesAccessKeys()
    {
        foreach (var field in new[]
                 {
                     nameof(ContentPresenter.ContentStringFormatProperty),
                     nameof(ContentPresenter.RecognizesAccessKeyProperty),
                 })
        {
            AssertField<ContentPresenter>(field);
        }

        AssertVirtual<ContentPresenter>("ChooseTemplate");
        AssertVirtual<ContentPresenter>("OnContentStringFormatChanged", typeof(string), typeof(string));
        AssertVirtual<ContentPresenter>("OnContentTemplateChanged", typeof(DataTemplate), typeof(DataTemplate));
        AssertVirtual<ContentPresenter>("OnContentTemplateSelectorChanged", typeof(Controls.DataTemplateSelector), typeof(Controls.DataTemplateSelector));
        AssertVirtual<ContentPresenter>("OnTemplateChanged", typeof(DataTemplate), typeof(DataTemplate));

        var template = new DataTemplate();
        template.SetVisualTree(() => new Border());
        var presenter = new TrackingContentPresenter
        {
            Content = 3.25,
            ContentStringFormat = "{0:0.00}",
            ContentTemplate = template,
        };

        Assert.Same(template, presenter.ExposedChooseTemplate());
        Assert.IsType<Border>(presenter.GetVisualChild(0));
        Assert.True(presenter.ShouldSerializeContentTemplateSelector() == false);

        presenter.ContentTemplate = null;
        Assert.Equal("3.25", Assert.IsType<TextBlock>(presenter.GetVisualChild(0)).Text);

        presenter.ContentStringFormat = null;
        presenter.RecognizesAccessKey = true;
        presenter.Content = "_File";
        Assert.Equal("_File", Assert.IsType<AccessText>(presenter.GetVisualChild(0)).Text);
        Assert.True(presenter.TemplateChangedCalls >= 2);
        Assert.True(presenter.StringFormatChangedCalls >= 2);
    }

    [Fact]
    public void HeaderedControls_TrackHeaderStateCallbacksAndLogicalChildren()
    {
        AssertHeaderSurface<HeaderedContentControl>();
        AssertHeaderSurface<HeaderedItemsControl>();

        var header = new Border();
        var content = new TextBlock { Text = "content" };
        var contentControl = new TrackingHeaderedContentControl
        {
            Content = content,
            Header = header,
            HeaderStringFormat = "[{0}]",
            HeaderTemplate = new DataTemplate(),
            HeaderTemplateSelector = new Controls.DataTemplateSelector(),
        };

        Assert.True(contentControl.HasHeader);
        Assert.Equal(1, contentControl.HeaderChangedCalls);
        Assert.Equal(1, contentControl.HeaderTemplateChangedCalls);
        Assert.Equal(1, contentControl.HeaderSelectorChangedCalls);
        Assert.Equal(1, contentControl.HeaderStringFormatChangedCalls);
        Assert.Equal(new object[] { header, content }, contentControl.GetLogicalChildren());
        Assert.Equal(header.ToString(), contentControl.ToString());

        var itemsControl = new TrackingHeaderedItemsControl { Header = "items" };
        Assert.True(itemsControl.HasHeader);
        Assert.Contains("items", itemsControl.GetLogicalChildren());
        itemsControl.Header = null;
        Assert.False(itemsControl.HasHeader);
        Assert.Equal(2, itemsControl.HeaderChangedCalls);
    }

    [Fact]
    public void GroupItem_StoresPerItemValuesAndReportsVirtualizationState()
    {
        Assert.True(typeof(IContainItemStorage).IsAssignableFrom(typeof(GroupItem)));
        Assert.True(typeof(IHierarchicalVirtualizationAndScrollInfo).IsAssignableFrom(typeof(GroupItem)));

        var group = new GroupItem();
        var storage = (IContainItemStorage)group;
        var first = new object();
        var second = new object();

        storage.StoreItemValue(first, FrameworkElement.TagProperty, "first");
        storage.StoreItemValue(second, FrameworkElement.TagProperty, "second");
        storage.StoreItemValue(42, FrameworkElement.TagProperty, "boxed");
        Assert.Equal("first", storage.ReadItemValue(first, FrameworkElement.TagProperty));
        Assert.Equal("boxed", storage.ReadItemValue(42, FrameworkElement.TagProperty));
        storage.ClearItemValue(first, FrameworkElement.TagProperty);
        Assert.Same(DependencyProperty.UnsetValue, storage.ReadItemValue(first, FrameworkElement.TagProperty));
        storage.ClearValue(FrameworkElement.TagProperty);
        Assert.Same(DependencyProperty.UnsetValue, storage.ReadItemValue(second, FrameworkElement.TagProperty));

        var panel = new StackPanel { IsItemsHost = true };
        group.Content = panel;
        group.Measure(new Size(300, 200));

        var virtualization = (IHierarchicalVirtualizationAndScrollInfo)group;
        var constraints = new HierarchicalVirtualizationConstraints(
            new VirtualizationCacheLength(1),
            VirtualizationCacheLengthUnit.Item,
            new Rect(0, 0, 300, 200));
        virtualization.Constraints = constraints;
        virtualization.ItemDesiredSizes = new HierarchicalVirtualizationItemDesiredSizes();
        virtualization.MustDisableVirtualization = true;
        virtualization.InBackgroundLayout = true;

        Assert.Equal(constraints, virtualization.Constraints);
        Assert.Same(panel, virtualization.ItemsHost);
        Assert.True(virtualization.MustDisableVirtualization);
        Assert.True(virtualization.InBackgroundLayout);
        Assert.Equal(default, virtualization.HeaderDesiredSizes.PixelSize);
        Assert.Equal(default, virtualization.HeaderDesiredSizes.LogicalSize);
    }

    private static void AssertHeaderSurface<T>()
    {
        foreach (var field in new[]
                 {
                     "HasHeaderProperty", "HeaderStringFormatProperty",
                     "HeaderTemplateProperty", "HeaderTemplateSelectorProperty",
                 })
        {
            AssertField<T>(field);
        }

        AssertVirtual<T>("OnHeaderChanged", typeof(object), typeof(object));
        AssertVirtual<T>("OnHeaderStringFormatChanged", typeof(string), typeof(string));
        AssertVirtual<T>("OnHeaderTemplateChanged", typeof(DataTemplate), typeof(DataTemplate));
        AssertVirtual<T>("OnHeaderTemplateSelectorChanged", typeof(Controls.DataTemplateSelector), typeof(Controls.DataTemplateSelector));
    }

    private static void AssertField<T>(string name)
    {
        var field = typeof(T).GetField(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
        Assert.NotNull(field);
        Assert.True(field!.IsInitOnly);
        Assert.IsType<DependencyProperty>(field.GetValue(null));
    }

    private static void AssertVirtual<T>(string name, params Type[] parameterTypes)
    {
        var method = typeof(T).GetMethod(
            name,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly,
            null,
            parameterTypes,
            null);
        Assert.NotNull(method);
        Assert.True(method!.IsVirtual);
        Assert.True(method.IsFamily);
    }

    private sealed class TrackingContentControl : ContentControl
    {
        public int ContentChangedCalls { get; private set; }
        public int StringFormatChangedCalls { get; private set; }
        public int TemplateChangedCalls { get; private set; }
        public int SelectorChangedCalls { get; private set; }
        public UIElement? ExposedContentElement => ContentElement;
        public object?[] GetLogicalChildren() => Enumerate(LogicalChildren);

        protected override void OnContentChanged(object? oldValue, object? newValue)
        {
            ContentChangedCalls++;
            base.OnContentChanged(oldValue, newValue);
        }

        protected override void OnContentStringFormatChanged(string? oldValue, string? newValue) => StringFormatChangedCalls++;
        protected override void OnContentTemplateChanged(DataTemplate? oldValue, DataTemplate? newValue) => TemplateChangedCalls++;
        protected override void OnContentTemplateSelectorChanged(Controls.DataTemplateSelector? oldValue, Controls.DataTemplateSelector? newValue) => SelectorChangedCalls++;
    }

    private sealed class TrackingContentPresenter : ContentPresenter
    {
        public int StringFormatChangedCalls { get; private set; }
        public int TemplateChangedCalls { get; private set; }
        public DataTemplate? ExposedChooseTemplate() => ChooseTemplate();

        protected override void OnContentStringFormatChanged(string? oldValue, string? newValue) => StringFormatChangedCalls++;
        protected override void OnTemplateChanged(DataTemplate? oldValue, DataTemplate? newValue) => TemplateChangedCalls++;
    }

    private sealed class TrackingHeaderedContentControl : HeaderedContentControl
    {
        public int HeaderChangedCalls { get; private set; }
        public int HeaderStringFormatChangedCalls { get; private set; }
        public int HeaderTemplateChangedCalls { get; private set; }
        public int HeaderSelectorChangedCalls { get; private set; }
        public object?[] GetLogicalChildren() => Enumerate(LogicalChildren);

        protected override void OnHeaderChanged(object? oldValue, object? newValue)
        {
            HeaderChangedCalls++;
            base.OnHeaderChanged(oldValue, newValue);
        }

        protected override void OnHeaderStringFormatChanged(string? oldValue, string? newValue)
        {
            HeaderStringFormatChangedCalls++;
            base.OnHeaderStringFormatChanged(oldValue, newValue);
        }

        protected override void OnHeaderTemplateChanged(DataTemplate? oldValue, DataTemplate? newValue)
        {
            HeaderTemplateChangedCalls++;
            base.OnHeaderTemplateChanged(oldValue, newValue);
        }

        protected override void OnHeaderTemplateSelectorChanged(Controls.DataTemplateSelector? oldValue, Controls.DataTemplateSelector? newValue)
        {
            HeaderSelectorChangedCalls++;
            base.OnHeaderTemplateSelectorChanged(oldValue, newValue);
        }
    }

    private sealed class TrackingHeaderedItemsControl : HeaderedItemsControl
    {
        public int HeaderChangedCalls { get; private set; }
        public object?[] GetLogicalChildren() => Enumerate(LogicalChildren);

        protected override void OnHeaderChanged(object? oldValue, object? newValue)
        {
            HeaderChangedCalls++;
            base.OnHeaderChanged(oldValue, newValue);
        }
    }

    private static object?[] Enumerate(IEnumerator enumerator)
    {
        var values = new List<object?>();
        while (enumerator.MoveNext())
        {
            values.Add(enumerator.Current);
        }
        return values.ToArray();
    }
}

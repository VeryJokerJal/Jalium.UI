using System.ComponentModel;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Input;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;

namespace Jalium.UI.Tests;

public class FrameworkElementSurfaceTests
{
    [Fact]
    public void Surface_UsesWpfCompatibleTypesAndModifiers()
    {
        var type = typeof(FrameworkElement);

        Assert.Equal(typeof(InputScope), type.GetProperty(nameof(FrameworkElement.InputScope))!.PropertyType);
        Assert.Equal(typeof(XmlLanguage), type.GetProperty(nameof(FrameworkElement.Language))!.PropertyType);
        Assert.Equal(typeof(TriggerCollection), type.GetProperty(nameof(FrameworkElement.Triggers))!.PropertyType);
        Assert.Equal(typeof(DependencyObject), type.GetProperty(nameof(FrameworkElement.TemplatedParent))!.PropertyType);
        Assert.Equal(typeof(ContextMenuEventHandler), type.GetEvent(nameof(FrameworkElement.ContextMenuOpening))!.EventHandlerType);
        Assert.Equal(typeof(ToolTipEventHandler), type.GetEvent(nameof(FrameworkElement.ToolTipOpening))!.EventHandlerType);
        Assert.Equal(typeof(EventHandler<DataTransferEventArgs>), type.GetEvent(nameof(FrameworkElement.TargetUpdated))!.EventHandlerType);

        var registerName = type.GetMethod(nameof(FrameworkElement.RegisterName), [typeof(string), typeof(object)]);
        Assert.NotNull(registerName);

        var getBindingExpression = type.GetMethod(
            nameof(FrameworkElement.GetBindingExpression),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            binder: null,
            [typeof(DependencyProperty)],
            modifiers: null);
        Assert.Equal(typeof(BindingExpression), getBindingExpression!.ReturnType);

        var arrangeCore = type.GetMethod("ArrangeCore", BindingFlags.NonPublic | BindingFlags.Instance)!;
        Assert.True(arrangeCore.IsVirtual);
        Assert.True(arrangeCore.IsFinal);

        var visualParentChanged = type.GetMethod(
            "OnVisualParentChanged",
            BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly,
            binder: null,
            [typeof(DependencyObject)],
            modifiers: null)!;
        Assert.True(visualParentChanged.IsFamilyOrAssembly);
        Assert.True(visualParentChanged.IsVirtual);

        Assert.Equal(3, type.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Count(static method => method.Name == nameof(FrameworkElement.BeginStoryboard)));
    }

    [Fact]
    public void LanguageAndInputScope_AreInheritedDependencyProperties()
    {
        var parent = new ProbeElement();
        var child = new ProbeElement();
        parent.AddChild(child);

        var language = XmlLanguage.GetLanguage("zh-CN");
        var scope = new InputScope();
        scope.Names.Add(new InputScopeName(InputScopeNameValue.EmailSmtpAddress));
        parent.Language = language;
        parent.InputScope = scope;

        Assert.Same(language, child.Language);
        Assert.Same(scope, child.InputScope);
        Assert.Same(FrameworkElement.InputScopeProperty, InputMethod.InputScopeProperty);
        Assert.Same(scope, InputMethod.GetInputScope(child));
    }

    [Fact]
    public void DirectTriggers_AttachDetachAndSerializeFromTheirOwner()
    {
        var element = new ProbeElement();
        var action = new RecordingTriggerAction();
        var trigger = new EventTrigger(FrameworkElement.LoadedEvent);
        trigger.Actions.Add(action);

        Assert.False(element.ShouldSerializeTriggers());
        element.Triggers.Add(trigger);
        Assert.True(element.ShouldSerializeTriggers());

        element.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, element));
        Assert.Equal(1, action.InvocationCount);

        element.Triggers.Remove(trigger);
        element.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent, element));
        Assert.Equal(1, action.InvocationCount);
        Assert.False(element.ShouldSerializeTriggers());
    }

    [Fact]
    public void ServiceEvents_UseOneRoutedIdentityAndInvokeClrAndClassHooks()
    {
        var element = new ProbeElement();
        var toolTipClr = 0;
        var contextMenuClr = 0;
        element.ToolTipOpening += (_, _) => toolTipClr++;
        element.ContextMenuOpening += (_, _) => contextMenuClr++;

        Assert.Same(FrameworkElement.ToolTipOpeningEvent, ToolTipService.ToolTipOpeningEvent);
        Assert.Same(FrameworkElement.ContextMenuOpeningEvent, ContextMenuService.ContextMenuOpeningEvent);

        element.RaiseEvent(new ToolTipEventArgs(FrameworkElement.ToolTipOpeningEvent) { Source = element });
        element.RaiseEvent(new ContextMenuEventArgs(element, opening: true)
        {
            RoutedEvent = FrameworkElement.ContextMenuOpeningEvent,
        });

        Assert.Equal(1, toolTipClr);
        Assert.Equal(1, contextMenuClr);
        Assert.Equal(1, element.ToolTipOpeningHookCount);
        Assert.Equal(1, element.ContextMenuOpeningHookCount);
    }

    [Fact]
    public void BindingPathOverloadAndTransferEvents_AreFunctional()
    {
        var source = new BindingSource { Value = "initial" };
        var target = new ProbeElement { DataContext = source };
        var targetUpdates = 0;
        var sourceUpdates = 0;
        target.TargetUpdated += (_, e) =>
        {
            Assert.Same(target, e.TargetObject);
            Assert.Same(FrameworkElement.TagProperty, e.Property);
            targetUpdates++;
        };
        target.SourceUpdated += (_, _) => sourceUpdates++;

        var expression = target.SetBinding(FrameworkElement.TagProperty, new Binding(nameof(BindingSource.Value))
        {
            Mode = BindingMode.TwoWay,
            NotifyOnTargetUpdated = true,
            NotifyOnSourceUpdated = true,
        });

        Assert.Same(expression, target.GetBindingExpression(FrameworkElement.TagProperty));
        Assert.Equal("initial", target.Tag);
        Assert.Equal(1, targetUpdates);

        target.Tag = "updated";
        Assert.Equal("updated", source.Value);
        Assert.Equal(1, sourceUpdates);

        var pathExpression = target.SetBinding(FrameworkElement.NameProperty, nameof(BindingSource.Value));
        Assert.IsType<BindingExpression>(pathExpression);
        Assert.Equal("updated", target.Name);
    }

    [Fact]
    public void ParentLayoutAndStyleHooks_AreDrivenByFrameworkChanges()
    {
        var parent = new ProbeElement();
        var child = new ProbeElement();
        parent.AddChild(child);

        child.SetValue(ParentAffectingProperty, 1);
        Assert.Equal(1, parent.ParentLayoutInvalidatedCount);

        child.Style = new Style(typeof(ProbeElement));
        Assert.Equal(1, child.StyleChangedCount);
    }

    [Fact]
    public void BeginStoryboard_UsesTheElementAsItsContainingObject()
    {
        var element = new ProbeElement { Opacity = 0 };
        var animation = new DoubleAnimation
        {
            From = 0,
            To = 1,
            Duration = new Duration(TimeSpan.FromSeconds(1)),
        };
        Storyboard.SetTarget(animation, element);
        Storyboard.SetTargetProperty(animation, new PropertyPath(nameof(UIElement.Opacity)));
        var storyboard = new Storyboard();
        storyboard.Children.Add(animation);

        element.BeginStoryboard(storyboard, Jalium.UI.Media.Animation.HandoffBehavior.SnapshotAndReplace, isControllable: true);

        Assert.True(element.HasAnimation(UIElement.OpacityProperty));
        storyboard.Stop();
        Assert.False(element.HasAnimation(UIElement.OpacityProperty));
    }

    [Fact]
    public void XmlLanguage_NormalizesCachesConvertsAndRejectsMalformedTags()
    {
        var first = XmlLanguage.GetLanguage("EN-us");
        var second = XmlLanguage.GetLanguage("en-US");
        var converter = TypeDescriptor.GetConverter(typeof(XmlLanguage));

        Assert.Same(first, second);
        Assert.Equal("en-us", first.IetfLanguageTag);
        Assert.Equal("en-us", converter.ConvertToInvariantString(first));
        Assert.Same(first, converter.ConvertFromInvariantString("EN-US"));
        Assert.Throws<ArgumentException>(() => XmlLanguage.GetLanguage("en-"));
        Assert.Throws<ArgumentException>(() => XmlLanguage.GetLanguage("zh_zh"));
    }

    [Fact]
    public void ReplacingControlTemplate_ReleasesNamesOwnedByTheOldTemplateInstance()
    {
        var firstTemplate = new ControlTemplate(typeof(Control));
        firstTemplate.SetVisualTree(() => new Border { Name = "ThumbBorder" });
        var secondTemplate = new ControlTemplate(typeof(Control));
        secondTemplate.SetVisualTree(() => new Border { Name = "ThumbBorder" });
        var control = new Control { Template = firstTemplate };

        var firstPart = Assert.IsType<Border>(control.FindName("ThumbBorder"));
        control.Template = secondTemplate;
        var secondPart = Assert.IsType<Border>(control.FindName("ThumbBorder"));

        Assert.NotSame(firstPart, secondPart);
        Assert.Same(secondPart, control.FindName("ThumbBorder"));
    }

    private static readonly DependencyProperty ParentAffectingProperty =
        DependencyProperty.RegisterAttached(
            "ParentAffecting",
            typeof(int),
            typeof(FrameworkElementSurfaceTests),
            new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    private sealed class ProbeElement : FrameworkElement
    {
        public int ToolTipOpeningHookCount { get; private set; }
        public int ContextMenuOpeningHookCount { get; private set; }
        public int ParentLayoutInvalidatedCount { get; private set; }
        public int StyleChangedCount { get; private set; }

        public void AddChild(Visual child) => AddVisualChild(child);

        protected override void OnToolTipOpening(ToolTipEventArgs e)
        {
            ToolTipOpeningHookCount++;
            base.OnToolTipOpening(e);
        }

        protected override void OnContextMenuOpening(ContextMenuEventArgs e)
        {
            ContextMenuOpeningHookCount++;
            base.OnContextMenuOpening(e);
        }

        protected internal override void ParentLayoutInvalidated(UIElement child)
        {
            ParentLayoutInvalidatedCount++;
            base.ParentLayoutInvalidated(child);
        }

        protected internal override void OnStyleChanged(Style? oldStyle, Style? newStyle)
        {
            StyleChangedCount++;
            base.OnStyleChanged(oldStyle, newStyle);
        }
    }

    private sealed class RecordingTriggerAction : TriggerAction
    {
        public int InvocationCount { get; private set; }

        internal override void Invoke(FrameworkElement? element)
        {
            InvocationCount++;
        }
    }

    private sealed class BindingSource
    {
        public string Value { get; set; } = string.Empty;
    }
}

using System.Collections;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.Serialization;
using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Markup;
using Jalium.UI.Media.Media3D;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection(nameof(WpfParityFoundationBehaviorCollection))]
public sealed class CoreThreadingDataMedia3DWpfParityTests
{
    [Fact]
    public void DispatcherSynchronizationContextPreservesConfiguredPriority()
    {
        BaseCompatibilityPreferences.ResetForTests();
        try
        {
            var dispatcher = Dispatcher.GetForCurrentThread();
            var context = new DispatcherSynchronizationContext(
                dispatcher,
                DispatcherPriority.Background);

            var callbacks = new List<string>();
            context.Post(_ => callbacks.Add("background"), null);
            dispatcher.BeginInvoke(DispatcherPriority.Normal, () => callbacks.Add("normal"));
            dispatcher.ProcessQueue();

            Assert.Equal(new[] { "normal", "background" }, callbacks);
            Assert.Equal(
                DispatcherPriority.Background,
                GetSynchronizationContextPriority(context));

            var copy = Assert.IsType<DispatcherSynchronizationContext>(context.CreateCopy());
            Assert.NotSame(context, copy);
            Assert.Equal(
                DispatcherPriority.Background,
                GetSynchronizationContextPriority(copy));

            Assert.Throws<InvalidEnumArgumentException>(
                () => new DispatcherSynchronizationContext(
                    dispatcher,
                    (DispatcherPriority)int.MaxValue));
            Assert.Throws<ArgumentNullException>(
                () => new DispatcherSynchronizationContext(null!, DispatcherPriority.Normal));
        }
        finally
        {
            BaseCompatibilityPreferences.ResetForTests();
        }
    }

    [Fact]
    public void DispatcherSynchronizationContextCopyHonorsCompatibilityPreferences()
    {
        BaseCompatibilityPreferences.ResetForTests();
        try
        {
            BaseCompatibilityPreferences.ReuseDispatcherSynchronizationContextInstance = true;
            var context = new DispatcherSynchronizationContext(
                Dispatcher.GetForCurrentThread(),
                DispatcherPriority.Input);

            Assert.Same(context, context.CreateCopy());
        }
        finally
        {
            BaseCompatibilityPreferences.ResetForTests();
        }
    }

    [Fact]
    public void DataTransferEventArgsUsesItsStronglyTypedEventHandler()
    {
        var target = new Border();
        var args = new DataTransferEventArgs(target, FrameworkElement.DataContextProperty);
        object handlerTarget = new();
        object? observedTarget = null;
        DataTransferEventArgs? observedArgs = null;

        EventHandler<DataTransferEventArgs> handler = (sender, eventArgs) =>
        {
            observedTarget = sender;
            observedArgs = eventArgs;
        };

        args.InvokeHandler(handler, handlerTarget);

        Assert.Same(handlerTarget, observedTarget);
        Assert.Same(args, observedArgs);
        Assert.Throws<InvalidCastException>(
            () => args.InvokeHandler((RoutedEventHandler)((_, _) => { }), handlerTarget));
    }

    [Fact]
    public void ValueUnavailableExceptionHasWpfInheritanceAndSerializationShape()
    {
        Assert.Equal(typeof(SystemException), typeof(ValueUnavailableException).BaseType);
        Assert.False(typeof(ValueUnavailableException).IsSealed);
        Assert.NotNull(typeof(ValueUnavailableException).GetCustomAttribute<SerializableAttribute>());

        ConstructorInfo constructor = typeof(ValueUnavailableException).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            [typeof(SerializationInfo), typeof(StreamingContext)],
            null)!;
        Assert.True(constructor.IsFamily);
    }

    [Fact]
    public void Visual3DCollectionMaintainsOwnershipAndInvalidatesEnumerators()
    {
        var owner = new ExposedModelVisual3D();
        var first = new ProbeVisual3D();
        var second = new ProbeVisual3D();

        Assert.IsAssignableFrom<IList>(owner.Children);
        Assert.IsAssignableFrom<IList<Visual3D>>(owner.Children);

        owner.Children.Add(first);
        Assert.Same(owner, first.Parent);
        Assert.Equal(1, owner.ExposedVisual3DChildrenCount);
        Assert.Same(first, owner.ExposedGetVisual3DChild(0));

        var enumerator = owner.Children.GetEnumerator();
        Assert.True(enumerator.MoveNext());
        owner.Children.Add(second);
        Assert.Throws<InvalidOperationException>(() => enumerator.MoveNext());

        Assert.Throws<ArgumentException>(() => owner.Children.Add(first));
        Assert.True(owner.Children.Remove(first));
        Assert.Null(first.Parent);

        owner.Children[0] = first;
        Assert.Null(second.Parent);
        Assert.Same(owner, first.Parent);
        owner.Children.Clear();
        Assert.Null(first.Parent);
    }

    [Fact]
    public void ModelAndViewport3DExposeCanonicalChildrenContracts()
    {
        var model = new ModelVisual3D();
        var content = new GeometryModel3D();
        model.Content = content;
        model.Transform = new TranslateTransform3D(1, 2, 3);

        Assert.Same(content, model.GetValue(ModelVisual3D.ContentProperty));
        Assert.Same(model.Transform, model.GetValue(ModelVisual3D.TransformProperty));

        var markupModel = (Jalium.UI.Markup.IAddChild)model;
        var modelChild = new ProbeVisual3D();
        markupModel.AddChild(modelChild);
        markupModel.AddText(" \r\n\t");
        Assert.Same(modelChild, model.Children[0]);
        Assert.Throws<ArgumentException>(() => markupModel.AddChild(new object()));
        Assert.Throws<InvalidOperationException>(() => markupModel.AddText("content"));

        var viewport = new Viewport3D();
        Assert.True(Viewport3D.ChildrenProperty.ReadOnly);
        Assert.Same(viewport.Children, viewport.GetValue(Viewport3D.ChildrenProperty));
        Assert.Equal(1, viewport.VisualChildrenCount);
        Assert.IsType<Viewport3DVisual>(viewport.GetVisualChild(0));

        var viewportChild = new ProbeVisual3D();
        ((Jalium.UI.Markup.IAddChild)viewport).AddChild(viewportChild);
        Assert.Same(viewport.Children, ((Viewport3DVisual)viewport.GetVisualChild(0)!).Children);
        Assert.IsType<Viewport3DVisual>(viewportChild.Parent);
    }

    private static DispatcherPriority GetSynchronizationContextPriority(
        DispatcherSynchronizationContext context) =>
        (DispatcherPriority)typeof(DispatcherSynchronizationContext)
            .GetField("_priority", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(context)!;

    private sealed class ProbeVisual3D : Visual3D
    {
        public DependencyObject? Parent => Visual3DParent;
    }

    private sealed class ExposedModelVisual3D : ModelVisual3D
    {
        public int ExposedVisual3DChildrenCount => Visual3DChildrenCount;

        public Visual3D ExposedGetVisual3DChild(int index) => GetVisual3DChild(index);
    }
}

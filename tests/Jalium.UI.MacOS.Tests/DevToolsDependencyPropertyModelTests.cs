using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using Jalium.UI.Controls;
using Jalium.UI.Controls.DevTools;
using Jalium.UI.Data;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class DevToolsDependencyPropertyModelTests
{
    [Fact]
    public void StandardPropertyItemReadsWritesAndReceivesLiveChanges()
    {
        var target = new Border { Opacity = .5 };
        using var model = new DevToolsDependencyPropertyModel(target, UIElement.OpacityProperty, "Appearance");
        var item = model.EditorProperty; int changes = 0;
        item.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(PropertyItem.Value)) changes++; };
        Assert.Equal("Opacity", item.Name); Assert.Equal("Appearance", item.Category); Assert.Equal(typeof(double), item.PropertyType);
        target.Opacity = .75; Assert.Equal(.75, item.Value); Assert.True(changes > 0);
        item.Value = .25; Assert.Equal(.25, target.Opacity); Assert.Equal(.25, item.Value);
        Assert.Equal(DevToolsPropertyOrigin.Local, model.Origin); Assert.Contains("Local", model.SourceText);
    }
    [Fact]
    public void SameValueLocalOverrideUpdatesItsOriginAndReadonlyPropertiesRemainReadonly()
    {
        var target = new Border(); using var model = new DevToolsDependencyPropertyModel(target, UIElement.OpacityProperty);
        Assert.Equal(DevToolsPropertyOrigin.Default, model.Origin);
        int sourceChanges = 0; model.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(model.Origin)) sourceChanges++; };
        target.Opacity = 1;
        Assert.Equal(DevToolsPropertyOrigin.Local, model.Origin); Assert.Contains("Local", model.SourceText);
        target.ClearValue(UIElement.OpacityProperty); Assert.Equal(DevToolsPropertyOrigin.Default, model.Origin); Assert.True(sourceChanges >= 2);
        using var width = new DevToolsDependencyPropertyModel(target, FrameworkElement.ActualWidthProperty);
        Assert.True(width.EditorProperty.IsReadOnly); width.EditorProperty.Value = 100;
        Assert.Equal(target.ActualWidth, width.EditorProperty.Value);
    }
    [Fact]
    public void DisposalRemovesItsObserverAndPreventsFurtherWrites()
    {
        var target = new Border(); int before = Subscriptions(target);
        var model = new DevToolsDependencyPropertyModel(target, UIElement.OpacityProperty); Assert.Equal(before + 2, Subscriptions(target));
        model.Dispose(); Assert.Equal(before, Subscriptions(target)); Assert.Equal(DevToolsPropertyOrigin.Unavailable, model.Origin);
        Assert.Throws<ObjectDisposedException>(() => model.EditorProperty.Value = .5);
        target.Opacity = .25; Assert.Equal(DevToolsPropertyOrigin.Unavailable, model.Origin); model.Dispose();
    }
    [Fact]
    public void SameValueBindingsUpdateExpressionMetadataWithoutAValueChange()
    {
        var target = new Border { Opacity = .5 }; var source = new Border { Opacity = .5 };
        using var model = new DevToolsDependencyPropertyModel(target, UIElement.OpacityProperty);
        target.SetBinding(UIElement.OpacityProperty, new Binding(nameof(Border.Opacity)) { Source = source });
        Assert.Contains("bound", model.SourceText);
        target.ClearBinding(UIElement.OpacityProperty); Assert.DoesNotContain("bound", model.SourceText);
    }
    [Theory]
    [InlineData(true)] [InlineData(false)]
    public void SameValueAnimationStartAndStopUpdateTheirOrigin(bool discard)
    {
        var target = new Border { Opacity = .5 };
        using var model = new DevToolsDependencyPropertyModel(target, UIElement.OpacityProperty);
        target.SetAnimatedValue(UIElement.OpacityProperty, .5, false);
        Assert.Equal(DevToolsPropertyOrigin.Animated, model.Origin); Assert.Contains("animated", model.SourceText);
        if (discard) target.DiscardAnimatedValue(UIElement.OpacityProperty); else target.ClearAnimatedValue(UIElement.OpacityProperty);
        Assert.Equal(DevToolsPropertyOrigin.Local, model.Origin); Assert.DoesNotContain("animated", model.SourceText);
    }
    [Fact]
    public void SameValueCssAnimationAndImportantMaskChangesUpdateTheirOrigin()
    {
        var target = new Border(); using var model = new DevToolsDependencyPropertyModel(target, UIElement.OpacityProperty);
        target.SetCssAnimatedValue(UIElement.OpacityProperty, 1d); Assert.Equal(DevToolsPropertyOrigin.Animated, model.Origin);
        target.SetCssImportantProperties([UIElement.OpacityProperty]); Assert.Equal(DevToolsPropertyOrigin.Default, model.Origin);
        target.SetCssImportantProperties([]); Assert.Equal(DevToolsPropertyOrigin.Animated, model.Origin);
        target.ClearCssAnimatedValue(UIElement.OpacityProperty); Assert.Equal(DevToolsPropertyOrigin.Default, model.Origin);
    }
    [Fact]
    public void CoercionMetadataTracksBaseValueChangesEvenWhenTheEffectiveValueStaysTheSame()
    {
        var target = new CoercedProbe(); target.SetValue(CoercedProbe.LevelProperty, 1d);
        using var model = new DevToolsDependencyPropertyModel(target, CoercedProbe.LevelProperty);
        Assert.DoesNotContain("coerced", model.SourceText);
        target.SetValue(CoercedProbe.LevelProperty, 0d); Assert.Equal(1d, model.EditorProperty.Value); Assert.Contains("coerced", model.SourceText);
        target.SetValue(CoercedProbe.LevelProperty, 1d); Assert.DoesNotContain("coerced", model.SourceText);
    }
    private sealed class CoercedProbe : DependencyObject
    {
        public static readonly DependencyProperty LevelProperty = DependencyProperty.Register("Level", typeof(double), typeof(CoercedProbe), new PropertyMetadata(1d, null, (_, _) => 1d));
    }
    [Fact]
    public void AVisibleModelDoesNotOwnItsRetiredTarget()
    {
        var pair = CreateModel(); Collect(); Assert.False(pair.Target.IsAlive);
        Assert.Throws<InvalidOperationException>(() => pair.Model.EditorProperty.Value);
        pair.Model.Dispose(); GC.KeepAlive(pair.Model);
    }
    [Fact]
    public void ALongLivedTargetDoesNotOwnDiscardedModelsOrTheirEditorItems()
    {
        var target = new Border(); int before = Subscriptions(target);
        var weak = CreateDiscarded(target); Collect(); Assert.False(weak.Model.IsAlive); Assert.False(weak.Item.IsAlive);
        target.Opacity = .5; Assert.Equal(before, Subscriptions(target)); GC.KeepAlive(target);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (DevToolsDependencyPropertyModel Model, WeakReference Target) CreateModel()
    {
        var target = new Border(); return (new(target, UIElement.OpacityProperty), new(target));
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Model, WeakReference Item) CreateDiscarded(Border target)
    {
        var model = new DevToolsDependencyPropertyModel(target, UIElement.OpacityProperty); return (new(model), new(model.EditorProperty));
    }
    private static void Collect() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
    private static int Subscriptions(DependencyObject target) => new[] { "PropertyChangedInternal", "ValueSourceInvalidatedInternal" }
        .Sum(name => DependencyObjectSubscriptionProbe.Count(target, name));
}

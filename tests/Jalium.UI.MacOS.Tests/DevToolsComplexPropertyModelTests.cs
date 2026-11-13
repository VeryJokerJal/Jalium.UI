using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using Jalium.UI.Controls;
using Jalium.UI.Controls.DevTools;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class DevToolsComplexPropertyModelTests
{
    [Fact]
    public void ThicknessUsesTheFrameworkSerializerWithoutRoundingOrCultureLoss()
    {
        var previous = CultureInfo.CurrentCulture; CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        try
        {
            var target = new Border { Margin = new Thickness(1.25, 2.5, 3.75, 4) };
            using var model = new DevToolsDependencyPropertyModel(target, FrameworkElement.MarginProperty);
            Assert.Equal(typeof(string), model.EditorProperty.PropertyType); Assert.False(model.EditorProperty.IsReadOnly);
            Assert.Equal("1.25,2.5,3.75,4", model.EditorProperty.Value);
            model.EditorProperty.Value = "8.5,12.25"; Assert.Equal(new Thickness(8.5, 12.25), target.Margin);
            Assert.ThrowsAny<Exception>(() => model.EditorProperty.Value = "invalid"); Assert.Equal(new Thickness(8.5, 12.25), target.Margin);
            target.Margin = new Thickness(-2.5); Assert.Equal("-2.5,-2.5,-2.5,-2.5", model.EditorProperty.Value);
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
    [Fact]
    public void SolidBrushEditsCloneTheSourceAndPreserveOtherBrushProperties()
    {
        var shared = new SolidColorBrush(Color.FromRgb(10, 20, 30)) { Opacity = .5, Transform = new ScaleTransform(2, 3) }; shared.Freeze();
        var target = new Border { Background = shared };
        using var model = new DevToolsDependencyPropertyModel(target, Border.BackgroundProperty);
        Assert.Equal(typeof(Color), model.EditorProperty.PropertyType); Assert.Equal(shared.Color, model.EditorProperty.Value);
        var edited = Color.FromArgb(128, 40, 50, 60); model.EditorProperty.Value = edited;
        var replacement = Assert.IsType<SolidColorBrush>(target.Background);
        Assert.NotSame(shared, replacement); Assert.Equal(Color.FromRgb(10, 20, 30), shared.Color);
        Assert.Equal(.5, replacement.Opacity); Assert.Equal(2, Assert.IsType<ScaleTransform>(replacement.Transform).ScaleX);
        Assert.Equal(edited, replacement.Color); replacement.Color = Color.White;
        Assert.Equal(Color.White, model.EditorProperty.Value); Assert.Equal(Color.White, model.PreviewColor);
    }
    [Fact]
    public void NullSolidAndUnsupportedBrushesChangeEditorRepresentationSafely()
    {
        var target = new Border(); using var model = new DevToolsDependencyPropertyModel(target, Border.BackgroundProperty);
        var empty = model.EditorProperty; Assert.Equal(typeof(string), empty.PropertyType); Assert.False(empty.IsReadOnly); Assert.Null(empty.Value);
        empty.Value = "#FF102030"; var color = model.EditorProperty;
        Assert.NotSame(empty, color); Assert.Equal(typeof(Color), color.PropertyType); Assert.Equal(Color.FromRgb(16, 32, 48), color.Value);
        var retired = Assert.IsType<SolidColorBrush>(target.Background);
        target.Background = new LinearGradientBrush(); Assert.True(model.EditorProperty.IsReadOnly); Assert.Equal("LinearGradientBrush", model.EditorProperty.Value);
        int changes = 0; model.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(Border.Background)) changes++; };
        retired.Color = Color.Black; Assert.Equal(0, changes);
        target.Background = null; Assert.Equal(typeof(string), model.EditorProperty.PropertyType); Assert.False(model.EditorProperty.IsReadOnly); Assert.Null(model.PreviewColor);
    }
    [Fact]
    public void UnsupportedCollectionsStayReadonlyAndTheirCountUpdatesInPlace()
    {
        var values = new ObservableCollection<string> { "first" }; var target = new Border { Tag = values };
        using var model = new DevToolsDependencyPropertyModel(target, FrameworkElement.TagProperty);
        Assert.True(model.EditorProperty.IsReadOnly); Assert.EndsWith("(1)", Assert.IsType<string>(model.EditorProperty.Value));
        int changes = 0; model.EditorProperty.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(PropertyItem.Value)) changes++; };
        values.Add("second"); Assert.True(changes > 0); Assert.EndsWith("(2)", Assert.IsType<string>(model.EditorProperty.Value));
        model.EditorProperty.Value = "replacement"; Assert.Same(values, target.Tag);
        model.Dispose(); int afterDispose = changes; values.Add("third"); Assert.Equal(afterDispose, changes);
    }
    [Fact]
    public void ADeclaredNullableEnumKeepsItsNullableEditorTypeWhenItHasAValue()
    {
        var target = new NullableProbe(); target.SetValue(NullableProbe.AlignmentProperty, HorizontalAlignment.Right);
        using var model = new DevToolsDependencyPropertyModel(target, NullableProbe.AlignmentProperty);
        Assert.Equal(typeof(HorizontalAlignment?), model.EditorProperty.PropertyType);
        var item = model.EditorProperty; item.Value = null; Assert.Null(target.GetValue(NullableProbe.AlignmentProperty)); Assert.Same(item, model.EditorProperty);
    }
    private sealed class NullableProbe : DependencyObject
    {
        public static readonly DependencyProperty AlignmentProperty = DependencyProperty.Register("Alignment", typeof(HorizontalAlignment?), typeof(NullableProbe), new PropertyMetadata(null));
    }
    [Fact]
    public void ValueObserversDoNotRetainDiscardedModelsTargetsOrBrushes()
    {
        var target = new Border { Background = new SolidColorBrush(Color.Black) };
        var discarded = DiscardModel(target); Collect(); Assert.False(discarded.IsAlive);
        Assert.IsType<SolidColorBrush>(target.Background).Color = Color.White;
        var pair = KeepModel(); Collect(); Assert.False(pair.Target.IsAlive); Assert.False(pair.Brush.IsAlive);
        pair.Model.Dispose(); GC.KeepAlive(pair.Model); GC.KeepAlive(target);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference DiscardModel(Border target) => new(new DevToolsDependencyPropertyModel(target, Border.BackgroundProperty));
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (DevToolsDependencyPropertyModel Model, WeakReference Target, WeakReference Brush) KeepModel()
    {
        var brush = new SolidColorBrush(Color.Black); var target = new Border { Background = brush };
        return (new(target, Border.BackgroundProperty), new(target), new(brush));
    }
    private static void Collect() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
}

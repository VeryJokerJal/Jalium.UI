using Jalium.UI.Controls;
using Jalium.UI.Data;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class DependencyPropertyClearingTests
{
    [Fact]
    public void ClearValue_AfterDefaultCurrentValue_RestoresMetadataDefault()
    {
        var target = new ProbeElement();
        target.SetCurrentValue(UIElement.OpacityProperty, .4);
        Assert.False(target.HasLocalValue(UIElement.OpacityProperty));
        target.Changes.Clear();

        target.ClearValue(UIElement.OpacityProperty);

        Assert.Equal(1.0, target.Opacity);
        Assert.Equal(new[] { (.4, 1.0) }, target.Changes);
    }

    [Fact]
    public void ClearBinding_RemovesTargetValueAndStopsSourceUpdates()
    {
        var target = new ProbeElement();
        var source = Bind(target, .6);
        target.Changes.Clear();

        BindingOperations.ClearBinding(target, UIElement.OpacityProperty);

        Assert.Equal(1.0, target.Opacity);
        Assert.False(BindingOperations.IsDataBound(target, UIElement.OpacityProperty));
        Assert.Equal(new[] { (.6, 1.0) }, target.Changes);
        source.Value = .8;
        Assert.Equal(1.0, target.Opacity);
    }

    [Fact]
    public void ClearBinding_RevealsStyleBelowTheBinding()
    {
        var target = new ProbeElement();
        var style = new Style { TargetType = typeof(ProbeElement) };
        style.Setters.Add(new Setter { Property = UIElement.OpacityProperty, Value = .4 });
        target.Style = style;
        Bind(target, .8);

        BindingOperations.ClearBinding(target, UIElement.OpacityProperty);

        Assert.Equal(.4, target.Opacity);
        Assert.False(target.HasLocalValue(UIElement.OpacityProperty));
    }

    [Fact]
    public void ClearValue_RemovesBindingAndItsCurrentValue()
    {
        var target = new ProbeElement();
        var source = Bind(target, .6);
        target.SetCurrentValue(UIElement.OpacityProperty, .3);

        target.ClearValue(UIElement.OpacityProperty);

        Assert.Equal(1.0, target.Opacity);
        Assert.False(BindingOperations.IsDataBound(target, UIElement.OpacityProperty));
        source.Value = .8;
        Assert.Equal(1.0, target.Opacity);
    }

    [Fact]
    public void ClearAllBindings_RestoresBothDefaults()
    {
        var target = new ProbeElement();
        var source = Bind(target, .6);
        BindingOperations.SetBinding(target, FrameworkElement.WidthProperty,
            new Binding(nameof(OpacitySource.Value)) { Source = source, Mode = BindingMode.OneWay });

        BindingOperations.ClearAllBindings(target);

        Assert.Equal(1.0, target.Opacity);
        Assert.True(double.IsNaN(target.Width));
        Assert.False(BindingOperations.IsDataBound(target, UIElement.OpacityProperty));
        Assert.False(BindingOperations.IsDataBound(target, FrameworkElement.WidthProperty));
        source.Value = .8;
        Assert.Equal(1.0, target.Opacity);
        Assert.True(double.IsNaN(target.Width));
    }

    [Fact]
    public void ClearBinding_WithoutBinding_PreservesLocalValue()
    {
        var target = new ProbeElement { Opacity = .6 };
        target.Changes.Clear();

        BindingOperations.ClearBinding(target, UIElement.OpacityProperty);

        Assert.Equal(.6, target.Opacity);
        Assert.Empty(target.Changes);
    }

    [Fact]
    public void ReplaceBinding_TransfersDirectlyWithoutAnIntermediateDefault()
    {
        var target = new ProbeElement();
        Bind(target, .6);
        target.Changes.Clear();

        Bind(target, .8);

        Assert.Equal(.8, target.Opacity);
        Assert.Equal(new[] { (.6, .8) }, target.Changes);
    }

    [Fact]
    public void ClearLocalCurrentValue_PreservesTheStyleLayer()
    {
        var target = new ProbeElement();
        var style = new Style { TargetType = typeof(ProbeElement) };
        style.Setters.Add(new Setter { Property = UIElement.OpacityProperty, Value = .4 });
        target.Style = style;
        target.Opacity = .8;
        target.SetCurrentValue(UIElement.OpacityProperty, .6);

        target.ClearValue(UIElement.OpacityProperty);

        Assert.Equal(.4, target.Opacity);
    }

    private static OpacitySource Bind(ProbeElement target, double value)
    {
        var source = new OpacitySource { Value = value };
        BindingOperations.SetBinding(target, UIElement.OpacityProperty,
            new Binding(nameof(OpacitySource.Value)) { Source = source, Mode = BindingMode.OneWay });
        return source;
    }

    private sealed class ProbeElement : FrameworkElement
    {
        public List<(double Old, double New)> Changes { get; } = [];
        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.Property == OpacityProperty) Changes.Add(((double)e.OldValue!, (double)e.NewValue!));
        }
    }

    private sealed class OpacitySource : System.ComponentModel.INotifyPropertyChanged
    {
        private double _value;
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        public double Value
        {
            get => _value;
            set { _value = value; PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Value))); }
        }
    }
}

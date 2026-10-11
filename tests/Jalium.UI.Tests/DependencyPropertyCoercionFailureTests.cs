using System.ComponentModel;
using Jalium.UI.Data;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class DependencyPropertyCoercionFailureTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectedWrite_PreservesDefaultOrLocalValue(bool local)
    {
        var target = new ProbeElement();
        if (local) target.Value = 21;
        target.Changes.Clear();
        for (int attempt = 0; attempt < 3; attempt++)
        {
            Assert.Throws<InvalidOperationException>(() => target.Value = 99);
            Assert.Equal(local ? 21d : 14d, target.Value);
            Assert.Equal(local, target.HasLocalValue(ProbeElement.ValueProperty));
            Assert.Empty(target.Changes);
        }
        target.Value = 25;
        Assert.Equal(25d, target.Value);
        Assert.Single(target.Changes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectedCurrentOrLocalWrite_PreservesTheExistingCurrentSource(bool currentWriter)
    {
        var target = new ProbeElement();
        target.SetCurrentValue(ProbeElement.ValueProperty, 18d);
        var before = DependencyPropertyHelper.GetValueSource(target, ProbeElement.ValueProperty);
        target.Changes.Clear();

        Assert.Throws<InvalidOperationException>(() =>
        {
            if (currentWriter) target.SetCurrentValue(ProbeElement.ValueProperty, 99d);
            else target.Value = 99;
        });

        Assert.Equal(18d, target.Value);
        Assert.False(target.HasLocalValue(ProbeElement.ValueProperty));
        var after = DependencyPropertyHelper.GetValueSource(target, ProbeElement.ValueProperty);
        Assert.Equal(before.BaseValueSource, after.BaseValueSource);
        Assert.Equal(before.IsCurrent, after.IsCurrent);
        Assert.Empty(target.Changes);
        target.ClearValue(ProbeElement.ValueProperty);
        Assert.Equal(14d, target.Value);
    }

    [Fact]
    public void RejectedLocalWrite_PreservesTheUnderlyingStyle()
    {
        var target = new ProbeElement();
        target.SetLayerValue(ProbeElement.ValueProperty, 18d, DependencyObject.LayerValueSource.StyleSetter);
        target.Value = 22;
        target.Changes.Clear();
        Assert.Throws<InvalidOperationException>(() => target.Value = 99);
        Assert.Equal(22d, target.Value);
        Assert.Empty(target.Changes);
        target.ClearValue(ProbeElement.ValueProperty);
        Assert.Equal(18d, target.Value);
        Assert.Equal(BaseValueSource.Style, DependencyPropertyHelper.GetValueSource(target, ProbeElement.ValueProperty).BaseValueSource);
    }

    [Fact]
    public void RejectedClear_PreservesLocalValueUntilTheUnderlyingStyleIsValid()
    {
        var target = new ProbeElement { Value = 22 };
        target.SetLayerValue(ProbeElement.ValueProperty, 99d, DependencyObject.LayerValueSource.StyleSetter);
        target.Changes.Clear();
        Assert.Throws<InvalidOperationException>(() => target.ClearValue(ProbeElement.ValueProperty));
        Assert.Equal(22d, target.Value);
        Assert.Equal(22d, target.ReadLocalValue(ProbeElement.ValueProperty));
        Assert.Empty(target.Changes);
        target.SetLayerValue(ProbeElement.ValueProperty, 18d, DependencyObject.LayerValueSource.StyleSetter);
        target.ClearValue(ProbeElement.ValueProperty);
        Assert.Equal(18d, target.Value);
    }

    [Fact]
    public void RejectedLayerUpdateOrClear_PreservesThePreviousStyleContribution()
    {
        var target = new ProbeElement();
        target.SetLayerValue(ProbeElement.ValueProperty, 18d, DependencyObject.LayerValueSource.StyleSetter);
        target.Changes.Clear();
        Assert.Throws<InvalidOperationException>(() => target.SetLayerValue(ProbeElement.ValueProperty, 99d, DependencyObject.LayerValueSource.StyleSetter));
        Assert.Equal(18d, target.Value);
        target.RejectDefault = true;
        Assert.Throws<InvalidOperationException>(() => target.ClearLayerValue(ProbeElement.ValueProperty, DependencyObject.LayerValueSource.StyleSetter));
        Assert.Equal(18d, target.Value);
        Assert.Empty(target.Changes);
        target.RejectDefault = false;
        target.ClearLayerValue(ProbeElement.ValueProperty, DependencyObject.LayerValueSource.StyleSetter);
        Assert.Equal(14d, target.Value);
    }

    [Fact]
    public void RejectedClear_RestoresThePropertyWithoutUndoingOtherCallbackWrites()
    {
        var target = new ProbeElement();
        for (int index = 0; index < ProbeElement.OtherProperties.Length; index++)
            target.SetValue(ProbeElement.OtherProperties[index], (double)index);
        target.Value = 22;
        target.RejectDefault = true;
        target.WriteOtherOnRejection = true;
        target.Changes.Clear();

        Assert.Throws<InvalidOperationException>(() => target.ClearValue(ProbeElement.ValueProperty));

        Assert.Equal(22d, target.Value);
        Assert.Equal(42d, target.GetValue(ProbeElement.OtherProperties[0]));
        for (int index = 1; index < ProbeElement.OtherProperties.Length; index++)
            Assert.Equal((double)index, target.GetValue(ProbeElement.OtherProperties[index]));
        Assert.Empty(target.Changes);
    }

    [Fact]
    public void RejectedBindingTransfer_PreservesTheBindingAndCanTransferTheNextValidValue()
    {
        var target = new ProbeElement();
        var source = new ValueSource { Value = 18 };
        BindingOperations.SetBinding(target, ProbeElement.ValueProperty, new Binding(nameof(ValueSource.Value)) { Source = source });
        var binding = BindingOperations.GetBindingExpression(target, ProbeElement.ValueProperty);
        target.Changes.Clear();
        Assert.Throws<InvalidOperationException>(() => source.Value = 99);
        Assert.Equal(18d, target.Value);
        Assert.Same(binding, BindingOperations.GetBindingExpression(target, ProbeElement.ValueProperty));
        Assert.Empty(target.Changes);
        source.Value = 25;
        Assert.Equal(25d, target.Value);
        Assert.Single(target.Changes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectedAutomaticTransition_PreservesItsPreviousBaseAndAnimation(bool running)
    {
        var host = new ProbePanel();
        var target = new ProbeElement { TransitionProperty = nameof(ProbeElement.Value) };
        host.AddChild(target);
        Dispatcher.CurrentDispatcher.ProcessQueue();
        try
        {
            if (running) target.Value = 22;
            Assert.Equal(running, target.HasAutomaticTransition(ProbeElement.ValueProperty));
            var displayed = target.Value;
            target.Changes.Clear();

            Assert.Throws<InvalidOperationException>(() => target.Value = 99);

            Assert.Equal(displayed, target.Value);
            Assert.Equal(running ? 22d : 14d, target.GetEffectiveBaseValue(ProbeElement.ValueProperty));
            Assert.Equal(running, target.HasAnimatedValue(ProbeElement.ValueProperty));
            Assert.Equal(running, target.HasAutomaticTransition(ProbeElement.ValueProperty));
            Assert.Empty(target.Changes);
            target.Value = 25;
            Assert.Equal(25d, target.GetEffectiveBaseValue(ProbeElement.ValueProperty));
            Assert.True(target.HasAutomaticTransition(ProbeElement.ValueProperty));
        }
        finally { target.StopAutomaticTransition(ProbeElement.ValueProperty, clearAnimatedValue: true); }
    }

    [Fact]
    public void PropertyChangedException_LeavesTheAlreadyAcceptedValueCommitted()
    {
        var target = new ProbeElement { ThrowOnChange = true };
        Assert.Throws<NotSupportedException>(() => target.Value = 25);
        Assert.Equal(25d, target.Value);
        Assert.Equal(25d, target.ReadLocalValue(ProbeElement.ValueProperty));
    }

    private sealed class ProbePanel : FrameworkElement
    {
        public void AddChild(UIElement child) => AddVisualChild(child);
    }

    private sealed class ProbeElement : FrameworkElement
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            nameof(Value), typeof(double), typeof(ProbeElement), new PropertyMetadata(14d, null, Coerce));
        public static readonly DependencyProperty[] OtherProperties = Enumerable.Range(0, 5)
            .Select(index => DependencyProperty.Register($"Other{index}", typeof(double), typeof(ProbeElement), new PropertyMetadata(0d))).ToArray();
        public bool RejectDefault;
        public bool WriteOtherOnRejection;
        public bool ThrowOnChange;
        public List<double> Changes { get; } = [];
        public double Value { get => (double)GetValue(ValueProperty)!; set => SetValue(ValueProperty, value); }
        private static object? Coerce(DependencyObject owner, object? value)
        {
            var target = (ProbeElement)owner;
            double candidate = (double)value!;
            if (candidate > 80 || target.RejectDefault && candidate == 14)
            {
                if (target.WriteOtherOnRejection) target.SetValue(OtherProperties[0], 42d);
                throw new InvalidOperationException("Rejected candidate.");
            }
            return value;
        }
        protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnPropertyChanged(e);
            if (e.Property != ValueProperty) return;
            Changes.Add((double)e.NewValue!);
            if (ThrowOnChange) throw new NotSupportedException("Accepted value notification failed.");
        }
    }

    private sealed class ValueSource : INotifyPropertyChanged
    {
        private double _value;
        public double Value { get => _value; set { _value = value; PropertyChanged?.Invoke(this, new(nameof(Value))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }
}

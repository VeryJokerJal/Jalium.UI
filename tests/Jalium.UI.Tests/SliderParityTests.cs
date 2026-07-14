using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using MediaDoubleCollection = Jalium.UI.Media.DoubleCollection;

namespace Jalium.UI.Tests;

public class SliderParityTests
{
    [Fact]
    public void Slider_InheritsRangeBase_AndPublishesExpectedApiFields()
    {
        Assert.Equal(typeof(RangeBase), typeof(Slider).BaseType);
        Assert.Same(RangeBase.MinimumProperty, Slider.MinimumProperty);
        Assert.Same(RangeBase.MaximumProperty, Slider.MaximumProperty);
        Assert.Same(RangeBase.ValueProperty, Slider.ValueProperty);
        Assert.Same(RangeBase.SmallChangeProperty, Slider.SmallChangeProperty);
        Assert.Same(RangeBase.LargeChangeProperty, Slider.LargeChangeProperty);
        Assert.Same(RangeBase.ValueChangedEvent, Slider.ValueChangedEvent);
        Assert.Same(RepeatButton.DelayProperty, Slider.DelayProperty);
        Assert.Same(RepeatButton.IntervalProperty, Slider.IntervalProperty);

        var dependencyProperties = new (DependencyProperty Property, string Name)[]
        {
            (Slider.AutoToolTipPlacementProperty, nameof(Slider.AutoToolTipPlacement)),
            (Slider.AutoToolTipPrecisionProperty, nameof(Slider.AutoToolTipPrecision)),
            (Slider.DelayProperty, nameof(Slider.Delay)),
            (Slider.IntervalProperty, nameof(Slider.Interval)),
            (Slider.IsDirectionReversedProperty, nameof(Slider.IsDirectionReversed)),
            (Slider.IsMoveToPointEnabledProperty, nameof(Slider.IsMoveToPointEnabled)),
            (Slider.IsSelectionRangeEnabledProperty, nameof(Slider.IsSelectionRangeEnabled)),
            (Slider.SelectionStartProperty, nameof(Slider.SelectionStart)),
            (Slider.SelectionEndProperty, nameof(Slider.SelectionEnd)),
            (Slider.TickPlacementProperty, nameof(Slider.TickPlacement)),
            (Slider.TicksProperty, nameof(Slider.Ticks)),
        };

        foreach (var (property, name) in dependencyProperties)
        {
            Assert.NotNull(property);
            Assert.Equal(name, property.Name);
        }

        var commands = new[]
        {
            Slider.DecreaseLarge,
            Slider.DecreaseSmall,
            Slider.IncreaseLarge,
            Slider.IncreaseSmall,
            Slider.MaximizeValue,
            Slider.MinimizeValue,
        };

        Assert.Equal(
            new[] { "DecreaseLarge", "DecreaseSmall", "IncreaseLarge", "IncreaseSmall", "MaximizeValue", "MinimizeValue" },
            commands.Select(command => command.Name));
        Assert.All(commands, command => Assert.Equal(typeof(Slider), command.OwnerType));
    }

    [Fact]
    public void Slider_Defaults_MatchWpf()
    {
        var first = new Slider();
        var second = new Slider();

        Assert.Equal(0.0, first.Minimum);
        Assert.Equal(10.0, first.Maximum);
        Assert.Equal(0.0, first.Value);
        Assert.Equal(0.1, first.SmallChange);
        Assert.Equal(1.0, first.LargeChange);
        Assert.Equal(AutoToolTipPlacement.None, first.AutoToolTipPlacement);
        Assert.Equal(0, first.AutoToolTipPrecision);
        Assert.Equal(500, first.Delay);
        Assert.Equal(33, first.Interval);
        Assert.False(first.IsDirectionReversed);
        Assert.False(first.IsMoveToPointEnabled);
        Assert.False(first.IsSelectionRangeEnabled);
        Assert.Equal(0.0, first.SelectionStart);
        Assert.Equal(0.0, first.SelectionEnd);
        Assert.Equal(Orientation.Horizontal, first.Orientation);
        Assert.Equal(TickPlacement.None, first.TickPlacement);
        Assert.Equal(1.0, first.TickFrequency);
        Assert.False(first.IsSnapToTickEnabled);
        Assert.Empty(first.Ticks);
        Assert.Empty(second.Ticks);
        Assert.NotSame(first.Ticks, second.Ticks);
    }

    [Fact]
    public void Slider_RejectsInvalidApiValues()
    {
        var slider = new Slider();

        Assert.Throws<ArgumentException>(() => slider.AutoToolTipPlacement = (AutoToolTipPlacement)99);
        Assert.Throws<ArgumentException>(() => slider.AutoToolTipPrecision = -1);
        Assert.Throws<ArgumentException>(() => slider.Delay = -1);
        Assert.Throws<ArgumentException>(() => slider.Interval = 0);
        Assert.Throws<ArgumentException>(() => slider.Orientation = (Orientation)99);
        Assert.Throws<ArgumentException>(() => slider.TickPlacement = (TickPlacement)99);
        Assert.Throws<ArgumentException>(() => slider.TickFrequency = double.NaN);
        Assert.Throws<ArgumentException>(() => slider.TickFrequency = double.PositiveInfinity);
        Assert.Throws<ArgumentException>(() => slider.SelectionStart = double.NaN);
        Assert.Throws<ArgumentException>(() => slider.SelectionEnd = double.PositiveInfinity);

        slider.Delay = 0;
        slider.Interval = 1;
        slider.TickFrequency = 0;
        slider.TickFrequency = -1;

        Assert.Equal(0, slider.Delay);
        Assert.Equal(1, slider.Interval);
        Assert.Equal(-1, slider.TickFrequency);
    }

    [Fact]
    public void SelectionStartAndEnd_CoerceInBothDirections()
    {
        var slider = new Slider();

        slider.SelectionStart = 8;

        Assert.Equal(8, slider.SelectionStart);
        Assert.Equal(8, slider.SelectionEnd);

        slider.SelectionEnd = 3;

        Assert.Equal(8, slider.SelectionStart);
        Assert.Equal(8, slider.SelectionEnd);

        slider.SelectionStart = 2;
        slider.SelectionEnd = 9;
        slider.Maximum = 7;

        Assert.Equal(2, slider.SelectionStart);
        Assert.Equal(7, slider.SelectionEnd);

        slider.Minimum = 5;

        Assert.Equal(5, slider.SelectionStart);
        Assert.Equal(7, slider.SelectionEnd);
    }

    [Fact]
    public void TicksMutation_InvalidatesVisual_AndReplacementDetachesOldCollection()
    {
        var slider = new Slider();
        var original = new MediaDoubleCollection { 1 };
        var replacement = new MediaDoubleCollection { 2 };
        slider.Ticks = original;
        slider.ClearRenderDirty();

        original.Add(3);

        Assert.True(slider.IsRenderDirty);

        slider.ClearRenderDirty();
        slider.Ticks = replacement;
        slider.ClearRenderDirty();

        original.Add(4);

        Assert.False(slider.IsRenderDirty);

        replacement.Add(5);

        Assert.True(slider.IsRenderDirty);
    }

    [Fact]
    public void ArrowKeys_RespectDirectionReversal_AndRangeKeysUseExpectedChanges()
    {
        var slider = new ProbeSlider
        {
            Minimum = 0,
            Maximum = 10,
            Value = 5,
            SmallChange = 1,
            LargeChange = 2,
        };

        var left = KeyDown(Key.Left);
        slider.RaiseEvent(left);
        Assert.True(left.Handled);
        Assert.Equal(4, slider.Value);
        Assert.Equal(1, slider.DecreaseSmallCalls);

        var right = KeyDown(Key.Right);
        slider.RaiseEvent(right);
        Assert.True(right.Handled);
        Assert.Equal(5, slider.Value);
        Assert.Equal(1, slider.IncreaseSmallCalls);

        slider.IsDirectionReversed = true;
        slider.RaiseEvent(KeyDown(Key.Left));
        Assert.Equal(6, slider.Value);
        Assert.Equal(2, slider.IncreaseSmallCalls);

        slider.RaiseEvent(KeyDown(Key.Right));
        Assert.Equal(5, slider.Value);
        Assert.Equal(2, slider.DecreaseSmallCalls);

        slider.RaiseEvent(KeyDown(Key.PageUp));
        Assert.Equal(7, slider.Value);
        slider.RaiseEvent(KeyDown(Key.PageDown));
        Assert.Equal(5, slider.Value);
        slider.RaiseEvent(KeyDown(Key.End));
        Assert.Equal(10, slider.Value);
        slider.RaiseEvent(KeyDown(Key.Home));
        Assert.Equal(0, slider.Value);
    }

    [Fact]
    public void SliderCommands_InvokeIncreaseDecreaseAndEndpointHooks()
    {
        var slider = new ProbeSlider
        {
            Minimum = 0,
            Maximum = 10,
            Value = 5,
            SmallChange = 1,
            LargeChange = 3,
        };

        Assert.True(Slider.IncreaseSmall.CanExecute(null, slider));
        Slider.IncreaseSmall.Execute(null, slider);
        Slider.DecreaseSmall.Execute(null, slider);
        Slider.IncreaseLarge.Execute(null, slider);
        Slider.DecreaseLarge.Execute(null, slider);
        Slider.MaximizeValue.Execute(null, slider);
        Slider.MinimizeValue.Execute(null, slider);

        Assert.Equal(0, slider.Value);
        Assert.Equal(1, slider.IncreaseSmallCalls);
        Assert.Equal(1, slider.DecreaseSmallCalls);
        Assert.Equal(1, slider.IncreaseLargeCalls);
        Assert.Equal(1, slider.DecreaseLargeCalls);
        Assert.Equal(1, slider.MaximizeCalls);
        Assert.Equal(1, slider.MinimizeCalls);

        slider.IsEnabled = false;
        Assert.False(Slider.IncreaseSmall.CanExecute(null, slider));
    }

    [Fact]
    public void ThumbDragHooks_UpdateValue_AndRespectDirectionReversal()
    {
        var slider = new ProbeSlider
        {
            Minimum = 0,
            Maximum = 10,
            Value = 5,
        };
        slider.Arrange(new Rect(0, 0, 116, 24));

        slider.BeginDrag(50, 12);
        slider.DragBy(10, 0);
        slider.EndDrag(10, 0, canceled: false);

        Assert.Equal(6, slider.Value);
        Assert.Equal(1, slider.DragStartedCalls);
        Assert.Equal(1, slider.DragDeltaCalls);
        Assert.Equal(1, slider.DragCompletedCalls);
        Assert.False(slider.LastDragCanceled);

        slider.Value = 5;
        slider.IsDirectionReversed = true;
        slider.BeginDrag(50, 12);
        slider.DragBy(10, 0);
        slider.EndDrag(10, 0, canceled: true);

        Assert.Equal(4, slider.Value);
        Assert.Equal(2, slider.DragStartedCalls);
        Assert.Equal(2, slider.DragDeltaCalls);
        Assert.Equal(2, slider.DragCompletedCalls);
        Assert.True(slider.LastDragCanceled);
    }

    [Fact]
    public void PreviewTrackPress_PagesOrMovesToPoint_AccordingToMode()
    {
        var pagingSlider = ArrangeSlider(new ProbeSlider
        {
            Minimum = 0,
            Maximum = 10,
            Value = 2,
            LargeChange = 2,
        });
        var pagingPress = PreviewLeftDown(new Point(83, 12));

        pagingSlider.ProcessPreviewLeftButtonDown(pagingPress);

        Assert.True(pagingPress.Handled);
        Assert.Equal(4, pagingSlider.Value);
        Assert.Equal(1, pagingSlider.IncreaseLargeCalls);

        var moveSlider = ArrangeSlider(new ProbeSlider
        {
            Minimum = 0,
            Maximum = 10,
            Value = 2,
            IsMoveToPointEnabled = true,
        });
        var movePress = PreviewLeftDown(new Point(83, 12));

        moveSlider.ProcessPreviewLeftButtonDown(movePress);

        Assert.True(movePress.Handled);
        Assert.Equal(7.5, moveSlider.Value, precision: 6);
        Assert.Equal(0, moveSlider.IncreaseLargeCalls);

        moveSlider.Value = 2;
        moveSlider.IsDirectionReversed = true;
        moveSlider.ProcessPreviewLeftButtonDown(PreviewLeftDown(new Point(58, 12)));

        Assert.Equal(5, moveSlider.Value, precision: 6);
    }

    private static ProbeSlider ArrangeSlider(ProbeSlider slider)
    {
        slider.Arrange(new Rect(0, 0, 116, 24));
        return slider;
    }

    private static KeyEventArgs KeyDown(Key key) =>
        new(UIElement.KeyDownEvent, key, ModifierKeys.None, isDown: true, isRepeat: false, timestamp: 0);

    private static MouseButtonEventArgs PreviewLeftDown(Point position) =>
        new(
            UIElement.PreviewMouseLeftButtonDownEvent,
            position,
            MouseButton.Left,
            MouseButtonState.Pressed,
            clickCount: 1,
            leftButton: MouseButtonState.Pressed,
            middleButton: MouseButtonState.Released,
            rightButton: MouseButtonState.Released,
            xButton1: MouseButtonState.Released,
            xButton2: MouseButtonState.Released,
            modifiers: ModifierKeys.None,
            timestamp: 0);

    private sealed class ProbeSlider : Slider
    {
        public int IncreaseLargeCalls { get; private set; }
        public int IncreaseSmallCalls { get; private set; }
        public int DecreaseLargeCalls { get; private set; }
        public int DecreaseSmallCalls { get; private set; }
        public int MaximizeCalls { get; private set; }
        public int MinimizeCalls { get; private set; }
        public int DragStartedCalls { get; private set; }
        public int DragDeltaCalls { get; private set; }
        public int DragCompletedCalls { get; private set; }
        public bool LastDragCanceled { get; private set; }

        public void BeginDrag(double horizontalOffset, double verticalOffset) =>
            OnThumbDragStarted(new DragStartedEventArgs(horizontalOffset, verticalOffset));

        public void DragBy(double horizontalChange, double verticalChange) =>
            OnThumbDragDelta(new DragDeltaEventArgs(horizontalChange, verticalChange));

        public void EndDrag(double horizontalChange, double verticalChange, bool canceled) =>
            OnThumbDragCompleted(new DragCompletedEventArgs(horizontalChange, verticalChange, canceled));

        public void ProcessPreviewLeftButtonDown(MouseButtonEventArgs e) =>
            OnPreviewMouseLeftButtonDown(e);

        protected override void OnIncreaseLarge()
        {
            IncreaseLargeCalls++;
            base.OnIncreaseLarge();
        }

        protected override void OnIncreaseSmall()
        {
            IncreaseSmallCalls++;
            base.OnIncreaseSmall();
        }

        protected override void OnDecreaseLarge()
        {
            DecreaseLargeCalls++;
            base.OnDecreaseLarge();
        }

        protected override void OnDecreaseSmall()
        {
            DecreaseSmallCalls++;
            base.OnDecreaseSmall();
        }

        protected override void OnMaximizeValue()
        {
            MaximizeCalls++;
            base.OnMaximizeValue();
        }

        protected override void OnMinimizeValue()
        {
            MinimizeCalls++;
            base.OnMinimizeValue();
        }

        protected override void OnThumbDragStarted(DragStartedEventArgs e)
        {
            DragStartedCalls++;
            base.OnThumbDragStarted(e);
        }

        protected override void OnThumbDragDelta(DragDeltaEventArgs e)
        {
            DragDeltaCalls++;
            base.OnThumbDragDelta(e);
        }

        protected override void OnThumbDragCompleted(DragCompletedEventArgs e)
        {
            DragCompletedCalls++;
            LastDragCanceled = e.Canceled;
            base.OnThumbDragCompleted(e);
        }
    }
}

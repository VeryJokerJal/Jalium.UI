using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Input;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using Jalium.UI.Media.Animation;
using AnimationDuration = Jalium.UI.Media.Animation.Duration;

namespace Jalium.UI.Tests;

public class TransitionTests
{
    [Fact]
    public void DetachedElement_DoesNotStartAutomaticTransition()
    {
        var element = new TransitionElement();

        element.TestDouble = 12.0;

        Assert.Equal("None", element.TransitionProperty);
        Assert.Equal(12.0, element.TestDouble);
        Assert.False(element.HasAnimatedValue(TransitionElement.TestDoubleProperty));
        Assert.False(element.HasAutomaticTransition(TransitionElement.TestDoubleProperty));
    }

    [Fact]
    public void AttachedElement_LocalSet_StartsAutomaticTransition_WhenEnabled()
    {
        var host = new TransitionHostPanel();
        var element = new TransitionElement { TransitionProperty = "All" };
        host.AddChild(element);
        Dispatcher.GetForCurrentThread().ProcessQueue();

        element.TestDouble = 10.0;

        Assert.True(element.HasAnimatedValue(TransitionElement.TestDoubleProperty));
        Assert.True(element.HasAutomaticTransition(TransitionElement.TestDoubleProperty));
        Assert.Equal(0.0, element.TestDouble);
    }

    [Fact]
    public void AttachedElement_LocalSet_StartsAutomaticTransition_WhenPropertyListedInCollectionExpression()
    {
        var host = new TransitionHostPanel();
        var element = new TransitionElement
        {
            TransitionProperty = [nameof(TransitionElement.TestDouble)]
        };
        host.AddChild(element);
        Dispatcher.GetForCurrentThread().ProcessQueue();

        element.TestDouble = 10.0;

        Assert.True(element.HasAnimatedValue(TransitionElement.TestDoubleProperty));
        Assert.True(element.HasAutomaticTransition(TransitionElement.TestDoubleProperty));
        Assert.Equal(0.0, element.TestDouble);
    }

    [Fact]
    public void SetLayerValue_StartsAutomaticTransition_WhenElementIsArmedAndEnabled()
    {
        var host = new TransitionHostPanel();
        var element = new TransitionElement { TransitionProperty = "All" };
        host.AddChild(element);
        Dispatcher.GetForCurrentThread().ProcessQueue();

        element.SetLayerValue(
            TransitionElement.TestDoubleProperty,
            7.0,
            DependencyObject.LayerValueSource.StyleSetter);

        Assert.True(element.HasAutomaticTransition(TransitionElement.TestDoubleProperty));
        Assert.Equal(0.0, element.TestDouble);
    }

    [Fact]
    public void ClearValue_TransitionsToUnderlyingLayerValue()
    {
        var host = new TransitionHostPanel();
        var element = new TransitionElement { TransitionProperty = "All" };
        element.SetLayerValue(
            TransitionElement.TestDoubleProperty,
            3.0,
            DependencyObject.LayerValueSource.StyleSetter);
        element.TestDouble = 8.0;

        host.AddChild(element);
        Dispatcher.GetForCurrentThread().ProcessQueue();

        element.ClearValue(TransitionElement.TestDoubleProperty);

        Assert.True(element.HasAutomaticTransition(TransitionElement.TestDoubleProperty));
        Assert.Equal(8.0, element.TestDouble);
    }

    [Fact]
    public void ClearLayerValue_TransitionsToDefaultValue()
    {
        var host = new TransitionHostPanel();
        var element = new TransitionElement { TransitionProperty = "All" };
        element.SetLayerValue(
            TransitionElement.TestDoubleProperty,
            5.0,
            DependencyObject.LayerValueSource.StyleSetter);

        host.AddChild(element);
        Dispatcher.GetForCurrentThread().ProcessQueue();

        element.ClearLayerValue(
            TransitionElement.TestDoubleProperty,
            DependencyObject.LayerValueSource.StyleSetter);

        Assert.True(element.HasAutomaticTransition(TransitionElement.TestDoubleProperty));
        Assert.Equal(5.0, element.TestDouble);
    }

    [Fact]
    public void MidFlightRetarget_StartsFromCurrentDisplayedValue()
    {
        var host = new TransitionHostPanel();
        var element = new TransitionElement { TransitionProperty = "All" };
        host.AddChild(element);
        Dispatcher.GetForCurrentThread().ProcessQueue();

        element.TestDouble = 10.0;
        Thread.Sleep(50);
        TickAnimations(element);
        Thread.Sleep(50);
        TickAnimations(element);
        var midValue = element.TestDouble;

        Assert.InRange(midValue, 0.01, 9.99);

        element.TestDouble = 20.0;

        Assert.InRange(element.TestDouble, midValue - 0.05, midValue + 0.05);
        Assert.True(element.HasAutomaticTransition(TransitionElement.TestDoubleProperty));
    }

    [Fact]
    public void ExplicitAnimation_PreventsAutomaticTransition()
    {
        var host = new TransitionHostPanel();
        var element = new TransitionElement { TransitionProperty = "All" };
        host.AddChild(element);
        Dispatcher.GetForCurrentThread().ProcessQueue();

        element.BeginAnimation(
            TransitionElement.TestDoubleProperty,
            new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = TimeSpan.FromSeconds(1)
            });

        element.TestDouble = 5.0;

        Assert.True(element.HasExplicitAnimation(TransitionElement.TestDoubleProperty));
        Assert.False(element.HasAutomaticTransition(TransitionElement.TestDoubleProperty));
    }

    [Fact]
    public void TransitionProperty_MutatingConfiguredCollection_RebuildsLookup()
    {
        var host = new TransitionHostPanel();
        TransitionPropertyCollection configuredProperties = [];
        var element = new TransitionElement
        {
            TransitionProperty = configuredProperties
        };
        host.AddChild(element);
        Dispatcher.GetForCurrentThread().ProcessQueue();

        element.TestDouble = 3.0;

        Assert.False(element.HasAutomaticTransition(TransitionElement.TestDoubleProperty));
        Assert.Equal(3.0, element.TestDouble);

        configuredProperties.Add(nameof(TransitionElement.TestDouble));
        element.TestDouble = 6.0;

        Assert.True(element.HasAutomaticTransition(TransitionElement.TestDoubleProperty));
        Assert.Equal(3.0, element.TestDouble);
    }

    [Fact]
    public void UserDefinedControl_CustomDependencyProperty_TransitionsWhenOptedIn()
    {
        var host = new TransitionHostPanel();
        var element = new UserDefinedTransitionControl { TransitionProperty = "All" };
        host.AddChild(element);
        Dispatcher.GetForCurrentThread().ProcessQueue();

        element.UserScale = 24.0;

        Assert.True(element.HasAutomaticTransition(UserDefinedTransitionControl.UserScaleProperty));
        Assert.Equal(0.0, element.UserScale);
    }

    [Fact]
    public void UserDefinedAttachedProperty_TransitionsWhenOptedIn()
    {
        var host = new TransitionHostPanel();
        var element = new UserDefinedTransitionControl { TransitionProperty = "All" };
        host.AddChild(element);
        Dispatcher.GetForCurrentThread().ProcessQueue();

        element.SetValue(UserDefinedTransitionProperties.AttachedProgressProperty, 6.0);

        Assert.True(element.HasAutomaticTransition(UserDefinedTransitionProperties.AttachedProgressProperty));
        Assert.Equal(0.0, (double)element.GetValue(UserDefinedTransitionProperties.AttachedProgressProperty)!);
    }

    [Fact]
    public void UserDefinedCustomPropertyType_UsesMetadataTransitionFactory()
    {
        var host = new TransitionHostPanel();
        var element = new CustomTransitionValueElement { TransitionProperty = "All" };
        host.AddChild(element);
        Dispatcher.GetForCurrentThread().ProcessQueue();

        element.CustomValue = new CustomTransitionValue(10.0);

        Assert.True(element.HasAutomaticTransition(CustomTransitionValueElement.CustomValueProperty));
        Assert.Equal(0.0, element.CustomValue.Amount);

        Thread.Sleep(50);
        TickAnimations(element);
        Thread.Sleep(50);
        TickAnimations(element);

        Assert.InRange(element.CustomValue.Amount, 0.01, 9.99);
    }

    [Fact]
    public void UserDefinedControl_CanSuppressAutomaticTransitionForSpecificProperty()
    {
        var host = new TransitionHostPanel();
        var element = new SelectiveTransitionElement { TransitionProperty = "All" };
        host.AddChild(element);
        Dispatcher.GetForCurrentThread().ProcessQueue();

        element.SuppressibleValue = 7.0;
        element.NormalValue = 9.0;

        Assert.False(element.HasAutomaticTransition(SelectiveTransitionElement.SuppressibleValueProperty));
        Assert.Equal(7.0, element.SuppressibleValue);
        Assert.True(element.HasAutomaticTransition(SelectiveTransitionElement.NormalValueProperty));
        Assert.Equal(0.0, element.NormalValue);
    }

    [Fact]
    public void ButtonDerivedControls_DefaultTransitionProperty_IsNone()
    {
        Assert.Equal("None", new Button().TransitionProperty);
        Assert.Equal("None", new HyperlinkButton().TransitionProperty);
        Assert.Equal("None", new RepeatButton().TransitionProperty);
        Assert.Equal("None", new ToggleButton().TransitionProperty);
    }

    [Fact]
    public void ScrollBarAndPrimitives_DefaultTransitionProperty_IsNone()
    {
        var scrollBar = new ScrollBar();
        var host = new TransitionHostPanel();
        host.AddChild(scrollBar);
        Dispatcher.GetForCurrentThread().ProcessQueue();

        scrollBar.Value = 18.0;

        var lineUpButton = Assert.IsType<RepeatButton>(scrollBar.GetVisualChild(0));
        var track = Assert.IsType<Track>(scrollBar.GetVisualChild(1));
        var lineDownButton = Assert.IsType<RepeatButton>(scrollBar.GetVisualChild(2));
        var thumb = Assert.IsType<Thumb>(track.Thumb);

        Assert.Equal("None", scrollBar.TransitionProperty);
        Assert.Equal("None", lineUpButton.TransitionProperty);
        Assert.Equal("None", lineDownButton.TransitionProperty);
        Assert.Equal("None", track.TransitionProperty);
        Assert.Equal("None", thumb.TransitionProperty);
        Assert.False(lineUpButton.UseScrollBarArrowAnimation);
        Assert.False(lineDownButton.UseScrollBarArrowAnimation);
        Assert.Equal("None", track.DecreaseRepeatButton!.TransitionProperty);
        Assert.Equal("None", track.IncreaseRepeatButton!.TransitionProperty);
        Assert.False(scrollBar.HasAutomaticTransition(RangeBase.ValueProperty));
        Assert.Equal(18.0, scrollBar.Value);
    }

    [Fact]
    public void FrameworkAndHeavyControls_DefaultTransitionProperty_IsNone()
    {
        Assert.Equal("None", new TransitionElement().TransitionProperty);
        Assert.Equal("None", new UserDefinedTransitionControl().TransitionProperty);
        Assert.Equal("None", new ScrollViewer().TransitionProperty);
        Assert.Equal("None", new Markdown().TransitionProperty);
        Assert.Equal("None", new DockLayout().TransitionProperty);
        Assert.Equal("None", new DockTabPanel().TransitionProperty);
        Assert.Equal("None", new DockSplitPanel().TransitionProperty);
        Assert.Equal("None", new DockItem().TransitionProperty);
        Assert.Equal("None", new ListBox().TransitionProperty);
        Assert.Equal("None", new ListBoxItem().TransitionProperty);
        Assert.Equal("None", new ListView().TransitionProperty);
        Assert.Equal("None", new ListViewItem().TransitionProperty);
        Assert.Equal("None", new TreeView().TransitionProperty);
        Assert.Equal("None", new TreeViewItem().TransitionProperty);
        Assert.Equal("None", new ComboBox().TransitionProperty);
        Assert.Equal("None", new DatePicker().TransitionProperty);
        Assert.Equal("None", new TimePicker().TransitionProperty);
        Assert.Equal("None", new Slider().TransitionProperty);
        Assert.Equal("None", new NumberBox().TransitionProperty);
        Assert.Equal("None", new ColorPicker().TransitionProperty);
        Assert.Equal("None", new Expander().TransitionProperty);
        Assert.Equal("None", new NavigationViewItem().TransitionProperty);
    }

    [Fact]
    public void AutomaticTransition_FirstRenderingTickStaysAtOriginValue()
    {
        var host = new TransitionHostPanel();
        var element = new TransitionElement { TransitionProperty = "All" };
        host.AddChild(element);
        Dispatcher.GetForCurrentThread().ProcessQueue();

        element.TestDouble = 10.0;

        Thread.Sleep(50);
        TickAnimations(element);
        Assert.Equal(0.0, element.TestDouble);

        Thread.Sleep(50);
        TickAnimations(element);
        Assert.InRange(element.TestDouble, 0.01, 9.99);
    }

    [Fact]
    public void AutomaticTransition_IntProperty_DoesNotStartMidProgress()
    {
        var host = new TransitionHostPanel();
        var element = new IntTransitionElement { TransitionProperty = "All" };
        host.AddChild(element);
        Dispatcher.GetForCurrentThread().ProcessQueue();

        element.TestInt = 6;

        Thread.Sleep(50);
        TickAnimations(element);
        Assert.Equal(0, element.TestInt);

        Thread.Sleep(50);
        TickAnimations(element);
        Assert.InRange(element.TestInt, 1, 5);
    }

    [Fact]
    public void Slider_ValueChangesDuringDrag_DoNotStartAutomaticTransition_WhenValueTransitionEnabled()
    {
        var host = new TransitionHostPanel();
        var slider = new Slider
        {
            Width = 120,
            Height = 24,
            TransitionProperty = "Value"
        };

        host.AddChild(slider);
        Dispatcher.GetForCurrentThread().ProcessQueue();
        slider.Measure(new Size(120, 24));
        slider.Arrange(new Rect(0, 0, 120, 24));

        slider.RaiseEvent(CreateMouseDown(new Point(8, 12)));
        slider.Value = 60.0;

        Assert.False(slider.HasAutomaticTransition(Slider.ValueProperty));
        Assert.Equal(60.0, slider.Value);

        slider.RaiseEvent(CreateMouseUp(new Point(8, 12)));
    }

    [Fact]
    public void Slider_ValueChangesAfterDrag_StartAutomaticTransition_WhenValueTransitionEnabled()
    {
        var host = new TransitionHostPanel();
        var slider = new Slider
        {
            Width = 120,
            Height = 24,
            TransitionProperty = "Value"
        };

        host.AddChild(slider);
        Dispatcher.GetForCurrentThread().ProcessQueue();
        slider.Measure(new Size(120, 24));
        slider.Arrange(new Rect(0, 0, 120, 24));

        slider.RaiseEvent(CreateMouseDown(new Point(8, 12)));
        slider.Value = 40.0;
        slider.RaiseEvent(CreateMouseUp(new Point(8, 12)));

        slider.Value = 90.0;

        Assert.True(slider.HasAutomaticTransition(Slider.ValueProperty));
        Assert.Equal(40.0, slider.Value);
    }

    [Fact]
    public void NumberBox_CommitValue_DoesNotStartAutomaticTransition()
    {
        var host = new TransitionHostPanel();
        var numberBox = new NumberBox { TransitionProperty = "Value" };
        host.AddChild(numberBox);
        Dispatcher.GetForCurrentThread().ProcessQueue();

        var isEditingField = typeof(NumberBox).GetField("_isEditing", BindingFlags.Instance | BindingFlags.NonPublic);
        var commitMethod = typeof(NumberBox).GetMethod("CommitValue", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(isEditingField);
        Assert.NotNull(commitMethod);

        isEditingField!.SetValue(numberBox, true);
        numberBox.Text = "123";

        commitMethod!.Invoke(numberBox, null);

        Assert.False(numberBox.HasAutomaticTransition(NumberBox.ValueProperty));
        Assert.Equal(123.0, numberBox.Value);
        Assert.Equal("123", numberBox.Text);
    }

    [Fact]
    public void ColorPicker_InternalUpdateColor_DoesNotStartAutomaticTransition()
    {
        var host = new TransitionHostPanel();
        var colorPicker = new ColorPicker { TransitionProperty = "Color" };
        host.AddChild(colorPicker);
        Dispatcher.GetForCurrentThread().ProcessQueue();

        var alphaField = typeof(ColorPicker).GetField("_alpha", BindingFlags.Instance | BindingFlags.NonPublic);
        var updateColorMethod = typeof(ColorPicker).GetMethod("UpdateColor", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(alphaField);
        Assert.NotNull(updateColorMethod);

        alphaField!.SetValue(colorPicker, (byte)96);
        updateColorMethod!.Invoke(colorPicker, null);

        Assert.False(colorPicker.HasAutomaticTransition(ColorPicker.ColorProperty));
        Assert.Equal(Color.FromArgb(96, 255, 255, 255), colorPicker.Color);
    }

    [Fact]
    public void ColorPicker_ExternalColorSet_StartsAutomaticTransition_WhenEnabled()
    {
        var host = new TransitionHostPanel();
        var colorPicker = new ColorPicker { TransitionProperty = "Color" };
        host.AddChild(colorPicker);
        Dispatcher.GetForCurrentThread().ProcessQueue();

        var newColor = Color.FromArgb(96, 0, 128, 255);
        colorPicker.Color = newColor;

        Assert.True(colorPicker.HasAutomaticTransition(ColorPicker.ColorProperty));
        Assert.Equal(Color.White, colorPicker.Color);
    }

    [Fact]
    public void XamlReader_ParsesTransitionConfiguration()
    {
        const string xaml = """
            <StackPanel xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                <Border />
                <Border TransitionProperty="All" />
                <Border TransitionProperty="None" />
                <Border TransitionProperty="Opacity, Width, Background"
                        TransitionDuration="0:0:0.18"
                        TransitionTimingFunction="Recommended" />
            </StackPanel>
            """;

        var panel = Assert.IsType<StackPanel>(XamlReader.Parse(xaml));
        var defaultBorder = Assert.IsType<Border>(panel.Children[0]);
        var allBorder = Assert.IsType<Border>(panel.Children[1]);
        var noneBorder = Assert.IsType<Border>(panel.Children[2]);
        var configuredBorder = Assert.IsType<Border>(panel.Children[3]);

        Assert.Equal("None", defaultBorder.TransitionProperty);
        Assert.Equal("All", allBorder.TransitionProperty);
        Assert.Equal("None", noneBorder.TransitionProperty);
        Assert.Equal("Opacity, Width, Background", configuredBorder.TransitionProperty);
        Assert.Equal(new AnimationDuration(TimeSpan.FromMilliseconds(180)), configuredBorder.TransitionDuration);
        Assert.Equal(TransitionTimingFunction.Recommended, configuredBorder.TransitionTimingFunction);
    }

    [Fact]
    public void XamlReader_ParsesTransitionPropertyStyleSetterValue()
    {
        const string xaml = """
            <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                   TargetType="Border">
                <Setter Property="TransitionProperty"
                        Value="Background, Foreground, BorderBrush, SelectionBrush, CaretBrush, LineNumberForeground, CurrentLineBackground, GutterBackground" />
            </Style>
            """;

        var style = Assert.IsType<Style>(XamlReader.Parse(xaml));
        var setter = Assert.IsType<Setter>(Assert.Single(style.Setters));
        var value = Assert.IsType<TransitionPropertyCollection>(setter.Value);

        Assert.Equal(
            "Background, Foreground, BorderBrush, SelectionBrush, CaretBrush, LineNumberForeground, CurrentLineBackground, GutterBackground",
            value);
    }

    private static void TickAnimations(UIElement element)
    {
        var tickMethod = typeof(UIElement).GetMethod(
            "OnRenderingTick",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(tickMethod);
        tickMethod!.Invoke(element, new object?[] { null, EventArgs.Empty });
    }

    private static MouseButtonEventArgs CreateMouseDown(Point position)
    {
        return new MouseButtonEventArgs(
            UIElement.MouseDownEvent,
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
    }

    private static MouseButtonEventArgs CreateMouseUp(Point position)
    {
        return new MouseButtonEventArgs(
            UIElement.MouseUpEvent,
            position,
            MouseButton.Left,
            MouseButtonState.Released,
            clickCount: 1,
            leftButton: MouseButtonState.Released,
            middleButton: MouseButtonState.Released,
            rightButton: MouseButtonState.Released,
            xButton1: MouseButtonState.Released,
            xButton2: MouseButtonState.Released,
            modifiers: ModifierKeys.None,
            timestamp: 1);
    }

    private sealed class TransitionHostPanel : FrameworkElement
    {
        public void AddChild(UIElement child)
        {
            AddVisualChild(child);
        }
    }

    private sealed class TransitionElement : FrameworkElement
    {
        public static readonly DependencyProperty TestDoubleProperty =
            DependencyProperty.Register(
                nameof(TestDouble),
                typeof(double),
                typeof(TransitionElement),
                new PropertyMetadata(0.0));

        public double TestDouble
        {
            get => (double)GetValue(TestDoubleProperty)!;
            set => SetValue(TestDoubleProperty, value);
        }
    }

    private sealed class UserDefinedTransitionControl : FrameworkElement
    {
        public static readonly DependencyProperty UserScaleProperty =
            DependencyProperty.Register(
                nameof(UserScale),
                typeof(double),
                typeof(UserDefinedTransitionControl),
                new PropertyMetadata(0.0));

        public double UserScale
        {
            get => (double)GetValue(UserScaleProperty)!;
            set => SetValue(UserScaleProperty, value);
        }
    }

    private sealed class IntTransitionElement : FrameworkElement
    {
        public static readonly DependencyProperty TestIntProperty =
            DependencyProperty.Register(
                nameof(TestInt),
                typeof(int),
                typeof(IntTransitionElement),
                new PropertyMetadata(0));

        public int TestInt
        {
            get => (int)GetValue(TestIntProperty)!;
            set => SetValue(TestIntProperty, value);
        }
    }

    private sealed class SelectiveTransitionElement : FrameworkElement
    {
        public static readonly DependencyProperty SuppressibleValueProperty =
            DependencyProperty.Register(
                nameof(SuppressibleValue),
                typeof(double),
                typeof(SelectiveTransitionElement),
                new PropertyMetadata(0.0));

        public static readonly DependencyProperty NormalValueProperty =
            DependencyProperty.Register(
                nameof(NormalValue),
                typeof(double),
                typeof(SelectiveTransitionElement),
                new PropertyMetadata(0.0));

        public double SuppressibleValue
        {
            get => (double)GetValue(SuppressibleValueProperty)!;
            set => SetValue(SuppressibleValueProperty, value);
        }

        public double NormalValue
        {
            get => (double)GetValue(NormalValueProperty)!;
            set => SetValue(NormalValueProperty, value);
        }

        protected override bool ShouldSuppressAutomaticTransition(DependencyProperty dp)
        {
            return ReferenceEquals(dp, SuppressibleValueProperty);
        }
    }

    private static class UserDefinedTransitionProperties
    {
        public static readonly DependencyProperty AttachedProgressProperty =
            DependencyProperty.RegisterAttached(
                "AttachedProgress",
                typeof(double),
                typeof(UserDefinedTransitionProperties),
                new PropertyMetadata(0.0));
    }

    private readonly struct CustomTransitionValue : IEquatable<CustomTransitionValue>
    {
        public CustomTransitionValue(double amount)
        {
            Amount = amount;
        }

        public double Amount { get; }

        public bool Equals(CustomTransitionValue other) => Amount.Equals(other.Amount);

        public override bool Equals(object? obj) => obj is CustomTransitionValue other && Equals(other);

        public override int GetHashCode() => Amount.GetHashCode();
    }

    private sealed class CustomTransitionValueElement : FrameworkElement
    {
        public static readonly DependencyProperty CustomValueProperty =
            DependencyProperty.Register(
                nameof(CustomValue),
                typeof(CustomTransitionValue),
                typeof(CustomTransitionValueElement),
                new PropertyMetadata(new CustomTransitionValue(0.0))
                {
                    AutomaticTransitionFactory = static (_, fromValue, toValue, duration, _) =>
                    {
                        return new CustomTransitionValueAnimation
                        {
                            From = fromValue is CustomTransitionValue from ? from : null,
                            To = toValue is CustomTransitionValue to ? to : null,
                            Duration = new AnimationDuration(duration)
                        };
                    }
                });

        public CustomTransitionValue CustomValue
        {
            get => (CustomTransitionValue)GetValue(CustomValueProperty)!;
            set => SetValue(CustomValueProperty, value);
        }
    }

    private sealed class CustomTransitionValueAnimation : AnimationTimeline<CustomTransitionValue>
    {
        public CustomTransitionValue? From { get; init; }

        public CustomTransitionValue? To { get; init; }

        protected override CustomTransitionValue GetCurrentValueCore(
            CustomTransitionValue defaultOriginValue,
            CustomTransitionValue defaultDestinationValue,
            AnimationClock animationClock)
        {
            var from = From ?? defaultOriginValue;
            var to = To ?? defaultDestinationValue;
            var progress = animationClock.CurrentProgress;

            return new CustomTransitionValue(from.Amount + (to.Amount - from.Amount) * progress);
        }
    }
}

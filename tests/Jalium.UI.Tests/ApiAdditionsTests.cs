using System.Collections.ObjectModel;
using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Input;
using Jalium.UI.Media.Animation;
using Jalium.UI.Media.Media3D;

namespace Jalium.UI.Tests;

/// <summary>
/// Covers the WPF-parity APIs added to close audited gaps: the 3-D type
/// animations, <see cref="TextBox"/> casing / line limits,
/// <see cref="ComboBox.StaysOpenOnEdit"/> and the <see cref="DataGrid"/>
/// row-add / row-delete / frozen-column properties.
/// </summary>
[Collection("Application")]
public class ApiAdditionsTests
{
    #region 3-D Animations

    [Fact]
    public void Point3DAnimation_GetCurrentValue_InterpolatesLinearly()
    {
        var animation = new Point3DAnimation(new Point3D(0, 0, 0), new Point3D(10, 20, 30), TimeSpan.FromSeconds(1));

        var value = (Point3D)animation.GetCurrentValue(default(Point3D), default(Point3D), CreateClock(animation, 0.5));

        Assert.Equal(5.0, value.X, 3);
        Assert.Equal(10.0, value.Y, 3);
        Assert.Equal(15.0, value.Z, 3);
    }

    [Fact]
    public void Point3DAnimation_TargetPropertyType_IsPoint3D()
    {
        Assert.Equal(typeof(Point3D), new Point3DAnimation().TargetPropertyType);
    }

    [Fact]
    public void Vector3DAnimation_GetCurrentValue_InterpolatesLinearly()
    {
        var animation = new Vector3DAnimation(new Vector3D(2, 4, 6), new Vector3D(12, 24, 36), TimeSpan.FromSeconds(1));

        var value = (Vector3D)animation.GetCurrentValue(default(Vector3D), default(Vector3D), CreateClock(animation, 0.25));

        Assert.Equal(4.5, value.X, 3);
        Assert.Equal(9.0, value.Y, 3);
        Assert.Equal(13.5, value.Z, 3);
    }

    [Fact]
    public void Vector3DAnimation_By_ComputesDestinationFromOrigin()
    {
        var animation = new Vector3DAnimation
        {
            From = new Vector3D(1, 1, 1),
            By = new Vector3D(10, 20, 30),
            Duration = TimeSpan.FromSeconds(1),
        };

        var value = (Vector3D)animation.GetCurrentValue(default(Vector3D), default(Vector3D), CreateClock(animation, 1.0));

        Assert.Equal(11.0, value.X, 3);
        Assert.Equal(21.0, value.Y, 3);
        Assert.Equal(31.0, value.Z, 3);
    }

    [Fact]
    public void Size3DAnimation_GetCurrentValue_InterpolatesLinearly()
    {
        var animation = new Size3DAnimation(new Size3D(0, 0, 0), new Size3D(8, 16, 24), TimeSpan.FromSeconds(1));

        var value = (Size3D)animation.GetCurrentValue(default(Size3D), default(Size3D), CreateClock(animation, 0.5));

        Assert.Equal(4.0, value.X, 3);
        Assert.Equal(8.0, value.Y, 3);
        Assert.Equal(12.0, value.Z, 3);
    }

    [Fact]
    public void Size3DAnimation_NeverProducesNegativeComponents()
    {
        // From larger than To and read past the end would interpolate below
        // zero without the clamp built into Size3DAnimation.
        var animation = new Size3DAnimation(new Size3D(10, 10, 10), new Size3D(0, 0, 0), TimeSpan.FromSeconds(1));

        var value = (Size3D)animation.GetCurrentValue(default(Size3D), default(Size3D), CreateClock(animation, 1.0));

        Assert.True(value.X >= 0.0 && value.Y >= 0.0 && value.Z >= 0.0);
    }

    [Fact]
    public void Rotation3DAnimation_GetCurrentValue_ReturnsQuaternionRotation()
    {
        var from = new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0);
        var to = new AxisAngleRotation3D(new Vector3D(0, 1, 0), 90);
        var animation = new Rotation3DAnimation(from, to, TimeSpan.FromSeconds(1));

        var value = animation.GetCurrentValue(from, to, CreateClock(animation, 0.5));

        Assert.IsType<QuaternionRotation3D>(value);
    }

    [Fact]
    public void Rotation3DAnimation_AtFullProgress_MatchesTargetRotation()
    {
        var from = new AxisAngleRotation3D(new Vector3D(0, 1, 0), 0);
        var to = new AxisAngleRotation3D(new Vector3D(0, 1, 0), 90);
        var animation = new Rotation3DAnimation(from, to, TimeSpan.FromSeconds(1));

        var result = (QuaternionRotation3D)animation.GetCurrentValue(from, to, CreateClock(animation, 1.0));
        var expected = new Quaternion(new Vector3D(0, 1, 0), 90);

        Assert.Equal(expected.X, result.Quaternion.X, 3);
        Assert.Equal(expected.Y, result.Quaternion.Y, 3);
        Assert.Equal(expected.Z, result.Quaternion.Z, 3);
        Assert.Equal(expected.W, result.Quaternion.W, 3);
    }

    [Fact]
    public void LinearPoint3DKeyFrame_InterpolateValue_IsLinear()
    {
        var keyFrame = new LinearPoint3DKeyFrame(new Point3D(10, 10, 10));

        var value = keyFrame.InterpolateValue(new Point3D(0, 0, 0), 0.5);

        Assert.Equal(5.0, value.X, 3);
        Assert.Equal(5.0, value.Y, 3);
        Assert.Equal(5.0, value.Z, 3);
    }

    [Fact]
    public void Point3DAnimationUsingKeyFrames_ExposesKeyFrameCollection()
    {
        var animation = new Point3DAnimationUsingKeyFrames();

        Assert.NotNull(animation.KeyFrames);
        animation.KeyFrames.Add(new LinearPoint3DKeyFrame(new Point3D(1, 2, 3)));
        Assert.Single(animation.KeyFrames);
    }

    [Fact]
    public void ThreeDimensionalAnimations_FromAndTo_DefaultToNull()
    {
        Assert.Null(new Point3DAnimation().From);
        Assert.Null(new Point3DAnimation().To);
        Assert.Null(new Vector3DAnimation().To);
        Assert.Null(new Size3DAnimation().To);
        Assert.Null(new Rotation3DAnimation().To);
    }

    #endregion

    #region TextBox CharacterCasing / MaxLines / MinLines

    [Fact]
    public void TextBox_CharacterCasing_DefaultsToNormal()
    {
        Assert.Equal(CharacterCasing.Normal, new TextBox().CharacterCasing);
    }

    [Fact]
    public void TextBox_CharacterCasing_Upper_UppercasesTypedInput()
    {
        var textBox = new CasingProbeTextBox { CharacterCasing = CharacterCasing.Upper };

        textBox.TypeText("hello");

        Assert.Equal("HELLO", textBox.Text);
    }

    [Fact]
    public void TextBox_CharacterCasing_Lower_LowercasesTypedInput()
    {
        var textBox = new CasingProbeTextBox { CharacterCasing = CharacterCasing.Lower };

        textBox.TypeText("HeLLo");

        Assert.Equal("hello", textBox.Text);
    }

    [Fact]
    public void TextBox_CharacterCasing_Normal_LeavesTypedInputUnchanged()
    {
        var textBox = new CasingProbeTextBox { CharacterCasing = CharacterCasing.Normal };

        textBox.TypeText("MixedCase");

        Assert.Equal("MixedCase", textBox.Text);
    }

    [Fact]
    public void TextBox_MinLines_DefaultsToOne()
    {
        Assert.Equal(1, new TextBox().MinLines);
    }

    [Fact]
    public void TextBox_MaxLines_DefaultsToIntMaxValue()
    {
        Assert.Equal(int.MaxValue, new TextBox().MaxLines);
    }

    [Fact]
    public void TextBox_MinLines_IncreasesMeasuredHeight()
    {
        ResetApplicationState();
        _ = new Application();

        var single = new TextBox();
        single.Measure(new Size(300, double.PositiveInfinity));
        var singleHeight = single.DesiredSize.Height;

        var tall = new TextBox { MinLines = 8 };
        tall.Measure(new Size(300, double.PositiveInfinity));
        var tallHeight = tall.DesiredSize.Height;

        Assert.True(tallHeight > singleHeight, $"MinLines=8 height {tallHeight} should exceed default {singleHeight}");
    }

    [Fact]
    public void TextBox_MaxLines_CapsMeasuredHeight()
    {
        ResetApplicationState();
        _ = new Application();

        const string eightLines = "1\n2\n3\n4\n5\n6\n7\n8";

        var uncapped = new TextBox { AcceptsReturn = true, Text = eightLines };
        uncapped.Measure(new Size(300, double.PositiveInfinity));
        var uncappedHeight = uncapped.DesiredSize.Height;

        var capped = new TextBox { AcceptsReturn = true, Text = eightLines, MaxLines = 2 };
        capped.Measure(new Size(300, double.PositiveInfinity));
        var cappedHeight = capped.DesiredSize.Height;

        Assert.True(cappedHeight < uncappedHeight, $"MaxLines=2 height {cappedHeight} should be below uncapped {uncappedHeight}");
    }

    #endregion

    #region ComboBox StaysOpenOnEdit

    [Fact]
    public void ComboBox_StaysOpenOnEdit_DefaultsToFalse()
    {
        Assert.False(new ComboBox().StaysOpenOnEdit);
    }

    [Fact]
    public void ComboBox_StaysOpenOnEdit_IsSettable()
    {
        var comboBox = new ComboBox { StaysOpenOnEdit = true };

        Assert.True(comboBox.StaysOpenOnEdit);
    }

    #endregion

    #region DataGrid CanUserAddRows / CanUserDeleteRows / FrozenColumnCount

    [Fact]
    public void DataGrid_CanUserAddRows_DefaultsToTrue()
    {
        Assert.True(new DataGrid().CanUserAddRows);
    }

    [Fact]
    public void DataGrid_CanUserDeleteRows_DefaultsToTrue()
    {
        Assert.True(new DataGrid().CanUserDeleteRows);
    }

    [Fact]
    public void DataGrid_FrozenColumnCount_DefaultsToZero()
    {
        Assert.Equal(0, new DataGrid().FrozenColumnCount);
    }

    [Fact]
    public void DataGrid_FrozenColumnCount_IsSettable()
    {
        var grid = new DataGrid { FrozenColumnCount = 2 };

        Assert.Equal(2, grid.FrozenColumnCount);
    }

    [Fact]
    public void DataGrid_DeleteKey_RemovesSelectedRowFromSource_WhenAllowed()
    {
        var people = new ObservableCollection<Person>
        {
            new() { Name = "Alice" },
            new() { Name = "Bob" },
            new() { Name = "Carol" },
        };
        var grid = new DataGrid { AutoGenerateColumns = false, ItemsSource = people };
        grid.SelectedItem = people[1];

        grid.RaiseEvent(new KeyEventArgs(UIElement.KeyDownEvent, Key.Delete, ModifierKeys.None, isDown: true, isRepeat: false, timestamp: 0));

        Assert.Equal(2, people.Count);
        Assert.DoesNotContain(people, p => p.Name == "Bob");
    }

    [Fact]
    public void DataGrid_DeleteKey_KeepsRow_WhenDeletionDisallowed()
    {
        var people = new ObservableCollection<Person>
        {
            new() { Name = "Alice" },
            new() { Name = "Bob" },
        };
        var grid = new DataGrid { AutoGenerateColumns = false, CanUserDeleteRows = false, ItemsSource = people };
        grid.SelectedItem = people[0];

        grid.RaiseEvent(new KeyEventArgs(UIElement.KeyDownEvent, Key.Delete, ModifierKeys.None, isDown: true, isRepeat: false, timestamp: 0));

        Assert.Equal(2, people.Count);
    }

    #endregion

    #region Helpers

    /// <summary>
    /// A simple bindable item used to populate <see cref="DataGrid.ItemsSource"/>.
    /// </summary>
    public sealed class Person
    {
        public string Name { get; set; } = string.Empty;
    }

    /// <summary>
    /// Exposes the protected <see cref="TextBoxBase"/> insertion path so casing
    /// behavior can be exercised without a live input stack.
    /// </summary>
    private sealed class CasingProbeTextBox : TextBox
    {
        public void TypeText(string text) => InsertText(text);
    }

    private static AnimationClock CreateClock(Timeline timeline, double progress)
    {
        var clock = new AnimationClock(timeline);
        clock.Begin();

        var progressField = typeof(AnimationClock).GetField(
            "_currentProgress",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(progressField);
        progressField!.SetValue(clock, progress);
        return clock;
    }

    private static void ResetApplicationState()
    {
        var currentField = typeof(Application).GetField("_current",
            BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);

        var resetMethod = typeof(ThemeManager).GetMethod("Reset",
            BindingFlags.NonPublic | BindingFlags.Static);
        resetMethod?.Invoke(null, null);
    }

    #endregion
}

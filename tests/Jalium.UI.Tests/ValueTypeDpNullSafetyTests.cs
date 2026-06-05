using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Data;

namespace Jalium.UI.Tests;

/// <summary>
/// A user-defined value type NOT in BindingValueCoercion's known-type table — exercises the
/// Activator.CreateInstance fallback in DependencyProperty's value-type default synthesis.
/// </summary>
public struct ProbeValueStruct
{
    public int X;
    public int Y;
}

/// <summary>
/// Coverage for the framework-wide "a non-nullable value-type dependency property never holds or yields
/// null" hardening that backs the TemplateBinding fix. Complements <see cref="TemplateBindingNullGuardTests"/>
/// by exercising the other write/read paths an illegal value can travel: SetCurrentValue, local-value
/// promotion (ApplyTemplate), Style setters/triggers, and a value-type DP registered with a null default.
/// Every test here would throw NullReferenceException / InvalidCastException on the first read/layout
/// before the fix.
/// </summary>
public class ValueTypeDpNullSafetyTests
{
    // --- #5: value-type DP registered with a null/absent default synthesizes default(T) on read ---

    [Fact]
    public void ValueTypeDp_WithNullOrAbsentDefault_SynthesizesDefault_NoUnboxCrash()
    {
        var probe = new NullSafetyProbe();

        // Each registered its DP with a null/absent metadata default, so a plain GetValue used to return
        // boxed null and unbox-crash. The default-value synthesis returns default(T) instead.
        Assert.Equal(default(Thickness), probe.NullDefaultThickness); // PropertyMetadata(null), framework struct table
        Assert.Equal(0.0, probe.NullDefaultDouble);                   // 3-arg Register (no metadata), primitive
        Assert.Equal(default(ProbeValueStruct), probe.NullDefaultCustom); // user struct -> Activator fallback
    }

    [Fact]
    public void ReferenceTypeDp_WithNullDefault_StaysNull()
    {
        var probe = new NullSafetyProbe();

        // The synthesis must apply ONLY to non-nullable value types; reference types keep their real null.
        Assert.Null(probe.NullDefaultReference);
    }

    // --- #1: SetCurrentValue degrades an illegal null instead of pinning it (Default/Inherited path) ---

    [Fact]
    public void SetCurrentValue_NullIntoNonNullableValueType_DegradesToDefault_NoCrash()
    {
        var border = new Border();

        // Base source is Default, so this routes through SetCurrentValueForSource's Default branch which
        // writes _currentValues directly, bypassing the SetLayerValueCore backstop. Without the entry
        // guard it pins (null, Default) and the getter unbox-crashes.
        border.SetCurrentValue(Border.BorderThicknessProperty, null);

        Assert.Equal(new Thickness(0), border.BorderThickness);
    }

    [Fact]
    public void SetCurrentValue_NullIntoReferenceType_IsAccepted()
    {
        var border = new Border();

        border.SetCurrentValue(Border.BackgroundProperty, null);

        Assert.Null(border.Background); // null is a legitimate Brush value — the guard must not block it
    }

    // --- #3: local-value promotion (ApplyTemplate path) drops an illegal null value-type local ---

    [Fact]
    public void PromoteLocalValuesToLayer_DropsNullValueTypeLocal_NoCrash()
    {
        var probe = new NullSafetyProbe();

        // Deliberate untyped null local on a value-type DP (the only way to plant one — typed setters
        // can't). TargetThickness has no change-callback, so the local set itself is harmless.
        probe.SetValue(NullSafetyProbe.TargetThicknessProperty, (object?)null);

        // The ApplyTemplate-time promotion writes the layer dictionaries directly (bypassing the
        // SetLayerValueCore backstop); the guard there drops the null instead of promoting it.
        probe.PromoteLocalValuesToLayer(DependencyObject.LayerValueSource.ParentTemplate);

        Assert.Equal(new Thickness(9), probe.TargetThickness); // dropped -> registered default
    }

    // --- #4: Style setters / triggers degrade a type-mismatched value into a value-type DP ---

    [Fact]
    public void StyleSetter_TypeMismatchedValueIntoValueTypeDp_IsSkipped()
    {
        var style = new Style(typeof(NullSafetyProbe));
        style.Setters.Add(new Setter(NullSafetyProbe.TargetThicknessProperty, new object()));

        var probe = new NullSafetyProbe { Style = style };

        // A non-null, type-incompatible value survives ConvertValueIfNeeded unchanged; pinning it would
        // throw InvalidCastException at the getter. The post-conversion IsValidType gate skips it.
        Assert.Equal(new Thickness(9), probe.TargetThickness);
        Assert.Equal(
            BaseValueSource.Default,
            DependencyPropertyHelper.GetValueSource(probe, NullSafetyProbe.TargetThicknessProperty).BaseValueSource);
    }

    [Fact]
    public void StyleSetter_UnconvertibleStringIntoValueTypeDp_IsSkipped()
    {
        var style = new Style(typeof(NullSafetyProbe));
        // A string with no converter for ProbeValueStruct: ConvertValueIfNeeded returns it unchanged.
        // The old trigger guard explicitly EXCLUDED strings — this is the gap the post-conversion gate closes.
        style.Setters.Add(new Setter(NullSafetyProbe.NullDefaultCustomProperty, "not a ProbeValueStruct"));

        var probe = new NullSafetyProbe { Style = style };

        Assert.Equal(default(ProbeValueStruct), probe.NullDefaultCustom);
    }

    [Fact]
    public void StyleSetter_ValidValueIntoValueTypeDp_StillApplies()
    {
        var style = new Style(typeof(NullSafetyProbe));
        style.Setters.Add(new Setter(NullSafetyProbe.TargetThicknessProperty, new Thickness(3)));

        var probe = new NullSafetyProbe { Style = style };

        // Regression guard: a legitimate struct value must still flow through (the gate rejects nothing valid).
        Assert.Equal(new Thickness(3), probe.TargetThickness);
        Assert.Equal(
            BaseValueSource.Style,
            DependencyPropertyHelper.GetValueSource(probe, NullSafetyProbe.TargetThicknessProperty).BaseValueSource);
    }

    [Fact]
    public void StyleTrigger_TypeMismatchedValueIntoValueTypeDp_IsSkipped()
    {
        var style = new Style(typeof(NullSafetyProbe));
        var trigger = new Trigger { Property = NullSafetyProbe.FlagProperty, Value = true };
        trigger.Setters.Add(new Setter(NullSafetyProbe.TargetThicknessProperty, new object()));
        style.Triggers.Add(trigger);

        var probe = new NullSafetyProbe { Style = style };
        probe.SetValue(NullSafetyProbe.FlagProperty, true); // activate the trigger

        // The trigger setter's mismatched value is skipped post-conversion; the property keeps its default.
        Assert.Equal(new Thickness(9), probe.TargetThickness);
    }

    // --- Round 3: local-write backstop (plain SetValue + the data-binding coerced target write) ---

    [Fact]
    public void SetValue_NullIntoNonNullableValueType_DegradesToDefault_NoCrash()
    {
        var probe = new NullSafetyProbe();

        // Loosely-typed null into a value-type DP — the canonical write path (plain SetValue and the
        // binding pipeline's coerced target write both land here). Without the local backstop this pins
        // a boxed null and the getter unbox-crashes.
        probe.SetValue(NullSafetyProbe.TargetThicknessProperty, (object?)null);

        Assert.Equal(new Thickness(9), probe.TargetThickness); // dropped -> registered default
        Assert.Equal(
            BaseValueSource.Default,
            DependencyPropertyHelper.GetValueSource(probe, NullSafetyProbe.TargetThicknessProperty).BaseValueSource);
    }

    [Fact]
    public void SetValue_NullIntoValueTypeAbsentFromCoercionTable_DegradesToDefault()
    {
        var probe = new NullSafetyProbe();

        // GridLength is NOT in BindingValueCoercion's reflection-free default table, so a {Binding} to a
        // null source coerces to null and stores it via SetValue — this is exactly the crash vector the
        // local backstop closes (RowDefinition.Height / ColumnDefinition.Width / ColorPicker.Color shape).
        probe.SetValue(NullSafetyProbe.GridLengthValueProperty, (object?)null);

        Assert.Equal(new GridLength(7), probe.GridLengthValue);
    }

    [Fact]
    public void SetValue_NullIntoReferenceType_IsStored()
    {
        var probe = new NullSafetyProbe();

        probe.SetValue(NullSafetyProbe.NullDefaultReferenceProperty, (object?)null);

        Assert.Null(probe.NullDefaultReference); // null is legal for a reference type — must not be dropped
    }

    [Fact]
    public void Binding_NullSourceIntoValueTypeDp_DegradesToDefault_NoCrash()
    {
        var probe = new NullSafetyProbe { DataContext = new NullSource() };

        // {Binding Value} where Value == null onto a GridLength target (absent from the coercion table):
        // null -> Coerce returns null -> Target.SetValue(null) -> local backstop -> registered default.
        probe.SetBinding(NullSafetyProbe.GridLengthValueProperty, new Binding("Value"));

        Assert.Equal(new GridLength(7), probe.GridLengthValue);
    }

    private sealed class NullSource
    {
        public object? Value => null;
    }

    private sealed class NullSafetyProbe : Control
    {
        public static readonly DependencyProperty NullDefaultThicknessProperty =
            DependencyProperty.Register(
                nameof(NullDefaultThickness), typeof(Thickness), typeof(NullSafetyProbe), new PropertyMetadata(null));

        public Thickness NullDefaultThickness
        {
            get => (Thickness)GetValue(NullDefaultThicknessProperty)!;
            set => SetValue(NullDefaultThicknessProperty, value);
        }

        // 3-arg Register overload: metadata defaults to null -> DefaultValue is null.
        public static readonly DependencyProperty NullDefaultDoubleProperty =
            DependencyProperty.Register(nameof(NullDefaultDouble), typeof(double), typeof(NullSafetyProbe));

        public double NullDefaultDouble
        {
            get => (double)GetValue(NullDefaultDoubleProperty)!;
            set => SetValue(NullDefaultDoubleProperty, value);
        }

        public static readonly DependencyProperty NullDefaultCustomProperty =
            DependencyProperty.Register(
                nameof(NullDefaultCustom), typeof(ProbeValueStruct), typeof(NullSafetyProbe), new PropertyMetadata(null));

        public ProbeValueStruct NullDefaultCustom
        {
            get => (ProbeValueStruct)GetValue(NullDefaultCustomProperty)!;
            set => SetValue(NullDefaultCustomProperty, value);
        }

        // Reference-typed DP with a null default — must NOT be synthesized.
        public static readonly DependencyProperty NullDefaultReferenceProperty =
            DependencyProperty.Register(
                nameof(NullDefaultReference), typeof(object), typeof(NullSafetyProbe), new PropertyMetadata(null));

        public object? NullDefaultReference
        {
            get => GetValue(NullDefaultReferenceProperty);
            set => SetValue(NullDefaultReferenceProperty, value);
        }

        // Value-type DP with a REAL non-zero default, used as the target for mismatch/promotion tests so
        // "kept its default" is distinguishable from "got the illegal value".
        public static readonly DependencyProperty TargetThicknessProperty =
            DependencyProperty.Register(
                nameof(TargetThickness), typeof(Thickness), typeof(NullSafetyProbe), new PropertyMetadata(new Thickness(9)));

        public Thickness TargetThickness
        {
            get => (Thickness)GetValue(TargetThicknessProperty)!;
            set => SetValue(TargetThicknessProperty, value);
        }

        // GridLength is a framework struct deliberately ABSENT from BindingValueCoercion's table, so a
        // null binding source coerces to null — used to cover the local-write backstop for that vector.
        public static readonly DependencyProperty GridLengthValueProperty =
            DependencyProperty.Register(
                nameof(GridLengthValue), typeof(GridLength), typeof(NullSafetyProbe), new PropertyMetadata(new GridLength(7)));

        public GridLength GridLengthValue
        {
            get => (GridLength)GetValue(GridLengthValueProperty)!;
            set => SetValue(GridLengthValueProperty, value);
        }

        public static readonly DependencyProperty FlagProperty =
            DependencyProperty.Register(nameof(Flag), typeof(bool), typeof(NullSafetyProbe), new PropertyMetadata(false));

        public bool Flag
        {
            get => (bool)GetValue(FlagProperty)!;
            set => SetValue(FlagProperty, value);
        }
    }
}

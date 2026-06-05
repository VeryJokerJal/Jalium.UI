using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

/// <summary>
/// Regression coverage for the TemplateBinding value-type null/mismatch guard.
///
/// Before the fix, a TemplateBinding whose source value could not legally inhabit a non-nullable
/// value-type target (a null reference, or a type-incompatible instance) was written verbatim into
/// the target's ParentTemplate precedence layer. That layer outranks the registered default, so the
/// CLR accessor — e.g. <c>(Thickness)GetValue(BorderThicknessProperty)</c> — unboxed a boxed null and
/// threw <see cref="System.NullReferenceException"/> during the first layout pass (the reported
/// Gallery initial-layout crash). The fix mirrors WPF's <c>StyleHelper</c>: an invalid value degrades
/// to <c>UnsetValue</c> semantics, so the property falls through to its default / lower-precedence
/// value instead of pinning the bad value.
/// </summary>
public class TemplateBindingNullGuardTests
{
    // --- The named layer: TemplateBindingExpression (SetTemplateBinding(DP, DP) / code templates) ---

    [Fact]
    public void TemplateBinding_NullSourceIntoValueTypeTarget_FallsBackToDefault_NoCrash()
    {
        var control = new GuardProbeControl { Width = 100, Height = 40 };

        // Tag is a null object — a mis-typed null source bound onto a non-nullable Thickness target.
        control.Template = BuildBorderTemplate(b =>
            b.SetTemplateBinding(Border.BorderThicknessProperty, FrameworkElement.TagProperty));

        // Initial layout: this is exactly where (Thickness)null previously unbox-crashed.
        control.Measure(new Size(200, 100));
        control.Arrange(new Rect(0, 0, 200, 100));

        var border = Assert.IsType<Border>(control.GetVisualChild(0));
        Assert.Equal(new Thickness(0), border.BorderThickness); // Border's registered default
        Assert.Equal(
            BaseValueSource.Default,
            DependencyPropertyHelper.GetValueSource(border, Border.BorderThicknessProperty).BaseValueSource);
    }

    [Fact]
    public void TemplateBinding_TypeMismatchedNonNullSource_FallsBackToDefault()
    {
        var control = new GuardProbeControl { Width = 100, Height = 40, Tag = "not a thickness" };

        control.Template = BuildBorderTemplate(b =>
            b.SetTemplateBinding(Border.BorderThicknessProperty, FrameworkElement.TagProperty));

        control.Measure(new Size(200, 100));
        control.Arrange(new Rect(0, 0, 200, 100));

        var border = Assert.IsType<Border>(control.GetVisualChild(0));
        // A non-null but type-incompatible value (string -> Thickness) is also rejected: TemplateBinding
        // does no implicit conversion, so the property keeps its default rather than storing garbage.
        Assert.Equal(new Thickness(0), border.BorderThickness);
        Assert.Equal(
            BaseValueSource.Default,
            DependencyPropertyHelper.GetValueSource(border, Border.BorderThicknessProperty).BaseValueSource);
    }

    [Fact]
    public void TemplateBinding_ValidStructSource_StillTransfers()
    {
        // SourceThickness defaults to Thickness(5); distinct from Border's Thickness(0) default so we can
        // tell "transferred" apart from "fell back to default".
        var control = new GuardProbeControl { Width = 100, Height = 40 };

        control.Template = BuildBorderTemplate(b =>
            b.SetTemplateBinding(Border.BorderThicknessProperty, GuardProbeControl.SourceThicknessProperty));

        control.Measure(new Size(200, 100));
        control.Arrange(new Rect(0, 0, 200, 100));

        var border = Assert.IsType<Border>(control.GetVisualChild(0));
        Assert.Equal(new Thickness(5), border.BorderThickness);
        Assert.Equal(
            BaseValueSource.ParentTemplate,
            DependencyPropertyHelper.GetValueSource(border, Border.BorderThicknessProperty).BaseValueSource);
    }

    [Fact]
    public void TemplateBinding_NullSourceIntoReferenceTypeTarget_StillTransfersNull()
    {
        var control = new GuardProbeControl { Width = 100, Height = 40 };

        // Background is a reference type — null is a legitimate value and must NOT be clobbered by the
        // guard. This asserts the fix is surgical and does not over-reach to nullable/reference targets.
        control.Template = BuildBorderTemplate(b =>
            b.SetTemplateBinding(Border.BackgroundProperty, FrameworkElement.TagProperty));

        control.Measure(new Size(200, 100));
        control.Arrange(new Rect(0, 0, 200, 100));

        var border = Assert.IsType<Border>(control.GetVisualChild(0));
        Assert.Null(border.Background);
        // The template binding genuinely contributes null at the ParentTemplate layer (not cleared).
        Assert.Equal(
            BaseValueSource.ParentTemplate,
            DependencyPropertyHelper.GetValueSource(border, Border.BackgroundProperty).BaseValueSource);
    }

    // --- The named layer: DeferredTemplateBindingExpression ({TemplateBinding} markup / jalxaml) ---

    [Fact]
    public void DeferredTemplateBinding_NullSourceIntoValueTypeTarget_FallsBackToDefault_NoCrash()
    {
        var control = new GuardProbeControl { Width = 100, Height = 40 };

        // Resolves "Tag" by name against the templated parent — the markup-extension code path.
        control.Template = BuildBorderTemplate(b =>
            b.SetBinding(Border.BorderThicknessProperty, new DeferredTemplateBinding("Tag")));

        control.Measure(new Size(200, 100));
        control.Arrange(new Rect(0, 0, 200, 100));

        var border = Assert.IsType<Border>(control.GetVisualChild(0));
        Assert.Equal(new Thickness(0), border.BorderThickness);
        Assert.Equal(
            BaseValueSource.Default,
            DependencyPropertyHelper.GetValueSource(border, Border.BorderThicknessProperty).BaseValueSource);
    }

    // --- The central backstop: SetLayerValueCore (Style setters/triggers, DynamicResource, SetCurrentValue) ---

    [Fact]
    public void SetLayerValue_NullIntoNonNullableValueType_IsDroppedToDefault()
    {
        var border = new Border();

        // Any layer writer funnels through SetLayerValueCore; without the backstop this null would later
        // unbox-crash at the (Thickness) getter.
        border.SetLayerValue(Border.BorderThicknessProperty, null, DependencyObject.LayerValueSource.StyleSetter);

        Assert.Equal(new Thickness(0), border.BorderThickness);
        Assert.Equal(
            BaseValueSource.Default,
            DependencyPropertyHelper.GetValueSource(border, Border.BorderThicknessProperty).BaseValueSource);
    }

    [Fact]
    public void SetLayerValue_NullAfterValidValue_RevertsToDefault()
    {
        var border = new Border();

        border.SetLayerValue(Border.BorderThicknessProperty, new Thickness(4), DependencyObject.LayerValueSource.StyleSetter);
        Assert.Equal(new Thickness(4), border.BorderThickness);

        // A later null on the same layer drops the contribution rather than pinning null.
        border.SetLayerValue(Border.BorderThicknessProperty, null, DependencyObject.LayerValueSource.StyleSetter);
        Assert.Equal(new Thickness(0), border.BorderThickness);
        Assert.Equal(
            BaseValueSource.Default,
            DependencyPropertyHelper.GetValueSource(border, Border.BorderThicknessProperty).BaseValueSource);
    }

    [Fact]
    public void SetLayerValue_NullIntoReferenceType_IsStoredNormally()
    {
        var border = new Border();

        // null is legal for a reference-typed DP — the backstop must leave it untouched.
        border.SetLayerValue(Border.BackgroundProperty, null, DependencyObject.LayerValueSource.StyleSetter);

        Assert.Null(border.Background);
        Assert.Equal(
            BaseValueSource.Style,
            DependencyPropertyHelper.GetValueSource(border, Border.BackgroundProperty).BaseValueSource);
    }

    // --- The predicate itself ---

    [Fact]
    public void IsValidType_NullIsInvalidForNonNullableValueType_ValidOtherwise()
    {
        // null into a non-nullable value type -> invalid (the crash case)
        Assert.False(Border.BorderThicknessProperty.IsValidType(null));
        // valid struct value -> valid
        Assert.True(Border.BorderThicknessProperty.IsValidType(new Thickness(1)));
        // non-null but incompatible type -> invalid
        Assert.False(Border.BorderThicknessProperty.IsValidType("not a thickness"));

        // null into a reference type -> valid
        Assert.True(Border.BackgroundProperty.IsValidType(null));

        // Nullable<T> accepts null, and a boxed T (runtime type T) is valid for a Nullable<T> target.
        Assert.True(GuardProbeControl.NullableDoubleProperty.IsValidType(null));
        Assert.True(GuardProbeControl.NullableDoubleProperty.IsValidType(3.0));
    }

    private static ControlTemplate BuildBorderTemplate(Action<Border> configure)
    {
        var template = new ControlTemplate(typeof(GuardProbeControl));
        template.SetVisualTree(() =>
        {
            var border = new Border();
            configure(border);
            return border;
        });
        return template;
    }

    private sealed class GuardProbeControl : Control
    {
        public static readonly DependencyProperty SourceThicknessProperty =
            DependencyProperty.Register(
                nameof(SourceThickness),
                typeof(Thickness),
                typeof(GuardProbeControl),
                new PropertyMetadata(new Thickness(5)));

        public Thickness SourceThickness
        {
            get => (Thickness)GetValue(SourceThicknessProperty)!;
            set => SetValue(SourceThicknessProperty, value);
        }

        public static readonly DependencyProperty NullableDoubleProperty =
            DependencyProperty.Register(
                nameof(NullableDouble),
                typeof(double?),
                typeof(GuardProbeControl),
                new PropertyMetadata(null));

        public double? NullableDouble
        {
            get => (double?)GetValue(NullableDoubleProperty);
            set => SetValue(NullableDoubleProperty, value);
        }
    }
}

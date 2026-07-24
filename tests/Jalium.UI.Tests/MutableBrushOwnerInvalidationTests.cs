using System.Reflection;
using System.Runtime.CompilerServices;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class MutableBrushOwnerInvalidationTests
{
    private static readonly FieldInfo s_isRenderDirtyField =
        typeof(Visual).GetField("_isRenderDirty", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Visual._isRenderDirty field not found.");

    [Fact]
    public void SolidColorMutation_InvalidatesOnlyVisualOwners()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0x10, 0x20, 0x30));
        var owner = new Border { Background = brush };
        var unrelated = new Border();
        _ = owner.Background;
        ClearRenderDirty(owner, unrelated);

        brush.Color = Color.FromRgb(0x40, 0x50, 0x60);

        Assert.True(IsRenderDirty(owner));
        Assert.False(IsRenderDirty(unrelated));
    }

    [Fact]
    public void SharedBrushMutation_InvalidatesEveryOwner()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0x10, 0x20, 0x30));
        var first = new Border { Background = brush };
        var second = new Border { BorderBrush = brush };
        _ = first.Background;
        _ = second.BorderBrush;
        ClearRenderDirty(first, second);

        brush.Color = Color.FromRgb(0x40, 0x50, 0x60);

        Assert.True(IsRenderDirty(first));
        Assert.True(IsRenderDirty(second));
    }

    [Fact]
    public void ReplacingBrush_RemovesOnlyThatPropertyUse()
    {
        var shared = new SolidColorBrush(Color.FromRgb(0x10, 0x20, 0x30));
        var replacement = new SolidColorBrush(Color.FromRgb(0x70, 0x80, 0x90));
        var owner = new Border
        {
            Background = shared,
            BorderBrush = shared
        };
        _ = owner.Background;
        _ = owner.BorderBrush;

        owner.Background = replacement;
        _ = owner.Background;
        ClearRenderDirty(owner);
        shared.Color = Color.FromRgb(0x40, 0x50, 0x60);
        Assert.True(IsRenderDirty(owner));

        owner.BorderBrush = replacement;
        _ = owner.BorderBrush;
        ClearRenderDirty(owner);
        shared.Color = Color.FromRgb(0xA0, 0xB0, 0xC0);
        Assert.False(IsRenderDirty(owner));

        replacement.Color = Color.FromRgb(0x01, 0x02, 0x03);
        Assert.True(IsRenderDirty(owner));
    }

    [Fact]
    public void GradientStopMutation_InvalidatesGradientOwner()
    {
        var gradient = new LinearGradientBrush(
            Color.FromRgb(0x10, 0x20, 0x30),
            Color.FromRgb(0x40, 0x50, 0x60),
            new Point(0, 0),
            new Point(1, 1));
        var owner = new Border { Background = gradient };
        _ = owner.Background;
        ClearRenderDirty(owner);

        gradient.GradientStops[0].Color = Color.FromRgb(0x70, 0x80, 0x90);

        Assert.True(IsRenderDirty(owner));
    }

    [Fact]
    public void BrushStoredInNonBrushProperty_DoesNotBecomeRenderOwner()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0x10, 0x20, 0x30));
        var element = new Border { Tag = brush };
        _ = element.Tag;
        ClearRenderDirty(element);

        brush.Color = Color.FromRgb(0x40, 0x50, 0x60);

        Assert.False(IsRenderDirty(element));
    }

    [Fact]
    public void ReadingInheritedBrush_RegistersTheDrawingChildAsOwner()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0x10, 0x20, 0x30));
        var parent = new VisualHostControl { Foreground = brush };
        var child = new TextBlock { Text = "Inherited foreground" };
        parent.Attach(child);

        Assert.Same(brush, child.Foreground);
        ClearRenderDirty(parent, child);

        brush.Color = Color.FromRgb(0x40, 0x50, 0x60);

        Assert.True(IsRenderDirty(child));
    }

    [Fact]
    public void SharedBrushOwnerRegistration_DoesNotKeepElementAlive()
    {
        var brush = new SolidColorBrush(Color.FromRgb(0x10, 0x20, 0x30));
        WeakReference ownerReference = CreateWeakOwner(brush);

        CollectOwner();

        Assert.False(ownerReference.IsAlive);

        // Also exercises dead-registration pruning; a collected owner must neither throw nor
        // leave a permanent tombstone when the shared brush changes later.
        brush.Color = Color.FromRgb(0x40, 0x50, 0x60);
        GC.KeepAlive(brush);
    }

    [Fact]
    public void SingleOwner_UsesCompactWeakSlot_AndPromotesOnlyWhenShared()
    {
        var singleOwnerField = typeof(Brush).GetField(
            "_singleRenderOwner",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var sharedOwnersField = typeof(Brush).GetField(
            "_renderOwners",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var brush = new SolidColorBrush(Color.FromRgb(0x10, 0x20, 0x30));
        var first = new Border { Background = brush };
        _ = first.Background;

        Assert.NotNull(singleOwnerField.GetValue(brush));
        Assert.Null(sharedOwnersField.GetValue(brush));

        var second = new Border { Background = brush };
        _ = second.Background;

        Assert.Null(singleOwnerField.GetValue(brush));
        Assert.NotNull(sharedOwnersField.GetValue(brush));
    }

    [Fact]
    public void ThemeSwitch_DoesNotDirtyVisualWithoutThemeBrushDependencies()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var application = new Application();

        try
        {
            var probe = new RenderProbe();
            var root = new StackPanel();
            root.Children.Add(probe);
            application.MainWindow = new Window { Content = root };
            ClearRenderDirty(probe);

            ThemeVariant target = ThemeManager.CurrentTheme == ThemeVariant.Dark
                ? ThemeVariant.Light
                : ThemeVariant.Dark;
            ThemeManager.ApplyTheme(target);

            Assert.False(IsRenderDirty(probe));
        }
        finally
        {
            ResetApplicationState();
        }
    }

    private static bool IsRenderDirty(Visual visual) => (bool)s_isRenderDirtyField.GetValue(visual)!;

    private static void ClearRenderDirty(params Visual[] visuals)
    {
        foreach (Visual visual in visuals)
        {
            s_isRenderDirtyField.SetValue(visual, false);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference CreateWeakOwner(Brush brush)
    {
        var owner = new Border { Background = brush };
        _ = owner.Background;
        return new WeakReference(owner);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CollectOwner()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static void ResetApplicationState()
    {
        typeof(Application)
            .GetField("_current", BindingFlags.NonPublic | BindingFlags.Static)?
            .SetValue(null, null);
        typeof(ThemeManager)
            .GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static)?
            .Invoke(null, null);
    }

    private sealed class VisualHostControl : Control
    {
        public void Attach(UIElement child) => AddVisualChild(child);
    }

    private sealed class RenderProbe : FrameworkElement
    {
    }
}

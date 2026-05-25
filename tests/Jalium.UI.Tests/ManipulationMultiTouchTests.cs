using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

/// <summary>
/// Drives the multi-touch <c>PointerManipulationSession</c> (a private nested type
/// inside <see cref="WindowInputDispatcher"/>) via reflection to validate that two
/// fingers spreading apart produce a scale &gt; 1 and rotating produces a non-zero
/// rotation. The session is the production source of truth for translation/scale/
/// rotation aggregation, so we exercise it directly rather than spinning up a Window.
/// </summary>
public class ManipulationMultiTouchTests
{
    [Fact]
    public void TwoFingersSpreading_YieldsScaleGreaterThanOne()
    {
        var session = CreateSession();
        // Two pointers symmetric about origin (0,0), 10 DIPs apart on the x-axis.
        AddPointer(session, id: 1, x: -5, y: 0, ts: 0);
        AddPointer(session, id: 2, x: 5, y: 0, ts: 0);

        // Spread to 20 DIPs apart on the x-axis (2× larger).
        UpdatePointer(session, id: 1, x: -10, y: 0);
        UpdatePointer(session, id: 2, x: 10, y: 0);
        var frame = ComputeFrameDelta(session, ts: 16);

        // Frame scale ≈ 2.0 (new spread 10 / base spread 5).
        Assert.True(frame.FrameScaleX > 1.5, $"Expected scale > 1.5 got {frame.FrameScaleX}");
        Assert.True(frame.DeltaExpansionX > 0, $"Expected expansion > 0 got {frame.DeltaExpansionX}");
    }

    [Fact]
    public void TwoFingersRotating_YieldsNonZeroRotation()
    {
        var session = CreateSession();
        // Two pointers along the x-axis.
        AddPointer(session, id: 10, x: -5, y: 0, ts: 0);
        AddPointer(session, id: 11, x: 5, y: 0, ts: 0);

        // Rotate ~90° (now along the y-axis, same spread).
        UpdatePointer(session, id: 10, x: 0, y: -5);
        UpdatePointer(session, id: 11, x: 0, y: 5);
        var frame = ComputeFrameDelta(session, ts: 16);

        // Rotation should be near ±90°.
        Assert.True(Math.Abs(frame.DeltaRotation) > 60.0,
            $"Expected |rotation| > 60° got {frame.DeltaRotation}°");
    }

    [Fact]
    public void SingleFinger_OnlyTranslates_NoScaleNoRotation()
    {
        var session = CreateSession();
        AddPointer(session, id: 50, x: 0, y: 0, ts: 0);
        UpdatePointer(session, id: 50, x: 10, y: 5);
        var frame = ComputeFrameDelta(session, ts: 16);

        Assert.Equal(10, frame.DeltaTranslationX, precision: 3);
        Assert.Equal(5, frame.DeltaTranslationY, precision: 3);
        Assert.Equal(0, frame.DeltaRotation, precision: 3);
        Assert.Equal(1.0, frame.FrameScaleX, precision: 3);
    }

    [Fact]
    public void RemovingOnePointer_RebaselinesWithoutJump()
    {
        var session = CreateSession();
        AddPointer(session, id: 100, x: 0, y: 0, ts: 0);
        AddPointer(session, id: 101, x: 100, y: 0, ts: 0);

        // Initial frame to "consume" the spread, then remove pointer 101.
        UpdatePointer(session, id: 100, x: 0, y: 0);
        UpdatePointer(session, id: 101, x: 100, y: 0);
        _ = ComputeFrameDelta(session, ts: 16);

        RemovePointer(session, id: 101);
        Rebaseline(session, ts: 32);

        // Moving the remaining pointer should yield a clean translation
        // without echoing the removed pointer's contribution.
        UpdatePointer(session, id: 100, x: 5, y: 0);
        var frame = ComputeFrameDelta(session, ts: 48);
        Assert.Equal(5, frame.DeltaTranslationX, precision: 3);
        Assert.Equal(1.0, frame.FrameScaleX, precision: 3);
        Assert.Equal(0, frame.DeltaRotation, precision: 3);
    }

    // ────────────────────────────────────────────────────────────────────
    //  Reflection plumbing — keeps these tests pinned to the production
    //  session implementation rather than a duplicate test copy.
    // ────────────────────────────────────────────────────────────────────

    private static readonly Type SessionType = typeof(WindowInputDispatcher)
        .GetNestedType("PointerManipulationSession", BindingFlags.NonPublic)!;

    private static object CreateSession()
    {
        var target = new Border();
        // PointerManipulationSession(UIElement, Point, int, ManipulationModes, bool, ManipulationPivot?)
        return SessionType
            .GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            .First()
            .Invoke(new object?[] { target, new Point(0, 0), 0, ManipulationModes.All, false, null });
    }

    private static void AddPointer(object session, uint id, double x, double y, int ts)
    {
        SessionType.GetMethod("AddPointer")!.Invoke(session, new object[] { id, new Point(x, y), ts });
    }

    private static void UpdatePointer(object session, uint id, double x, double y)
    {
        SessionType.GetMethod("UpdatePointer")!.Invoke(session, new object[] { id, new Point(x, y) });
    }

    private static void RemovePointer(object session, uint id)
    {
        SessionType.GetMethod("RemovePointer")!.Invoke(session, new object[] { id });
    }

    private static void Rebaseline(object session, int ts)
    {
        SessionType.GetMethod("Rebaseline")!.Invoke(session, new object[] { ts });
    }

    private static FrameSnapshot ComputeFrameDelta(object session, int ts)
    {
        var result = SessionType.GetMethod("ComputeFrameDelta")!.Invoke(session, new object[] { ts });
        Type frameType = result!.GetType();
        Vector deltaTranslation = (Vector)frameType.GetProperty("DeltaTranslation")!.GetValue(result)!;
        double deltaRotation = (double)frameType.GetProperty("DeltaRotation")!.GetValue(result)!;
        Vector deltaExpansion = (Vector)frameType.GetProperty("DeltaExpansion")!.GetValue(result)!;
        Vector frameScale = (Vector)frameType.GetProperty("FrameScale")!.GetValue(result)!;

        return new FrameSnapshot
        {
            DeltaTranslationX = deltaTranslation.X,
            DeltaTranslationY = deltaTranslation.Y,
            DeltaRotation = deltaRotation,
            DeltaExpansionX = deltaExpansion.X,
            FrameScaleX = frameScale.X
        };
    }

    private sealed class FrameSnapshot
    {
        public double DeltaTranslationX;
        public double DeltaTranslationY;
        public double DeltaRotation;
        public double DeltaExpansionX;
        public double FrameScaleX;
    }
}

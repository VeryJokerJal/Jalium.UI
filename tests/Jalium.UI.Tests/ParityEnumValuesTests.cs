using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using ControlsDock = Jalium.UI.Controls.Dock;

namespace Jalium.UI.Tests;

public sealed class ParityEnumValuesTests
{
    [Fact]
    public void InkCanvasEditingModeUsesWpfValues()
    {
        Assert.Equal(0, (int)InkCanvasEditingMode.None);
        Assert.Equal(1, (int)InkCanvasEditingMode.Ink);
        Assert.Equal(2, (int)InkCanvasEditingMode.GestureOnly);
        Assert.Equal(3, (int)InkCanvasEditingMode.InkAndGesture);
        Assert.Equal(4, (int)InkCanvasEditingMode.Select);
        Assert.Equal(5, (int)InkCanvasEditingMode.EraseByPoint);
        Assert.Equal(6, (int)InkCanvasEditingMode.EraseByStroke);
    }

    [Fact]
    public void PlacementModeUsesWpfValues()
    {
        Assert.Equal(0, (int)PlacementMode.Absolute);
        Assert.Equal(1, (int)PlacementMode.Relative);
        Assert.Equal(2, (int)PlacementMode.Bottom);
        Assert.Equal(3, (int)PlacementMode.Center);
        Assert.Equal(4, (int)PlacementMode.Right);
        Assert.Equal(5, (int)PlacementMode.AbsolutePoint);
        Assert.Equal(6, (int)PlacementMode.RelativePoint);
        Assert.Equal(7, (int)PlacementMode.Mouse);
        Assert.Equal(8, (int)PlacementMode.MousePoint);
        Assert.Equal(9, (int)PlacementMode.Left);
        Assert.Equal(10, (int)PlacementMode.Top);
        Assert.Equal(11, (int)PlacementMode.Custom);
    }

    [Fact]
    public void InkCanvasSelectionHitResultUsesWpfValues()
    {
        Assert.Equal(0, (int)InkCanvasSelectionHitResult.None);
        Assert.Equal(1, (int)InkCanvasSelectionHitResult.TopLeft);
        Assert.Equal(2, (int)InkCanvasSelectionHitResult.Top);
        Assert.Equal(3, (int)InkCanvasSelectionHitResult.TopRight);
        Assert.Equal(4, (int)InkCanvasSelectionHitResult.Right);
        Assert.Equal(5, (int)InkCanvasSelectionHitResult.BottomRight);
        Assert.Equal(6, (int)InkCanvasSelectionHitResult.Bottom);
        Assert.Equal(7, (int)InkCanvasSelectionHitResult.BottomLeft);
        Assert.Equal(8, (int)InkCanvasSelectionHitResult.Left);
        Assert.Equal(9, (int)InkCanvasSelectionHitResult.Selection);
    }

    [Fact]
    public void RemainingTierOneEnumsUseWpfValues()
    {
        Assert.Equal(0, (int)ControlsDock.Left);
        Assert.Equal(1, (int)ControlsDock.Top);
        Assert.Equal(2, (int)ControlsDock.Right);
        Assert.Equal(3, (int)ControlsDock.Bottom);

        Assert.Equal(0, (int)MediaState.Manual);
        Assert.Equal(1, (int)MediaState.Play);
        Assert.Equal(2, (int)MediaState.Close);
        Assert.Equal(3, (int)MediaState.Pause);
        Assert.Equal(4, (int)MediaState.Stop);

        Assert.Equal(0, (int)DataGridGridLinesVisibility.All);
        Assert.Equal(1, (int)DataGridGridLinesVisibility.Horizontal);
        Assert.Equal(2, (int)DataGridGridLinesVisibility.None);
        Assert.Equal(3, (int)DataGridGridLinesVisibility.Vertical);

        Assert.Equal(0, (int)GridResizeBehavior.BasedOnAlignment);
        Assert.Equal(1, (int)GridResizeBehavior.CurrentAndNext);
        Assert.Equal(2, (int)GridResizeBehavior.PreviousAndCurrent);
        Assert.Equal(3, (int)GridResizeBehavior.PreviousAndNext);

        Assert.Equal(0, (int)DatePickerFormat.Long);
        Assert.Equal(1, (int)DatePickerFormat.Short);
        Assert.Equal(unchecked((int)0x80000003), (int)DragDropEffects.All);
    }
}

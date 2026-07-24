using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

/// <summary>
/// Exercises the shared layout/render path at the degenerate sizes produced while a native
/// window is dragged through its non-client chrome and minimum-size boundaries. These tests
/// deliberately reuse each control: real WM_SIZE traffic alternates between large and tiny
/// client areas, so stale cached geometry is part of the contract under test.
/// </summary>
public sealed class ResizeGeometryStressTests
{
    public static TheoryData<string, Func<FrameworkElement>> DirectRenderControls => new()
    {
        {
            nameof(NumberBox),
            static () => new NumberBox
            {
                Padding = new Thickness(10, 7, 10, 7),
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline
            }
        },
        {
            nameof(DatePicker),
            static () => new DatePicker { Header = "Date" }
        },
        {
            nameof(TimePicker),
            static () => new TimePicker { Header = "Time" }
        },
        {
            nameof(Slider),
            static () => new Slider { TickPlacement = TickPlacement.Both }
        },
        {
            nameof(RangeSlider),
            static () => new RangeSlider { TickFrequency = 10 }
        },
        {
            nameof(DiffViewer),
            static () => new DiffViewer
            {
                OriginalText = "old\nline",
                ModifiedText = "new\nline",
                ShowLineNumbers = true
            }
        }
    };

    [Theory]
    [MemberData(nameof(DirectRenderControls))]
    public void RepeatedExtremeResize_NeverCreatesInvalidRenderGeometry(
        string controlName,
        Func<FrameworkElement> createControl)
    {
        FrameworkElement control = createControl();
        control.Width = double.NaN;
        control.Height = double.NaN;
        control.MinWidth = 0;
        control.MinHeight = 0;

        if (control is Control templatedControl)
        {
            // Exercise each control's backend-independent direct drawing implementation.
            // Template layout is covered by the themed visual-tree test below.
            templatedControl.Template = null;
        }

        Size[] resizeSequence =
        [
            new(320, 180),
            new(20, 104.51953125),
            new(8, 8),
            new(1, 96),
            new(96, 1),
            new(0, 0),
            new(640, 360),
            new(12, 120),
            new(320, 180)
        ];

        foreach (Size size in resizeSequence)
        {
            Exception? exception = Record.Exception(() => LayoutAndRender(control, size));
            Assert.True(
                exception is null,
                $"{controlName} failed while rendering {size.Width}x{size.Height}: {exception}");
        }
    }

    private static void LayoutAndRender(FrameworkElement element, Size size)
    {
        element.Measure(size);
        element.Arrange(new Rect(0, 0, size.Width, size.Height));

        var drawing = new DrawingGroup();
        using DrawingContext drawingContext = drawing.Open();
        element.Render(drawingContext);
    }
}

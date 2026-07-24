using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class ResizeGeometryControlMatrixTests
{
    [Fact]
    public void ConstructibleControls_ExtremeResizeMatrix_CompletesLayoutAndRendering()
    {
        Type frameworkElementType = typeof(FrameworkElement);
        Type[] controlTypes = typeof(Control).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && !type.ContainsGenericParameters)
            .Where(type => frameworkElementType.IsAssignableFrom(type))
            .Where(type => type.GetConstructor(Type.EmptyTypes) is not null)
            .Where(type => type != typeof(Window))
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        Size[] resizeSequence =
        [
            new(320, 180),
            new(20, 104.51953125),
            new(8, 8),
            new(1, 96),
            new(96, 1),
            new(0, 0),
            new(320, 180)
        ];

        var failures = new List<string>();
        foreach (Type controlType in controlTypes)
        {
            FrameworkElement control;
            try
            {
                control = (FrameworkElement)Activator.CreateInstance(controlType)!;
                control.Width = double.NaN;
                control.Height = double.NaN;
                control.MinWidth = 0;
                control.MinHeight = 0;

                if (control is Control templatedControl)
                {
                    templatedControl.Template = null;
                }
            }
            catch
            {
                // A public default constructor can still require platform services. Those
                // controls are integration-tested by their backend-specific suites.
                continue;
            }

            foreach (Size size in resizeSequence)
            {
                try
                {
                    control.Measure(size);
                    control.Arrange(new Rect(0, 0, size.Width, size.Height));

                    var drawing = new DrawingGroup();
                    using DrawingContext drawingContext = drawing.Open();
                    control.Render(drawingContext);
                }
                catch (Exception exception)
                {
                    failures.Add(
                        $"{controlType.FullName} at {size.Width}x{size.Height}: {exception}");
                    break;
                }
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine + Environment.NewLine, failures));
    }
}

using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;
using Jalium.UI.Markup;
using Jalium.UI.Controls.Shapes;
using ShapePath = Jalium.UI.Controls.Shapes.Path;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class DynamicResourceLayeredTests
{
    [Fact]
    public void StyleSetterDynamicResource_ShouldRefreshInStyleLayer_AndClearWhenMissing()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var brush1 = new SolidColorBrush(Color.FromRgb(0x10, 0x20, 0x30));
            var brush2 = new SolidColorBrush(Color.FromRgb(0x30, 0x50, 0x70));
            app.Resources["ProbeBrush"] = brush1;

            var border = new Border();
            border.Style = new Style(typeof(Border))
            {
                Setters =
                {
                    new Setter(Border.BackgroundProperty, new DynamicResourceReference("ProbeBrush"))
                }
            };

            Assert.Same(brush1, border.Background);
            Assert.Equal(BaseValueSource.Style, DependencyPropertyHelper.GetValueSource(border, Border.BackgroundProperty).BaseValueSource);

            app.Resources["ProbeBrush"] = brush2;
            DynamicResourceBindingOperations.RefreshAll();
            Assert.Same(brush2, border.Background);
            Assert.Equal(BaseValueSource.Style, DependencyPropertyHelper.GetValueSource(border, Border.BackgroundProperty).BaseValueSource);

            app.Resources.Remove("ProbeBrush");
            DynamicResourceBindingOperations.RefreshAll();
            Assert.Null(border.Background);
            Assert.Equal(BaseValueSource.Default, DependencyPropertyHelper.GetValueSource(border, Border.BackgroundProperty).BaseValueSource);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void InlineDynamicResource_OnDetachedElement_ResolvesWhenAttachedToAncestorWithResource()
    {
        // Reproduces the IDE toolbar "empty icon" bug: an inline content element such as
        // <Path Stroke="{DynamicResource Foo}"> has its dynamic resource wired up by the
        // XAML source generator while the element is still detached. If the key is not
        // reachable at that instant the value resolves to null, and — because Shape.Stroke
        // defaults to null — the shape draws nothing. The fix re-resolves dynamic resources
        // when the element gains a visual parent, the same way implicit styles are re-run.
        ResetApplicationState();
        try
        {
            var brush = new SolidColorBrush(Color.FromRgb(0x4B, 0x55, 0x63));

            // Wire the dynamic resource up while detached (no Application, no parent) — the
            // key is unreachable, so it resolves to null exactly like at XAML build time.
            var path = new ShapePath { Data = "M3,5 L13,5", StrokeThickness = 1.4, Width = 14, Height = 14 };
            DynamicResourceBindingOperations.SetDynamicResource(path, Shape.StrokeProperty, "ProbeBrush");
            Assert.Null(path.Stroke); // precondition: unresolved while detached

            // The key only becomes reachable through an ancestor once attached to the tree.
            var host = new Grid();
            host.Resources["ProbeBrush"] = brush;
            host.Children.Add(path);

            // After the fix, attaching re-resolves the inline dynamic resource.
            Assert.Same(brush, path.Stroke);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    private static void ResetApplicationState()
    {
        var currentField = typeof(Application).GetField("_current", BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);

        var resetMethod = typeof(ThemeManager).GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static);
        resetMethod?.Invoke(null, null);
    }
}

using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;
using Jalium.UI.Markup;

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

    private static void ResetApplicationState()
    {
        var currentField = typeof(Application).GetField("_current", BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);

        var resetMethod = typeof(ThemeManager).GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static);
        resetMethod?.Invoke(null, null);
    }
}

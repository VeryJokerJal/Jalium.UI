using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class ThemeTemplateSmokeTests
{
    [Fact]
    public void CommonControlStyles_ShouldBePresent_AndTemplatesShouldApply()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var controls = new Control[]
            {
                new Button(),
                new TextBox(),
                new ScrollViewer(),
                new ListBox(),
                new ListView(),
                new ComboBox(),
                new ScrollBar(),
                new PasswordBox()
            };

            var host = new StackPanel { Width = 1000, Height = 800 };
            foreach (var control in controls)
            {
                Assert.True(app.Resources.TryGetValue(control.GetType(), out var styleObj), $"Missing style for {control.GetType().Name}");
                Assert.IsType<Style>(styleObj);
                host.Children.Add(control);
            }

            host.Measure(new Size(1000, 800));
            host.Arrange(new Rect(0, 0, 1000, 800));

            Assert.All(controls, control => Assert.True(control.VisualChildrenCount > 0 || control.Template == null));
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

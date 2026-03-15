using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class ProgressBarThemeTests
{
    private static void ResetApplicationState()
    {
        var currentField = typeof(Application).GetField("_current",
            BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);

        var resetMethod = typeof(ThemeManager).GetMethod("Reset",
            BindingFlags.NonPublic | BindingFlags.Static);
        resetMethod?.Invoke(null, null);
    }

    [Fact]
    public void ProgressBar_ImplicitThemeStyle_ShouldApplyWithoutLocalHeightOverride()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var progressBar = new ProgressBar();
            var host = new StackPanel { Width = 320, Height = 80 };
            host.Children.Add(progressBar);

            host.Measure(new Size(320, 80));
            host.Arrange(new Rect(0, 0, 320, 80));

            Assert.True(app.Resources.TryGetValue(typeof(ProgressBar), out var styleObj));
            Assert.IsType<Style>(styleObj);

            Assert.False(progressBar.HasLocalValue(FrameworkElement.HeightProperty));
            Assert.False(progressBar.HasLocalValue(Control.BackgroundProperty));
            Assert.False(progressBar.HasLocalValue(ProgressBar.ProgressBrushProperty));
            Assert.Equal(8, progressBar.Height);
            Assert.NotNull(progressBar.Background);
            Assert.NotNull(progressBar.ProgressBrush);
            Assert.True(progressBar.RenderSize.Height >= 8);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void ProgressBar_InternalResolvers_ShouldUseThemeResources()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var sliderTrack = Assert.IsAssignableFrom<Brush>(app.Resources["SliderTrack"]);
            var accentBrush = Assert.IsAssignableFrom<Brush>(app.Resources["AccentBrush"]);
            var disabledAccentBrush = Assert.IsAssignableFrom<Brush>(app.Resources["AccentBrushDisabled"]);
            var progressBar = new ProgressBar();

            Assert.Same(sliderTrack, InvokePrivateBrushResolver(progressBar, "ResolveTrackBrush"));
            Assert.Same(accentBrush, InvokePrivateBrushResolver(progressBar, "ResolveProgressBrush"));

            progressBar.IsEnabled = false;
            Assert.Same(disabledAccentBrush, InvokePrivateBrushResolver(progressBar, "ResolveProgressBrush"));

            var cornerRadius = InvokePrivateCornerRadiusResolver(progressBar, "ResolveCornerRadius");
            Assert.Equal(4, cornerRadius.TopLeft);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    private static Brush InvokePrivateBrushResolver(ProgressBar progressBar, string methodName)
    {
        var method = typeof(ProgressBar).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<Brush>(method!.Invoke(progressBar, null));
    }

    private static CornerRadius InvokePrivateCornerRadiusResolver(ProgressBar progressBar, string methodName)
    {
        var method = typeof(ProgressBar).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsType<CornerRadius>(method!.Invoke(progressBar, null));
    }
}

using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Data;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class SplashScreenTemplateTests
{
    private static readonly Lazy<ResourceDictionary> Defaults = new(() =>
    {
        ThemeLoader.Initialize();
        using var stream = new MemoryStream();
        return ThemeManager.XamlLoader!(stream, "Themes/Controls/SplashScreen.jalxaml", typeof(Control).Assembly)!;
    });

    [Fact]
    public void DefaultImageAndFrameUseSourceAndScalingProperties()
    {
        var source = Source();
        var presenter = Create(source);
        var imageStyle = new Style(typeof(Image));
        imageStyle.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center));
        imageStyle.Setters.Add(new Setter(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center));
        presenter.Resources[typeof(Image)] = imageStyle;
        Layout(presenter);
        Assert.IsType<Border>(presenter.GetVisualChild(0));
        var image = Assert.IsType<Image>(presenter.FindName("PART_Image"));
        Assert.Same(source, image.Source);
        Assert.Equal(Stretch.None, image.Stretch);
        Assert.True(presenter.HasImage);
        Assert.False(presenter.Focusable); Assert.False(presenter.IsTabStop);
        presenter.Stretch = Stretch.UniformToFill;
        presenter.StretchDirection = StretchDirection.DownOnly;
        presenter.Padding = new Thickness(4); presenter.BorderThickness = new Thickness(2);
        Layout(presenter);
        Assert.Equal(Stretch.UniformToFill, image.Stretch);
        Assert.Equal(StretchDirection.DownOnly, image.StretchDirection);
        Assert.Equal(new Point(6, 6), image.TransformToAncestor(presenter));
        Assert.Equal(new Size(108, 48), image.RenderSize);
    }

    [Fact]
    public void BoundSourceSurvivesMissingPartsAndRetiredImageStopsUpdating()
    {
        var source = new Image { Source = Source() };
        var presenter = Create(null);
        BindingOperations.SetBinding(presenter, SplashScreenPresenter.SourceProperty,
            new Binding(nameof(Image.Source)) { Source = source });
        Layout(presenter);
        var retired = Assert.IsType<Image>(presenter.FindName("PART_Image"));
        var custom = new ControlTemplate(typeof(SplashScreenPresenter));
        custom.SetVisualTree(() => new Border { Height = 20 });
        presenter.Template = custom; Layout(presenter);
        var previous = retired.Source;
        source.Source = Source();
        Assert.Same(previous, retired.Source);
        presenter.ClearValue(Control.TemplateProperty); Layout(presenter);
        var current = Assert.IsType<Image>(presenter.FindName("PART_Image"));
        Assert.NotSame(retired, current); Assert.Same(source.Source, current.Source);
        Assert.NotNull(BindingOperations.GetBindingExpression(presenter, SplashScreenPresenter.SourceProperty));
    }

    [Fact]
    public void AvailabilityStatesWorkWithAReplacementTemplateWithoutImageParts()
    {
        var presenter = Create(null);
        presenter.Template = (ControlTemplate)XamlReader.Parse("""
            <ControlTemplate xmlns="http://schemas.jalium.ui/2024" TargetType="SplashScreenPresenter">
                <Border Name="StateRoot">
                    <VisualStateManager.VisualStateGroups>
                        <VisualStateGroup Name="ImageStates">
                            <VisualState Name="Empty"><VisualState.Setters><Setter TargetName="StateRoot" Property="Opacity" Value="0.25" /></VisualState.Setters></VisualState>
                            <VisualState Name="ImageAvailable"><VisualState.Setters><Setter TargetName="StateRoot" Property="Opacity" Value="1" /></VisualState.Setters></VisualState>
                        </VisualStateGroup>
                    </VisualStateManager.VisualStateGroups>
                </Border>
            </ControlTemplate>
            """);
        Layout(presenter);
        var root = Assert.IsType<Border>(presenter.FindName("StateRoot"));
        Assert.Equal(.25, root.Opacity);
        presenter.Source = Source(); Assert.Equal(1, root.Opacity);
        presenter.Source = null; Assert.Equal(.25, root.Opacity);
        Assert.False(presenter.HasImage);
        Assert.Throws<InvalidOperationException>(() => presenter.SetValue(SplashScreenPresenter.HasImageProperty, true));
    }

    private static SplashScreenPresenter Create(ImageSource? source)
    {
        var presenter = new SplashScreenPresenter { Source = source };
        presenter.Resources.MergedDictionaries.Add(Defaults.Value); return presenter;
    }
    private static BitmapSource Source() => BitmapSource.Create(4, 2, 96, 96, PixelFormat.Bgra32, null, new byte[32], 16);
    private static void Layout(FrameworkElement element)
    {
        element.InvalidateMeasure(); element.Measure(new Size(120, 60));
        element.Arrange(new Rect(0, 0, 120, 60)); element.UpdateLayout();
    }
}

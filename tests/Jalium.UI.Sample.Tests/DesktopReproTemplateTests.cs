using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.DesktopDemo;
using Jalium.UI.Media;

namespace Jalium.UI.Sample.Tests;

public sealed class DesktopReproTemplateTests
{
    private static readonly Lazy<ResourceDictionary> Defaults = new(() =>
    {
        Jalium.UI.Markup.ThemeLoader.Initialize(); using var stream = new MemoryStream();
        return ThemeManager.XamlLoader!(stream, "Themes/Generic.jalxaml", typeof(Control).Assembly)!;
    });
    [Fact]
    public void HoverRowsAndOptionalScrollerRemainReplaceable()
    {
        var surface = new HoverProbeSurface(); Layout(surface);
        Assert.Equal(40, surface.Items.Count); Assert.IsType<ScrollViewer>(surface.FindName("PART_ScrollViewer"));
        surface.RowCount = 12; Assert.Equal(12, surface.Items.Count);
        surface.Stop(); Assert.Equal("SETTLE", surface.Phase);
        var blank = new ControlTemplate(typeof(HoverProbeSurface)); blank.SetVisualTree(() => new Border()); surface.Template = blank; Layout(surface);
        surface.Restart(); Assert.Equal("RUNNING", surface.Phase);
        surface.ClearValue(Control.TemplateProperty); Layout(surface); Assert.IsType<ScrollViewer>(surface.FindName("PART_ScrollViewer"));
        Assert.Equal(12, surface.Items.Count); surface.Stop();
    }
    [Fact]
    public void HoverPhaseUpdatesTheNativeHostTitleBinding()
    {
        var window = HoverProbeWindow.Build();
        var surface = Assert.IsType<HoverProbeSurface>(window.Content);
        Assert.Equal("HoverProbe RUNNING", window.Title);
        surface.Stop(); Assert.Equal("HoverProbe SETTLE", window.Title);
        window.Content = null;
    }
    [Fact]
    public void RotationSpecimensBindTheActualNonVisualTransformAndKeepBitmapIdentity()
    {
        var specimen = new RotationBorderSpecimen { Angle = 180 }; Layout(specimen);
        var border = Assert.IsType<Border>(Assert.IsType<Grid>(specimen.GetVisualChild(0)).Children[0]); Assert.Equal(180, Assert.IsType<RotateTransform>(border.RenderTransform).Angle);
        specimen.Angle = 90; Assert.Equal(90, Assert.IsType<RotateTransform>(border.RenderTransform).Angle);
        var surface = new RotationReproSurface(); Layout(surface); var bitmap = surface.QuadrantBitmap;
        Assert.Equal(4, surface.ImageCases.Count); Assert.Equal(4, surface.BorderCases.Count); Assert.Equal(2, surface.ContainerCases.Count);
        var blank = new ControlTemplate(typeof(RotationReproSurface)); blank.SetVisualTree(() => new Border()); surface.Template = blank; Layout(surface);
        surface.ClearValue(Control.TemplateProperty); Layout(surface); Assert.Same(bitmap, surface.QuadrantBitmap);
    }
    [Fact]
    public void EffectTimelineRetainsTheOriginalTransformsAndLayoutDamageAcrossTemplates()
    {
        var surface = new EffectReproSurface { IsPlaying = false }; Layout(surface);
        var moving = Assert.IsType<Border>(surface.FindName("PART_MovingBlock"));
        surface.ElapsedSeconds = 1.2;
        Assert.Equal(1, Assert.IsType<ScaleTransform>(surface.GlowTransform.Children[0]).ScaleX, 6);
        Assert.Equal(0, Assert.IsType<TranslateTransform>(surface.GlowTransform.Children[1]).Y, 6);
        var previousLeft = Canvas.GetLeft(moving);
        var blank = new ControlTemplate(typeof(EffectReproSurface)); blank.SetVisualTree(() => new Border()); surface.Template = blank; Layout(surface);
        surface.ElapsedSeconds = 2.4; Assert.Equal(previousLeft, Canvas.GetLeft(moving));
        surface.ClearValue(Control.TemplateProperty); Layout(surface);
        Assert.Equal(0.88, Assert.IsType<ScaleTransform>(surface.GlowTransform.Children[0]).ScaleX, 6);
        Assert.Equal(46, Assert.IsType<TranslateTransform>(surface.ShadowTransform.Children[1]).Y, 6);
        Assert.NotEqual(previousLeft, Canvas.GetLeft(Assert.IsType<Border>(surface.FindName("PART_MovingBlock"))));
        Assert.Equal("Paused", VisualStateManager.GetCurrentStateName(surface, "PlaybackStates"));
    }
    [Fact]
    public void PathWorkloadKeepsSixtyGlyphsAndTheSameSharedScaleTransform()
    {
        var surface = new PathPerfSurface { IsPlaying = false }; Layout(surface);
        Assert.Equal(60, surface.Items.Count);
        var glyphs = Descendants<Jalium.UI.Shapes.Path>(surface).ToArray(); Assert.Equal(60, glyphs.Length);
        Assert.All(glyphs, path => Assert.NotEmpty(Assert.IsType<PathGeometry>(path.Data).Figures));
        surface.ElapsedSeconds = 1.5; Assert.Equal(4, surface.CurrentScale, 6); Assert.Equal(4, surface.ScaleTransform.ScaleY, 6);
        var transform = surface.ScaleTransform;
        surface.ElapsedSeconds = 4.5; Assert.Equal(0.5, surface.CurrentScale, 6);
        var blank = new ControlTemplate(typeof(PathPerfSurface)); blank.SetVisualTree(() => new Border()); surface.Template = blank; Layout(surface);
        surface.ClearValue(Control.TemplateProperty); Layout(surface);
        Assert.Same(transform, surface.ScaleTransform); Assert.Equal(60, surface.Items.Count);
        Assert.Equal("Paused", VisualStateManager.GetCurrentStateName(surface, "PlaybackStates"));
    }
    [Fact]
    public void VectorMatrixPreservesFillRulesStrokeVariantsAndGradientModes()
    {
        var surface = new VelloTestSurface(); Layout(surface);
        Assert.Equal(FillRule.EvenOdd, Assert.IsType<PathGeometry>(Assert.IsType<Jalium.UI.Shapes.Path>(surface.FindName("PART_EvenOdd")).Data).FillRule);
        Assert.Equal(FillRule.Nonzero, Assert.IsType<PathGeometry>(Assert.IsType<Jalium.UI.Shapes.Path>(surface.FindName("PART_Nonzero")).Data).FillRule);
        var gearTransform = Assert.IsType<TransformGroup>(Assert.IsType<Jalium.UI.Shapes.Path>(surface.FindName("PART_GearRotated")).RenderTransform);
        Assert.Equal(2, gearTransform.Children.Count); Assert.Equal(4, Assert.IsType<ScaleTransform>(gearTransform.Children[0]).ScaleX); Assert.Equal(30, Assert.IsType<RotateTransform>(gearTransform.Children[1]).Angle);
        Assert.Equal(PenLineJoin.Round, Assert.IsType<Jalium.UI.Shapes.Path>(surface.FindName("PART_Round")).StrokeLineJoin);
        Assert.Equal(GradientSpreadMethod.Repeat, Assert.IsType<LinearGradientBrush>(Assert.IsType<Jalium.UI.Shapes.Path>(surface.FindName("PART_LinearRepeat")).Fill).SpreadMethod);
        Assert.Equal(GradientSpreadMethod.Reflect, Assert.IsType<RadialGradientBrush>(Assert.IsType<Jalium.UI.Shapes.Path>(surface.FindName("PART_RadialReflect")).Fill).SpreadMethod);
    }
    [Fact]
    public void ImageCategoriesKeepSharedSourcesAndDisconnectRetiredButtons()
    {
        var surface = new ImagePerfSurface { IsMonitoring = false }; Layout(surface);
        var sources = surface.SourceImages!; Assert.Equal(5, sources.Count); Assert.Equal(31, surface.Items.Count);
        var b = Assert.IsType<Button>(surface.FindName("PART_CategoryB")); b.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, b));
        Assert.Equal(1, surface.SelectedCategory); Assert.Same(sources[1], surface.Items[0]);
        var c = Assert.IsType<Button>(surface.FindName("PART_CategoryC"));
        var blank = new ControlTemplate(typeof(ImagePerfSurface)); blank.SetVisualTree(() => new Border()); surface.Template = blank; Layout(surface);
        c.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, c)); Assert.Equal(1, surface.SelectedCategory);
        surface.ClearValue(Control.TemplateProperty); Layout(surface); Assert.Same(sources, surface.SourceImages); Assert.Same(sources[1], surface.Items[0]);
        surface.SourceImages = null; Layout(surface); Assert.Empty(surface.Items);
        Assert.Equal("Empty", VisualStateManager.GetCurrentStateName(surface, "DataStates"));
    }
    [Fact]
    public void ScrollWorkloadRetainsSharedSourcesAndConfigurableCardDimensions()
    {
        var source = Jalium.UI.Media.Imaging.BitmapImage.FromPixels(new byte[16], 2, 2);
        var surface = new ScrollStressSurface { SourceImages = new[] { source }, IsMonitoring = false }; Layout(surface);
        Assert.Equal(240, surface.Items.Count);
        var card = Assert.IsType<ScrollStressCard>(surface.Items[239]); Assert.Same(source, card.Source); Assert.Equal("卡片 #239", card.Title);
        Assert.Equal(492, card.Width); Assert.Equal(212, card.Height);
        surface.CardCount = 5; surface.CardWidth = 320; Layout(surface);
        Assert.Equal(5, surface.Items.Count); Assert.Equal(312, Assert.IsType<ScrollStressCard>(surface.Items[0]).Width);
        var blank = new ControlTemplate(typeof(ScrollStressSurface)); blank.SetVisualTree(() => new Border()); surface.Template = blank; Layout(surface);
        surface.ClearValue(Control.TemplateProperty); Layout(surface); Assert.Same(source, Assert.IsType<ScrollStressCard>(surface.Items[0]).Source);
        surface.SourceImages = null; Layout(surface); Assert.Empty(surface.Items); Assert.Equal("Empty", VisualStateManager.GetCurrentStateName(surface, "DataStates"));
    }
    [Fact]
    public void NotificationCommandsExposeOriginalPayloadsWithoutSendingDuringTheTest()
    {
        var surface = new NotificationDemoSurface(); Layout(surface);
        var requests = new List<NotificationDemoRequestEventArgs>();
        surface.ActionRequested += (_, e) => { e.Handled = true; requests.Add(e); };
        Assert.IsType<Button>(surface.FindName("PART_Actions")).PerformClick();
        Assert.Single(requests); var content = requests[0].Content!; Assert.Equal("download-1", content.Tag); Assert.Equal("downloads", content.Group);
        Assert.Equal(new[] { "open", "folder" }, content.Actions.Select(action => action.Id));
        Assert.IsType<Button>(surface.FindName("PART_Silent")).PerformClick(); Assert.True(requests[1].Content!.Silent);
        Assert.IsType<Button>(surface.FindName("PART_HighPriority")).PerformClick(); Assert.Equal(Jalium.UI.Notifications.NotificationPriority.High, requests[2].Content!.Priority);
        Assert.IsType<Button>(surface.FindName("PART_Image")).PerformClick(); Assert.Equal(32, Assert.IsType<Jalium.UI.Media.Imaging.BitmapImage>(requests[3].Content!.Icon).PixelWidth);
        surface.IsEnabled = false; surface.RequestAction(NotificationDemoAction.Simple); Assert.Equal(4, requests.Count);
        surface.IsEnabled = true;
        var executions = 0; surface.ActionCommand = new Jalium.UI.Input.RelayCommand(_ => executions++);
        Assert.IsType<Button>(surface.FindName("PART_Simple")).PerformClick(); Assert.Equal(1, executions);
        var blank = new ControlTemplate(typeof(NotificationDemoSurface)); blank.SetVisualTree(() => new Border()); surface.Template = blank; Layout(surface);
        surface.ClearValue(Control.TemplateProperty); Layout(surface); Assert.IsType<Button>(surface.FindName("PART_Simple")).PerformClick(); Assert.Equal(2, executions);
    }
    private static IEnumerable<T> Descendants<T>(Visual root) where T : Visual
    {
        for (var index = 0; index < root.VisualChildrenCount; index++)
        {
            var child = root.GetVisualChild(index); if (child is T match) yield return match;
            if (child is not null) foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }
    private static void Layout(FrameworkElement element)
    {
        if (!element.Resources.MergedDictionaries.Contains(Defaults.Value)) element.Resources.MergedDictionaries.Add(Defaults.Value);
        element.InvalidateMeasure(); element.Measure(new Size(720, 560)); element.Arrange(new Rect(0, 0, 720, 560)); element.UpdateLayout();
    }
}

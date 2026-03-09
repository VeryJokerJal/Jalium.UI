using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class PrimitiveThemeTests
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
    public void HyperlinkButton_ImplicitThemeStyle_ShouldApplyWithoutLocalVisualOverrides()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var hyperlinkButton = new HyperlinkButton();
            var host = new StackPanel { Width = 320, Height = 80 };
            host.Children.Add(hyperlinkButton);

            host.Measure(new Size(320, 80));
            host.Arrange(new Rect(0, 0, 320, 80));

            Assert.True(app.Resources.TryGetValue(typeof(HyperlinkButton), out var styleObj));
            Assert.IsType<Style>(styleObj);

            Assert.False(hyperlinkButton.HasLocalValue(Control.ForegroundProperty));
            Assert.False(hyperlinkButton.HasLocalValue(Control.BorderThicknessProperty));
            Assert.False(hyperlinkButton.HasLocalValue(Control.PaddingProperty));
            Assert.NotNull(hyperlinkButton.Foreground);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void HyperlinkButton_DirectRenderBrushResolution_ShouldUseThemeResource()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            Assert.True(app.Resources.TryGetValue("HyperlinkForegroundHover", out var hoverObj));
            var hyperlinkButton = new HyperlinkButton();

            var resolveMethod = typeof(HyperlinkButton).GetMethod("ResolveHyperlinkBrush",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(resolveMethod);

            var brush = resolveMethod!.Invoke(hyperlinkButton, new object[]
            {
                "HyperlinkForegroundHover",
                new SolidColorBrush(Color.Black)
            });

            Assert.Same(hoverObj, brush);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void Button_TemplateChrome_ShouldNotStartAutomaticTransition()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var button = new Button
            {
                Content = "Hover me"
            };
            var host = new StackPanel { Width = 320, Height = 80 };
            host.Children.Add(button);

            host.Measure(new Size(320, 80));
            host.Arrange(new Rect(0, 0, 320, 80));

            var backgroundBorder = Assert.IsType<Border>(button.FindName("BackgroundBorder"));
            var hoverOverlay = Assert.IsType<Border>(button.FindName("HoverOverlay"));
            var pressedOverlay = Assert.IsType<Border>(button.FindName("PressedOverlay"));
            var newBrush = new SolidColorBrush(Color.FromRgb(12, 34, 56));

            button.Background = newBrush;

            Assert.Equal("None", button.TransitionProperty);
            Assert.Equal("None", backgroundBorder.TransitionProperty);
            Assert.Equal("Opacity", hoverOverlay.TransitionProperty);
            Assert.Equal("Opacity", pressedOverlay.TransitionProperty);
            Assert.False(backgroundBorder.HasAutomaticTransition(Border.BackgroundProperty));
            Assert.Same(newBrush, backgroundBorder.Background);
            Assert.Same(app.Resources["ControlBackgroundHover"], hoverOverlay.Background);
            Assert.Same(app.Resources["ButtonBackgroundPressed"], pressedOverlay.Background);
            Assert.Equal(0.0, hoverOverlay.Opacity);
            Assert.Equal(0.0, pressedOverlay.Opacity);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void Slider_ImplicitThemeStyle_ShouldApplyWithoutLocalHeightOverride()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var slider = new Slider();
            var host = new StackPanel { Width = 320, Height = 80 };
            host.Children.Add(slider);

            host.Measure(new Size(320, 80));
            host.Arrange(new Rect(0, 0, 320, 80));

            Assert.True(app.Resources.TryGetValue(typeof(Slider), out var styleObj));
            Assert.IsType<Style>(styleObj);

            Assert.False(slider.HasLocalValue(FrameworkElement.HeightProperty));
            Assert.Equal(24, slider.Height);
            Assert.True(slider.RenderSize.Height >= 24);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void TickBar_ImplicitThemeStyle_ShouldApplyWithoutLocalFillOverride()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var tickBar = new TickBar();
            var host = new StackPanel { Width = 320, Height = 24 };
            host.Children.Add(tickBar);

            host.Measure(new Size(320, 24));
            host.Arrange(new Rect(0, 0, 320, 24));

            Assert.True(app.Resources.TryGetValue(typeof(TickBar), out var styleObj));
            Assert.IsType<Style>(styleObj);
            Assert.False(tickBar.HasLocalValue(TickBar.FillProperty));
            Assert.Same(app.Resources["TextSecondary"], tickBar.Fill);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void Panel_BackgroundProperty_ShouldRenderFill()
    {
        var brush = new SolidColorBrush(Color.FromRgb(12, 34, 56));
        var panel = new StackPanel
        {
            Width = 64,
            Height = 32,
            Background = brush
        };

        panel.Measure(new Size(64, 32));
        panel.Arrange(new Rect(0, 0, 64, 32));

        var drawingContext = new RecordingDrawingContext();
        panel.Render(drawingContext);

        Assert.Same(brush, drawingContext.LastBackgroundBrush);
        Assert.Equal(new Rect(0, 0, 64, 32), drawingContext.LastRectangle);
    }

    [Fact]
    public void InfrastructurePanels_ImplicitThemeStyles_ShouldApplyBackgroundWithoutLocalOverrides()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var virtualizingPanel = new VirtualizingStackPanel { Width = 320, Height = 32 };
            var toolBarPanel = new ToolBarPanel { Width = 320, Height = 32 };
            var toolBarOverflowPanel = new ToolBarOverflowPanel { Width = 320, Height = 48 };
            var uniformGrid = new UniformGrid { Width = 320, Height = 32 };
            var selectiveScrollingGrid = new SelectiveScrollingGrid { Width = 320, Height = 32 };
            var tabPanel = new TabPanel { Width = 320, Height = 32 };

            var host = new StackPanel { Width = 320, Height = 240 };
            host.Children.Add(virtualizingPanel);
            host.Children.Add(toolBarPanel);
            host.Children.Add(toolBarOverflowPanel);
            host.Children.Add(uniformGrid);
            host.Children.Add(selectiveScrollingGrid);
            host.Children.Add(tabPanel);

            host.Measure(new Size(320, 240));
            host.Arrange(new Rect(0, 0, 320, 240));

            Assert.False(virtualizingPanel.HasLocalValue(Panel.BackgroundProperty));
            Assert.False(toolBarPanel.HasLocalValue(Panel.BackgroundProperty));
            Assert.False(toolBarOverflowPanel.HasLocalValue(Panel.BackgroundProperty));
            Assert.False(uniformGrid.HasLocalValue(Panel.BackgroundProperty));
            Assert.False(selectiveScrollingGrid.HasLocalValue(Panel.BackgroundProperty));
            Assert.False(tabPanel.HasLocalValue(Panel.BackgroundProperty));

            Assert.Equal(0x00, Assert.IsType<SolidColorBrush>(virtualizingPanel.Background).Color.A);
            Assert.Equal(0x00, Assert.IsType<SolidColorBrush>(toolBarPanel.Background).Color.A);
            Assert.Same(app.Resources["SurfaceBackground"], toolBarOverflowPanel.Background);
            Assert.Equal(0x00, Assert.IsType<SolidColorBrush>(uniformGrid.Background).Color.A);
            Assert.Equal(0x00, Assert.IsType<SolidColorBrush>(selectiveScrollingGrid.Background).Color.A);
            Assert.Same(app.Resources["TabStripBackground"], tabPanel.Background);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void Label_TemplatedRender_ShouldNotDrawTextTwice()
    {
        ResetApplicationState();
        ThemeLoader.Initialize();
        var app = new Application();

        try
        {
            var label = new Label
            {
                Content = "Username:"
            };

            var host = new StackPanel { Width = 200, Height = 40 };
            host.Children.Add(label);

            host.Measure(new Size(200, 40));
            host.Arrange(new Rect(0, 0, 200, 40));

            var drawingContext = new RecordingDrawingContext();
            label.Render(drawingContext);

            Assert.NotNull(label.FindName("LabelBorder"));
            Assert.Equal(1, drawingContext.DrawTextCalls);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void Thumb_ImplicitThemeStyle_ShouldApplyWithoutLocalVisualOverrides()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var thumb = new Thumb();
            var host = new StackPanel { Width = 48, Height = 48 };
            host.Children.Add(thumb);

            host.Measure(new Size(48, 48));
            host.Arrange(new Rect(0, 0, 48, 48));

            Assert.True(app.Resources.TryGetValue(typeof(Thumb), out var styleObj));
            Assert.IsType<Style>(styleObj);
            Assert.False(thumb.HasLocalValue(Control.BackgroundProperty));
            Assert.False(thumb.HasLocalValue(Control.BorderBrushProperty));
            Assert.Same(app.Resources["ControlBackground"], thumb.Background);
            Assert.Same(app.Resources["ControlBorder"], thumb.BorderBrush);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void Thumb_AndTickBar_InternalResolvers_ShouldUseThemeResources()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var thumb = new Thumb();
            var tickBar = new TickBar();

            Assert.Same(app.Resources["ControlBackground"], InvokePrivateBrushResolver(thumb, "ResolveDefaultBackgroundBrush"));
            Assert.Same(app.Resources["ControlBackgroundPressed"], InvokePrivateBrushResolver(thumb, "ResolveDraggingBackgroundBrush"));
            Assert.Same(app.Resources["TextTertiary"], InvokePrivatePenResolver(thumb, "ResolveGripPen").Brush);
            Assert.Same(app.Resources["TextSecondary"], InvokePrivateBrushResolver(tickBar, "ResolveTickBrush"));
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void DocumentPageView_InternalResolvers_ShouldUseThemeResources()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var pageView = new DocumentPageView();

            Assert.Same(app.Resources["ControlBackground"], InvokePrivateBrushResolver(pageView, "ResolvePlaceholderBrush"));
            Assert.Same(app.Resources["WindowBackground"], InvokePrivateBrushResolver(pageView, "ResolvePageBrush"));
            Assert.Same(app.Resources["SmokeFillColorDefaultBrush"], InvokePrivateBrushResolver(pageView, "ResolveShadowBrush"));
            Assert.Same(app.Resources["ControlBorder"], InvokePrivatePenResolver(pageView, "ResolveBorderPen").Brush);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void DocumentPageView_ImplicitThemeStyle_ShouldApplyWithoutLocalVisualOverrides()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var pageView = new DocumentPageView
            {
                Width = 120,
                Height = 180
            };
            var host = new StackPanel { Width = 160, Height = 220 };
            host.Children.Add(pageView);

            host.Measure(new Size(160, 220));
            host.Arrange(new Rect(0, 0, 160, 220));

            Assert.True(app.Resources.TryGetValue(typeof(DocumentPageView), out var styleObj));
            Assert.IsType<Style>(styleObj);
            Assert.False(pageView.HasLocalValue(DocumentPageView.BackgroundProperty));
            Assert.False(pageView.HasLocalValue(DocumentPageView.BorderBrushProperty));
            Assert.False(pageView.HasLocalValue(DocumentPageView.BorderThicknessProperty));
            Assert.Same(app.Resources["WindowBackground"], pageView.Background);
            Assert.Same(app.Resources["ControlBorder"], pageView.BorderBrush);
            Assert.Equal(new Thickness(1), pageView.BorderThickness);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void AppBarSeparator_OnRender_ShouldHonorForegroundBrush()
    {
        var brush = new SolidColorBrush(Color.FromRgb(12, 34, 56));
        var separator = new AppBarSeparator
        {
            Foreground = brush,
            Width = 12,
            Height = 32
        };

        separator.Measure(new Size(12, 32));
        separator.Arrange(new Rect(0, 0, 12, 32));

        var drawingContext = new RecordingDrawingContext();
        separator.Render(drawingContext);

        Assert.Same(brush, drawingContext.LastPen?.Brush);
    }

    [Fact]
    public void AppBarSeparator_ThemeFallback_ShouldUseThemeResource()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var separator = new AppBarSeparator
            {
                Width = 12,
                Height = 32
            };

            var resolveMethod = typeof(AppBarSeparator).GetMethod("ResolveSeparatorBrush",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(resolveMethod);

            var brush = resolveMethod!.Invoke(separator, null);
            Assert.Same(app.Resources["AppBarSeparatorForeground"], brush);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void Separator_ImplicitThemeStyle_ShouldApplyWithoutLocalMarginOverride()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var separator = new Separator();
            var host = new StackPanel { Width = 320, Height = 80 };
            host.Children.Add(separator);

            host.Measure(new Size(320, 80));
            host.Arrange(new Rect(0, 0, 320, 80));

            Assert.False(separator.HasLocalValue(FrameworkElement.MarginProperty));
        }
        finally
        {
            ResetApplicationState();
        }
    }

    private sealed class RecordingDrawingContext : DrawingContext
    {
        public Pen? LastPen { get; private set; }
        public Brush? LastBackgroundBrush { get; private set; }
        public Rect LastRectangle { get; private set; }
        public int DrawTextCalls { get; private set; }

        public override void DrawLine(Pen pen, Point point0, Point point1)
        {
            LastPen = pen;
        }

        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle)
        {
            LastBackgroundBrush = brush;
            LastRectangle = rectangle;
        }

        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY)
        {
        }

        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY)
        {
        }

        public override void DrawText(FormattedText formattedText, Point origin)
        {
            DrawTextCalls++;
        }

        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry)
        {
        }

        public override void DrawImage(ImageSource imageSource, Rect rectangle)
        {
        }

        public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect, CornerRadius cornerRadius)
        {
        }

        public override void PushTransform(Transform transform)
        {
        }

        public override void PushClip(Geometry clipGeometry)
        {
        }

        public override void PushOpacity(double opacity)
        {
        }

        public override void Pop()
        {
        }

        public override void Close()
        {
        }
    }

    private static Brush InvokePrivateBrushResolver(object control, string methodName, params object[] args)
    {
        var method = control.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<Brush>(method!.Invoke(control, args));
    }

    private static Pen InvokePrivatePenResolver(object control, string methodName, params object[] args)
    {
        var method = control.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return Assert.IsType<Pen>(method!.Invoke(control, args));
    }
}

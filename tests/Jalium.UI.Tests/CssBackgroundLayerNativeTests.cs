using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;
using System.Reflection;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssBackgroundLayerNativeTests
{
    [RequiresWindowsBackendFact(RenderBackend.Software)]
    public void Software_TransparentImageCompositesOverColor() => Verify(RenderBackend.Software);

    [RequiresWindowsBackendFact(RenderBackend.D3D12)]
    public void D3D12_TransparentImageCompositesOverColor() => Verify(RenderBackend.D3D12);

    [RequiresWindowsBackendFact(RenderBackend.Vulkan)]
    public void Vulkan_TransparentImageCompositesOverColor() => Verify(RenderBackend.Vulkan);

    private static void Verify(RenderBackend backend)
    {
        const int width = 160, height = 100;
        using var window = new HiddenNativeWindow(width, height);
        using var context = new RenderContext(backend);
        using var target = context.CreateRenderTarget(window.Hwnd, width, height);
        using var drawing = new RenderTargetDrawingContext(target, context);

        byte[] Render(FrameworkElement element, string style, bool recorded = false,
            FrameworkElement? renderRoot = null, Action<FrameworkElement>? beforeDraw = null)
        {
            Css.SetStyle(element, style);
            var root = renderRoot ?? element;
            CssBorderRadiusTests.Layout(root, 80, 40);
            beforeDraw?.Invoke(element);
            target.SetFullInvalidation();
            var deadline = Environment.TickCount64 + 2000;
            while (!target.TryBeginDraw()) { Assert.True(Environment.TickCount64 < deadline); Thread.Sleep(1); }
            target.Clear(0, 0, 0, 1);
            if (recorded)
            {
                var recorder = new DrawingRecorder();
                recorder.BindWholeFrame();
                ((IOffsetDrawingContext)recorder).Offset = new Point(40, 30);
                root.Render(recorder);
                var commands = recorder.Commit();
                Assert.True(commands.FullyRecordable);
                DrawingReplayer.Replay(commands, drawing);
            }
            else
            {
                drawing.Offset = new Point(40, 30);
                try { root.Render(drawing); }
                finally { drawing.Offset = default; }
            }
            Assert.Equal(JaliumResult.Ok, target.RequestReadback());
            Assert.Equal(JaliumResult.Ok, target.TryEndDraw());
            var pixels = new byte[width * height * 4];
            Assert.Equal(JaliumResult.Ok, target.FetchReadback(pixels, width * 4u, out _, out _));
            return pixels;
        }

        static byte Channel(byte[] pixels, int x, int y, int channel)
            => pixels[(y * width + x) * 4 + channel];

        const string image =
            "linear-gradient(rgb(255 0 0 / .5),rgb(255 0 0 / .5))";
        foreach (FrameworkElement element in new FrameworkElement[] { new Grid(), new TextBlock() })
        foreach (var recorded in new[] { false, true })
        {
            var rounded = Render(element,
                "border-radius:50%;background-color:blue;background-image:" + image,
                recorded);
            Assert.True(Channel(rounded, 41, 31, 0) < 8 && Channel(rounded, 41, 31, 2) < 8,
                $"{backend} {element.GetType().Name} recorded={recorded}: CSS background escaped radius");
            Assert.InRange(Channel(rounded, 80, 50, 0), (byte)125, (byte)130);
            Assert.InRange(Channel(rounded, 80, 50, 2), (byte)125, (byte)130);
        }
        var clippedPanel = new Grid();
        clippedPanel.Children.Add(new Border { Background = Brushes.Red });
        foreach (var recorded in new[] { false, true })
        {
            var rounded = Render(clippedPanel, "border-radius:50%;overflow:hidden", recorded);
            Assert.True(Channel(rounded, 41, 31, 2) < 8,
                $"{backend} recorded={recorded}: panel child escaped radius clip");
            Assert.True(Channel(rounded, 80, 50, 2) > 245);
        }
        var border = new Border();
        var layered = Render(border,
            "border-radius:20px / 10px;background-color:blue;background-image:" + image);
        Assert.InRange(Channel(layered, 80, 50, 2), (byte)125, (byte)130);
        Assert.InRange(Channel(layered, 80, 50, 0), (byte)125, (byte)130);
        Assert.True(Channel(layered, 41, 31, 2) < 8 &&
                    Channel(layered, 41, 31, 0) < 8,
            $"{backend}: background layers escaped the elliptical border radius");

        var replayed = Render(border,
            "border-radius:20px / 10px;background-color:blue;background-image:" + image,
            recorded: true);
        Assert.InRange(Channel(replayed, 80, 50, 0), (byte)125, (byte)130);

        const string usedSize = "background-color:blue;background-image:" +
            "linear-gradient(to right, rgb(255 0 0 / .5) 0px, transparent 20px)";
        var directLength = Render(new Border(), usedSize);
        var recordedLength = Render(new Border(), usedSize, recorded: true);
        foreach (var sampleX in new[] { 45, 50, 55, 60 })
        {
            Assert.InRange(Math.Abs(Channel(directLength, sampleX, 50, 0) -
                                    Channel(recordedLength, sampleX, 50, 0)), 0, 2);
            Assert.InRange(Math.Abs(Channel(directLength, sampleX, 50, 2) -
                                    Channel(recordedLength, sampleX, 50, 2)), 0, 2);
        }

        var shorthand = Render(new Border(), "background:" + image + " blue");
        Assert.InRange(Channel(shorthand, 80, 50, 0), (byte)125, (byte)130);

        var positionedGradient = new Border
        {
            BorderThickness = new Thickness(2),
            Padding = new Thickness(3),
        };
        const string positionedGradientStyle =
            "background-color:blue;" +
            "background-image:linear-gradient(to right, red, lime);" +
            "background-size:20px 10px;background-position:right 2px bottom 2px;" +
            "background-repeat:no-repeat;background-origin:content-box;" +
            "background-clip:content-box";
        foreach (var recorded in new[] { false, true })
        {
            var gradientPixels = Render(positionedGradient, positionedGradientStyle, recorded);
            Assert.True(Channel(gradientPixels, 42, 55, 0) < 8);
            Assert.True(Channel(gradientPixels, 50, 55, 0) > 245);
            Assert.True(Channel(gradientPixels, 95, 55, 2) > 180);
            Assert.True(Channel(gradientPixels, 110, 55, 1) > 180);
        }

        const string directControlBackgroundStyle =
            "border:4px solid transparent;padding:3px;background-color:blue;" +
            "background-image:linear-gradient(to right,red,lime);" +
            "background-size:20px 10px;background-position:right bottom;" +
            "background-repeat:no-repeat;background-origin:content-box;" +
            "background-clip:content-box";
        foreach (var recorded in new[] { false, true })
        {
            var scrollBarPixels = Render(
                new ScrollBar { IsOverlayStyle = false }, directControlBackgroundStyle, recorded);
            Assert.True(Channel(scrollBarPixels, 45, 40, 0) < 8,
                $"{backend} recorded={recorded}: ScrollBar background escaped content box");
            Assert.True(Channel(scrollBarPixels, 50, 40, 0) > 245);
            Assert.True(Channel(scrollBarPixels, 100, 59, 2) > 100);
            Assert.True(Channel(scrollBarPixels, 110, 59, 1) > 100);

            var roundedBar = Render(new ScrollBar { IsOverlayStyle = false },
                "border-radius:50%;background-color:blue", recorded);
            Assert.True(Channel(roundedBar, 41, 31, 0) < 8,
                $"{backend} recorded={recorded}: ScrollBar background escaped radius");
            Assert.True(Channel(roundedBar, 80, 39, 0) > 245);

            var statusBarPixels = Render(new StatusBar(), directControlBackgroundStyle, recorded);
            Assert.True(Channel(statusBarPixels, 45, 50, 0) < 8,
                $"{backend} recorded={recorded}: StatusBar background escaped content box");
            Assert.True(Channel(statusBarPixels, 50, 50, 0) > 245);
            Assert.True(Channel(statusBarPixels, 100, 59, 2) > 100);
            Assert.True(Channel(statusBarPixels, 110, 59, 1) > 100);

            var roundedStatusBar = Render(new StatusBar(),
                "border-radius:50%;background-color:blue", recorded);
            Assert.True(Channel(roundedStatusBar, 41, 31, 0) < 8,
                $"{backend} recorded={recorded}: StatusBar background escaped radius");
            Assert.True(Channel(roundedStatusBar, 80, 50, 0) > 245);

            var passwordBoxPixels = Render(new PasswordBox(), directControlBackgroundStyle, recorded);
            Assert.True(Channel(passwordBoxPixels, 45, 50, 0) < 8,
                $"{backend} recorded={recorded}: PasswordBox background escaped content box");
            Assert.True(Channel(passwordBoxPixels, 50, 50, 0) > 245);
            Assert.True(Channel(passwordBoxPixels, 100, 59, 2) > 100);
            Assert.True(Channel(passwordBoxPixels, 110, 59, 1) > 100);

            var roundedPasswordBox = Render(new PasswordBox(),
                "border-radius:50%;background-color:blue", recorded);
            Assert.True(Channel(roundedPasswordBox, 41, 31, 0) < 8,
                $"{backend} recorded={recorded}: PasswordBox background escaped radius");
            Assert.True(Channel(roundedPasswordBox, 80, 50, 0) > 245);

            var statusItemPixels = Render(new StatusBarItem(), directControlBackgroundStyle, recorded);
            Assert.True(Channel(statusItemPixels, 45, 50, 0) < 8,
                $"{backend} recorded={recorded}: StatusBarItem background escaped content box");
            Assert.True(Channel(statusItemPixels, 50, 50, 0) > 245);
            Assert.True(Channel(statusItemPixels, 100, 59, 2) > 100);
            Assert.True(Channel(statusItemPixels, 110, 59, 1) > 100);

            var roundedStatusItem = Render(new StatusBarItem(),
                "border-radius:50%;background-color:blue", recorded);
            Assert.True(Channel(roundedStatusItem, 41, 31, 0) < 8,
                $"{backend} recorded={recorded}: StatusBarItem background escaped radius");
            Assert.True(Channel(roundedStatusItem, 80, 50, 0) > 245);

            var pagePixels = Render(new Page(), directControlBackgroundStyle, recorded);
            Assert.True(Channel(pagePixels, 45, 50, 0) < 8,
                $"{backend} recorded={recorded}: Page background escaped content box");
            Assert.True(Channel(pagePixels, 50, 50, 0) > 245);
            Assert.True(Channel(pagePixels, 100, 59, 2) > 100);
            Assert.True(Channel(pagePixels, 110, 59, 1) > 100);

            var roundedPage = Render(new Page(),
                "border-radius:50%;background-color:blue", recorded);
            Assert.True(Channel(roundedPage, 41, 31, 0) < 8,
                $"{backend} recorded={recorded}: Page background escaped radius");
            Assert.True(Channel(roundedPage, 80, 50, 0) > 245);

            NumberBox DirectNumberBox() => new()
            {
                Template = null,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden,
                Foreground = Brushes.Transparent,
            };
            var numberBoxPixels = Render(DirectNumberBox(), directControlBackgroundStyle, recorded);
            Assert.True(Channel(numberBoxPixels, 45, 50, 0) < 8,
                $"{backend} recorded={recorded}: NumberBox background escaped content box");
            Assert.True(Channel(numberBoxPixels, 50, 50, 0) > 245);
            Assert.True(Channel(numberBoxPixels, 100, 59, 2) > 100);
            Assert.True(Channel(numberBoxPixels, 110, 59, 1) > 100);

            var roundedNumberBox = Render(DirectNumberBox(),
                "border-radius:50%;background-color:blue", recorded);
            Assert.True(Channel(roundedNumberBox, 41, 31, 0) < 8,
                $"{backend} recorded={recorded}: NumberBox background escaped radius");
            Assert.True(Channel(roundedNumberBox, 80, 50, 0) > 245);

            DatePicker DirectDatePicker() => new()
            {
                Foreground = Brushes.Transparent,
                PlaceholderText = string.Empty,
            };
            var datePickerPixels = Render(DirectDatePicker(), directControlBackgroundStyle, recorded);
            Assert.True(Channel(datePickerPixels, 45, 50, 0) < 8,
                $"{backend} recorded={recorded}: DatePicker background escaped content box");
            Assert.True(Channel(datePickerPixels, 50, 50, 0) > 245);
            Assert.True(Channel(datePickerPixels, 100, 59, 2) > 100);
            Assert.True(Channel(datePickerPixels, 110, 59, 1) > 100);

            var roundedDatePicker = Render(DirectDatePicker(),
                "border-radius:50%;background-color:blue", recorded);
            Assert.True(Channel(roundedDatePicker, 41, 31, 0) < 8,
                $"{backend} recorded={recorded}: DatePicker background escaped radius");
            Assert.True(Channel(roundedDatePicker, 80, 50, 0) > 245);

            TimePicker DirectTimePicker() => new()
            {
                Foreground = Brushes.Transparent,
                PlaceholderText = string.Empty,
            };
            var timePickerPixels = Render(DirectTimePicker(), directControlBackgroundStyle, recorded);
            Assert.True(Channel(timePickerPixels, 45, 50, 0) < 8,
                $"{backend} recorded={recorded}: TimePicker background escaped content box");
            Assert.True(Channel(timePickerPixels, 50, 50, 0) > 245);
            Assert.True(Channel(timePickerPixels, 100, 59, 2) > 100);
            Assert.True(Channel(timePickerPixels, 110, 59, 1) > 100);

            var roundedTimePicker = Render(DirectTimePicker(),
                "border-radius:50%;background-color:blue", recorded);
            Assert.True(Channel(roundedTimePicker, 41, 31, 0) < 8,
                $"{backend} recorded={recorded}: TimePicker background escaped radius");
            Assert.True(Channel(roundedTimePicker, 80, 50, 0) > 245);

            ColorPicker DirectColorPicker() => new()
            {
                IsColorSpectrumVisible = false,
                IsColorSliderVisible = false,
                IsColorPreviewVisible = false,
            };
            var colorPickerPixels = Render(DirectColorPicker(), directControlBackgroundStyle, recorded);
            Assert.True(Channel(colorPickerPixels, 45, 50, 0) < 8,
                $"{backend} recorded={recorded}: ColorPicker background escaped content box");
            Assert.True(Channel(colorPickerPixels, 50, 50, 0) > 245);
            Assert.True(Channel(colorPickerPixels, 100, 59, 2) > 100);
            Assert.True(Channel(colorPickerPixels, 110, 59, 1) > 100);

            var roundedColorPicker = Render(DirectColorPicker(),
                "border-radius:50%;background-color:blue", recorded);
            Assert.True(Channel(roundedColorPicker, 41, 31, 0) < 8,
                $"{backend} recorded={recorded}: ColorPicker background escaped radius");
            Assert.True(Channel(roundedColorPicker, 80, 50, 0) > 245);

            ProgressBar DirectProgressBar() => new() { Template = null, Value = 0 };
            var progressBarPixels = Render(DirectProgressBar(), directControlBackgroundStyle, recorded);
            Assert.True(Channel(progressBarPixels, 45, 50, 0) < 8,
                $"{backend} recorded={recorded}: ProgressBar background escaped content box");
            Assert.True(Channel(progressBarPixels, 50, 50, 0) > 245);
            Assert.True(Channel(progressBarPixels, 100, 59, 2) > 100);
            Assert.True(Channel(progressBarPixels, 110, 59, 1) > 100);

            var roundedProgressBar = Render(DirectProgressBar(),
                "border-radius:50%;background-color:blue", recorded);
            Assert.True(Channel(roundedProgressBar, 41, 31, 0) < 8,
                $"{backend} recorded={recorded}: ProgressBar background escaped radius");
            Assert.True(Channel(roundedProgressBar, 80, 50, 0) > 245);

            foreach (Control control in new Control[]
                     {
                         new Slider { Template = null, Value = 0 },
                         new RangeSlider { Template = null, RangeStart = 0, RangeEnd = 0 },
                         new Thumb { Template = null, Width = 80, Height = 40, ShowGrip = false },
                     })
            {
                var controlPixels = Render(control, directControlBackgroundStyle, recorded);
                Assert.True(Channel(controlPixels, 45, 50, 0) < 8,
                    $"{backend} {control.GetType().Name} recorded={recorded}: background escaped content box");
                Assert.True(Channel(controlPixels, 90, 40, 0) > 245);
                Assert.True(Channel(controlPixels, 100, 59, 2) > 100);
                Assert.True(Channel(controlPixels, 110, 59, 1) > 100);

                var radiusPixels = Render(control,
                    "border-radius:50%;background-color:blue", recorded);
                Assert.True(Channel(radiusPixels, 41, 31, 0) < 8,
                    $"{backend} {control.GetType().Name} recorded={recorded}: background escaped radius");
                Assert.True(Channel(radiusPixels, 80, 40, 0) > 245);
            }

            foreach (Control menuControl in new Control[]
                     {
                         new CommandBar
                         {
                             Width = 80, Height = 40,
                             OverflowButtonVisibility = CommandBarOverflowButtonVisibility.Collapsed,
                         },
                         new MenuBar { Width = 80, Height = 40 },
                         new Menu { Width = 80, Height = 40 },
                         new MenuItem { Width = 80, Height = 40 },
                         new MenuBarItem { Width = 80, Height = 40 },
                         new MenuFlyoutItem { Width = 80, Height = 40 },
                         new MenuFlyoutSubItem { Width = 80, Height = 40 },
                         new ToggleMenuFlyoutItem { Width = 80, Height = 40 },
                     })
            {
                var menuPixels = Render(menuControl, directControlBackgroundStyle, recorded);
                Assert.True(Channel(menuPixels, 45, 50, 0) < 8,
                    $"{backend} {menuControl.GetType().Name} recorded={recorded}: background escaped content box");
                Assert.True(Channel(menuPixels, 50, 50, 0) > 245);
                Assert.True(Channel(menuPixels, 100, 59, 2) > 100);
                Assert.True(Channel(menuPixels, 110, 59, 1) > 100);

                var radiusPixels = Render(menuControl,
                    "border-radius:50%;background-color:blue", recorded);
                Assert.True(Channel(radiusPixels, 41, 31, 0) < 8,
                    $"{backend} {menuControl.GetType().Name} recorded={recorded}: background escaped radius");
                Assert.True(Channel(radiusPixels, 80, 50, 0) > 245);
            }

            foreach (Control separator in new Control[]
                     {
                         new MenuFlyoutSeparator { Width = 80, Height = 40 },
                         new Separator { Width = 80, Height = 40, Template = null },
                         new AppBarSeparator { Width = 80, Height = 40 },
                     })
            {
                var separatorPixels = Render(separator, directControlBackgroundStyle, recorded);
                Assert.True(Channel(separatorPixels, 45, 50, 0) < 8,
                    $"{backend} {separator.GetType().Name} recorded={recorded}: background escaped content box");
                Assert.True(Channel(separatorPixels, 50, 55, 0) > 245);
                Assert.True(Channel(separatorPixels, 100, 59, 2) > 100);
                Assert.True(Channel(separatorPixels, 110, 59, 1) > 100);

                var radiusPixels = Render(separator,
                    "border-radius:50%;background-color:blue", recorded);
                Assert.True(Channel(radiusPixels, 41, 31, 0) < 8,
                    $"{backend} {separator.GetType().Name} recorded={recorded}: background escaped radius");
                Assert.True(Channel(radiusPixels,
                    separator is AppBarSeparator ? 88 : 80, 45, 0) > 245);
            }

            foreach (FrameworkElement toolBarSurface in new FrameworkElement[]
                     {
                         new ToolBar { Width = 80, Height = 40 },
                         new ToolBarTray { Width = 80, Height = 40 },
                     })
            {
                var surfacePixels = Render(toolBarSurface, directControlBackgroundStyle, recorded);
                Assert.True(Channel(surfacePixels, 45, 50, 0) < 8,
                    $"{backend} {toolBarSurface.GetType().Name} recorded={recorded}: background escaped content box");
                Assert.True(Channel(surfacePixels, 50, 50, 0) > 245);
                Assert.True(Channel(surfacePixels, 100, 59, 2) > 100);
                Assert.True(Channel(surfacePixels, 110, 59, 1) > 100);

                var radiusPixels = Render(toolBarSurface,
                    "border-radius:50%;background-color:blue", recorded);
                Assert.True(Channel(radiusPixels, 41, 31, 0) < 8,
                    $"{backend} {toolBarSurface.GetType().Name} recorded={recorded}: background escaped radius");
                Assert.True(Channel(radiusPixels, 80, 50, 0) > 245);
            }

            AutoCompleteBox DirectAutoCompleteBox() => new()
            {
                Template = null,
                Foreground = Brushes.Transparent,
                PlaceholderText = string.Empty,
            };
            var autoCompletePixels = Render(DirectAutoCompleteBox(), directControlBackgroundStyle, recorded);
            Assert.True(Channel(autoCompletePixels, 45, 50, 0) < 8,
                $"{backend} recorded={recorded}: AutoCompleteBox background escaped content box");
            Assert.True(Channel(autoCompletePixels, 50, 50, 0) > 245);
            Assert.True(Channel(autoCompletePixels, 100, 59, 2) > 100);
            Assert.True(Channel(autoCompletePixels, 110, 59, 1) > 100);

            var roundedAutoComplete = Render(DirectAutoCompleteBox(),
                "border-radius:50%;background-color:blue", recorded);
            Assert.True(Channel(roundedAutoComplete, 41, 31, 0) < 8,
                $"{backend} recorded={recorded}: AutoCompleteBox background escaped radius");
            Assert.True(Channel(roundedAutoComplete, 80, 50, 0) > 245);
        }

        const string lowerImage =
            "linear-gradient(rgb(0 255 0 / .5),rgb(0 255 0 / .5))";
        var imageListStyle = "border-radius:20px / 10px;background:" +
            image + ",none," + lowerImage + " blue";
        foreach (var recorded in new[] { false, true })
        {
            var imageList = Render(new Border(), imageListStyle, recorded);
            Assert.InRange(Channel(imageList, 80, 50, 2), (byte)125, (byte)130);
            Assert.InRange(Channel(imageList, 80, 50, 1), (byte)62, (byte)66);
            Assert.InRange(Channel(imageList, 80, 50, 0), (byte)62, (byte)66);
            Assert.True(Channel(imageList, 41, 31, 2) < 8 &&
                        Channel(imageList, 41, 31, 1) < 8 &&
                        Channel(imageList, 41, 31, 0) < 8,
                $"{backend}: background image list escaped the elliptical border radius");
        }

        using var narrowTop = BitmapImage.FromPixels(
            [0, 0, 255, 128, 0, 0, 255, 128], 1, 2);
        using var wideBottom = BitmapImage.FromPixels(
            [0, 255, 0, 128, 0, 255, 0, 128], 1, 2);
        var sizedLayers = new CssLayeredBackgroundBrush(
            new CssLayeredBackgroundBrush(Brushes.Blue,
                new ImageBrush(wideBottom) { Stretch = Stretch.UniformToFill }),
            new ImageBrush(narrowTop) { Stretch = Stretch.Uniform });
        foreach (var recorded in new[] { false, true })
        {
            var sized = Render(new Border { Background = sizedLayers },
                "border-radius:20px / 10px", recorded);
            Assert.InRange(Channel(sized, 80, 50, 2), (byte)125, (byte)130);
            Assert.InRange(Channel(sized, 80, 50, 1), (byte)62, (byte)66);
            Assert.InRange(Channel(sized, 45, 50, 2), (byte)0, (byte)3);
            Assert.InRange(Channel(sized, 45, 50, 1), (byte)125, (byte)130);
            Assert.InRange(Channel(sized, 45, 50, 0), (byte)125, (byte)130);

            var cssContain = Render(new Border { Background = new ImageBrush(narrowTop)
            {
                CssBackgroundLayout = new CssBackgroundImageLayout(
                    CssBackgroundSizeMode.Contain, CssBackgroundRepeat.Both),
            } }, "", recorded);
            Assert.InRange(Channel(cssContain, 45, 50, 2), (byte)125, (byte)130);
            Assert.InRange(Channel(cssContain, 65, 50, 2), (byte)125, (byte)130);
        }

        var tilePixels = new byte[20 * 20 * 4];
        for (var y = 0; y < 20; y++)
        for (var x = 0; x < 20; x++)
        {
            var at = (y * 20 + x) * 4;
            tilePixels[at + (x < 10 ? 2 : 1)] = 255;
            tilePixels[at + 3] = 255;
        }
        using var cssTile = BitmapImage.FromPixels(tilePixels, 20, 20);
        foreach (var recorded in new[] { false, true })
        {
            var positioned = Render(new Border(),
                "background-color:blue;background-image:url('tile.png');" +
                "background-position:right 10px bottom 5px;background-repeat:no-repeat",
                recorded, beforeDraw: element =>
                {
                    var layers = Assert.IsType<CssLayeredBackgroundBrush>(((Border)element).Background);
                    Assert.IsType<ImageBrush>(layers.Top).ImageSource = cssTile;
                });
            Assert.True(Channel(positioned, 95, 50, 2) > 250);
            Assert.True(Channel(positioned, 105, 50, 1) > 250);
            Assert.True(Channel(positioned, 85, 50, 0) > 250);
            Assert.True(Channel(positioned, 115, 50, 0) > 250);
            Assert.True(Channel(positioned, 95, 68, 0) > 250);

            var explicitlySized = Render(new Border(),
                "background-color:blue;background-image:url('tile.png');" +
                "background-size:40px 10px;background-position:right 5px bottom 5px;" +
                "background-repeat:no-repeat",
                recorded, beforeDraw: element =>
                {
                    var layers = Assert.IsType<CssLayeredBackgroundBrush>(((Border)element).Background);
                    Assert.IsType<ImageBrush>(layers.Top).ImageSource = cssTile;
                });
            Assert.True(Channel(explicitlySized, 85, 60, 2) > 250);
            Assert.True(Channel(explicitlySized, 105, 60, 1) > 250);
            Assert.True(Channel(explicitlySized, 70, 60, 0) > 250);
            Assert.True(Channel(explicitlySized, 117, 60, 0) > 250);
            Assert.True(Channel(explicitlySized, 85, 68, 0) > 250);

            var spaced = Render(new Border(),
                "background-color:blue;background-image:url('tile.png');" +
                "background-size:18px 20px;background-repeat:space no-repeat;" +
                "background-position:right bottom",
                recorded, beforeDraw: element =>
                {
                    var layers = Assert.IsType<CssLayeredBackgroundBrush>(((Border)element).Background);
                    Assert.IsType<ImageBrush>(layers.Top).ImageSource = cssTile;
                });
            Assert.True(Channel(spaced, 45, 60, 2) > 250);
            Assert.True(Channel(spaced, 59, 60, 0) > 250);
            Assert.True(Channel(spaced, 65, 60, 2) > 250);
            Assert.True(Channel(spaced, 115, 60, 1) > 250);
            Assert.True(Channel(spaced, 65, 40, 0) > 250);

            var rounded = Render(new Border(),
                "background-color:blue;background-image:url('tile.png');" +
                "background-size:23px 20px;background-repeat:round no-repeat;" +
                "background-position:left bottom",
                recorded, beforeDraw: element =>
                {
                    var layers = Assert.IsType<CssLayeredBackgroundBrush>(((Border)element).Background);
                    Assert.IsType<ImageBrush>(layers.Top).ImageSource = cssTile;
                });
            Assert.True(Channel(rounded, 45, 60, 2) > 250);
            Assert.True(Channel(rounded, 60, 60, 1) > 250);
            Assert.True(Channel(rounded, 72, 60, 2) > 250);
            Assert.True(Channel(rounded, 110, 60, 1) > 250);
            Assert.True(Channel(rounded, 65, 40, 0) > 250);

            var boxed = Render(new Border
            {
                BorderThickness = new Thickness(4), Padding = new Thickness(6),
            }, "background-color:blue;background-image:url('tile.png');" +
               "background-size:8px 8px;background-repeat:no-repeat;" +
               "background-origin:content-box",
                recorded, beforeDraw: element =>
                {
                    var layers = Assert.IsType<CssLayeredBackgroundBrush>(((Border)element).Background);
                    Assert.IsType<ImageBrush>(layers.Top).ImageSource = cssTile;
                });
            Assert.True(Channel(boxed, 45, 45, 0) > 250);
            Assert.True(Channel(boxed, 52, 42, 2) > 250);
            Assert.True(Channel(boxed, 56, 42, 1) > 250);

            var clipped = Render(new Border
            {
                BorderThickness = new Thickness(4), Padding = new Thickness(6),
            }, "background-color:blue;background-image:url('tile.png');" +
               "background-size:8px 8px;background-repeat:no-repeat;" +
               "background-origin:content-box;background-clip:content-box",
                recorded, beforeDraw: element =>
                {
                    var layers = Assert.IsType<CssLayeredBackgroundBrush>(((Border)element).Background);
                    Assert.IsType<ImageBrush>(layers.Top).ImageSource = cssTile;
                });
            Assert.True(Channel(clipped, 45, 45, 0) < 5 &&
                        Channel(clipped, 45, 45, 1) < 5 &&
                        Channel(clipped, 45, 45, 2) < 5);
            Assert.True(Channel(clipped, 52, 42, 2) > 250);
            Assert.True(Channel(clipped, 75, 50, 0) > 250);

            var roundedClip = Render(new Border
            {
                BorderThickness = new Thickness(4), Padding = new Thickness(6),
            }, "border-radius:20px / 18px;background-color:blue;" +
               "background-clip:content-box", recorded);
            Assert.True(Channel(roundedClip, 51, 41, 0) < 5);
            Assert.True(Channel(roundedClip, 60, 41, 0) > 250);

            var zeroAreaOrigin = Render(new Border
            {
                BorderThickness = new Thickness(20, 10, 20, 10),
                Padding = new Thickness(20, 10, 20, 10),
            }, "background-image:url('tile.png');background-size:8px 8px;" +
               "background-repeat:no-repeat;background-origin:content-box",
                recorded, beforeDraw: element =>
                    Assert.IsType<ImageBrush>(((Border)element).Background).ImageSource = cssTile);
            Assert.True(Channel(zeroAreaOrigin, 75, 50, 2) < 5);
            Assert.True(Channel(zeroAreaOrigin, 82, 52, 2) > 250);
            Assert.True(Channel(zeroAreaOrigin, 86, 52, 1) > 250);

            var tiled = Render(new Border { Background = new ImageBrush(cssTile)
            {
                CssBackgroundLayout = new CssBackgroundImageLayout(
                    CssBackgroundSizeMode.Auto, CssBackgroundRepeat.Both),
            } }, "border-radius:20px / 10px", recorded);
            Assert.True(Channel(tiled, 45, 50, 2) > 250);
            Assert.True(Channel(tiled, 55, 50, 1) > 250);
            Assert.True(Channel(tiled, 65, 50, 2) > 250);
            Assert.True(Channel(tiled, 41, 31, 2) < 5 &&
                        Channel(tiled, 41, 31, 1) < 5);

            var noRepeat = Render(new Border { Background = new CssLayeredBackgroundBrush(
                Brushes.Blue, new ImageBrush(cssTile)
                {
                    CssBackgroundLayout = new CssBackgroundImageLayout(
                        CssBackgroundSizeMode.Auto, new CssBackgroundRepeat(false, false)),
                }) }, "border-radius:20px / 10px", recorded);
            Assert.True(Channel(noRepeat, 45, 45, 2) > 250);
            Assert.True(Channel(noRepeat, 65, 45, 0) > 250);
            Assert.True(Channel(noRepeat, 65, 45, 2) < 5);
        }

        var panel = Render(new StackPanel(),
            "background-color:blue;background-image:" + image);
        Assert.InRange(Channel(panel, 80, 50, 0), (byte)125, (byte)130);

        foreach (var recorded in new[] { false, true })
        {
            var gridBackground = Render(new Grid(),
                "display:grid;padding:10px;background-color:blue;" +
                "background-image:linear-gradient(red,red);" +
                "background-repeat:no-repeat;background-origin:content-box;" +
                "background-clip:content-box", recorded);
            Assert.True(Channel(gridBackground, 45, 35, 2) < 8);
            Assert.True(Channel(gridBackground, 52, 42, 2) > 245);
            Assert.True(Channel(gridBackground, 52, 42, 0) < 8);

            var flex = new StackPanel();
            var item = new Border { Background = Brushes.Lime };
            flex.Children.Add(item);
            FlexPanel.SetBasis(item, 20);
            var flexBackground = Render(flex,
                "display:flex;padding:10px;background-color:blue;" +
                "background-clip:content-box", recorded);
            Assert.True(Channel(flexBackground, 45, 35, 1) < 8);
            Assert.True(Channel(flexBackground, 52, 42, 1) > 245);
            Assert.True(Channel(flexBackground, 75, 50, 0) > 245);

            var textBackground = Render(new TextBlock(),
                "padding:10px;background-color:blue;" +
                "background-image:linear-gradient(red,red);" +
                "background-repeat:no-repeat;background-origin:content-box;" +
                "background-clip:content-box", recorded);
            Assert.True(Channel(textBackground, 45, 35, 2) < 8);
            Assert.True(Channel(textBackground, 52, 42, 2) > 245);
        }

        using var bitmap = BitmapImage.FromPixels([0, 0, 255, 128], 1, 1);
        var raster = Render(new Border
        {
            Background = new CssLayeredBackgroundBrush(Brushes.Blue,
                new ImageBrush(bitmap)),
        }, "border-radius:20px / 10px");
        Assert.InRange(Channel(raster, 80, 50, 0), (byte)125, (byte)130);
        Assert.True(Channel(raster, 41, 31, 0) < 8,
            $"{backend}: layered raster image escaped the ellipse");

        var unavailable = Render(new Border
        {
            Background = new CssLayeredBackgroundBrush(Brushes.Blue,
                new ImageBrush()),
        }, "");
        Assert.True(Channel(unavailable, 80, 50, 0) > 250,
            $"{backend}: unavailable image removed the background color fallback");

        ResetTheme();
        var app = new Application();
        try
        {
            var buttonElement = new Button { Content = "" };
            var host = new Border { Child = buttonElement };
            var button = Render(buttonElement,
                "background-color:blue;background-image:" + image,
                renderRoot: host);
            Assert.IsType<CssLayeredBackgroundBrush>(buttonElement.Background);
            Assert.NotNull(buttonElement.FindName("RootBorder"));
            Assert.InRange(Channel(button, 80, 50, 0), (byte)125, (byte)130);

            foreach (Control slider in new Control[]
                     {
                         new Slider { Width = 80, Height = 40, Value = 0 },
                         new RangeSlider { Width = 80, Height = 40, RangeStart = 0, RangeEnd = 0 },
                     })
            {
                var sliderHost = new Border { Child = slider };
                foreach (var recorded in new[] { false, true })
                {
                    var sliderPixels = Render(slider, directControlBackgroundStyle,
                        recorded, renderRoot: sliderHost);
                    Assert.NotNull(slider.FindName("PART_Root"));
                    Assert.True(Channel(sliderPixels, 45, 50, 0) < 8,
                        $"{backend} {slider.GetType().Name} theme recorded={recorded}: background escaped content box");
                    Assert.True(Channel(sliderPixels, 90, 40, 0) > 245);
                    Assert.True(Channel(sliderPixels, 100, 59, 2) > 100);
                    Assert.True(Channel(sliderPixels, 110, 59, 1) > 100);
                }
            }
        }
        finally
        {
            ResetTheme();
            GC.KeepAlive(app);
        }

        border.Background = Brushes.Green;
        var local = Render(border, "background-color:blue;background-image:" + image);
        Assert.True(Channel(local, 80, 50, 1) > 100 &&
                    Channel(local, 80, 50, 2) < 8,
            $"{backend}: CSS background layers displaced a native local brush");
    }

    private static void ResetTheme()
    {
        typeof(Application).GetField("_current", BindingFlags.Static | BindingFlags.NonPublic)!
            .SetValue(null, null);
        typeof(Jalium.UI.Controls.Themes.ThemeManager)
            .GetMethod("Reset", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, null);
    }
}

using AppKit;
using Foundation;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;

namespace Jalium.UI.MacOS;

// Synthetic target DPI is separate from the actual display's backing scale.
internal static class WindowBitmapDpiChecks
{
    internal static int Run()
    {
        var app = NSApplication.SharedApplication;
        using var host = Initialize(app);
        int passed = 0;
        app.BeginInvokeOnMainThread(() =>
        {
            foreach (bool custom in new[] { false, true })
                foreach (bool rotated in new[] { false, true })
                    foreach (int dpi in new[] { 96, 144, 192 })
                    {
                        Window? window = null;
                        try
                        {
                            var bitmap = Snapshot(dpi, rotated);
                            var output = Environment.GetEnvironmentVariable("JALIUM_BITMAP_DPI_OUTPUT");
                            if (!string.IsNullOrEmpty(output))
                            {
                                Directory.CreateDirectory(output);
                                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                                using var file = File.Create(Path.Combine(output, $"{(custom ? "custom" : "native")}-{dpi}-{rotated}.png"));
                                encoder.Save(file);
                            }
                            var image = new Image { Source = bitmap, Width = 120, Height = 120, Stretch = Stretch.Fill };
                            var editor = new TextBox { Text = "窗口位图验证🙂", Height = 40 };
                            var panel = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
                            panel.Children.Add(image); panel.Children.Add(editor);
                            window = Owner(custom, panel); Application.Current!.MainWindow = window;
                            window.Show(); window.UpdateLayout();
                            if (!editor.Focus() || !ReferenceEquals(Keyboard.FocusedElement, editor))
                                throw new InvalidOperationException("Bitmap preview changed editor focus.");
                            Console.WriteLine($"PASS bitmap DPI: custom={custom}; dpi={dpi}; rotated={rotated}; pixels={bitmap.PixelWidth}x{bitmap.PixelHeight}; focus=True");
                            passed++;
                        }
                        catch (Exception e) { Console.Error.WriteLine($"FAIL bitmap DPI: custom={custom}; dpi={dpi}; rotated={rotated}; {e}"); }
                        finally { window?.Close(); }
                    }
            app.Terminate(app);
        });
        app.Run(); Console.WriteLine($"macOS bitmap DPI host checks: {passed}/12 passed");
        return passed == 12 ? 0 : 1;
    }

    internal static int Observe(bool custom)
    {
        var app = NSApplication.SharedApplication;
        using var host = Initialize(app);
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 10 };
        panel.Children.Add(new TextBlock { Text = "窗口位图与裁剪", FontSize = 22 });
        panel.Children.Add(new TextBlock { Text = "三列图形应大小一致，圆角外与内部孔洞均为空白。按 F6 切换旋转，再继续编辑。", FontSize = 15, TextWrapping = TextWrapping.Wrap });
        var status = new TextBlock { FontSize = 15, TextWrapping = TextWrapping.Wrap }; panel.Children.Add(status);
        var previews = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        var images = new List<Image>();
        foreach (int dpi in new[] { 96, 144, 192 })
        {
            var column = new StackPanel { Spacing = 10 };
            column.Children.Add(new TextBlock { Text = $"{dpi} DPI", FontSize = 16 });
            var image = new Image { Width = 120, Height = 120, Stretch = Stretch.Fill };
            AutomationProperties.SetName(image, $"{dpi} DPI 裁剪预览");
            images.Add(image); column.Children.Add(image);
            previews.Children.Add(new Border { Width = 140, Padding = new Thickness(10), CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(Color.FromRgb(0x24, 0x2a, 0x33)), Child = column });
        }
        panel.Children.Add(previews);
        var editor = new TextBox { Text = "窗口位图验证🙂", Height = 40 };
        AutomationProperties.SetName(editor, "位图检查后的编辑框"); panel.Children.Add(editor);
        var rotate = Button("切换旋转（F6）"); panel.Children.Add(rotate);
        var size = Button("切换到最小尺寸"); panel.Children.Add(size);
        var finish = Button("结束检查"); panel.Children.Add(finish);
        var owner = Owner(custom, panel); Application.Current!.MainWindow = owner;
        bool rotated = false, closed = false, failed = false; int changes = 0;
        void Rebuild()
        {
            try
            {
                for (int i = 0; i < images.Count; i++) images[i].Source = Snapshot(new[] { 96, 144, 192 }[i], rotated);
                status.Text = rotated ? "已旋转；三列位图均已通过像素检查。" : "未旋转；三列位图均已通过像素检查。";
                Console.WriteLine($"BITMAP MODE: rotated={rotated}; changes={changes}; pixels=True");
            }
            catch (Exception e) { failed = true; status.Text = "位图像素检查失败，详情见验证日志。"; Console.Error.WriteLine(e); }
        }
        void Switch() { rotated = !rotated; changes++; Rebuild(); owner.UpdateLayout(); editor.Focus(); }
        rotate.Click += (_, _) => Switch();
        size.Click += (_, _) => { owner.Width = owner.MinWidth; owner.Height = owner.MinHeight; owner.UpdateLayout(); editor.Focus(); };
        finish.Click += (_, _) => owner.Close();
        owner.PreviewKeyDown += (_, e) => { if (e.Key == Key.F6) { e.Handled = true; Switch(); } };
        owner.Closed += (_, _) => { closed = true; app.Terminate(app); };
        owner.Activated += (_, _) => Console.WriteLine($"BITMAP ACTIVE: custom={custom}; focus={Keyboard.FocusedElement?.GetType().Name}");
        app.BeginInvokeOnMainThread(() =>
        {
            Rebuild(); owner.Show(); owner.UpdateLayout(); editor.Focus();
            Console.WriteLine($"BITMAP READY: custom={custom}; title={owner.Title}");
            NSTimer.CreateScheduledTimer(900, _ => { if (!closed) owner.Close(); });
        });
        app.Run();
        Console.WriteLine($"BITMAP COMPLETE: custom={custom}; changes={changes}; closed={closed}; failed={failed}; text={editor.Text}");
        return closed && !failed ? 0 : 1;
    }

    private static RenderTargetBitmap Snapshot(int dpi, bool rotated)
    {
        var inner = new Canvas { Width = 100, Height = 100,
            Clip = Geometry.Parse("M0,0 L100,0 100,100 0,100 Z M50,15 L85,15 85,35 50,35 Z") };
        inner.Children.Add(new Jalium.UI.Shapes.Rectangle { Width = 100, Height = 100,
            Fill = new SolidColorBrush(Color.FromRgb(0x6e, 0x7d, 0xf5)) });
        var rounded = new Canvas { Width = 100, Height = 100,
            Clip = new RectangleGeometry(new Rect(0, 0, 100, 100), 30, 30) };
        rounded.Children.Add(inner);
        if (rotated) rounded.RenderTransform = new MatrixTransform(new Matrix(0, 1, -1, 0, 100, 0));
        var scene = new Canvas { Width = 120, Height = 120 };
        Canvas.SetLeft(rounded, 10); Canvas.SetTop(rounded, 8); scene.Children.Add(rounded);
        scene.Measure(new Size(120, 120)); scene.Arrange(new Rect(0, 0, 120, 120));
        int size = 120 * dpi / 96;
        var bitmap = new RenderTargetBitmap(size, size, dpi, dpi, PixelFormat.Bgra32);
        bitmap.Render(scene); var data = new byte[size * size * 4];
        bitmap.CopyPixels(new Int32Rect(0, 0, size, size), data, size * 4, 0);
        foreach (var (point, alpha) in new[] { (new Point(20, 50), 255), (new Point(95, 50), 255),
            (new Point(1, 1), 0), (new Point(65, 25), 0), (new Point(101, 50), 0) })
        {
            var location = rotated ? new Point(10 + 100 - point.Y, 8 + point.X) : new Point(10 + point.X, 8 + point.Y);
            int x = (int)(location.X * dpi / 96), y = (int)(location.Y * dpi / 96);
            if (data[(y * size + x) * 4 + 3] != alpha) throw new InvalidOperationException($"Incorrect bitmap mask at DPI {dpi}, rotated={rotated}, sample={point}.");
        }
        return bitmap;
    }
    private static Button Button(string text) => new() { Content = text, Height = 40, MinWidth = 350, HorizontalAlignment = HorizontalAlignment.Left };
    private static Window Owner(bool custom, UIElement content) => new()
    {
        Title = "Jalium Bitmap DPI v154 " + (custom ? "Custom" : "Native"),
        Width = 680, Height = 680, MinWidth = 520, MinHeight = 620, Content = content,
        TitleBarStyle = custom ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native,
        WindowStartupLocation = WindowStartupLocation.CenterScreen,
        Background = new SolidColorBrush(Color.FromRgb(0x1c, 0x20, 0x26)),
    };
    private static ValidationDelegate Initialize(NSApplication app)
    {
        app.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        var host = new ValidationDelegate(); app.Delegate = host; app.FinishLaunching();
        using (var launch = NSNotification.FromName(NSApplication.DidFinishLaunchingNotification, app)) host.DidFinishLaunching(launch);
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        return host;
    }
    private sealed class ValidationDelegate : JaliumMacApplicationDelegate
    {
        private bool _started;
        public override void DidFinishLaunching(NSNotification notification)
        { if (_started) return; _started = true; base.DidFinishLaunching(notification); }
        protected override JaliumApp CreateHostedApp() => AppBuilder.CreateBuilder(new AppBuilderSettings { DisableDefaults = true }).Build()
            .UseApplication(new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown });
    }
}

using AppKit;
using CoreGraphics;
using Foundation;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using ObjCRuntime;
using System.Collections.Specialized;
using System.Diagnostics;
using Jalium.UI.Controls.Platform;

namespace Jalium.UI.MacOS;

// Public clipboard checks lease and restore the existing board in memory.
// Private native tests cover malformed and oversized payloads independently.
internal static class WindowClipboardFileChecks
{
    private const string Text = "附带文本🙂";
    private const string Html = "<b>附带🙂</b>";
    private const string BinaryFormat = "Jalium.v151.Batch";
    private static readonly byte[] Binary = [0, 1, 255, 42];

    internal static int Run()
    {
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        using var lease = new ClipboardLease();
        string[] files = Fixtures();
        int passed = 0, count = 6;
        for (int index = 0; index < count; index++)
        {
            try
            {
                if (index == 0)
                {
                    var collection = new StringCollection(); collection.AddRange(files);
                    Clipboard.SetFileDropList(collection); lease.MarkWritten();
                    Verify(files, false);
                }
                else if (index == 1 || index == 2)
                {
                    string[] batch = index == 1 ? files : [files[1], files[0], files[1]];
                    Clipboard.SetDataObject(Data(batch), copy: true); lease.MarkWritten();
                    Verify(batch, true);
                }
                else if (index == 3)
                {
                    // A native writer simulates an external source. It changes the
                    // ownership token and exercises framework import, not the
                    // retained identity of an object this process just published.
                    using var web = new NSUrl("https://example.test/a%20b");
                    using var first = new NSUrl(new Uri(files[0]).AbsoluteUri);
                    using var second = new NSUrl(new Uri(files[1]).AbsoluteUri);
                    var board = NSPasteboard.GeneralPasteboard;
                    board.ClearContents();
                    Require(board.WriteObjects([web, first, second]), "native mixed URL source write failed");
                    lease.MarkWritten(); Verify(files[..2], false, nativeItems: 3);
                }
                else if (index == 4)
                {
                    Clipboard.SetText(Text); lease.MarkWritten();
                    Require(Clipboard.GetText() == Text && !Clipboard.ContainsFileDropList(), "text-only write advertised files");
                }
                else
                {
                    Clipboard.Clear(); lease.MarkWritten();
                    Require(!Clipboard.ContainsFileDropList() && NSPasteboard.GeneralPasteboard.PasteboardItems.Length == 0,
                        "clear retained file items");
                }
                passed++; Console.WriteLine($"PASS {index}: public clipboard and AppKit file reading");
            }
            catch (Exception e) { lease.MarkWritten(); Console.Error.WriteLine($"FAIL {index}: {e.Message}"); }
        }
        lease.Restore();
        Console.WriteLine($"macOS clipboard file batch host checks: {passed}/{count} passed; restored={lease.Restored}");
        return passed == count && lease.Restored ? 0 : 1;
    }

    internal static int Observe(bool custom)
    {
        var app = NSApplication.SharedApplication; app.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        using var host = new ValidationDelegate(); app.Delegate = host; app.FinishLaunching();
        using (var launch = NSNotification.FromName(NSApplication.DidFinishLaunchingNotification, app))
            host.DidFinishLaunching(launch);
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        using var lease = new ClipboardLease();
        string[] files = Fixtures();
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
        var status = new TextBlock { Text = "先复制，再聚焦接收区按 ⌘V。列表应包含四项并保留顺序。", FontSize = 15, TextWrapping = TextWrapping.Wrap };
        var list = new TextBlock { Text = string.Join("\n", files.Select(x => Path.GetFileName(x.TrimEnd('/')))), FontSize = 15, TextWrapping = TextWrapping.Wrap };
        var editor = new TextBox { Text = "复制粘贴后继续编辑🙂", Height = 40 };
        AutomationProperties.SetName(editor, "复制粘贴后的编辑框");
        panel.Children.Add(new TextBlock { Text = "多个文件的复制与粘贴", FontSize = 22 });
        panel.Children.Add(status); panel.Children.Add(list); panel.Children.Add(editor);
        var copy = Button("复制四个文件项目");
        var mixed = Button("复制文件及附带格式");
        var receiver = Button("接收区：按 ⌘V 或拖入文件"); receiver.AllowDrop = true;
        var finish = Button("结束并恢复原剪贴板");
        foreach (var button in new[] { copy, mixed, receiver, finish }) panel.Children.Add(button);
        var size = Button("切换到最小尺寸"); panel.Children.Add(size);
        int copies = 0, pastes = 0, drops = 0; bool closed = false, hasSecondary = false, failed = false;
        var window = new Window { Title = custom ? "Jalium Clipboard v151 Custom" : "Jalium Clipboard v151 Native",
            Width = 690, Height = 600, MinWidth = 520, MinHeight = 560,
            TitleBarStyle = custom ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, Content = panel,
            Background = new SolidColorBrush(Color.FromRgb(0x1c, 0x20, 0x26)) };
        Application.Current!.MainWindow = window;
        size.Click += (_, _) => { window.Width = 520; window.Height = 560; window.UpdateLayout(); };
        window.Closed += (_, _) => closed = true;
        void Copy(bool secondary)
        {
            if (secondary) Clipboard.SetDataObject(Data(files), copy: true);
            else { var values = new StringCollection(); values.AddRange(files); Clipboard.SetFileDropList(values); }
            lease.MarkWritten(); hasSecondary = secondary; Verify(files, secondary); copies++;
            status.Text = $"已复制四项，附带格式：{(secondary ? "保留" : "无")}。到接收区按 ⌘V。";
            Console.WriteLine($"CLIPBOARD COPY: copies={copies}; items={NSPasteboard.GeneralPasteboard.PasteboardItems.Length}; secondary={secondary}");
        }
        void Paste()
        {
            Verify(files, hasSecondary); pastes++;
            status.Text = $"已接收四项，顺序、重复项与中文路径正确。粘贴次数：{pastes}";
            Console.WriteLine($"CLIPBOARD PASTE: pastes={pastes}; files=4; secondary={hasSecondary}");
        }
        void Safe(Action action)
        {
            try { action(); window.UpdateLayout(); }
            catch (Exception e) { failed = true; status.Text = "检查失败：" + e.Message; Console.Error.WriteLine("CLIPBOARD FAIL: " + e); }
        }
        copy.Click += (_, _) => Safe(() => Copy(false)); mixed.Click += (_, _) => Safe(() => Copy(true));
        receiver.Click += (_, _) => Safe(Paste); finish.Click += (_, _) => window.Close();
        copy.CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, (_, e) => { Safe(() => Copy(false)); e.Handled = true; },
            (_, e) => { e.CanExecute = true; e.Handled = true; }));
        copy.InputBindings.Add(new KeyBinding(ApplicationCommands.Copy, new KeyGesture(Key.C, ModifierKeys.Windows)));
        receiver.CommandBindings.Add(new CommandBinding(ApplicationCommands.Paste, (_, e) => { Safe(Paste); e.Handled = true; },
            (_, e) => { e.CanExecute = Clipboard.ContainsFileDropList(); e.Handled = true; }));
        receiver.InputBindings.Add(new KeyBinding(ApplicationCommands.Paste, new KeyGesture(Key.V, ModifierKeys.Windows)));
        receiver.DragOver += (_, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
        receiver.Drop += (_, e) => Safe(() =>
        {
            Require(e.Data.GetData(DataFormats.FileDrop) is string[] actual && actual.SequenceEqual(files), "drag batch changed");
            Require((string?)e.Data.GetData(DataFormats.Html) == Html, "drag HTML changed");
            drops++; e.Effects = DragDropEffects.Copy; e.Handled = true;
            status.Text = $"已拖入完整四项，附带 HTML 保留。拖放次数：{drops}";
            Console.WriteLine($"CLIPBOARD DROP: drops={drops}; files=4; html=True");
        });
        Point? pressed = null; bool dragging = false;
        mixed.PreviewMouseDown += (_, e) => { pressed = e.GetPosition(mixed); };
        mixed.MouseMove += (_, e) =>
        {
            if (dragging || pressed is not { } start || e.LeftButton != MouseButtonState.Pressed) return;
            Point at = e.GetPosition(mixed);
            if (Math.Abs(at.X - start.X) + Math.Abs(at.Y - start.Y) < 6) return;
            pressed = null; dragging = true; e.Handled = true;
            try { Console.WriteLine("CLIPBOARD DRAG BEGIN"); Console.WriteLine("CLIPBOARD DRAG RETURN: " +
                DragDrop.DoDragDrop(mixed, Data(files), DragDropEffects.Copy)); }
            catch (Exception error) { failed = true; Console.Error.WriteLine(error); }
            finally { dragging = false; mixed.ReleaseMouseCapture(); }
        };
        window.Show(); window.UpdateLayout(); editor.Focus();
        var watch = Stopwatch.StartNew(); string? last = null;
        using var timer = NSTimer.CreateRepeatingTimer(.2, _ =>
        {
            string state = $"active={app.Active}; key={app.KeyWindow?.Title}; focus={FocusedName()}; size={window.ActualWidth}x{window.ActualHeight}; text={editor.Text}";
            if (state != last) { last = state; Console.WriteLine("CLIPBOARD STATE: " + state); }
            if (!closed && watch.Elapsed.TotalSeconds < 900) return;
            if (!closed) window.Close();
            NativeMethods.PlatformQuit(0);
        });
        NSRunLoop.Main.AddTimer(timer, NSRunLoopMode.Common); NSRunLoop.Main.AddTimer(timer, NSRunLoopMode.ModalPanel);
        app.Run(); timer.Invalidate(); lease.Restore();
        Console.WriteLine($"CLIPBOARD COMPLETE: copies={copies}; pastes={pastes}; drops={drops}; closed={closed}; failed={failed}; restored={lease.Restored}; text={editor.Text}");
        return !failed && lease.Restored ? 0 : 1;
    }
    private static Button Button(string text) => new() { Content = text, Height = 40, HorizontalAlignment = HorizontalAlignment.Left, MinWidth = 350 };
    private static string FocusedName() => Keyboard.FocusedElement is DependencyObject d ? AutomationProperties.GetName(d) : Keyboard.FocusedElement?.GetType().Name ?? "none";
    private static string[] Fixtures()
    {
        string root = Environment.GetEnvironmentVariable("JALIUM_CLIPBOARD_FIXTURE_ROOT") ??
            throw new InvalidOperationException("Set JALIUM_CLIPBOARD_FIXTURE_ROOT to an owned validation directory.");
        Directory.CreateDirectory(root); Directory.CreateDirectory(Path.Combine(root, "folder"));
        string first = Path.Combine(root, "one file.txt"), second = Path.Combine(root, "中文🙂#%.txt");
        File.WriteAllText(first, "fixture"); File.WriteAllText(second, "fixture🙂");
        return [first, second, Path.Combine(root, "folder") + "/", first];
    }
    private static DataObject Data(string[] files)
    {
        var value = new DataObject();
        value.SetData(DataFormats.FileDrop, files); value.SetData(DataFormats.UnicodeText, Text);
        value.SetData(DataFormats.Html, Html); value.SetData(BinaryFormat, Binary, false);
        return value;
    }
    private static void Verify(string[] files, bool secondary, int? nativeItems = null)
    {
        var board = NSPasteboard.GeneralPasteboard;
        Require(board.PasteboardItems.Length == (nativeItems ?? files.Length), "native URL item count changed");
        using var options = NSDictionary.FromObjectAndKey(NSNumber.FromBoolean(true), new NSString("NSPasteboardURLReadingFileURLsOnlyKey"));
        var values = board.ReadObjectsForClasses([new Class(typeof(NSUrl))], options).Cast<NSUrl>().ToArray();
        Require(values.Length == files.Length, "AppKit lost native file URLs");
        // AppKit URL paths omit the trailing directory slash; URI-list decoding keeps it.
        Require(values.Select(x => x.Path!.TrimEnd('/')).SequenceEqual(files.Select(x => x.TrimEnd('/'))), "AppKit file order/path changed");
        Require(Clipboard.ContainsFileDropList(), "framework did not advertise files");
        Require(Clipboard.GetFileDropList().Cast<string>().SequenceEqual(files), "framework lost file order/path");
        var imported = ClipboardPlatform.GetDataObject();
        Require(imported?.GetData(DataFormats.FileDrop) is string[] actual && actual.SequenceEqual(files),
            "native clipboard import lost file order/path");
        if (!secondary) return;
        Require(Clipboard.GetText() == Text && Clipboard.GetText(TextDataFormat.Html) == Html, "secondary text or HTML changed");
        Require(Clipboard.GetData(BinaryFormat) is byte[] bytes && bytes.SequenceEqual(Binary), "custom binary format changed");
        Require((string?)imported!.GetData(DataFormats.UnicodeText) == Text && (string?)imported.GetData(DataFormats.Html) == Html,
            "native clipboard import lost secondary text or HTML");
        Require(imported.GetData(BinaryFormat) is byte[] nativeBytes && nativeBytes.SequenceEqual(Binary),
            "native clipboard import lost the registered binary format");
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class ClipboardLease : IDisposable
    {
        private readonly NSPasteboard _board = NSPasteboard.GeneralPasteboard;
        private readonly List<NSPasteboardItem> _saved = [];
        private nint _owned;
        private bool _finished;
        internal bool Restored { get; private set; }
        internal ClipboardLease()
        {
            _owned = _board.ChangeCount; long total = 0;
            var originals = _board.PasteboardItems;
            Require(originals.Length <= 64, "existing clipboard has too many items for this bounded lease");
            foreach (var original in originals)
            {
                Require(original.Types.Length <= 32 && original.Types.All(x => !x.Contains("promise", StringComparison.OrdinalIgnoreCase)),
                    "existing clipboard has promised data; leave it untouched");
                var saved = new NSPasteboardItem(); _saved.Add(saved);
                foreach (string type in original.Types)
                {
                    using var data = original.GetDataForType(type);
                    Require(data != null && data.Length <= 4 * 1024 * 1024, "existing clipboard data cannot be safely leased");
                    total += (long)data!.Length; Require(total <= 32 * 1024 * 1024, "existing clipboard is too large to lease");
                    Require(saved.SetDataForType(data, type), "clipboard snapshot failed");
                }
            }
            Require(_board.ChangeCount == _owned, "clipboard changed during snapshot");
            Console.WriteLine($"CLIPBOARD LEASE: savedItems={_saved.Count}; bytes={total}; contents=private");
        }
        internal void MarkWritten() => _owned = _board.ChangeCount;
        internal void Restore()
        {
            if (_finished) return; _finished = true;
            if (_board.ChangeCount != _owned) { Console.WriteLine("CLIPBOARD LEASE: external change preserved"); return; }
            _board.ClearContents();
            Require(_saved.Count == 0 || _board.WriteObjects(_saved.ToArray()), "clipboard restoration failed");
            var restored = _board.PasteboardItems;
            Require(restored.Length == _saved.Count, "restored clipboard item count changed");
            for (int i = 0; i < restored.Length; i++)
            {
                Require(restored[i].Types.SequenceEqual(_saved[i].Types), "restored clipboard types changed");
                foreach (string type in _saved[i].Types)
                {
                    using var expected = _saved[i].GetDataForType(type); using var actual = restored[i].GetDataForType(type);
                    Require(actual != null && expected!.ToArray().SequenceEqual(actual.ToArray()), "restored clipboard bytes changed");
                }
            }
            Restored = true; Console.WriteLine("CLIPBOARD LEASE: all original items/types/bytes restored");
        }
        public void Dispose() { Restore(); foreach (var item in _saved) item.Dispose(); }
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

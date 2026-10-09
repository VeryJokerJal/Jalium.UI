using AppKit;
using CoreGraphics;
using Foundation;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Threading;
using ObjCRuntime;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Jalium.UI.MacOS;

// A real Jalium/AppKit window receives delegate calls from this process's owned
// dragging-info object and unique pasteboard. This proves native-to-managed
// routing and snapshots; it does not simulate an OS-tracked cross-app gesture.
internal static class WindowDragRepresentationChecks
{
    private const int Count = 12;
    internal static int RunAll()
    {
        int passed = 0;
        for (int index = 0; index < Count; index++)
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
            start.Environment.Remove("JALIUM_MACOS_DRAG_OBSERVE_SECONDS");
            start.ArgumentList.Add($"--window-drag-representation-case={index}");
            using var process = Process.Start(start)!;
            if (!process.WaitForExit(30_000))
            {
                process.Kill(); process.WaitForExit();
                Console.Error.WriteLine($"FAIL {index}: drag representation case timed out");
            }
            else if (process.ExitCode == 0) passed++;
        }
        Console.WriteLine($"macOS Window drag representation host checks: {passed}/{Count} passed");
        return passed == Count ? 0 : 1;
    }

    internal static int RunCase(string argument)
    {
        if (!int.TryParse(argument.AsSpan("--window-drag-representation-case=".Length), out int index) || (uint)index >= Count) return 2;
        int.TryParse(Environment.GetEnvironmentVariable("JALIUM_MACOS_DRAG_OBSERVE_SECONDS"), out int seconds);
        JaliumMacApplication.Initialize();
        NSApplication.SharedApplication.ActivationPolicy = seconds > 0
            ? NSApplicationActivationPolicy.Regular : NSApplicationActivationPolicy.Prohibited;
        NSApplication.SharedApplication.FinishLaunching();
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var editor = new TextBox { Text = "拖放后保留编辑🙂", Height = 40 };
        AutomationProperties.SetName(editor, "拖放验证编辑框");
        var result = new TextBlock { FontSize = 16, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap };
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "链接与文件拖放", FontSize = 22, Foreground = Brushes.White });
        panel.Children.Add(new TextBlock { Text = "检查网页链接和文件的识别结果，再用 Tab 检查编辑与焦点。", FontSize = 15,
            Foreground = Brushes.LightGray, TextWrapping = TextWrapping.Wrap });
        panel.Children.Add(result); panel.Children.Add(editor);
        var web = new Button { Content = "检查网页链接", Height = 40, HorizontalAlignment = HorizontalAlignment.Left, Width = 180 };
        var files = new Button { Content = "检查两个文件", Height = 40, HorizontalAlignment = HorizontalAlignment.Left, Width = 180 };
        var finish = new Button { Content = "结束拖放验证", Height = 40, HorizontalAlignment = HorizontalAlignment.Left, Width = 180 };
        panel.Children.Add(web); panel.Children.Add(files); panel.Children.Add(finish);
        var window = new Window
        {
            Title = "Jalium Window Drag v143", Width = 640, Height = 460, MinWidth = 520, MinHeight = 450,
            TitleBarStyle = index % 2 == 0 ? WindowTitleBarStyle.Native : WindowTitleBarStyle.Custom,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, ShowActivated = seconds > 0, AllowDrop = true,
            Background = new SolidColorBrush(Color.FromRgb(0x1c, 0x20, 0x26)), Content = panel
        };
        application.MainWindow = window;
        bool closed = false;
        window.Closed += (_, _) => closed = true;
        finish.Click += (_, _) => window.Close();
        int currentKind = index / 2, previews = 0, enters = 0, drops = 0, runs = 0;
        IDataObject? entered = null, retained = null;
        string[] expectedFiles = [];
        byte[] expectedUris = [];
        window.PreviewDragEnter += (_, e) => { previews++; Classify(e); };
        window.DragEnter += (_, e) => { enters++; entered = e.Data; Classify(e); };
        window.Drop += (_, e) =>
        {
            drops++; Classify(e); Require(ReferenceEquals(entered, e.Data), "Drop replaced the external visit data object");
            retained = e.Data;
            Require((string?)e.Data.GetData(DataFormats.Html) == "<b>保留🙂</b>", "Drop lost its secondary HTML representation");
            Require((string?)e.Data.GetData(DataFormats.Text) == "保留文本🙂", "Drop lost its text representation");
        };
        void Classify(DragEventArgs e)
        {
            bool hasFiles = expectedFiles.Length != 0;
            Require(e.Data.GetDataPresent(DataFormats.FileDrop) == hasFiles, "URI list advertised the wrong FileDrop availability");
            Require(e.Data.GetDataPresent("FileNameW") == hasFiles, "file-name alias advertised the wrong availability");
            Require(e.Data.GetFormats().Contains(DataFormats.FileDrop) == hasFiles, "format enumeration misclassified the URI list");
            Require(e.Data.GetData("text/uri-list", false) is byte[] raw && raw.SequenceEqual(expectedUris), "raw URI representation changed");
            if (hasFiles) Require(e.Data.GetData(DataFormats.FileDrop) is string[] paths && paths.SequenceEqual(expectedFiles), "file paths changed");
            else Require(e.Data.GetData(DataFormats.FileDrop) == null, "web-only URI list returned a file value");
            e.Effects = DragDropEffects.Copy;
        }
        void Replay(int kind)
        {
            currentKind = kind;
            string[] values = kind switch
            {
                0 => ["https://example.test/中文?q=🙂"],
                1 => ["file:///tmp/one%20file.txt", "file:///tmp/中文%F0%9F%99%82.txt"],
                2 => ["https://example.test/\r\nfile:///tmp/mixed.txt"],
                3 => ["# file:///tmp/comment.txt\r\nhttps://example.test/"],
                4 => ["https://example.test/", "file:///tmp/second.txt"],
                _ => [""]
            };
            expectedFiles = kind switch
            {
                1 => ["/tmp/one file.txt", "/tmp/中文🙂.txt"], 2 => ["/tmp/mixed.txt"], 4 => ["/tmp/second.txt"], _ => []
            };
            expectedUris = Encoding.UTF8.GetBytes(string.Join("\r\n", values) + "\r\n");
            entered = retained = null;
            using var board = NSPasteboard.CreateWithUniqueName();
            var items = values.Select((value, itemIndex) =>
            {
                var item = new NSPasteboardItem();
                string type = kind == 1 || kind == 4 && itemIndex == 1 ? "public.file-url" : "public.url";
                Require(item.SetStringForType(value, type), "unique URI pasteboard item could not be written");
                if (itemIndex == 0)
                {
                    Require(item.SetStringForType("保留文本🙂", "public.utf8-plain-text"), "text representation write failed");
                    using var html = NSData.FromString("<b>保留🙂</b>");
                    Require(item.SetDataForType(html, "public.html"), "HTML representation write failed");
                }
                return item;
            }).ToArray();
            try
            {
                Require(board.WriteObjects(items), "unique pasteboard could not retain the owned items");
                var view = Runtime.GetNSObject<NSView>(window.Handle)!;
                using var info = new JaliumOwnedDragInfoV143(board,
                    view.ConvertPointToView(new CGPoint(view.Bounds.Width / 2, view.Bounds.Height / 2), null), ++runs);
                int beforeEnter = enters, beforePreview = previews, beforeDrop = drops;
                Require(Send(view.Handle, new Selector("draggingEntered:").Handle, info.Handle) == (nuint)NSDragOperation.Copy,
                    "owned native DragEnter did not select Copy");
                Require(Send(view.Handle, new Selector("performDragOperation:").Handle, info.Handle) != 0,
                    "owned native Drop was rejected");
                Require(previews == beforePreview + 1 && enters == beforeEnter + 1 && drops == beforeDrop + 1,
                    "native delegate replay lost or duplicated routed events");
                board.ClearContents(); // Snapshot must survive loss of its source representations.
                Require(retained != null && retained.GetDataPresent(DataFormats.FileDrop) == (expectedFiles.Length != 0), "retired snapshot changed file availability");
                Require(retained!.GetData("text/uri-list", false) is byte[] copied && copied.SequenceEqual(expectedUris), "retired snapshot lost raw URIs");
                Require((string?)retained.GetData(DataFormats.Html) == "<b>保留🙂</b>", "retired snapshot lost HTML");
                result.Text = expectedFiles.Length == 0 ? "网页／空链接：0 个文件，链接数据已保留。" : $"文件：{expectedFiles.Length} 个，路径与文本已保留。";
                Console.WriteLine($"REPLAY {runs}: kind={currentKind}; files={expectedFiles.Length}; previews={previews}; enters={enters}; drops={drops}; retained=true");
            }
            finally { foreach (var item in items) item.Dispose(); board.ReleaseGlobally(); }
            Pump(window);
        }
        web.Click += (_, _) => Replay(0); files.Click += (_, _) => Replay(1);
        try
        {
            window.Show(); Pump(window); Replay(currentKind);
            if (seconds > 0)
            {
                window.Activate(); Pump(window); Require(editor.Focus(), "observer editor focus was rejected");
                int tabs = 0;
                window.PreviewKeyDown += (_, e) => { if (e.Key == Key.Tab) Console.WriteLine($"OBSERVE Tab {++tabs}: modifiers={e.KeyboardModifiers}; text={editor.Text}"); };
                Console.WriteLine($"OBSERVE READY: style={window.TitleBarStyle}; frame={Runtime.GetNSObject<NSView>(window.Handle)!.Window!.Frame}; result={result.Text}");
                var elapsed = Stopwatch.StartNew();
                while (!closed && elapsed.Elapsed < TimeSpan.FromSeconds(seconds)) Pump(window);
                Console.WriteLine($"OBSERVE COMPLETE: runs={runs}; tabs={tabs}; text={editor.Text}; closed={closed}");
            }
            Console.WriteLine($"PASS {index}: {window.TitleBarStyle}, native routed URI kind={currentKind}, snapshot retained");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine($"FAIL {index}: {e}"); return 1; }
        finally { if (!closed) window.Close(); }
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static void Pump(Window window)
    {
        NativeMethods.PlatformPollEvents(); Dispatcher.CurrentDispatcher.ProcessQueue(); window.UpdateLayout();
        NSRunLoop.Main.RunUntil(NSDate.FromTimeIntervalSinceNow(.01));
    }
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern nuint Send(nint receiver, nint selector, nint info);
}

[Register("JaliumOwnedDragInfoV143")]
internal sealed class JaliumOwnedDragInfoV143(NSPasteboard board, CGPoint location, nint sequence) : NSObject
{
    [Export("draggingPasteboard")] public NSPasteboard Pasteboard() => board;
    [Export("draggingLocation")] public CGPoint Location() => location;
    [Export("draggingSequenceNumber")] public nint SequenceNumber() => sequence;
    [Export("draggingSourceOperationMask")] public nuint SourceOperationMask() => (nuint)NSDragOperation.Copy;
    [Export("draggingSource")] public NSObject? Source() => null;
}

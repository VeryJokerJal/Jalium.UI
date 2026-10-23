using AppKit;
using CoreGraphics;
using Foundation;
using ObjCRuntime;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Documents;
using Jalium.UI.Hosting;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace Jalium.UI.MacOS;

// Uses the target's own NSView and the public Writing Tools delegate protocol.
// Independent AX clients exercise selection separately; no OS input is injected.
internal static class WindowOccurrenceChecks
{
    internal static int Run(string argument)
    {
        if (!int.TryParse(argument.Split('=').Last(), out int index) || (uint)index >= 12) return 2;
        string output = Environment.GetEnvironmentVariable("JALIUM_OCCURRENCE_OUTPUT")!;
        Directory.CreateDirectory(output);
        bool custom = index % 2 == 1, software = index >= 6; int kind = index % 6 / 2;
        var app = NSApplication.SharedApplication; app.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        using var host = new Delegate(); app.Delegate = host; app.FinishLaunching();
        using (var note = NSNotification.FromName(NSApplication.DidFinishLaunchingNotification, app)) host.DidFinishLaunching(note);
        RenderContext.GetOrCreateCurrent(software ? RenderBackend.Software : RenderBackend.Metal, forceReplace: true);
        Controls.Themes.ThemeManager.Initialize(Application.Current!);
        var list = new ListBox { Height = 148, SelectionMode = SelectionMode.Multiple };
        Name(list, "重复项目选择", "occurrence-list");
        var rich = new RichTextBox { Height = 124, FontSize = 18 };
        Name(rich, "段落格式与撤销", "occurrence-editor");
        var probe = new ProbeControl { Content = "双击检查区域", Height = 40, Focusable = true };
        Name(probe, "双击检查区域", "occurrence-mouse");
        var status = new TextBlock { Text = "检查准备中", FontSize = 15, TextWrapping = TextWrapping.Wrap };
        var content = new StackPanel { Margin = new Thickness(16), Spacing = 10 };
        content.Children.Add(new TextBlock { Text = "重复项目与编辑", FontSize = 22 });
        content.Children.Add(list); content.Children.Add(rich); content.Children.Add(probe); content.Children.Add(status);
        var subject = new Border { Width = 360, Height = 516, Child = content, CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1) };
        subject.SetResourceReference(Border.BackgroundProperty, "LayerFillColorDefaultBrush"); subject.SetResourceReference(Border.BorderBrushProperty, "ControlStrokeColorDefaultBrush");
        var anchor = new Button { Content = "展开检查区域", Width = 160, Height = 40 };
        var area = new Canvas { Height = 560 }; area.Children.Add(kind == 0 ? subject : anchor);
        Canvas.SetLeft(subject, 5); Canvas.SetTop(subject, 5); Canvas.SetLeft(anchor, 5); Canvas.SetTop(anchor, 5);
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 12 };
        panel.Children.Add(new TextBlock { Text = "窗口选择与格式", FontSize = 24 }); panel.Children.Add(area);
        var actions = new WrapPanel { ItemWidth = 120, ItemHeight = 46 };
        Button Action(string text, string id) { var button = new Button { Content = text, Width = 116, Height = 42 }; Name(button, text, id); actions.Children.Add(button); return button; }
        var insert = Action("前面插入", "occurrence-insert"); var remove = Action("移除选中项", "occurrence-remove");
        var single = Action("切换单选", "occurrence-single"); var multiple = Action("切换多选", "occurrence-multiple");
        var capture = Action("记录当前画面", "occurrence-capture"); var next = Action("下一组项目", "occurrence-next");
        panel.Children.Add(actions);
        var owner = new Window { Title = "Jalium Window Occurrence " + index, Width = kind == 0 ? 760 : 420, Height = 800,
            MinWidth = 420, MinHeight = 700, TitleBarStyle = custom ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native, Content = panel };
        owner.SetResourceReference(Window.BackgroundProperty, "SolidBackgroundFillColorBaseBrush"); Application.Current!.MainWindow = owner;
        Popup? popup = kind == 0 ? null : new Popup { Child = subject, PlacementTarget = anchor, Placement = PlacementMode.Bottom,
            PreferExternalWindow = kind == 2, ShouldConstrainToRootBounds = kind == 1, StaysOpen = true, PopupAnimation = PopupAnimation.None };
        ObservableCollection<object> source = new();
        var checks = new List<string>(); var failures = new List<string>(); var scenarios = new List<object>();
        int dataKind = -1, pendingKind = 0, phase = 0, captures = 0, nestedLoops = 0, tail = 0; bool running = false, done = false, nested = false;
        string mousePhase = ""; Window? surface = null; NSView? view = null; var watch = Stopwatch.StartNew();
        void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
        ListBoxAutomationPeer Peer() => (ListBoxAutomationPeer)list.GetAutomationPeer()!;
        ListBoxItemAutomationPeer[] Rows() => Peer().GetChildren().Cast<ListBoxItemAutomationPeer>().ToArray();
        int[] Selected() { var rows = Rows(); var selected = Peer().GetSelection(); return rows.Select((row, i) => (row, i)).Where(x => selected.Any(p => ReferenceEquals(p, x.row))).Select(x => x.i).ToArray(); }
        void Layout() { owner.UpdateLayout(); if (surface != null && surface != owner) surface.UpdateLayout(); }
        void Attempt(string name, Action action)
        {
            int start = checks.Count; Exception? error = null;
            try { action(); } catch (Exception failure) { error = failure; failures.Add("data=" + dataKind + " " + name + ": " + failure); }
            scenarios.Add(new { dataKind, name, passed = error is null, checks = checks.Skip(start).ToArray(), error = error?.ToString() });
        }
        void PrepareRows()
        {
            for (int i = 0; i < source.Count; i++)
                if (list.ItemContainerGenerator.ContainerFromIndex(i) is FrameworkElement row) Name(row, i < 2 ? "重复条目🙂" : "普通项目", "occurrence-row-" + i);
        }
        void Record()
        {
            PrepareRows();
            var state = new { processId = Environment.ProcessId, index, custom, software, kind, dataKind, phase, captures, pendingKind, running, closed = done,
                selected = list.SelectedIndex, selectedRows = Selected(), selectedCount = list.SelectedItems.Count, count = source.Count,
                realizedSelected = Enumerable.Range(0, source.Count).Select(i => list.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem row && row.IsSelected).ToArray(),
                checks, failures, scenarios, nestedLoops, appRunning = app.Running, appActive = app.Active,
                frontmostPID = NSWorkspace.SharedWorkspace.FrontmostApplication?.ProcessIdentifier, ownerBackend = owner.RenderTarget?.Backend.ToString(), surfaceBackend = surface?.RenderTarget?.Backend.ToString(),
                ownNSViewSDK = true, writingToolsDelegateSDK = true, appleIntelligenceService = false, physicalInput = false, desktopScreenshot = false };
            File.WriteAllText(Path.Combine(output, "state.tmp"), JsonSerializer.Serialize(state)); File.Move(Path.Combine(output, "state.tmp"), Path.Combine(output, "state.json"), true);
        }
        void Mouse(UIElement target, int clicks, bool up = false)
        {
            var point = target.TranslatePoint(new Point(target.RenderSize.Width / 2, target.RenderSize.Height / 2), surface);
            CGPoint location = view!.ConvertPointToView(new CGPoint(point.X, point.Y), null);
            using var e = NSEvent.MouseEvent(up ? NSEventType.LeftMouseUp : NSEventType.LeftMouseDown, location, 0,
                watch.Elapsed.TotalSeconds + 1, view.Window!.WindowNumber, null, 0, clicks, 1)!;
            if (up) view.MouseUp(e); else view.MouseDown(e);
        }
        void ReplaceMouse()
        {
            if (nested) return; nested = true; bool delivered = false; Exception? error = null;
            using var timer = NSTimer.CreateScheduledTimer(TimeSpan.FromMilliseconds(1), _ => { try { Mouse(probe, 1); } catch (Exception failure) { error = failure; } finally { delivered = true; } });
            var wait = Stopwatch.StartNew(); while (!delivered && wait.Elapsed.TotalSeconds < 2)
            { using var until = NSDate.FromTimeIntervalSinceNow(.01); NSRunLoop.Current.RunUntil(until); }
            Require(delivered && error == null, "new mouse packet delivered in a nested own AppKit loop" + (error == null ? "" : ": " + error)); nestedLoops++;
        }
        probe.Callback = e => { if (mousePhase.StartsWith("class", StringComparison.Ordinal) &&
            ((e.RoutedEvent == Control.PreviewMouseDoubleClickEvent) == mousePhase.EndsWith("preview", StringComparison.Ordinal))) ReplaceMouse(); };
        probe.PreviewMouseDoubleClick += (_, _) => { if (mousePhase == "instance-preview") ReplaceMouse(); };
        probe.MouseDoubleClick += (_, _) => { if (mousePhase == "instance-bubble") ReplaceMouse(); };
        probe.PreviewMouseDoubleClick += (_, _) => { if (mousePhase.EndsWith("preview", StringComparison.Ordinal) || mousePhase == "ordinary") tail++; };
        probe.MouseDoubleClick += (_, _) => { if (mousePhase.EndsWith("bubble", StringComparison.Ordinal) || mousePhase == "ordinary") tail++; };
        void DoubleClicks()
        {
            foreach (string name in new[] { "instance-preview", "instance-bubble", "class-preview", "class-bubble" })
                Attempt(name, () => { mousePhase = name; nested = false; tail = 0; Mouse(probe, 2); Require(nested && tail == 0, "retired synthetic double-click stops later handlers"); mousePhase = ""; Mouse(probe, 1, up: true); });
            Attempt("ordinary-double-click", () => { mousePhase = "ordinary"; tail = 0; Mouse(probe, 2); Require(tail == 2, "ordinary preview and bubble double-click still dispatch"); mousePhase = ""; Mouse(probe, 1, up: true); });
        }
        void RichProposal()
        {
            var paragraph = new Paragraph(new Run("旧段🙂")) { TextAlignment = TextAlignment.Left, FlowDirection = FlowDirection.LeftToRight };
            var document = new FlowDocument(); document.Blocks.Add(new Paragraph(new Run("前段"))); document.Blocks.Add(paragraph);
            rich.Document = document; Layout(); Require(rich.Focus(), "live rich editor receives focus");
            var coordinator = view!.WritingToolsCoordinator;
            Require(coordinator?.WeakDelegate != null, "focused native view exposes a Writing Tools coordinator and delegate");
            var client = Runtime.GetINativeObject<INSWritingToolsCoordinatorDelegate>(coordinator!.WeakDelegate!.Handle, false)!;
            NSWritingToolsCoordinatorContext? context = null; int completions = 0;
            client.RequestsContexts(coordinator, NSWritingToolsCoordinatorContextScope.FullDocument, values => { completions++; context = values.FirstOrDefault(); });
            string before = document.GetText(); Require(completions == 1 && context?.AttributedString.Value == before, "SDK context retains the full paragraph projection");
            using var style = new NSMutableParagraphStyle { Alignment = NSTextAlignment.Center, BaseWritingDirection = NSWritingDirection.RightToLeft };
            using var proposal = new NSAttributedString("新文🙂", new NSStringAttributes { ParagraphStyle = style, Font = NSFont.SystemFontOfSize(18) });
            NSAttributedString? accepted = null;
            client.ReplaceRange(coordinator, new NSRange((nint)"前段\n".Length, (nint)"旧段🙂".Length), context!, proposal,
                NSWritingToolsCoordinatorTextReplacementReason.Noninteractive, null!, value => accepted = value);
            Require(accepted?.Value == "新文🙂" && document.GetText() == "前段\n新文🙂\n", "actual SDK formatted replacement changes only the requested paragraph");
            var changed = document.EnumerateParagraphs().Last();
            Require(changed.TextAlignment == TextAlignment.Center && changed.FlowDirection == FlowDirection.RightToLeft, "first incoming paragraph keeps alignment and writing direction; actual=" + changed.TextAlignment + "/" + changed.FlowDirection + "; native=" + (long)style.Alignment + "/" + (long)style.BaseWritingDirection + "; rows=" + string.Join("|", document.EnumerateParagraphs().Select(p => p.TextAlignment + "/" + p.FlowDirection + ":" + string.Concat(p.Inlines.OfType<Run>().Select(r => r.Text)))));
            Require(rich.Undo() && document.GetText() == before && ReferenceEquals(document.Blocks[1], paragraph) && paragraph.TextAlignment == TextAlignment.Left && paragraph.FlowDirection == FlowDirection.LeftToRight,
                "one Undo restores original paragraph identity and formatting");
            Require(rich.Redo(), "one Redo restores the formatted proposal"); changed = document.EnumerateParagraphs().Last();
            Require(changed.TextAlignment == TextAlignment.Center && changed.FlowDirection == FlowDirection.RightToLeft, "Redo retains the imported paragraph formatting");
        }
        void SwitchData(int value)
        {
            dataKind = value; source = new ObservableCollection<object>();
            Attempt("physical-backend", () => Require(owner.RenderTarget?.Backend == (software ? RenderBackend.Software : RenderBackend.Metal) && surface.RenderTarget.Backend == (software ? RenderBackend.Software : RenderBackend.Metal) && (surface != owner) == (kind == 2), "actual owner and presenting surface match the requested configuration"));
            object one = value == 2 ? new EqualContainer(7, "重复条目🙂") : new Entry(7, "重复条目🙂");
            object two = value == 1 ? one : value == 2 ? new EqualContainer(7, "重复条目🙂") : new Entry(7, "重复条目🙂");
            source.Add(one); source.Add(two); source.Add(value == 2 ? new EqualContainer(8, "普通项目") : new Entry(8, "普通项目"));
            list.ItemsSource = source; Layout();
            Attempt("index-and-realized-row", () => { list.SelectionMode = SelectionMode.Single; list.SelectedIndex = 1; var rows = Rows();
                Require(rows.Length == 3 && !ReferenceEquals(rows[0], rows[1]), "equal data occurrences have distinct logical peers");
                Require(Selected().SequenceEqual(new[] { 1 }), "selection by index returns only the second occurrence");
                Require(list.ItemContainerGenerator.ContainerFromIndex(0) is ListBoxItem first && !first.IsSelected && list.ItemContainerGenerator.ContainerFromIndex(1) is ListBoxItem second && second.IsSelected,
                    "realized highlights select only the requested row"); });
            Attempt("multiple-occurrences", () => { list.SelectionMode = SelectionMode.Multiple; list.UnselectAll(); var rows = Rows(); rows[1].Select(); rows[0].AddToSelection();
                Require(list.SelectedItems.Count == 2 && Selected().SequenceEqual(new[] { 0, 1 }), "multiple selection retains both equal occurrences"); rows[0].RemoveFromSelection();
                Require(Selected().SequenceEqual(new[] { 1 }), "removing one equal row preserves its sibling"); });
            Attempt("native-arrow", () => { list.SelectionMode = SelectionMode.Single; list.SelectedIndex = 0; Require(list.Focus(), "list accepts focus before SDK navigation");
                using var key = NSEvent.KeyEvent(NSEventType.KeyDown, CGPoint.Empty, 0, watch.Elapsed.TotalSeconds + 1, view!.Window!.WindowNumber, null, "\uf701", "\uf701", false, 125)!;
                view.KeyDown(key); Require(list.SelectedIndex == 1 && Selected().SequenceEqual(new[] { 1 }), "actual NSView arrow advances to the second equal row"); });
            Attempt("native-pointer", () => { list.SelectionMode = SelectionMode.Single; list.SelectedIndex = 0;
                var row = (UIElement)list.ItemContainerGenerator.ContainerFromIndex(1)!; Mouse(row, 1); Mouse(row, 1, up: true);
                Require(list.SelectedIndex == 1 && Selected().SequenceEqual(new[] { 1 }), "actual NSView click selects the row under the pointer"); });
            DoubleClicks(); Attempt("formatted-sdk-proposal", RichProposal);
            mousePhase = ""; list.SelectionMode = SelectionMode.Multiple; list.SelectedIndex = 1; Layout(); phase++;
            status.Text = "选择第二项，核对同名项目的选区。"; next.IsEnabled = value < 2; PrepareRows();
        }
        void Capture()
        {
            void Save(string name, Window window)
            {
                var image = MacOSWritingToolsBitmapCapture.Capture((int)Math.Ceiling(window.ActualWidth * window.DpiScale), (int)Math.Ceiling(window.ActualHeight * window.DpiScale), window.DpiScale,
                    drawing => { drawing.DrawRectangle(window.Background, null, new Rect(0, 0, window.ActualWidth, window.ActualHeight)); window.Render(drawing); }, () => window.Handle != 0)
                    ?? throw new InvalidOperationException("backend readback unavailable");
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image)); using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
            }
            Save("kind-" + dataKind + "-source", surface!); if (surface != owner) Save("kind-" + dataKind + "-owner", owner); captures++; Record();
        }
        insert.Click += (_, _) => { source.Insert(0, dataKind == 2 ? new EqualContainer(9, "插入项目") : new Entry(9, "插入项目")); Layout(); phase++; Record(); };
        remove.Click += (_, _) => { if ((uint)list.SelectedIndex < (uint)source.Count) source.RemoveAt(list.SelectedIndex); Layout(); phase++; Record(); };
        single.Click += (_, _) => { list.SelectionMode = SelectionMode.Single; phase++; Record(); };
        multiple.Click += (_, _) => { list.SelectionMode = SelectionMode.Multiple; phase++; Record(); };
        capture.Click += (_, _) => Capture(); next.Click += (_, _) => { if (dataKind < 2) pendingKind = dataKind + 1; };
        owner.Show(); Layout(); if (popup != null) popup.IsOpen = true;
        using var pulse = NSTimer.CreateRepeatingScheduledTimer(.03, _ =>
        {
            if (done || running) return;
            try
            {
                if (File.Exists(Path.Combine(output, "finish.signal")) || watch.Elapsed.TotalSeconds > 120)
                {
                    if (watch.Elapsed.TotalSeconds > 120) failures.Add("host watchdog expired");
                    done = true; if (popup != null) popup.IsOpen = false; owner.Close(); Record();
                    File.WriteAllText(Path.Combine(output, "host-result.json"), File.ReadAllText(Path.Combine(output, "state.json")));
                    app.Stop(app); using var wake = NSEvent.OtherEvent(NSEventType.ApplicationDefined, default, 0, 0, 0, null, 0, 0, 0); app.PostEvent(wake, false); return;
                }
                surface = (Window?)typeof(Window).GetMethod("GetMacOSPhysicalWindow", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [list]);
                if (surface?.RenderTarget == null || list.ActualWidth == 0) return;
                view = Runtime.GetNSObject<NSView>(surface.Handle); if (view?.Window == null) return;
                if (pendingKind >= 0)
                {
                    running = true; int target = pendingKind; pendingKind = -1;
                    SwitchData(target); running = false;
                }
                Record();
            }
            catch (Exception failure) { failures.Add(failure.ToString()); running = false; Record(); }
        });
        try { app.Run(); return failures.Count == 0 ? 0 : 1; }
        finally { pulse.Invalidate(); if (popup != null) popup.IsOpen = false; owner.Close(); using var note = NSNotification.FromName(NSApplication.WillTerminateNotification, app); host.WillTerminate(note); app.Delegate = null!; }
    }
    private static void Name(DependencyObject element, string text, string id) { AutomationProperties.SetName(element, text); AutomationProperties.SetAutomationId(element, id); }
    private sealed class Entry(int code, string caption)
    { internal int Code => code; public override string ToString() => caption; public override bool Equals(object? value) => value is Entry entry && entry.Code == code; public override int GetHashCode() => code; }
    private sealed class EqualContainer : ListBoxItem
    {
        private readonly int code;
        internal EqualContainer(int value, string caption) { code = value; Content = caption; }
        public override bool Equals(object? value) => value is EqualContainer row && row.code == code;
        public override int GetHashCode() => code;
    }
    private sealed class ProbeControl : ContentControl
    { internal Action<MouseButtonEventArgs>? Callback; protected override void OnMouseDoubleClick(MouseButtonEventArgs e) { base.OnMouseDoubleClick(e); Callback?.Invoke(e); } protected override void OnPreviewMouseDoubleClick(MouseButtonEventArgs e) { base.OnPreviewMouseDoubleClick(e); Callback?.Invoke(e); } }
    private sealed class Delegate : JaliumMacApplicationDelegate
    {
        private bool started; public override void DidFinishLaunching(NSNotification note) { if (started) return; started = true; base.DidFinishLaunching(note); }
        protected override JaliumApp CreateHostedApp() => AppBuilder.CreateBuilder(new AppBuilderSettings { DisableDefaults = true }).Build().UseApplication(new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown });
    }
}

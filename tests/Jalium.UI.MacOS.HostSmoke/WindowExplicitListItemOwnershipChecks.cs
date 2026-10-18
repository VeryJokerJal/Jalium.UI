using AppKit;
using Foundation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Text.Json;

namespace Jalium.UI.MacOS;

internal static partial class WindowSelectionNotificationChecks
{
    internal static int RunExplicitOwnershipCase(string argument)
    {
        if (!int.TryParse(argument.Split('=').Last(), out int index) || (uint)index >= 8) return 2;
        string output = Environment.GetEnvironmentVariable("JALIUM_SELECTION_OUTPUT")
            ?? throw new InvalidOperationException("JALIUM_SELECTION_OUTPUT is required.");
        Directory.CreateDirectory(output);
        var native = NSApplication.SharedApplication; native.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        using var host = new ValidationDelegate(); native.Delegate = host; native.FinishLaunching();
        using (var note = NSNotification.FromName(NSApplication.DidFinishLaunchingNotification, native)) host.DidFinishLaunching(note);
        var backend = index % 4 < 2 ? RenderBackend.Metal : RenderBackend.Software;
        var context = RenderContext.GetOrCreateCurrent(backend, forceReplace: true);
        context.DefaultRenderingEngine = RenderingEngine.Impeller;
        bool custom = index % 2 == 1, direct = index >= 4;
        var list = new ListBox { Height = 150, SelectionMode = SelectionMode.Multiple };
        var other = new ListBox { Height = 78, SelectionMode = SelectionMode.Multiple };
        Name(list, "当前列表", "ownership-list"); Name(other, "复用列表", "ownership-other");
        var editor = new TextBox { Text = "中文🙂，继续编辑", Height = 40 };
        Name(editor, "编辑内容", "ownership-editor");
        var source = new OwnershipSource(); var originalSource = source;
        var retired = new List<(ListBoxItem Row, ListBoxItemAutomationPeer Direct, ItemAutomationPeer Logical)>();
        int phase = 0, checkedPhase = -1; bool closed = false, timedOut = false, moved = false, reused = false;
        bool detached = true, logicalRetired = true, oldActionsPreserved = true, editingPreserved = true;
        var errors = new List<string>(); var checks = new List<string>(); var watch = Stopwatch.StartNew();
        ListBoxItem[] Rows() => list.Items.Cast<ListBoxItem>().ToArray();
        ListBoxItem[] Fresh() => [new() { Content = "保留项目", MinHeight = 38 }, new() { Content = "中文🙂", MinHeight = 38 }, new() { Content = "末项", MinHeight = 38 }];
        void Identify()
        { var rows = Rows(); for (int i = 0; i < rows.Length; i++) Name(rows[i], rows[i].Content!.ToString()!, "ownership-row-" + i); }
        void Install(ListBoxItem[] rows)
        {
            if (direct && phase < 3)
            { list.Items.Clear(); foreach (var row in rows) list.Items.Add(row); }
            else
            {
                if (list.ItemsSource == null) list.Items.Clear();
                source = new OwnershipSource(); foreach (var row in rows) source.Add(row); list.ItemsSource = source;
            }
            Identify();
        }
        if (direct) Install(Fresh());
        else { foreach (var row in Fresh()) source.Add(row); list.ItemsSource = source; Identify(); }
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 10 };
        TextBlock Text(string value, double size)
        { var label = new TextBlock { Text = value, FontSize = size, TextWrapping = TextWrapping.Wrap }; label.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush"); return label; }
        panel.Children.Add(Text("列表项目归属", 24));
        panel.Children.Add(Text("更新列表后，旧项目不再操作当前选择。", 15)); panel.Children.Add(list);
        panel.Children.Add(Text("复用到另一列表", 15)); panel.Children.Add(other); panel.Children.Add(editor);
        var change = Button("变更列表", "ownership-change"); var check = Button("检查旧项目", "ownership-check");
        var move = Button("移动当前项", "ownership-move"); var reuse = Button("复用旧项目", "ownership-reuse");
        var finish = Button("结束检查", "ownership-finish");
        var actions = new WrapPanel { HorizontalSpacing = 8, VerticalSpacing = 8 };
        foreach (var button in new[] { change, check, move, reuse, finish })
        { button.MinWidth = 112; button.HorizontalAlignment = HorizontalAlignment.Left; actions.Children.Add(button); }
        panel.Children.Add(actions);
        var owner = new Window { Title = "Jalium Explicit Item Ownership " + index,
            TitleBarStyle = custom ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native,
            Width = custom ? 420 : 760, Height = 700, MinWidth = 400, MinHeight = 620,
            Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }, WindowStartupLocation = WindowStartupLocation.CenterScreen };
        owner.SetResourceReference(Window.BackgroundProperty, "WindowBackground"); Application.Current!.MainWindow = owner;
        void Require(bool value, string message)
        { if (!value) throw new InvalidOperationException(message); checks.Add(message); }
        void FocusEditor()
        {
            Require(editor.Focus(), "current editor accepts programmatic focus");
            Require(ReferenceEquals(Keyboard.FocusedElement, editor), "logical editor focus is current");
            var view = ObjCRuntime.Runtime.GetNSObject<NSView>(owner.Handle);
            Require(view != null && view.Window?.FirstResponder?.Handle == view.Handle, "own content view is the native first responder");
        }
        void Capture(string name)
        {
            if (index is not (0 or 3 or 4 or 7)) return;
            owner.UpdateLayout(); double dpi = owner.DpiScale;
            var bitmap = MacOSWritingToolsBitmapCapture.Capture((int)Math.Ceiling(owner.ActualWidth * dpi),
                (int)Math.Ceiling(owner.ActualHeight * dpi), dpi,
                drawing => { drawing.DrawRectangle(owner.Background, null, new Rect(0, 0, owner.ActualWidth, owner.ActualHeight)); owner.Render(drawing); }, () => owner.Handle != 0)
                ?? throw new InvalidOperationException("Own backend readback failed.");
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
        }
        void Record()
        {
            var state = new { index, custom, direct, phase, checkedPhase, closed, timedOut, moved, reused,
                detached, logicalRetired, oldActionsPreserved, editingPreserved, retired = retired.Count,
                selectedIndex = list.SelectedIndex, otherSelectedIndex = other.SelectedIndex,
                editorText = editor.Text, editorSelectionStart = editor.SelectionStart, editorSelectionLength = editor.SelectionLength,
                backend = context.Backend.ToString(), expectedBackend = backend.ToString(), width = owner.ActualWidth,
                height = owner.ActualHeight, checks = checks.ToArray(), errors = errors.ToArray(),
                processId = Environment.ProcessId, appRunning = native.Running, appActive = native.Active,
                frontmostPID = NSWorkspace.SharedWorkspace.FrontmostApplication?.ProcessIdentifier,
                physicalInput = false, externalAX = true, desktopScreenshot = false, offscreenRendering = true };
            File.WriteAllText(Path.Combine(output, "state.tmp"), JsonSerializer.Serialize(state));
            File.Move(Path.Combine(output, "state.tmp"), Path.Combine(output, "state.json"), true);
        }
        void Perform(Action action)
        { try { action(); } catch (Exception error) { errors.Add(error.ToString()); } Record(); }
        change.Click += (_, _) => Perform(() =>
        {
            Require(phase < 4, "four collection operations are exercised in order");
            var old = Rows()[1]; var parent = (ListBoxAutomationPeer)list.GetAutomationPeer()!;
            var logical = parent.GetChildren().OfType<ItemAutomationPeer>().Single(x => ReferenceEquals(x.Item, old));
            retired.Add((old, (ListBoxItemAutomationPeer)old.GetAutomationPeer()!, logical));
            if (phase == 0) { if (direct) list.Items.Clear(); else source.Clear(); Install(Fresh()); }
            else if (phase == 1) { var fresh = Fresh()[1]; if (direct) list.Items[1] = fresh; else source[1] = fresh; Identify(); }
            else if (phase == 2)
            {
                var rows = Rows(); rows[1] = Fresh()[1];
                if (direct) { list.Items.Clear(); foreach (var row in rows) list.Items.Add(row); }
                else source.Reset(rows);
                Identify();
            }
            else Install(Fresh());
            phase++; ((ListBoxItemAutomationPeer)Rows()[0].GetAutomationPeer()!).Select(); FocusEditor();
        });
        check.Click += (_, _) => Perform(() =>
        {
            detached = retired.All(x => x.Direct.SelectionContainer == null);
            foreach (var old in retired)
            {
                try { ((Jalium.UI.Automation.Provider.ISelectionItemProvider)old.Logical.GetPattern(PatternInterface.SelectionItem)!).Select(); logicalRetired = false; }
                catch (InvalidOperationException) { }
                old.Direct.Select(); old.Direct.AddToSelection(); old.Direct.RemoveFromSelection();
                old.Row.RaiseEvent(new MouseButtonEventArgs(UIElement.MouseDownEvent, new Point(2, 2), MouseButton.Left,
                    MouseButtonState.Pressed, 1, MouseButtonState.Pressed, MouseButtonState.Released,
                    MouseButtonState.Released, MouseButtonState.Released, MouseButtonState.Released, ModifierKeys.None, 0));
            }
            oldActionsPreserved &= list.SelectedIndex == 0;
            editingPreserved &= editor.Text == "中文🙂，继续编辑" && editor.SelectionStart == 2 && editor.SelectionLength == 2;
            checkedPhase = phase; FocusEditor();
            if (phase == 4) owner.Width = 420;
        });
        move.Click += (_, _) => Perform(() =>
        {
            Require(phase == 4, "move follows source rebinding");
            var row = Rows()[1]; var peer = row.GetAutomationPeer(); source.Move(1, 2); Identify();
            Require(ReferenceEquals(peer, row.GetAutomationPeer()), "moving preserves the direct item peer");
            Require(ReferenceEquals(((ListBoxItemAutomationPeer)peer!).SelectionContainer, list.GetAutomationPeer()), "moving preserves the selection container");
            moved = true; FocusEditor(); Capture("retired");
        });
        reuse.Click += (_, _) => Perform(() =>
        {
            Require(moved, "reuse follows movement of the current item");
            var old = retired[0]; Name(old.Row, "复用的中文🙂", "ownership-reused"); other.Items.Add(old.Row);
            originalSource.Clear(); old.Direct.Select();
            reused = ReferenceEquals(old.Direct.SelectionContainer, other.GetAutomationPeer()) && other.SelectedIndex == 0;
            Require(reused, "reused direct provider acts on its new list");
            Require(ReferenceEquals(old.Direct.GetParent(), other.GetAutomationPeer()), "reused direct peer reports its current list as parent");
            phase = 5; FocusEditor(); Capture("reused");
        });
        finish.Click += (_, _) => owner.Close();
        owner.Closed += (_, _) => { closed = true; Record(); native.BeginInvokeOnMainThread(() => native.Terminate(native)); };
        owner.Show(); owner.UpdateLayout(); editor.Focus(); editor.Select(2, 2);
        Perform(() => { Require(context.Backend == backend, "actual native backend matches the requested case"); FocusEditor(); Capture("initial"); });
        using var timer = NSTimer.CreateRepeatingScheduledTimer(TimeSpan.FromMilliseconds(40), _ =>
        {
            if (!closed && (watch.Elapsed.TotalSeconds > 90 || File.Exists(Path.Combine(output, "finish.signal"))))
            { timedOut = watch.Elapsed.TotalSeconds > 90; owner.Close(); }
            else Record();
        });
        native.Run(); return 0;
    }

    private sealed class OwnershipSource : ObservableCollection<object>
    {
        internal void Reset(IEnumerable<ListBoxItem> current)
        { Items.Clear(); foreach (var row in current) Items.Add(row); OnPropertyChanged(new("Count")); OnPropertyChanged(new("Item[]")); OnCollectionChanged(new(NotifyCollectionChangedAction.Reset)); }
    }
}

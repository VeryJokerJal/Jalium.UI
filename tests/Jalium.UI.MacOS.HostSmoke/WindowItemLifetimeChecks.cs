using AppKit;
using Foundation;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Jalium.UI.MacOS;

internal static partial class WindowSelectionNotificationChecks
{
    internal static int RunItemLifetimeCase(string argument)
    {
        const string prefix = "--window-item-lifetime-case=";
        if (!int.TryParse(argument.AsSpan(prefix.Length), out int index) || (uint)index >= 6) return 2;
        bool custom = index % 2 == 1; int kind = index / 2;
        string output = Environment.GetEnvironmentVariable("JALIUM_SELECTION_OUTPUT")
            ?? throw new InvalidOperationException("JALIUM_SELECTION_OUTPUT is required.");
        Directory.CreateDirectory(output); var watch = Stopwatch.StartNew(); var app = NSApplication.SharedApplication;
        app.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        using var host = new ValidationDelegate(); app.Delegate = host; app.FinishLaunching();
        using (var launch = NSNotification.FromName(NSApplication.DidFinishLaunchingNotification, app)) host.DidFinishLaunching(launch);
        RenderContext.GetOrCreateCurrent(RenderBackend.Software, forceReplace: true);
        Jalium.UI.Controls.Themes.ThemeManager.Initialize(Application.Current!);
        var entries = new[] { new LifetimeEntry(0,"保留项目"),new LifetimeEntry(1,"中文🙂"),new LifetimeEntry(2,"末项") };
        var source = new ObservableCollection<object>(entries.Cast<object>());
        ItemsControl items = kind switch { 0 => new ItemsControl { Height = 180 }, 1 => new ListBox { Height = 180 },
            _ => new ComboBox { Width = 360, Height = 40, DisplayMemberPath = nameof(LifetimeEntry.Caption), MaxDropDownHeight = 180 } };
        Name(items,"动态项目生命周期","lifetime-root");
        var invocations = new List<int>();
        if (kind != 2)
        {
            var template = new DataTemplate();
            template.SetVisualTree(() =>
            {
                var button = new Button { Height = 36, MinWidth = 260, Margin = new Thickness(2) };
                button.SetBinding(ContentControl.ContentProperty,new Binding(nameof(LifetimeEntry.Caption)));
                button.Click += (_, _) => { if (button.DataContext is LifetimeEntry entry) invocations.Add(entry.Id); };
                return button;
            });
            items.ItemTemplate = template;
        }
        items.ItemsSource = source;
        Window? owner = null; ItemAutomationPeer? retained = null; ISelectionItemProvider? retainedSelection = null;
        WeakReference? probeData = null, probePeer = null; var rejected = new List<string>();
        int phase = 0, captures = 0; bool closed = false, timedOut = false, narrowed = false, metadataRetired = false;
        bool probeCreated = false;
        ItemsControlAutomationPeer Parent() => (ItemsControlAutomationPeer)items.GetAutomationPeer()!;
        int Selected() => items is Jalium.UI.Controls.Primitives.Selector selector && selector.SelectedItem is LifetimeEntry entry ? entry.Id : -1;
        void ChooseSurvivor() { if (items is Jalium.UI.Controls.Primitives.Selector selector) selector.SelectedItem = entries[0]; }
        T? Find<T>(Visual root) where T : Visual
        {
            if (root is T match) return match;
            for (int i = 0; i < root.VisualChildrenCount; i++)
                if (root.GetVisualChild(i) is { } child && Find<T>(child) is { } found) return found;
            return null;
        }
        void PrepareRows()
        {
            for (int i = 0; i < source.Count; i++)
            {
                if (items.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement row || source[i] is not LifetimeEntry entry) continue;
                AutomationProperties.SetAutomationId(row,"lifetime-row-" + entry.Id);
                if (kind != 2 && Find<Button>(row) is { } command) AutomationProperties.SetAutomationId(command,"lifetime-command-" + entry.Id);
            }
        }
        void Capture()
        {
            retained = Parent().GetChildren().OfType<ItemAutomationPeer>().Single(x => ReferenceEquals(x.Item,entries[1]));
            retainedSelection = retained.GetPattern(PatternInterface.SelectionItem) as ISelectionItemProvider; captures++;
        }
        void Record()
        {
            PrepareRows();
            var state = new { processId = Environment.ProcessId, index, custom, kind, phase, captures,
                ids = source.Cast<LifetimeEntry>().Select(x => x.Id).ToArray(), selected = Selected(),
                logicalNames = Parent().GetChildren().Select(x => x.GetName()).ToArray(),
                rejected = rejected.ToArray(), metadataRetired, probeCreated, probeDataAlive = probeData?.IsAlive,
                probePeerAlive = probePeer?.IsAlive, invocations = invocations.ToArray(),
                open = items is ComboBox combo && combo.IsDropDownOpen, closed, timedOut, narrowed,
                width = owner?.ActualWidth, height = owner?.ActualHeight, appRunning = app.Running, appActive = app.Active,
                frontmostPID = NSWorkspace.SharedWorkspace.FrontmostApplication?.ProcessIdentifier,
                physicalInput = false, externalAX = true };
            string temporary = Path.Combine(output,"state.tmp"); File.WriteAllText(temporary,JsonSerializer.Serialize(state));
            File.Move(temporary,Path.Combine(output,"state.json"),true);
        }
        var remove = Button("移除后插回","lifetime-reinsert"); var rebind = Button("重绑定源","lifetime-rebind");
        var capture = Button("捕获当前引用","lifetime-capture"); var check = Button("检查旧引用","lifetime-check");
        var probe = Button("生成并移除探针","lifetime-probe"); var collect = Button("收集已移除对象","lifetime-collect");
        var open = Button("展开选择","lifetime-open"); var narrow = Button("窄窗口","lifetime-narrow");
        var finish = Button("结束检查","lifetime-finish");
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 10 };
        panel.Children.Add(Label("动态项目与旧引用",24)); panel.Children.Add(Label("更换数据后，旧项目引用应停止操作，新项目继续可用。",15)); panel.Children.Add(items);
        var actions = new WrapPanel { ItemWidth = 160, ItemHeight = 48 };
        foreach (var action in new[] { remove, rebind, capture, check, probe, collect, open, narrow, finish })
        { action.Width = 156; action.Margin = new Thickness(2); actions.Children.Add(action); }
        panel.Children.Add(actions);
        owner = new Window { Title = "Jalium Item Lifetime " + index, Width = 700, Height = 660, MinWidth = 540, MinHeight = 540,
            TitleBarStyle = custom ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native,
            Background = new SolidColorBrush(Color.FromRgb(0x1c,0x20,0x26)),
            Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } };
        Application.Current!.MainWindow = owner;
        remove.Click += (_, _) => { source.Remove(entries[1]); source.Add(entries[1]); ChooseSurvivor(); phase++; owner.UpdateLayout(); Record(); };
        rebind.Click += (_, _) => { source = new ObservableCollection<object>(entries.Cast<object>()); items.ItemsSource = source;
            ChooseSurvivor(); phase++; owner.UpdateLayout(); Record(); };
        capture.Click += (_, _) => { Capture(); Record(); };
        check.Click += (_, _) =>
        {
            metadataRetired = retained is not null && !retained.IsEnabled() && retained.IsOffscreen() && retained.GetBoundingRectangle().IsEmpty;
            if (retainedSelection is not null)
                foreach (var action in new Action[] { retainedSelection.Select, retainedSelection.AddToSelection, retainedSelection.RemoveFromSelection })
                    try { action(); rejected.Add("accepted"); } catch (InvalidOperationException) { rejected.Add("retired"); }
            Record();
        };
        probe.Click += (_, _) => { (probeData,probePeer) = CreateLifetimeProbe(items,source,owner); probeCreated = true; Record(); };
        collect.Click += (_, _) => { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); Record(); };
        open.Click += (_, _) => { if (items is ComboBox combo) combo.IsDropDownOpen = true; owner.UpdateLayout(); Record(); };
        narrow.Click += (_, _) => { owner.Width = 540; owner.Height = 540; owner.UpdateLayout(); narrowed = true; Record(); };
        finish.Click += (_, _) => owner.Close(); owner.Closed += (_, _) => { closed = true; Record(); };
        owner.Show(); ChooseSurvivor(); Capture(); Record();
        using var pulse = NSTimer.CreateRepeatingScheduledTimer(.1, _ =>
        {
            Record(); if (watch.Elapsed.TotalSeconds > 50) timedOut = true;
            if (!closed && !timedOut && !File.Exists(Path.Combine(output,"finish.signal"))) return;
            if (owner.Handle != 0) owner.Close(); app.Stop(app);
            using var wake = NSEvent.OtherEvent(NSEventType.ApplicationDefined,default,0,0,0,null,0,0,0); app.PostEvent(wake,false);
        });
        try { app.Run(); Record(); return timedOut ? 1 : 0; }
        finally
        {
            pulse.Invalidate(); if (owner.Handle != 0) owner.Close();
            using var termination = NSNotification.FromName(NSApplication.WillTerminateNotification,app);
            host.WillTerminate(termination); app.Delegate = null!;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Data, WeakReference Peer) CreateLifetimeProbe(ItemsControl owner, ObservableCollection<object> source, Window window)
    {
        var item = new LifetimeEntry(999,"临时数据"); source.Add(item); window.UpdateLayout();
        var peer = ((ItemsControlAutomationPeer)owner.GetAutomationPeer()!).GetChildren().OfType<ItemAutomationPeer>()
            .Single(x => ReferenceEquals(x.Item,item));
        var result = (new WeakReference(item),new WeakReference(peer)); source.Remove(item); window.UpdateLayout(); return result;
    }
    private sealed record LifetimeEntry(int Id,string Caption) { public override string ToString() => Caption; }
}

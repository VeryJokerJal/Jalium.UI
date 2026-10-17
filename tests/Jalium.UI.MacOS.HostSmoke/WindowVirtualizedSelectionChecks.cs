using AppKit;
using Foundation;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Jalium.UI.MacOS;

internal static partial class WindowSelectionNotificationChecks
{
    internal static int RunVirtualizedCase(string argument)
    {
        const string prefix = "--window-virtualized-selection-case=";
        if (!int.TryParse(argument.AsSpan(prefix.Length), out int index) || (uint)index >= 6) return 2;
        bool custom = index % 2 == 1;
        SelectionMode mode = (index / 2) switch { 0 => SelectionMode.Single, 1 => SelectionMode.Multiple, _ => SelectionMode.Extended };
        string output = Environment.GetEnvironmentVariable("JALIUM_SELECTION_OUTPUT")
            ?? throw new InvalidOperationException("JALIUM_SELECTION_OUTPUT is required.");
        Directory.CreateDirectory(output);
        var watch = Stopwatch.StartNew(); var app = NSApplication.SharedApplication;
        app.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        using var host = new ValidationDelegate(); app.Delegate = host; app.FinishLaunching();
        using (var launch = NSNotification.FromName(NSApplication.DidFinishLaunchingNotification, app)) host.DidFinishLaunching(launch);
        RenderContext.GetOrCreateCurrent(RenderBackend.Software, forceReplace: true);
        Jalium.UI.Controls.Themes.ThemeManager.Initialize(Application.Current!);
        var list = new VirtualList { SelectionMode = mode, Height = 180 };
        Name(list, "虚拟化选择", "virtual-list");
        for (int i = 0; i < 500; i++) list.Items.Add("item-" + i);
        var middle = Button("中段", "virtual-middle"); var bottom = Button("底部", "virtual-bottom");
        var top = Button("顶部", "virtual-top"); var clear = Button("清空选择", "virtual-clear");
        var finish = Button("结束检查", "virtual-finish");
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 8 };
        panel.Children.Add(Label("虚拟化选择与引用", 24)); panel.Children.Add(list);
        foreach (var button in new[] { middle, bottom, top, clear, finish }) panel.Children.Add(button);
        var owner = new Window { Title = "Jalium Virtualized Selection " + index,
            TitleBarStyle = custom ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native,
            Width = 640, Height = 640, MinWidth = 520, MinHeight = 580,
            Background = new SolidColorBrush(Color.FromRgb(0x1c, 0x20, 0x26)), Content = panel };
        Application.Current!.MainWindow = owner;
        bool closed = false, timedOut = false, finishing = false; int phase = 0;
        VirtualRow? first = null;
        void Record()
        {
            var rows = list.Realized.OfType<VirtualRow>().Select(row => new
            { index = list.ItemContainerGenerator.IndexFromContainer(row), physical = RuntimeHelpers.GetHashCode(row) }).ToArray();
            var value = new { processId = Environment.ProcessId, index, custom, mode = mode.ToString(), phase,
                selected = list.SelectedItems.Cast<string>().Select(x => int.Parse(x.AsSpan(5))).Order().ToArray(),
                firstPhysical = first == null ? 0 : RuntimeHelpers.GetHashCode(first),
                firstIndex = first == null ? -1 : list.ItemContainerGenerator.IndexFromContainer(first),
                rows, invocations = list.Invocations.ToArray(), closed, timedOut,
                appRunning = app.Running, physicalInput = false, externalAX = true };
            string temporary = Path.Combine(output, "state.tmp"); File.WriteAllText(temporary, JsonSerializer.Serialize(value));
            File.Move(temporary, Path.Combine(output, "state.json"), true);
        }
        void Scroll(int item, int next) { list.ScrollIntoView("item-" + item); owner.UpdateLayout(); phase = next; Record(); }
        middle.Click += (_, _) => Scroll(250, 1); bottom.Click += (_, _) => Scroll(499, 2);
        top.Click += (_, _) => Scroll(0, 3); clear.Click += (_, _) => { list.SelectedItems.Clear(); Record(); };
        finish.Click += (_, _) => { finishing = true; owner.Close(); Record(); };
        owner.Closed += (_, _) => { closed = true; Record(); };
        owner.Show(); owner.UpdateLayout(); first = (VirtualRow)list.ItemContainerGenerator.ContainerFromIndex(0)!; Record();
        using var pulse = NSTimer.CreateRepeatingScheduledTimer(.1, _ =>
        {
            Record(); if (watch.Elapsed.TotalSeconds > 45) timedOut = true;
            if (!finishing && !timedOut && !File.Exists(Path.Combine(output, "finish.signal"))) return;
            if (owner.Handle != 0) owner.Close(); app.Stop(app);
            using var wake = NSEvent.OtherEvent(NSEventType.ApplicationDefined, default, 0, 0, 0, null, 0, 0, 0); app.PostEvent(wake, false);
        });
        try { app.Run(); Record(); return timedOut ? 1 : 0; }
        finally
        {
            pulse.Invalidate(); if (owner.Handle != 0) owner.Close();
            using var termination = NSNotification.FromName(NSApplication.WillTerminateNotification, app);
            host.WillTerminate(termination); app.Delegate = null!;
        }
    }
    // A custom provider exposes the row's interactive template, so the check
    // covers descendants as well as the framework's standard row peer.
    private sealed class VirtualRow : ListBoxItem
    {
        internal readonly Button Action = new() { MinHeight = 30 };
        protected override AutomationPeer OnCreateAutomationPeer() => new VirtualPeer(this);
        private sealed class VirtualPeer(VirtualRow row) : ListBoxItemAutomationPeer(row)
        {
            protected override List<AutomationPeer> GetChildrenCore() => [row.Action.GetAutomationPeer()!];
        }
    }
    private sealed class VirtualList : ListBox
    {
        internal IEnumerable<UIElement> Realized => ItemsHost?.Children.Cast<UIElement>() ?? [];
        internal readonly List<string> Invocations = [];
        protected override FrameworkElement GetContainerForItem(object item)
        {
            var row = new VirtualRow();
            row.Action.Click += (_, _) => { if (row.Content is string value) Invocations.Add(value); };
            var template = new ControlTemplate(typeof(ListBoxItem));
            template.SetVisualTree(() => row.Action); row.Template = template;
            return row;
        }
        protected override void PrepareContainerForItem(FrameworkElement element, object item)
        {
            base.PrepareContainerForItem(element, item); element.Height = 32;
            var row = (VirtualRow)element; row.Action.Content = item;
            string value = (string)item;
            Name(row, value, "virtual-row-" + value[5..]); Name(row.Action, "执行 " + value, "virtual-action-" + value[5..]);
        }
    }
}

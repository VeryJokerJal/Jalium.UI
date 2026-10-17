using AppKit;
using Foundation;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using System.Diagnostics;
using System.Text.Json;

namespace Jalium.UI.MacOS;

// Only public/protected selection APIs and ordinary buttons are used. Notifications are
// observed from another process; this host never posts accessibility events.
internal static class WindowSelectionNotificationChecks
{
    internal static int RunCase(string argument)
    {
        const string prefix = "--window-selection-notifications-case=";
        if (!int.TryParse(argument.AsSpan(prefix.Length), out int index) || (uint)index >= 10) return 2;
        bool custom = index % 2 == 1, outline = index is 2 or 3, dataItems = index is 8 or 9;
        bool mutateCollection = Environment.GetEnvironmentVariable("JALIUM_SELECTION_MUTATIONS") == "1";
        SelectionMode mode = index is 4 or 5 ? SelectionMode.Single : index is 6 or 7 ? SelectionMode.Extended : SelectionMode.Multiple;
        string output = Environment.GetEnvironmentVariable("JALIUM_SELECTION_OUTPUT")
            ?? throw new InvalidOperationException("JALIUM_SELECTION_OUTPUT is required.");
        Directory.CreateDirectory(output);
        var watch = Stopwatch.StartNew();
        var app = NSApplication.SharedApplication;
        app.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        using var host = new ValidationDelegate();
        app.Delegate = host; app.FinishLaunching();
        using (var launch = NSNotification.FromName(NSApplication.DidFinishLaunchingNotification, app))
            host.DidFinishLaunching(launch);
        RenderContext.GetOrCreateCurrent(RenderBackend.Software, forceReplace: true);
        Jalium.UI.Controls.Themes.ThemeManager.Initialize(Application.Current!);

        string[] labels = ["Alpha", "中文🙂", "Gamma"];
        var list = new SelectionList { SelectionMode = mode, Height = 240 };
        var tree = new TreeView { Height = 240 };
        ListBoxItem[] listRows = labels.Select(text => new ListBoxItem { Content = text, MinHeight = 40 }).ToArray();
        TreeViewItem[] treeRows = labels.Select(text => new TreeViewItem { Header = text }).ToArray();
        for (int i = 0; i < 3; i++)
        {
            Name(listRows[i], labels[i], "selection-row-" + i);
            Name(treeRows[i], labels[i], "selection-row-" + i);
            list.Items.Add(dataItems ? labels[i] : listRows[i]);
        }
        var group = new TreeViewItem { Header = "展开的分组", IsExpanded = true };
        foreach (var row in treeRows) group.Items.Add(row);
        tree.Items.Add(group);
        Control selection = outline ? tree : list;
        Name(selection, outline ? "嵌套树" : mode == SelectionMode.Single ? "单选列表" : "多选列表", "selection-container");
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
        panel.Children.Add(Label("选择通知", 24));
        panel.Children.Add(Label("修改选择后，辅助技术应立即读到新的已选集合。", 15));
        panel.Children.Add(selection);
        var second = Button("选择第二项", "selection-second");
        var change = Button(outline || mode == SelectionMode.Single ? "选择第三项" : "选择第一及第三项", "selection-change");
        var shrink = Button(outline ? "选择第一项" : "只保留第三项", "selection-shrink");
        var clear = Button("清空选择", "selection-clear");
        var finish = Button("结束检查", "selection-finish");
        var closeNext = Button("选择后关闭", "selection-close-next");
        foreach (var button in new[] { second, change, shrink, clear, closeNext, finish }) panel.Children.Add(button);
        var owner = new Window
        {
            Title = "Jalium Selection Notifications " + index,
            TitleBarStyle = custom ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native,
            Width = 640, Height = 760, MinWidth = 520, MinHeight = 620,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = new SolidColorBrush(Color.FromRgb(0x1c, 0x20, 0x26)),
            Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }
        };
        Application.Current!.MainWindow = owner;
        bool closed = false, timedOut = false; int phase = 0;
        void Record(string operation)
        {
            int[] selected = Enumerable.Range(0, 3).Where(i => outline ? treeRows[i].IsSelected : listRows[i].IsSelected).ToArray();
            var value = new { processId = Environment.ProcessId, index, custom, outline, phase, operation,
                selected, selectedItem = (outline ? tree.SelectedItem : list.SelectedItem)?.ToString(),
                selectedItems = list.SelectedItems.Cast<object>().Select(item => item.ToString()).ToArray(),
                closed, timedOut, windowVisible = owner.IsVisible, active = owner.IsActive,
                frontmostPID = NSWorkspace.SharedWorkspace.FrontmostApplication?.ProcessIdentifier,
                appActive = app.Active, appRunning = app.Running, physicalInput = false, externalAX = true, mutateCollection };
            string json = JsonSerializer.Serialize(value);
            File.WriteAllText(Path.Combine(output, "state.json.tmp"), json);
            File.Move(Path.Combine(output, "state.json.tmp"), Path.Combine(output, "state.json"), true);
            Console.WriteLine("SELECTION STATE: " + json);
        }
        void Select(int next)
        {
            phase = next;
            if (mutateCollection && !outline)
            {
                object Item(int i) => dataItems ? labels[i] : listRows[i];
                if (next == 1) { list.SelectedItems.Clear(); list.SelectedItems.Add(Item(1)); }
                else if (next == 2)
                {
                    if (mode == SelectionMode.Single) list.SelectedItems[0] = Item(2);
                    else { list.SelectedItems.Insert(0, Item(0)); list.SelectedItems[1] = Item(2); }
                }
                else if (next == 3)
                {
                    if (mode == SelectionMode.Single) { list.SelectedItems.Clear(); list.SelectedItems.Add(Item(2)); }
                    else list.SelectedItems.Remove(Item(0));
                }
                else list.SelectedItems.Clear();
                Record("public collection mutation");
                return;
            }
            if (outline)
            {
                if (next == 4) foreach (var row in treeRows) row.IsSelected = false;
                else treeRows[next == 1 ? 1 : next == 2 ? 2 : 0].IsSelected = true;
            }
            else
            {
                list.UnselectAll();
                if (next == 1) list.SelectedIndex = 1;
                else if (next == 2)
                {
                    if (mode == SelectionMode.Single) list.SelectedIndex = 2;
                    else list.Choose(labels[0], labels[2]);
                }
                else if (next == 3) list.SelectedIndex = 2;
            }
            Record("button selection");
        }
        second.Click += (_, _) => Select(1);
        change.Click += (_, _) => Select(2);
        shrink.Click += (_, _) => Select(3);
        clear.Click += (_, _) => Select(4);
        // Close from a real selection callback; the automation notification
        // emitted after that callback must not resurrect or query a retired tree.
        void CloseOnSelection()
        {
            if (phase == 5) owner.Close();
        }
        list.SelectionChanged += (_, _) => CloseOnSelection();
        tree.SelectedItemChanged += (_, _) => CloseOnSelection();
        closeNext.Click += (_, _) => phase = 5;
        finish.Click += (_, _) =>
        {
            phase = 5;
            if (outline) treeRows[1].IsSelected = true;
            else if (mutateCollection) list.SelectedItems.Add(dataItems ? labels[1] : listRows[1]);
            else list.SelectedIndex = 1;
        };
        owner.Closed += (_, _) => { closed = true; Record("closed"); };
        owner.Show(); owner.UpdateLayout();
        if (dataItems)
            for (int i = 0; i < 3; i++)
            {
                listRows[i] = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(i)!;
                Name(listRows[i], labels[i], "selection-row-" + i);
            }
        Record("ready");
        using var pulse = NSTimer.CreateRepeatingScheduledTimer(.1, _ =>
        {
            Record("observed state");
            if (watch.Elapsed.TotalSeconds > 30) timedOut = true;
            if (!timedOut && !File.Exists(Path.Combine(output, "finish.signal"))) return;
            if (owner.Handle != 0) owner.Close();
            app.Stop(app);
            using var wake = NSEvent.OtherEvent(NSEventType.ApplicationDefined, default, 0, 0, 0, null, 0, 0, 0);
            app.PostEvent(wake, false);
        });
        try
        {
            app.Run(); Record("stopped");
            File.Copy(Path.Combine(output, "state.json"), Path.Combine(output, "host-result.json"), true);
            return timedOut ? 1 : 0;
        }
        finally
        {
            pulse.Invalidate(); if (owner.Handle != 0) owner.Close();
            using var termination = NSNotification.FromName(NSApplication.WillTerminateNotification, app);
            host.WillTerminate(termination); app.Delegate = null!;
        }
    }
    private sealed class SelectionList : ListBox
    {
        internal void Choose(params object[] values)
        {
            if (!SetSelectedItems(values)) throw new InvalidOperationException("Selection was refused.");
        }
    }
    private static TextBlock Label(string text, double size) => new()
    { Text = text, FontSize = size, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap };
    private static Button Button(string text, string id)
    {
        var button = new Button { Content = text, MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Stretch };
        Name(button, text, id); return button;
    }
    private static void Name(DependencyObject element, string name, string id)
    { AutomationProperties.SetName(element, name); AutomationProperties.SetAutomationId(element, id); }
    private sealed class ValidationDelegate : JaliumMacApplicationDelegate
    {
        private bool _started;
        public override void DidFinishLaunching(NSNotification notification)
        { if (_started) return; _started = true; base.DidFinishLaunching(notification); }
        protected override JaliumApp CreateHostedApp() => AppBuilder.CreateBuilder(new AppBuilderSettings { DisableDefaults = true }).Build()
            .UseApplication(new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown });
    }
}

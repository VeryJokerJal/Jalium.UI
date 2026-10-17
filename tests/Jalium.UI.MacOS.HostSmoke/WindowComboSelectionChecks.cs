using AppKit;
using Foundation;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;

namespace Jalium.UI.MacOS;

internal static partial class WindowSelectionNotificationChecks
{
    internal static int RunComboSelectionCase(string argument)
    {
        const string prefix = "--window-combo-selection-case=";
        if (!int.TryParse(argument.AsSpan(prefix.Length), out int index) || (uint)index >= 8) return 2;
        bool custom = index % 2 == 1, own = index % 4 >= 2, editable = index >= 4;
        string output = Environment.GetEnvironmentVariable("JALIUM_SELECTION_OUTPUT")
            ?? throw new InvalidOperationException("JALIUM_SELECTION_OUTPUT is required.");
        Directory.CreateDirectory(output); var watch = Stopwatch.StartNew(); var app = NSApplication.SharedApplication;
        app.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        using var host = new ValidationDelegate(); app.Delegate = host; app.FinishLaunching();
        using (var launch = NSNotification.FromName(NSApplication.DidFinishLaunchingNotification, app)) host.DidFinishLaunching(launch);
        RenderContext.GetOrCreateCurrent(RenderBackend.Software, forceReplace: true);
        Jalium.UI.Controls.Themes.ThemeManager.Initialize(Application.Current!);
        var data = new List<ComboEntry> { new(0,"起点"),new(1,"中文🙂"),new(2,"重复"),new(3,"重复"),new(4,"禁用") };
        var owned = new Dictionary<ComboBoxItem, ComboEntry>();
        object Item(ComboEntry entry)
        {
            if (!own) return entry;
            var row = new ComboBoxItem { Content = entry.Caption }; owned.Add(row, entry); return row;
        }
        ComboEntry Entry(object item) => item is ComboBoxItem row ? owned[row] : (ComboEntry)item;
        var items = new ObservableCollection<object>(data.Select(Item));
        var combo = new ComboBox { Width = 360, Height = 40, MaxDropDownHeight = 220,
            ItemsSource = items, IsEditable = editable, DisplayMemberPath = own ? string.Empty : nameof(ComboEntry.Caption) };
        Name(combo,"选择项目 中文🙂","combo-root"); combo.SelectedIndex = 1;
        Window? owner = null; int phase = 0; bool closed = false, closeNext = false, timedOut = false, narrowed = false;
        var changes = new List<int>();
        void PrepareRows()
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (combo.ItemContainerGenerator.ContainerFromIndex(i) is not ComboBoxItem row) continue;
                var entry = Entry(items[i]); AutomationProperties.SetAutomationId(row,"combo-row-" + entry.Id);
                row.IsEnabled = entry.Id != 4;
            }
        }
        void Record()
        {
            PrepareRows();
            var state = new { processId = Environment.ProcessId, index, custom, own, editable, phase,
                selectedIndex = combo.SelectedIndex, selectedId = combo.SelectedItem is { } selected ? Entry(selected).Id : -1,
                text = combo.Text, open = combo.IsDropDownOpen, enabled = combo.IsEnabled, readOnly = combo.IsReadOnly,
                focused = combo.IsKeyboardFocused, focusWithin = combo.IsKeyboardFocusWithin, changes = changes.ToArray(),
                rows = items.Select((item, i) => new { id = Entry(item).Id, caption = Entry(item).Caption,
                    realized = combo.ItemContainerGenerator.ContainerFromIndex(i) is ComboBoxItem,
                    selected = (combo.ItemContainerGenerator.ContainerFromIndex(i) as ComboBoxItem)?.IsSelected,
                    enabled = (combo.ItemContainerGenerator.ContainerFromIndex(i) as ComboBoxItem)?.IsEnabled }).ToArray(),
                closed, timedOut, narrowed, width = owner?.ActualWidth, height = owner?.ActualHeight,
                appRunning = app.Running, appActive = app.Active, ownerActive = owner?.IsActive,
                frontmostPID = NSWorkspace.SharedWorkspace.FrontmostApplication?.ProcessIdentifier,
                physicalInput = false, externalAX = true };
            string temp = Path.Combine(output,"state.tmp"); File.WriteAllText(temp,JsonSerializer.Serialize(state));
            File.Move(temp,Path.Combine(output,"state.json"),true);
        }
        var open = Button("展开列表","combo-open"); var collapse = Button("收起列表","combo-collapse");
        var clear = Button("清空选择","combo-clear"); var replace = Button("替换第二项","combo-replace");
        var disable = Button("禁用选择框","combo-disable"); var enable = Button("启用选择框","combo-enable");
        var narrow = Button("窄窗口","combo-narrow"); var close = Button("选择后关闭","combo-close-next");
        var finish = Button("结束检查","combo-finish"); var readOnly = Button("切换只读编辑","combo-readonly");
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
        panel.Children.Add(Label("下拉选择与辅助技术",24)); panel.Children.Add(Label("展开列表并选择项目；所选文字、行状态和模型应一致。",15));
        panel.Children.Add(combo); var actions = new WrapPanel { ItemWidth = 140, ItemHeight = 48 };
        foreach (var action in new[] { open, collapse, clear, replace, disable, enable, narrow, close, readOnly, finish })
        { action.Width = 136; action.Margin = new Thickness(2); actions.Children.Add(action); }
        panel.Children.Add(actions);
        owner = new Window { Title = "Jalium Combo Selection " + index,
            TitleBarStyle = custom ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native,
            Width = 680, Height = 600, MinWidth = 480, MinHeight = 500,
            Background = new SolidColorBrush(Color.FromRgb(0x1c,0x20,0x26)),
            Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } };
        Application.Current!.MainWindow = owner;
        open.Click += (_, _) => { combo.IsDropDownOpen = true; owner.UpdateLayout(); PrepareRows(); Record(); };
        collapse.Click += (_, _) => { combo.IsDropDownOpen = false; Record(); };
        clear.Click += (_, _) => { combo.SelectedIndex = -1; Record(); };
        replace.Click += (_, _) => { items[1] = Item(new ComboEntry(5,"替换后🙂")); phase++; owner.UpdateLayout(); Record(); };
        disable.Click += (_, _) => { combo.IsEnabled = false; Record(); };
        enable.Click += (_, _) => { combo.IsEnabled = true; Record(); };
        narrow.Click += (_, _) => { owner.Width = 480; owner.Height = 500; owner.UpdateLayout(); narrowed = true; Record(); };
        close.Click += (_, _) => { closeNext = true; Record(); };
        readOnly.Click += (_, _) => { combo.IsReadOnly = !combo.IsReadOnly; Record(); };
        finish.Click += (_, _) => owner.Close(); owner.Closed += (_, _) => { closed = true; Record(); };
        combo.SelectionChanged += (_, _) =>
        {
            changes.Add(combo.SelectedItem is { } selected ? Entry(selected).Id : -1);
            if (closeNext) owner.Close(); Record();
        };
        owner.Show(); Record();
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
    private sealed record ComboEntry(int Id,string Caption)
    { public override string ToString() => "opaque model"; }
}

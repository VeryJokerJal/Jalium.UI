using AppKit;
using Foundation;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.Input;
using Jalium.UI.Media;
using System.Diagnostics;
using System.Text.Json;

namespace Jalium.UI.MacOS;

internal static partial class WindowSelectionNotificationChecks
{
    internal static int RunItemContentCase(string argument)
    {
        const string prefix = "--window-item-content-case=";
        if (!int.TryParse(argument.AsSpan(prefix.Length), out int index) || (uint)index >= 12) return 2;
        bool custom = index % 2 == 1, outline = index % 4 >= 2; int mode = index / 4;
        string output = Environment.GetEnvironmentVariable("JALIUM_SELECTION_OUTPUT")
            ?? throw new InvalidOperationException("JALIUM_SELECTION_OUTPUT is required.");
        Directory.CreateDirectory(output); var watch = Stopwatch.StartNew(); var app = NSApplication.SharedApplication;
        app.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        using var host = new ValidationDelegate(); app.Delegate = host; app.FinishLaunching();
        using (var launch = NSNotification.FromName(NSApplication.DidFinishLaunchingNotification, app)) host.DidFinishLaunching(launch);
        RenderContext.GetOrCreateCurrent(RenderBackend.Software, forceReplace: true);
        Jalium.UI.Controls.Themes.ThemeManager.Initialize(Application.Current!);

        Window? owner = null; var widgets = new List<ItemWidgets>(); var invocations = new List<int>();
        ItemsControl items = outline ? new TreeView { Height = 240 } : new ListBox { Height = 240 };
        Name(items, outline ? "树内控件" : "列表内控件", "item-container");
        // Use the loaded default styles; plain headers keep their original
        // minimum while taller header content contributes to measure.
        double ProbeHeader(bool selector, bool tall)
        {
            FrameworkElement row = selector
                ? new TreeSelectorItem { Header = tall ? new Border { Height = 48 } : "普通项目" }
                : new TreeViewItem { Header = tall ? new Border { Height = 48 } : "普通项目" };
            if (owner?.TryFindResource(row.GetType()) is Style style) ((Control)row).Style = style;
            ((Control)row).ApplyTemplate();
            row.Measure(new Size(550, double.PositiveInfinity));
            return row.DesiredSize.Height;
        }
        var measuredHeaders = new { treePlain = 0d, treeTall = 0d, selectorPlain = 0d, selectorTall = 0d };
        int generation = 0, phase = 0; bool closed = false, closeNext = false, timedOut = false, narrowed = false;
        FrameworkElement? Row() => items.ItemContainerGenerator.ContainerFromIndex(0) as FrameworkElement;
        bool Within(UIElement element, UIElement ancestor)
        { for (var current = element; current != null; current = current.VisualParent as UIElement) if (ReferenceEquals(current, ancestor)) return true; return false; }
        ItemWidgets? Live()
        { var row = Row(); return row == null ? null : widgets.LastOrDefault(x => Within(x.Check, row)); }
        void Record()
        {
            var live = Live(); var row = Row();
            var state = new { processId = Environment.ProcessId, index, custom, outline, mode, phase,
                measuredHeaders, generation = live?.Generation ?? 0, checkedValue = live?.Check.IsChecked,
                text = live?.Editor.Text, checkFocused = live?.Check.IsKeyboardFocused,
                editorFocused = live?.Editor.IsKeyboardFocused,
                selected = outline ? ((TreeView)items).SelectedItem != null : ((ListBox)items).SelectedIndex != -1,
                expanded = (row as TreeViewItem)?.IsExpanded, invocations = invocations.ToArray(),
                widgets = widgets.Select(x => new { generation = x.Generation, check = x.Check.IsChecked, text = x.Editor.Text }).ToArray(),
                closed, timedOut, narrowed, width = owner?.ActualWidth, height = owner?.ActualHeight,
                ownerActive = owner?.IsActive, keyWindowTitle = app.KeyWindow?.Title,
                globalFocus = Keyboard.FocusedElement is DependencyObject focused ? AutomationProperties.GetAutomationId(focused) : null,
                appRunning = app.Running, appActive = app.Active,
                frontmostPID = NSWorkspace.SharedWorkspace.FrontmostApplication?.ProcessIdentifier,
                physicalInput = false, externalAX = true };
            string temp = Path.Combine(output, "state.tmp"); File.WriteAllText(temp, JsonSerializer.Serialize(state));
            File.Move(temp, Path.Combine(output, "state.json"), true);
        }
        FrameworkElement Content()
        {
            int current = ++generation;
            var check = new CheckBox { Content = "启用", Width = 100, MinHeight = 36 };
            var action = new Button { Content = "行内操作", Width = 110, MinHeight = 36 };
            var editor = new TextBox { Text = "原文", Width = 220, Height = 36 };
            Name(check, "项目开关 中文🙂", "item-check-" + current);
            Name(action, "项目操作 中文🙂", "item-action-" + current);
            Name(editor, "项目备注 中文🙂", "item-editor-" + current);
            widgets.Add(new(current, check, action, editor));
            action.Click += (_, _) => { invocations.Add(current); if (closeNext) owner?.Close(); Record(); };
            var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
            panel.Children.Add(check); panel.Children.Add(action); panel.Children.Add(editor);
            return new Border { Padding = new Thickness(6), Child = panel };
        }
        DataTemplate Template() { var value = new DataTemplate(); value.SetVisualTree(Content); return value; }
        if (mode == 0) items.Items.Add(outline ? new TreeViewItem { Header = Content() } : new ListBoxItem { Content = Content() });
        else
        {
            if (mode == 1) items.ItemTemplate = Template(); else items.ItemTemplateSelector = new ItemContentSelector(Template());
            items.Items.Add("数据项目 中文🙂");
        }
        var replace = Button("替换项目内容", "item-replace"); var collapse = Button("折叠", "item-collapse");
        var expand = Button("展开", "item-expand"); var close = Button("行内操作后关闭", "item-close-next");
        var finish = Button("结束检查", "item-finish"); var narrow = Button("窄窗口", "item-narrow");
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 10 };
        panel.Children.Add(Label("行内控件与辅助技术", 24));
        panel.Children.Add(Label("勾选、编辑和执行行内操作时，保留控件语义与项目选择状态。", 15)); panel.Children.Add(items);
        var actions = new WrapPanel { ItemWidth = 180, ItemHeight = 48 };
        foreach (var button in new[] { narrow, replace, collapse, expand, close, finish })
        { button.Width = 176; button.Margin = new Thickness(2); actions.Children.Add(button); }
        panel.Children.Add(actions);
        owner = new Window { Title = "Jalium Item Content " + index,
            TitleBarStyle = custom ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native,
            Width = 720, Height = 700, MinWidth = 600, MinHeight = 620,
            Background = new SolidColorBrush(Color.FromRgb(0x1c, 0x20, 0x26)),
            Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } };
        Application.Current!.MainWindow = owner;
        void SetupRow()
        {
            owner.UpdateLayout(); var row = Row(); if (row == null) return;
            Name(row, "包含交互控件的项目", "item-root");
            if (row is TreeViewItem treeRow)
            {
                if (treeRow.Items.Count == 0)
                {
                    var child = new TreeViewItem { Header = "嵌套项目" }; Name(child, "嵌套项目", "item-nested"); treeRow.Items.Add(child);
                }
                treeRow.IsExpanded = true;
            }
            owner.UpdateLayout();
        }
        narrow.Click += (_, _) => { owner.Width = 600; owner.Height = 620; owner.UpdateLayout(); narrowed = true; Record(); };
        replace.Click += (_, _) =>
        {
            if (mode == 0)
            { if (Row() is TreeViewItem treeRow) treeRow.Header = Content(); else if (Row() is ListBoxItem listRow) listRow.Content = Content(); }
            else if (mode == 1) items.ItemTemplate = Template();
            else items.ItemTemplateSelector = new ItemContentSelector(Template());
            SetupRow(); phase++; Record();
        };
        collapse.Click += (_, _) => { if (Row() is TreeViewItem row) row.IsExpanded = false; owner.UpdateLayout(); Record(); };
        expand.Click += (_, _) => { if (Row() is TreeViewItem row) row.IsExpanded = true; owner.UpdateLayout(); Record(); };
        close.Click += (_, _) => { closeNext = true; Record(); };
        finish.Click += (_, _) => owner.Close(); owner.Closed += (_, _) => { closed = true; Record(); };
        owner.Show(); SetupRow();
        measuredHeaders = new { treePlain = ProbeHeader(false, false), treeTall = ProbeHeader(false, true),
            selectorPlain = ProbeHeader(true, false), selectorTall = ProbeHeader(true, true) };
        Record();
        using var pulse = NSTimer.CreateRepeatingScheduledTimer(.1, _ =>
        {
            Record(); if (watch.Elapsed.TotalSeconds > 50) timedOut = true;
            if (!closed && !timedOut && !File.Exists(Path.Combine(output, "finish.signal"))) return;
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
    private sealed record ItemWidgets(int Generation, CheckBox Check, Button Action, TextBox Editor);
    private sealed class ItemContentSelector(DataTemplate template) : DataTemplateSelector
    { public override DataTemplate? SelectTemplate(object? item, DependencyObject container) => template; }
}

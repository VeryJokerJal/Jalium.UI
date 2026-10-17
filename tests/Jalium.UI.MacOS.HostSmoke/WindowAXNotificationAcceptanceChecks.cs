using AppKit;
using Foundation;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Threading;
using System.Text.Json;
using System.Diagnostics;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Documents;

namespace Jalium.UI.MacOS;

// External AX clients operate the existing controls and subscribe to notifications.
// The host records managed state; it does not synthesize input or AX notifications.
internal static class WindowAXNotificationAcceptanceChecks
{
    internal static int RunCase(string argument)
    {
        const string prefix = "--window-ax-notifications-case=";
        if (!int.TryParse(argument.AsSpan(prefix.Length), out int index) || (uint)index >= 6) return 2;
        bool custom = index % 2 == 1; int editorKind = index / 2;
        string output = Environment.GetEnvironmentVariable("JALIUM_AX_NOTIFICATION_OUTPUT")
            ?? throw new InvalidOperationException("JALIUM_AX_NOTIFICATION_OUTPUT is required.");
        Directory.CreateDirectory(output);
        var watch = Stopwatch.StartNew(); bool timedOut = false;
        var app = NSApplication.SharedApplication;
        app.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        using var host = new ValidationDelegate();
        app.Delegate = host; app.FinishLaunching();
        using (var launch = NSNotification.FromName(NSApplication.DidFinishLaunchingNotification, app))
            host.DidFinishLaunching(launch);
        RenderContext.GetOrCreateCurrent(RenderBackend.Software, forceReplace: true);
        Jalium.UI.Controls.Themes.ThemeManager.Initialize(Application.Current!);

        Control editor = editorKind switch
        {
            0 => new TextBox { Text = "通知原文🙂 e\u0301", AcceptsReturn = true },
            1 => new RichTextBox(new FlowDocument(new Paragraph(new Run("通知原文🙂 e\u0301")))),
            _ => new EditControl { Text = "通知原文🙂 e\u0301" },
        };
        editor.Height = 48;
        var source = ((AutomationTextProvider)UIElementAutomationPeer.CreatePeerForElement(editor)!
            .GetPattern(PatternInterface.Text)!).Source;
        var second = new TextBox { Text = "另一处焦点", Height = 48 };
        Name(editor, "编辑内容", "notification-editor");
        Name(second, "另一处焦点", "notification-second");
        var note = Label("这段说明可以隐藏、恢复、移除和重新添加。", 15);
        Name(note, "可变说明", "notification-note");
        var slot = new Border { Child = note, MinHeight = 44 };
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
        panel.Children.Add(Label("窗口通知", 24));
        panel.Children.Add(Label("编辑、选中文字或切换焦点；窗口恢复后应保留内容。", 15));
        panel.Children.Add(editor); panel.Children.Add(second); panel.Children.Add(slot);
        var sample = Action("更新示例文本");
        Name(sample, "更新示例文本", "notification-sample");
        var hideNote = Action("隐藏或显示说明（F6）");
        var removeNote = Action("移除或添加说明（F7）");
        var size = Action("切换最小尺寸（F8）");
        var hideWindow = Action("隐藏窗口后恢复（F9）");
        var finish = Action("结束检查");
        Name(hideNote, "隐藏或显示说明", "notification-hide-note");
        Name(removeNote, "移除或添加说明", "notification-remove-note");
        Name(size, "切换最小尺寸", "notification-size");
        Name(hideWindow, "隐藏窗口后恢复", "notification-hide-window");
        Name(finish, "结束检查", "notification-finish");
        foreach (var action in new[] { sample, hideNote, removeNote, size, hideWindow, finish }) panel.Children.Add(action);
        var owner = new Window
        {
            Title = "Jalium AX Notification Acceptance " + index,
            Width = 640, Height = 800, MinWidth = 520, MinHeight = 620,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            TitleBarStyle = custom ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native,
            Background = new SolidColorBrush(Color.FromRgb(0x1c, 0x20, 0x26)),
            Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }
        };
        Name(owner, "窗口通知检查", "notification-window");
        Application.Current!.MainWindow = owner;
        bool closed = false;
        int visibilityChanges = 0, attachmentChanges = 0, windowRestores = 0;
        var restore = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        void Record(string operation)
        {
            var value = new { index, editorKind, processId = Environment.ProcessId,
                backend = "Software", physicalInput = false, externalAX = true,
                frontmostPID = NSWorkspace.SharedWorkspace.FrontmostApplication?.ProcessIdentifier,
                appActive = app.Active, appRunning = app.Running, windowVisible = owner.IsVisible,
                custom, operation, closed, timedOut, visibilityChanges, attachmentChanges, windowRestores,
                noteVisible = note.Visibility == Visibility.Visible, noteAttached = slot.Child != null,
                editor = source.Text, selectionStart = source.SelectionStart, selectionLength = source.SelectionLength,
                second = second.Text, width = owner.Width, height = owner.Height, active = owner.IsActive,
                focused = Keyboard.FocusedElement is DependencyObject element ? AutomationProperties.GetAutomationId(element) : null };
            Console.WriteLine("NOTIFICATION STATE: " + JsonSerializer.Serialize(value));
            string path = Path.Combine(output, "state.json");
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(value));
            File.Move(path + ".tmp", path, true);
        }
        void ToggleNote()
        {
            note.Visibility = note.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            visibilityChanges++; owner.UpdateLayout(); Record("note visibility");
        }
        void AttachNote()
        {
            slot.Child = slot.Child == null ? note : null;
            attachmentChanges++; owner.UpdateLayout(); Record("note attachment");
        }
        void Resize()
        {
            bool small = owner.Width == owner.MinWidth;
            owner.Width = small ? 640 : owner.MinWidth;
            owner.Height = small ? 800 : owner.MinHeight;
            owner.UpdateLayout(); editor.Focus(); Record("resize");
        }
        void HideWindow()
        {
            if (restore.IsEnabled) return;
            owner.Hide(); Record("window hidden"); restore.Start();
        }
        restore.Tick += (_, _) =>
        {
            restore.Stop();
            if (closed) return;
            owner.Show(); owner.UpdateLayout(); editor.Focus(); windowRestores++; Record("window restored");
        };
        sample.Click += (_, _) =>
        {
            const string value = "示🙂 e\u0301 中文";
            switch (editor)
            {
                case TextBox box: box.Text = value; break;
                case RichTextBox rich: rich.Document = new FlowDocument(new Paragraph(new Run(value))); break;
                case EditControl edit: edit.Text = value; break;
            }
            Record("sample text");
        };
        hideNote.Click += (_, _) => ToggleNote(); removeNote.Click += (_, _) => AttachNote();
        size.Click += (_, _) => Resize(); hideWindow.Click += (_, _) => HideWindow();
        finish.Click += (_, _) => owner.Close();
        owner.PreviewKeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.F6: ToggleNote(); break;
                case Key.F7: AttachNote(); break;
                case Key.F8: Resize(); break;
                case Key.F9: HideWindow(); break;
                default: return;
            }
            e.Handled = true;
        };
        owner.Closed += (_, _) => { closed = true; restore.Stop(); Record("closed"); };
        owner.Show(); owner.UpdateLayout(); editor.Focus(); Record("ready");
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
            pulse.Invalidate(); restore.Stop(); if (owner.Handle != 0) owner.Close();
            using var termination = NSNotification.FromName(NSApplication.WillTerminateNotification, app);
            host.WillTerminate(termination); app.Delegate = null!;
        }
    }

    private static TextBlock Label(string text, double size) => new()
    { Text = text, FontSize = size, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap };
    private static Button Action(string text) => new()
    { Content = text, MinHeight = 44, HorizontalAlignment = HorizontalAlignment.Stretch };
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

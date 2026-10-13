using AppKit;
using Foundation;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Threading;
using System.Text.Json;

namespace Jalium.UI.MacOS;

// Pair with platform_apple_notification_observer.mm. The external observer
// only reads AX attributes; all input comes from the ordinary application UI.
internal static class WindowNotificationChecks
{
    internal static int Observe(bool custom)
    {
        var app = NSApplication.SharedApplication;
        app.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        using var host = new ValidationDelegate();
        app.Delegate = host; app.FinishLaunching();
        using (var launch = NSNotification.FromName(NSApplication.DidFinishLaunchingNotification, app))
            host.DidFinishLaunching(launch);
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        Jalium.UI.Controls.Themes.ThemeManager.Initialize(Application.Current!);

        var editor = new TextBox { Text = "通知原文🙂 e\u0301", Height = 48 };
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
        var hideNote = Action("隐藏或显示说明（F6）");
        var removeNote = Action("移除或添加说明（F7）");
        var size = Action("切换最小尺寸（F8）");
        var hideWindow = Action("隐藏窗口后恢复（F9）");
        var finish = Action("结束检查");
        foreach (var action in new[] { hideNote, removeNote, size, hideWindow, finish }) panel.Children.Add(action);
        var owner = new Window
        {
            Title = "Jalium Window Notifications " + (custom ? "Custom" : "Native"),
            Width = 640, Height = 740, MinWidth = 520, MinHeight = 620,
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
            var value = new { custom, operation, closed, visibilityChanges, attachmentChanges, windowRestores,
                noteVisible = note.Visibility == Visibility.Visible, noteAttached = slot.Child != null,
                editor = editor.Text, selectionStart = editor.SelectionStart, selectionLength = editor.SelectionLength,
                second = second.Text, width = owner.Width, height = owner.Height, active = owner.IsActive,
                focused = Keyboard.FocusedElement is DependencyObject element ? AutomationProperties.GetAutomationId(element) : null };
            Console.WriteLine("NOTIFICATION STATE: " + JsonSerializer.Serialize(value));
            var output = Environment.GetEnvironmentVariable("JALIUM_NOTIFICATION_OUTPUT");
            if (!string.IsNullOrEmpty(output))
            {
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, custom ? "custom-state.json" : "native-state.json"), JsonSerializer.Serialize(value));
            }
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
            owner.Height = small ? 740 : owner.MinHeight;
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
        editor.TextChanged += (_, _) => Record("text edited");
        owner.Closed += (_, _) => { closed = true; restore.Stop(); Record("closed"); app.Terminate(app); };
        owner.Show(); owner.UpdateLayout(); editor.Focus(); Record("ready");
        app.Run(); return 0;
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

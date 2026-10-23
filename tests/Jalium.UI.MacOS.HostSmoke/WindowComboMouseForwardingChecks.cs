#pragma warning disable WPF0001
using AppKit;
using Foundation;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using System.Text.Json;

namespace Jalium.UI.MacOS;

internal static class WindowComboMouseForwardingChecks
{
    internal static int Run(string argument)
    {
        if (!int.TryParse(argument.Split('=').Last(), out int index) || index is < 0 or > 3) return 2;
        string output = Environment.GetEnvironmentVariable("JALIUM_COMBO_MOUSE_OUTPUT")
            ?? throw new InvalidOperationException("Missing owned output directory.");
        Directory.CreateDirectory(output);
        var app = NSApplication.SharedApplication;
        app.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        using var host = new Delegate();
        app.Delegate = host; app.FinishLaunching();
        using (var launch = NSNotification.FromName(NSApplication.DidFinishLaunchingNotification, app))
            host.DidFinishLaunching(launch);
        var context = RenderContext.GetOrCreateCurrent(index >= 2 ? RenderBackend.Software : RenderBackend.Metal, forceReplace: true);
        if (index < 2) context.DefaultRenderingEngine = RenderingEngine.Impeller;
        Controls.Themes.ThemeManager.Initialize(Application.Current!);
        TextBox? editor = null;
        var combo = new ComboBox { IsEditable = true, Text = "窗口 Miii 中文🙂", Width = 360, Height = 56 };
        var template = new ControlTemplate(typeof(ComboBox));
        template.SetVisualTree(() => new Border
        {
            Padding = new Thickness(14, 8, 40, 8), Background = combo.Background ?? Brushes.Transparent,
            BorderBrush = combo.BorderBrush, BorderThickness = combo.BorderThickness,
            Child = editor = new TextBox { Name = "PART_EditableTextBox", Text = combo.Text }
        });
        combo.Template = template;
        AutomationProperties.SetName(combo, "可编辑选择框");
        AutomationProperties.SetAutomationId(combo, "combo-forward");
        var status = new TextBlock { Text = "点击选择框左侧留白，再检查原生编辑菜单。", TextWrapping = TextWrapping.Wrap };
        var finish = new Button { Content = "完成检查", Height = 38, Width = 140 };
        var panel = new StackPanel { Margin = new Thickness(24), Spacing = 14, Children =
        {
            new TextBlock { Text = "选择框与编辑菜单", FontSize = 24 },
            combo,
            new TextBlock { Text = "Command 点击保持编辑；Control 点击显示菜单。", TextWrapping = TextWrapping.Wrap },
            status, finish
        }};
        var window = new Window { Title = "Jalium Combo Mouse v313", Width = 520, Height = 360,
            MinWidth = 420, MinHeight = 320, Content = panel,
            TitleBarStyle = index % 2 == 1 ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native };
        var observations = new List<Observation>();
        ModifierKeys? rawPhysical = null; ModifierKeys rawLogical = default;
        bool rawPadding = false; int beforeMenus = 0, menus = 0; bool completed = false, timedOut = false;
        bool FromEditor(object? source)
        {
            for (var current = source as Visual; current != null; current = current.VisualParent)
                if (ReferenceEquals(current, editor)) return true;
            return false;
        }
        void Save(string name)
        {
            File.WriteAllText(Path.Combine(output, name), JsonSerializer.Serialize(new
            {
                index, backend = context.Backend.ToString(), custom = index % 2 == 1,
                processId = Environment.ProcessId, completed, timedOut, menus, observations,
                passed = completed && observations.Any(x => x.Padding && x.Physical == "Windows" &&
                    x.Logical == "Control" && x.MenuCount == 0) &&
                    observations.Any(x => x.Padding && x.Physical == "Control" &&
                    x.Logical == "Control" && x.MenuCount == 1) &&
                    observations.All(x => x.Physical == x.RawPhysical && x.Logical == x.RawLogical),
                appActive = app.Active, frontmostPID = NSWorkspace.SharedWorkspace.FrontmostApplication?.ProcessIdentifier,
                nativeKey = app.KeyWindow is { } key ? (long?)key.WindowNumber : null,
                physicalInput = false, simulatedRoutedEvents = false, nativeInputObserved = observations.Count > 0,
                managedLoadPaths = new[] { typeof(Window).Assembly.Location, typeof(JaliumMacApplication).Assembly.Location,
                    typeof(WindowComboMouseForwardingChecks).Assembly.Location }
            }));
        }
        void Stop()
        {
            completed = true; Save("result.json"); window.Close(); app.Stop(app);
            using var wake = NSEvent.OtherEvent(NSEventType.ApplicationDefined, default, 0, 0, 0, null, 0, 0, 0);
            app.PostEvent(wake, false);
        }
        combo.PreviewMouseDown += (_, e) =>
        {
            rawPhysical = e.PhysicalModifiers; rawLogical = e.KeyboardModifiers;
            rawPadding = !FromEditor(e.OriginalSource); beforeMenus = menus;
        };
        finish.Click += (_, _) => Stop();
        window.Show(); window.UpdateLayout();
        if (editor == null) throw new InvalidOperationException("Editable template was not applied.");
        AutomationProperties.SetName(editor, "窗口文字");
        editor.AddHandler(ContextMenuService.ContextMenuOpeningEvent, new ContextMenuEventHandler((_, _) => menus++), true);
        editor.AddHandler(UIElement.MouseDownEvent, new MouseButtonEventHandler((_, e) =>
        {
            observations.Add(new(rawPadding, rawPhysical?.ToString(), rawLogical.ToString(),
                e.PhysicalModifiers?.ToString(), e.KeyboardModifiers.ToString(), menus - beforeMenus));
            status.Text = "已记录 " + observations.Count + " 次编辑点击";
            Save("state.json");
        }), true);
        Save("state.json");
        using var timeout = NSTimer.CreateScheduledTimer(600, _ => { timedOut = true; Stop(); });
        app.Run();
        return timedOut ? 75 : observations.Count > 0 ? 0 : 1;
    }

    private sealed record Observation(bool Padding, string? RawPhysical, string RawLogical,
        string? Physical, string Logical, int MenuCount);

    private sealed class Delegate : JaliumMacApplicationDelegate
    {
        protected override JaliumApp CreateHostedApp() => AppBuilder.CreateBuilder(new AppBuilderSettings { DisableDefaults = true }).Build()
            .UseApplication(new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown, ThemeMode = ThemeMode.System });
    }
}

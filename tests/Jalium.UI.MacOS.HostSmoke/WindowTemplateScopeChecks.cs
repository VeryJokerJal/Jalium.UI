using AppKit;
using Foundation;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using System.Text.Json;

namespace Jalium.UI.MacOS;

public partial class WindowTemplateScopeView : UserControl
{
    public WindowTemplateScopeView() => InitializeComponent();
    internal Grid Outer => Shared;
    internal Button FirstButton => First;
    internal Button SecondButton => Second;
    internal TextBox TextEditor => Editor;
}

internal static class WindowTemplateScopeChecks
{
    internal static int Run()
    {
        var app = NSApplication.SharedApplication;
        app.ActivationPolicy = NSApplicationActivationPolicy.Regular;
        using var host = new ValidationDelegate();
        app.Delegate = host;
        app.FinishLaunching();
        using (var launch = NSNotification.FromName(NSApplication.DidFinishLaunchingNotification, app))
            host.DidFinishLaunching(launch);
        RenderContext.GetOrCreateCurrent(RenderBackend.Metal).DefaultRenderingEngine = RenderingEngine.Impeller;
        Jalium.UI.Controls.Themes.ThemeManager.Initialize(Application.Current!);
        int passed = 0;
        var failures = new List<string>();
        app.BeginInvokeOnMainThread(() =>
        {
            foreach (bool custom in new[] { false, true })
            foreach (int width in new[] { 760, 420 })
            {
                Window? owner = null;
                try
                {
                    var view = new WindowTemplateScopeView();
                    var outer = view.Outer;
                    owner = new Window
                    {
                        Title = "Jalium 模板名称作用域", Width = width, Height = 480,
                        MinWidth = 420, MinHeight = 360, Content = view,
                        TitleBarStyle = custom ? WindowTitleBarStyle.Custom : WindowTitleBarStyle.Native,
                        WindowStartupLocation = WindowStartupLocation.CenterScreen
                    };
                    Application.Current!.MainWindow = owner;
                    owner.Show();
                    owner.UpdateLayout();
                    var first = view.FirstButton;
                    var second = view.SecondButton;
                    first.ApplyTemplate();
                    second.ApplyTemplate();
                    var firstPart = Part(first);
                    var secondPart = Part(second);
                    Require(!ReferenceEquals(firstPart, secondPart), "shared template reused a visual instance");
                    Require(ReferenceEquals(firstPart.TemplatedParent, first) && ReferenceEquals(secondPart.TemplatedParent, second), "template owners differ");
                    Require(ReferenceEquals(view.FindName("Shared"), outer) && ReferenceEquals(view.Outer, outer), "template replaced the outer field");
                    Require(outer.ActualWidth > 0 && first.ActualWidth <= outer.ActualWidth, "window content has invalid bounds");
                    Require(view.TextEditor.Focus(), "editor could not focus");
                    var responder = app.KeyWindow?.FirstResponder;
                    Require(responder != null, "native editor responder was not installed");
                    Require(Label(first).Text == "第一项🙂" && Label(second).Text == "第二项中文", "template bindings differ");
                    first.Template = (ControlTemplate)view.Resources["second"]!;
                    owner.UpdateLayout();
                    first.ApplyTemplate();
                    Require(!ReferenceEquals(Part(first), firstPart) && ReferenceEquals(Part(second), secondPart), "replacing one template changed both controls");
                    first.Content = "替换后🙂";
                    owner.UpdateLayout();
                    Require(Label(first).Text == "替换后🙂" && Label(second).Text == "第二项中文", "replacement template lost its binding");
                    Require(ReferenceEquals(view.Outer, outer) && ReferenceEquals(view.FindName("Shared"), outer), "replacement changed the component scope");
                    Require(ReferenceEquals(Keyboard.FocusedElement, view.TextEditor) && ReferenceEquals(app.KeyWindow?.FirstResponder, responder), "template replacement moved editor focus");
                    Console.WriteLine($"PASS template scopes: custom={custom}; width={width}; separateParts=True; outerField=True; bindings=True; focus=True");
                    passed++;
                }
                catch (Exception error)
                {
                    failures.Add($"custom={custom}; width={width}: {error}");
                    Console.Error.WriteLine("FAIL template scopes: " + error);
                }
                finally { owner?.Close(); }
            }
            string? output = Environment.GetEnvironmentVariable("JALIUM_TEMPLATE_SCOPE_RESULT");
            if (!string.IsNullOrEmpty(output))
                File.WriteAllText(output, JsonSerializer.Serialize(new { passed, expected = 4, failures }));
            Console.WriteLine($"macOS template scope host checks: {passed}/4 passed");
            app.Terminate(app);
        });
        app.Run();
        return passed == 4 ? 0 : 1;
    }

    private static Border Part(Button button) => button.FindName("Shared") as Border ?? throw new InvalidOperationException("template part was not registered");
    private static TextBlock Label(Button button) => button.FindName("Label") as TextBlock ?? throw new InvalidOperationException("template label was not registered");
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }

    private sealed class ValidationDelegate : JaliumMacApplicationDelegate
    {
        private bool _started;
        public override void DidFinishLaunching(NSNotification notification)
        {
            if (_started) return;
            _started = true;
            base.DidFinishLaunching(notification);
        }
        protected override JaliumApp CreateHostedApp() => AppBuilder.CreateBuilder(new AppBuilderSettings { DisableDefaults = true }).Build()
            .UseApplication(new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown });
    }
}

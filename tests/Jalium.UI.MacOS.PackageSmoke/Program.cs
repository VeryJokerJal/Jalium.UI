using AppKit;
using Foundation;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Interop;
using Jalium.UI.MacOS;
using Jalium.UI.Media;
using System.Text.Json;
using System.Runtime.Versioning;
[assembly: SupportedOSPlatform("macos15.0")]
var app = JaliumMacApplication.Initialize();
app.ActivationPolicy = NSApplicationActivationPolicy.Regular;
app.FinishLaunching();
var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
context.DefaultRenderingEngine = RenderingEngine.Impeller;
var editor = new TextBox { Text = "保留中文🙂 Miii", FontSize = 24 };
var window = new Window { Title = "Jalium NuGet · Window", Width = 620, Height = 300,
 Content = new StackPanel { Margin = new Thickness(24), Children = {
 new TextBlock { Text = "通过 NuGet 包运行 Window", FontSize = 28 },
 new TextBlock { Text = "ARM64 / x64 资源按 RuntimeIdentifier 选择", FontSize = 18 }, editor } } };
window.Show();
var started = System.Diagnostics.Stopwatch.StartNew();
int.TryParse(Environment.GetEnvironmentVariable("JALIUM_MACOS_PACKAGE_SMOKE_HOLD_SECONDS"), out int hold);
string output = Environment.GetEnvironmentVariable("JALIUM_MACOS_PACKAGE_SMOKE_RESULT") ?? throw new InvalidOperationException("Set owned output path");
bool passed = false;
using var timer = NSTimer.CreateRepeatingScheduledTimer(.1, tick => {
 if (started.Elapsed.TotalSeconds < Math.Max(3,hold)) return;
 tick.Invalidate();
 passed = window.Handle != 0 && window.Visibility == Visibility.Visible && editor.Text == "保留中文🙂 Miii" && context.Backend == RenderBackend.Metal;
 File.WriteAllText(output, JsonSerializer.Serialize(new { passed, processId=Environment.ProcessId,
  handle=window.Handle.ToInt64(), renderer=context.Backend.ToString(), editor.Text,
  framework=typeof(Window).Assembly.Location, sdk=typeof(JaliumMacApplication).Assembly.Location,
  runtime=System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
  physicalInput=false, foregroundRequired=false }));
 window.Close(); app.Stop(app);
 using var wake = NSEvent.OtherEvent(NSEventType.ApplicationDefined, default,0,0,0,null,0,0,0); app.PostEvent(wake,false);
});
app.Run();
return passed ? 0 : 1;

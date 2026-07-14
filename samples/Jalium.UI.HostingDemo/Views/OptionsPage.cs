using Jalium.UI.Controls;
using Jalium.UI.HostingDemo.ViewModels;

namespace Jalium.UI.HostingDemo.Views;

/// <summary>
/// 展示 <c>builder.Configuration</c> + <c>ConfigureJalium()</c>:从 appsettings.json
/// 加载 <c>Jalium</c> 段,在 <see cref="OptionsViewModel"/> 里用
/// <see cref="Microsoft.Extensions.Options.IOptions{TOptions}"/> 获得强类型实例。
/// </summary>
public sealed class OptionsPage : Page
{
    public OptionsPage(OptionsViewModel vm)
    {
        Title = "Options";
        DataContext = vm;

        Content = PageLayout.Build(
            "Configuration · JaliumRuntimeOptions",
            "appsettings.json 的 Jalium 段被 builder.ConfigureJalium() 绑定到 JaliumRuntimeOptions。命令行参数 / 环境变量可覆盖,例如: --Jalium:Render:DebugRender=true 或 DOTNET_Jalium__Metrics__FpsWindowFrames=120",
            PageLayout.Mono($"Render.Backend        = {vm.RenderBackend}"),
            PageLayout.Mono($"Render.DebugRender    = {vm.DebugRender}"),
            PageLayout.Mono($"Render.ForceFullReplay= {vm.ForceFullReplay}"),
            PageLayout.Mono($"Metrics.Enabled       = {vm.MetricsEnabled}"),
            PageLayout.Mono($"Metrics.FpsWindow     = {vm.FpsWindow}"),
            PageLayout.Mono($"Mvvm.EnableViewFactory= {vm.MvvmEnabled}"),
            PageLayout.Mono($"Mvvm.AutoWire         = {vm.AutoWire}"),
            PageLayout.Mono($"WorkingSet.Enabled    = {vm.WorkingSetEnabled}"),
            PageLayout.Mono($"WorkingSet.Profile    = {vm.WorkingSetProfile}"),
            new TextBlock
            {
                Text = "─── IConfiguration 原始键值 ───",
                FontSize = 12,
                Foreground = new Media.SolidColorBrush(Media.Color.FromRgb(166, 173, 200)),
                Margin = new Thickness(0, 16, 0, 4)
            },
            PageLayout.Mono(vm.ConfigurationDump));
    }
}

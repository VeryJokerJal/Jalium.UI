using Jalium.UI.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Jalium.UI.HostingDemo.ViewModels;

/// <summary>
/// 展示 <c>builder.Configuration</c> + <c>ConfigureJalium()</c> 的效果:
/// 构造函数注入 <see cref="IOptions{TOptions}"/> 和 <see cref="IConfiguration"/>,
/// 把配置源(appsettings.json / 环境变量 / 命令行)读出来给 UI 看。
/// </summary>
public sealed class OptionsViewModel : ViewModelBase
{
    public OptionsViewModel(IOptions<JaliumRuntimeOptions> options, IConfiguration configuration)
    {
        var o = options.Value;
        RenderBackend = o.Render.Backend;
        DebugRender = o.Render.DebugRender;
        ForceFullReplay = o.Render.ForceFullReplay;
        MetricsEnabled = o.Metrics.Enabled;
        FpsWindow = o.Metrics.FpsWindowFrames;
        MvvmEnabled = o.Mvvm.EnableViewFactory;
        AutoWire = o.Mvvm.EnableAutoWireDataContext;
        WorkingSetEnabled = o.WorkingSet.Enabled;
        WorkingSetProfile = o.WorkingSet.Profile;

        // 读取原始 section,方便展示"配置来源"
        ConfigurationDump = string.Join(Environment.NewLine,
            configuration.AsEnumerable()
                .Where(kv => kv.Key.StartsWith(JaliumRuntimeOptions.SectionName) && kv.Value != null)
                .OrderBy(kv => kv.Key)
                .Select(kv => $"  {kv.Key} = {kv.Value}"));
    }

    public string RenderBackend { get; }
    public bool DebugRender { get; }
    public bool ForceFullReplay { get; }
    public bool MetricsEnabled { get; }
    public int FpsWindow { get; }
    public bool MvvmEnabled { get; }
    public bool AutoWire { get; }
    public bool WorkingSetEnabled { get; }
    public string WorkingSetProfile { get; }

    public string ConfigurationDump { get; }
}

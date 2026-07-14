using Jalium.UI;
using Jalium.UI.Hosting;
using Jalium.UI.HostingDemo;
using Jalium.UI.HostingDemo.Services;
using Jalium.UI.HostingDemo.ViewModels;
using Jalium.UI.HostingDemo.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jalium.UI.HostingDemo;

// ──────────────────────────────────────────────────────────────────────────────
// Jalium.UI AppBuilder 全家桶演示 — 在一个文件里把所有扩展点都用一遍。
//
// API 模型对齐 ASP.NET Core:
//   Build 之前 — builder.Services.Add*/builder.Configure* (注册/配置)
//   Build 之后 — app.Use*                                (激活,含 UseApplication)
//
// 运行后窗口左侧的菜单每个按钮对应一个主题,每个页面展示对应扩展的效果:
//   · 首页        : builder.Services + builder.Environment
//   · MVVM       : AddView<TView, TViewModel>() / IViewFactory / DataContext
//   · Logging    : builder.Logging.AddConsole() + ILogger<T> DI 注入
//   · Metrics    : app.UseJaliumMetrics() + JaliumMeter 实时 FPS
//   · Options    : builder.Configuration + ConfigureJalium() / IOptions<T>
//   · HostedSvc  : AddHostedService<ClockHostedService>() + JaliumApp 启停
// ──────────────────────────────────────────────────────────────────────────────
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // ── 1. 构造 builder ──────────────────────────────────────────────
        // CreateBuilder(args):args 会走 IConfiguration(支持 --Key=Value 形式)。
        // 需要完全控制时改用:
        //     AppBuilder.CreateBuilder(new AppBuilderSettings { Args = args, DisableDefaults = true });
        var builder = AppBuilder.CreateBuilder(args);

        // ── 2. builder.Configuration ─────────────────────────────────────
        // HostApplicationBuilder 默认已加载 appsettings.json;这里显式声明让 demo
        // 在非默认工作目录下也能工作。reloadOnChange 使 IOptionsMonitor 可观察变更。
        builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

        // 把 appsettings.json 的 "Jalium" 段绑到 JaliumRuntimeOptions
        builder.ConfigureJalium();

        // 任意业务选项同样方式绑定
        builder.Services.Configure<GreetingOptions>(builder.Configuration.GetSection("Greeting"));

        // ── 3. builder.Logging ───────────────────────────────────────────
        builder.Logging
            .ClearProviders()
            .AddConsole()
            .AddDebug();
        // appsettings.json 里的 "Logging:LogLevel" 节会被 HostApplicationBuilder 读入。

        // ── 4. builder.Environment ───────────────────────────────────────
        // IHostEnvironment 构造后立即可用;这里演示分支(生产/开发 vs 设计期)。
        if (builder.Environment.IsDevelopment() || builder.Environment.IsDesignTime())
        {
            // 开发/设计期可加额外诊断服务
        }

        // ── 5. builder.Services —— 业务服务 ─────────────────────────────
        builder.Services.AddSingleton<IGreeter, Greeter>();

        // ClockHostedService 需要同时作为 singleton 和 hosted service:
        //   singleton:VM 要通过构造函数注入它,订阅 Tick 事件
        //   hosted   :需要 StartAsync/StopAsync 生命周期
        builder.Services.AddSingleton<ClockHostedService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<ClockHostedService>());

        // ── 6. builder.Services —— MVVM (约定优于配置) ─────────────────
        // AddViewsAndViewModels() 是 Jalium 版的 AddControllersWithViews():
        //   · 扫描当前程序集里所有 FrameworkElement 且名字以 Page/Window/UserControl/View
        //     结尾的类 → 注册成 Transient
        //   · 扫描所有名字以 ViewModel 结尾的类 → 注册成 Transient
        //   · 按名字配对:FooPage ↔ FooViewModel, MainWindow ↔ MainWindowViewModel,...
        // 手动等价写法:
        //     builder.Services.AddView<MainWindow>();
        //     builder.Services.AddView<HomePage, HomeViewModel>();
        //     builder.Services.AddView<MvvmPage, MvvmViewModel>();
        //     ...
        // 需要细粒度控制时传 options 回调:
        //     builder.Services.AddViewsAndViewModels(o => {
        //         o.ViewModelLifetime = ServiceLifetime.Singleton;   // VM 跨导航保留
        //         o.ViewFilter = t => t.Namespace?.StartsWith("Jalium.UI.HostingDemo.Views") == true;
        //     });
        builder.Services.AddViewsAndViewModels();

        // ── 7. Build ────────────────────────────────────────────────────
        // Build 只生成 Host,不构造 Application —— Application 的选择由下面的
        // app.UseApplication<App>() 决定,对齐 ASP.NET Core 的语义:builder 只
        // 注册服务,app 上的 Use* 才是激活。
        using var app = builder.Build();

        // host.Services 与 Application.Current.Services 指向同一容器
        var bootLogger = app.Services.GetRequiredService<ILoggerFactory>()
            .CreateLogger("Jalium.UI.HostingDemo.Program");
        bootLogger.LogInformation("JaliumApp ready · env={Env} · app={App}",
            builder.Environment.EnvironmentName, builder.Environment.ApplicationName);

        // ── 8. app.Use* —— 全部 Build 之后激活 ─────────────────────────
        //   UseApplication<App>() → 绑定自定义 Application 子类 (必须最先,会触
        //                           发构造 + AttachHost + ConfigureApplication 回调)
        //   UseJaliumMetrics()    → 启动 Jalium.UI Meter(FPS/帧时间采样)
        //   UseDevTools()         → 解锁 F12 inspector 窗口 + Ctrl+Shift+C 拾取器
        //   UseDebugHud()         → 解锁 F3 屏幕 HUD(帧时间/脏区/backend 信息)
        //   UseDeveloperTools()   = 以上两个 dev 工具一次调
        // 不调 UseApplication 就用默认 Application;不调 dev 工具 F12/F3 全是 no-op,
        // 正式发布版本直接删掉即可,不会带出任何调试窗口或 HUD 资源。
        app.UseApplication<App>();
        app.UseJaliumMetrics();
        app.UseDeveloperTools();

        // ── 9. Run ──────────────────────────────────────────────────────
        return app.Run();   // 先 StartAsync hosted service,再进 UI 消息循环,退出时 StopAsync
    }
}

using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;
using Xunit.Abstractions;

namespace Jalium.UI.Tests;

/// <summary>
/// 量化「拖动取色器实时改主题强调色」每一帧要在 UI 线程上付的成本，即
/// <see cref="ThemeManager.ApplyAccent(Color)"/> 的整条链路：更新调色板字典、发布变更、
/// 把新值送到每一个引用了它的订阅、并把用到它的元素标脏。
///
/// <para>为什么需要它：这条链路的代价按**页面规模**增长，而不是按变更的键数增长——
/// 早期实现里改一次强调色要重新求值整棵可视树的隐式样式，再全量扫一遍动态资源注册表。
/// 20 个卡片的窗口上还只是 5 ms，200 个卡片就到了几十毫秒，于是拖动直接掉到 40 fps 以下。
/// 没有这个基准，退化只会以「用户说卡」的形式被发现。</para>
///
/// <para>★测量口径统一走 <see cref="BenchmarkHarness"/>：自适应轮数 + 剔除 GC 污染样本 +
/// 报告半四分位距。每条输出末尾的 ±X% 就是该次测量的分辨率，小于它的差异一律不算数。
/// 判断优化收益**必须**跑 Release（<c>-c Release</c>）。</para>
///
/// <para>断言只挡数量级退化，真正的用途是把耗时打进测试输出供对比。</para>
/// </summary>
[Collection("Application")]
public class AccentDragCostBenchmarkTests
{
    private readonly ITestOutputHelper _output;

    public AccentDragCostBenchmarkTests(ITestOutputHelper output) => _output = output;

    private static void ResetApplicationState()
    {
        var currentField = typeof(Application).GetField("_current", BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);

        var resetMethod = typeof(ThemeManager).GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static);
        resetMethod?.Invoke(null, null);
    }

    private static int CountVisualDescendants(Visual root)
    {
        var total = 1;
        for (var i = 0; i < root.VisualChildrenCount; i++)
        {
            if (root.GetVisualChild(i) is Visual child)
            {
                total += CountVisualDescendants(child);
            }
        }

        return total;
    }

    /// <summary>模仿 Theme Studio 页面：卡片 + 按钮 + 滑块 + 开关 + 进度条 + 文本。</summary>
    private static FrameworkElement BuildThemedPage(int cardCount)
    {
        var stack = new StackPanel();

        for (var i = 0; i < cardCount; i++)
        {
            var body = new StackPanel();
            body.Children.Add(new TextBlock { Text = "Section title", FontSize = 18 });
            body.Children.Add(new TextBlock { Text = "Supporting description text for the card." });
            body.Children.Add(new Button { Content = "Primary action" });
            body.Children.Add(new Button { Content = "Secondary" });
            body.Children.Add(new CheckBox { Content = "Enabled" });
            body.Children.Add(new Slider { Minimum = 0, Maximum = 100, Value = 40, Width = 220 });
            body.Children.Add(new ProgressBar { Minimum = 0, Maximum = 100, Value = 84, Width = 220 });
            body.Children.Add(new ToggleSwitch());

            stack.Children.Add(new Border { Child = body, Padding = new Thickness(12), Margin = new Thickness(8) });
        }

        return new Border { Child = stack };
    }

    private static (Application App, Window Window, FrameworkElement Page) CreateThemedApp(int cardCount)
    {
        ResetApplicationState();
        var app = new Application();
        ThemeManager.Initialize(app);

        var page = BuildThemedPage(cardCount);
        var window = new Window
        {
            TitleBarStyle = WindowTitleBarStyle.Native,
            Width = 1200,
            Height = 900,
            Content = page
        };
        app.MainWindow = window;

        window.Measure(new Size(1200, 900));
        window.Arrange(new Rect(0, 0, 1200, 900));

        return (app, window, page);
    }

#if DEBUG
    private const string ConfigurationNote =
        "★ Debug 构建：绝对值偏高，只可与同为 Debug 的历史数据比较。判断收益请用 -c Release。";
#else
    private const string ConfigurationNote = "构建配置: Release";
#endif

    private void WriteHeader(Window window)
    {
        _output.WriteLine(ConfigurationNote);
        _output.WriteLine($"window visual elements: {CountVisualDescendants(window)}");

        var diagnostics = typeof(DependencyObject).Assembly
            .GetType("Jalium.UI.DynamicResourceBindingOperations")
            ?.GetMethod("GetRegistryDiagnostics", BindingFlags.NonPublic | BindingFlags.Static)
            ?.Invoke(null, null);
        if (diagnostics != null)
        {
            _output.WriteLine($"dynamic-resource registry (targets, subscriptions, slots): {diagnostics}");
        }

        _output.WriteLine(new string('-', 100));
    }

    private static Action MakeAccentDragStep()
    {
        var hue = 0;
        return () =>
        {
            // 每一步都必须是新颜色：ApplyAccent 对「颜色没变」有早退，重复同色测不到成本。
            hue = (hue + 7) % 360;
            ThemeManager.ApplyAccent(
                Color.FromRgb((byte)(hue % 256), (byte)((hue * 3) % 256), (byte)((hue * 5) % 256)));
        };
    }

    [Fact]
    public void AccentDrag_ApplyAccent_IsMeasured()
    {
        var (app, window, _) = CreateThemedApp(20);

        try
        {
            WriteHeader(window);

            var stats = BenchmarkHarness.Measure(MakeAccentDragStep(), warmupRounds: 5, minRounds: 15);
            _output.WriteLine(stats.Format("ApplyAccent (20 cards)"));

            // 60fps 拖动的单帧预算是 16.7 ms，而改调色板只是其中一小段。
            Assert.True(
                stats.Best < 5d,
                $"ApplyAccent P10 {stats.Best:F2} ms——这条路径又开始按页面规模付费了。");
        }
        finally
        {
            ResetApplicationState();
            GC.KeepAlive(app);
        }
    }

    /// <summary>
    /// 大页面规模（约 8000 个可视元素、8000+ 个动态资源订阅）。
    /// 这条用例存在的意义是钉住**扩展性**：定向刷新的代价必须跟着「命中的订阅数」走，
    /// 而不是跟着「页面总订阅数」走。一旦有人把定向路径改回全量扫描，这里立刻会翻十倍。
    /// </summary>
    [Fact]
    public void AccentDrag_LargePage_ScalesWithHitsNotPageSize()
    {
        var (app, window, _) = CreateThemedApp(200);

        try
        {
            WriteHeader(window);

            var stats = BenchmarkHarness.Measure(MakeAccentDragStep(), warmupRounds: 4, minRounds: 13);
            _output.WriteLine(stats.Format("ApplyAccent (200 cards)"));

            Assert.True(
                stats.Best < 8d,
                $"ApplyAccent P10 {stats.Best:F2} ms——大页面上的改色成本回到了「按订阅总数付费」。");
        }
        finally
        {
            ResetApplicationState();
            GC.KeepAlive(app);
        }
    }

    /// <summary>
    /// 一次拖动帧的完整 UI 线程成本：改强调色 + 让布局系统把该重排的重排一遍。
    /// 同时报告被标脏的元素数——它决定渲染侧要重录多少命令列表，也是「失效是否精确」的直接读数。
    /// </summary>
    [Fact]
    public void AccentDrag_DirtyFootprint_IsMeasured()
    {
        var (app, window, _) = CreateThemedApp(20);

        try
        {
            WriteHeader(window);
            var total = CountVisualDescendants(window);

            ThemeManager.ApplyAccent(Color.FromRgb(0x10, 0x20, 0x30));
            window.Measure(new Size(1200, 900));
            window.Arrange(new Rect(0, 0, 1200, 900));
            ClearRenderDirty(window);

            ThemeManager.ApplyAccent(Color.FromRgb(0xF0, 0x20, 0x30));
            _output.WriteLine(
                $"改一次强调色后：render-dirty {CountRenderDirty(window)}/{total}，" +
                $"measure-dirty {CountMeasureDirty(window)}/{total}");

            var step = MakeAccentDragStep();
            var stats = BenchmarkHarness.Measure(
                timedBody: () =>
                {
                    step();
                    window.Measure(new Size(1200, 900));
                    window.Arrange(new Rect(0, 0, 1200, 900));
                },
                warmupRounds: 5, minRounds: 15);

            _output.WriteLine(stats.Format("ApplyAccent + Measure/Arrange"));
        }
        finally
        {
            ResetApplicationState();
            GC.KeepAlive(app);
        }
    }

    private static readonly FieldInfo? s_renderDirtyField =
        typeof(Visual).GetField("_isRenderDirty", BindingFlags.NonPublic | BindingFlags.Instance);

    private static void ClearRenderDirty(Visual root)
    {
        s_renderDirtyField?.SetValue(root, false);
        for (var i = 0; i < root.VisualChildrenCount; i++)
        {
            if (root.GetVisualChild(i) is Visual child)
            {
                ClearRenderDirty(child);
            }
        }
    }

    private static int CountRenderDirty(Visual root)
    {
        var count = s_renderDirtyField?.GetValue(root) is true ? 1 : 0;
        for (var i = 0; i < root.VisualChildrenCount; i++)
        {
            if (root.GetVisualChild(i) is Visual child)
            {
                count += CountRenderDirty(child);
            }
        }

        return count;
    }

    private static int CountMeasureDirty(Visual root)
    {
        var count = root is UIElement { IsMeasureValid: false } ? 1 : 0;
        for (var i = 0; i < root.VisualChildrenCount; i++)
        {
            if (root.GetVisualChild(i) is Visual child)
            {
                count += CountMeasureDirty(child);
            }
        }

        return count;
    }
}

using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.DesktopDemo;

// Vulkan hover 残留最小重现（对齐 Jalium.One 属性面板事件列表的真实配方）：
//   1. 窗口 SystemBackdrop=Mica（composition surface —— Vulkan 走 per-pixel
//      alpha swapchain / BLT 呈现路径，与普通不透明窗口不同）；
//   2. hover 进入 = 半透明绿 #181E8E3E（IdeAccentSubtle），离开 = Transparent
//      （PropertyEventRow.OnRowMouseLeave 的原样行为：擦除完全依赖重放流里
//      父背景在该行 damage 区域内重画）；
//   3. hover 走行与滚动交错。
// 结束时所有行恢复 Transparent —— 屏幕上任何残余绿色 = 呈现路径残留。
// 运行方式: JALIUM_DEMO_WINDOW=hoverprobe [JALIUM_RENDER_BACKEND=vulkan|d3d12]
internal static class HoverProbeWindow
{
    public static Window Build()
    {
        const int rowCount = 40;
        const double rowHeight = 22.0;

        var hoverBrush = new SolidColorBrush(Color.FromArgb(0x18, 0x1E, 0x8E, 0x3E));

        var window = new Window
        {
            Title = "HoverProbe RUNNING",
            Width = 620,
            Height = 560,
            SystemBackdrop = WindowBackdropType.Mica,
            Background = new SolidColorBrush(Color.FromRgb(0, 0, 0))
        };

        var stack = new StackPanel();
        var rows = new Border[rowCount];
        for (int i = 0; i < rowCount; i++)
        {
            var row = new Border
            {
                Height = rowHeight,
                Background = Brushes.Transparent,
                Child = new TextBlock
                {
                    Text = $"EventRow {i:D2}  LostMouseCapture",
                    Foreground = new SolidColorBrush(Color.FromRgb(220, 224, 228)),
                    FontSize = 13,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(10, 0, 0, 0)
                }
            };
            // PropertyEventRow 的原样 hover 行为：Enter=半透明绿，Leave=Transparent。
            // 由真实输入链（hit-test → MouseEnter/Leave）驱动，外部脚本注入
            // WM_MOUSEMOVE / WM_MOUSEWHEEL 时走的就是真实路径。
            row.MouseEnter += (_, _) => row.Background = hoverBrush;
            row.MouseLeave += (_, _) => row.Background = Brushes.Transparent;
            rows[i] = row;
            stack.Children.Add(row);
        }

        var scroll = new ScrollViewer
        {
            Content = stack,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        // 模拟属性面板容器：深色面板底（行默认 Transparent，靠它显色）。
        var panel = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 36)),
            Child = scroll,
            Margin = new Thickness(8)
        };
        window.Content = panel;

        // 内部只做平滑滚动（把行从静止的"光标"下滚过）；hover 的点亮/恢复完全
        // 交给真实输入链 —— 外部脚本向本窗口注入 WM_MOUSEMOVE / WM_MOUSEWHEEL，
        // hit-test → MouseEnter/Leave →（系统 TrackMouseEvent 的 WM_MOUSELEAVE
        // 风暴也照常发生）。窗口标题携带阶段：RUNNING → SETTLE（滚动停止，等
        // 外部停喷）→ 外部脚本最后把光标移出行区并截图。
        int tick = 0;
        double offset = 0;
        double scrollDir = 3.0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        timer.Tick += (_, _) =>
        {
            tick++;
            if (tick > 320)
            {
                scroll.ScrollToVerticalOffset(0);
                timer.Stop();
                window.Title = "HoverProbe SETTLE";
                return;
            }
            offset += scrollDir;
            double maxOff = rowCount * rowHeight - 400;
            if (offset > maxOff) { offset = maxOff; scrollDir = -scrollDir; }
            if (offset < 0) { offset = 0; scrollDir = -scrollDir; }
            scroll.ScrollToVerticalOffset(offset);
        };
        window.Loaded += (_, _) => timer.Start();
        return window;
    }
}

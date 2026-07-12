using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

/// <summary>
/// 复现 IdeSegmentedRadio 的 hover 残留：当 RadioButton 同时被 IsMouseOver 和
/// IsChecked 触发器锁定 Background，鼠标移开后期望恢复成 IsChecked 的值（紫色），
/// 而不是留下 hover 的浅色或滑回 baseline。
/// </summary>
public class SegmentedRadioHoverResidueTests
{
    private static readonly Brush HoverBrush = new SolidColorBrush(Color.FromArgb(255, 0xF3, 0xF4, 0xF6));
    private static readonly Brush CheckedBrush = new SolidColorBrush(Color.FromArgb(255, 0x7C, 0x5C, 0xFA));

    [Fact]
    public void IsMouseOver_Then_IsChecked_Then_Leave_ShouldKeepCheckedBackground()
    {
        var radio = BuildSegmentedRadio();
        radio.Measure(new Size(100, 30));
        radio.Arrange(new Rect(0, 0, 100, 30));

        var seg = Assert.IsAssignableFrom<Border>(radio.GetVisualChild(0));

        // 1) 鼠标进入：IsMouseOver 触发器激活
        radio.SetIsMouseOver(true);
        Assert.Equal(HoverBrush, seg.Background);

        // 2) 点击：IsChecked 触发器激活（覆盖 hover 的 background）
        radio.IsChecked = true;
        Assert.Equal(CheckedBrush, seg.Background);

        // 3) 鼠标离开：IsMouseOver 触发器失活，但 IsChecked 仍激活 → Background 应为 Checked
        radio.SetIsMouseOver(false);
        Assert.Equal(CheckedBrush, seg.Background);
    }

    /// <summary>
    /// WPF precedence: trigger collection 中 *后定义* 的 trigger 优先。jalxaml 中
    /// IsMouseOver 在前，IsChecked 在后，所以即便 hover 在 checked 之后激活，hover 也不能
    /// 覆盖 checked 的紫色背景。这是用户报告的"鼠标移到 checked 按钮变灰"的核心 bug。
    /// </summary>
    [Fact]
    public void IsChecked_Then_IsMouseOver_ShouldKeepCheckedBackground_NotHover()
    {
        var radio = BuildSegmentedRadio();
        radio.Measure(new Size(100, 30));
        radio.Arrange(new Rect(0, 0, 100, 30));

        var seg = Assert.IsAssignableFrom<Border>(radio.GetVisualChild(0));

        radio.IsChecked = true;
        Assert.Equal(CheckedBrush, seg.Background);

        // 关键断言：hover 在 checked 之后激活 — WPF 中 collection 后定义的 IsChecked 仍胜出
        radio.SetIsMouseOver(true);
        Assert.Equal(CheckedBrush, seg.Background);

        radio.SetIsMouseOver(false);
        Assert.Equal(CheckedBrush, seg.Background);
    }

    [Fact]
    public void IsMouseOver_Without_IsChecked_Leave_ShouldRestoreBaseline()
    {
        var radio = BuildSegmentedRadio();
        radio.Measure(new Size(100, 30));
        radio.Arrange(new Rect(0, 0, 100, 30));

        var seg = Assert.IsAssignableFrom<Border>(radio.GetVisualChild(0));

        var baselineBackground = seg.Background;

        radio.SetIsMouseOver(true);
        Assert.Equal(HoverBrush, seg.Background);

        radio.SetIsMouseOver(false);
        Assert.Equal(baselineBackground, seg.Background);
    }

    [Fact]
    public void WithTransition_IsChecked_Then_IsMouseOver_Then_Leave_ShouldKeepCheckedBaseValue()
    {
        var radio = BuildSegmentedRadio(withTransition: true);
        radio.Measure(new Size(100, 30));
        radio.Arrange(new Rect(0, 0, 100, 30));

        var seg = Assert.IsAssignableFrom<Border>(radio.GetVisualChild(0));

        radio.IsChecked = true;
        radio.SetIsMouseOver(true);
        radio.SetIsMouseOver(false);

        // Base value（去掉 transition 动画帧）应当是 checked 触发器的值
        var baseValue = seg.GetEffectiveBaseValue(Border.BackgroundProperty);
        Assert.Equal(CheckedBrush, baseValue);
    }

    /// <summary>
    /// hover-in 路径下，IsMouseOver 激活时若 IsChecked (collection 后定义) 已激活，hover
    /// trigger 必须被抑制 — 不能写 layer，也不能触发 PropertyChanged 引起多余 transition。
    /// </summary>
    [Fact]
    public void HoverEnter_OnAlreadyCheckedRadio_ShouldNotChangeBackground()
    {
        var radio = BuildSegmentedRadio();
        radio.Measure(new Size(100, 30));
        radio.Arrange(new Rect(0, 0, 100, 30));

        var seg = Assert.IsAssignableFrom<Border>(radio.GetVisualChild(0));

        radio.IsChecked = true;
        Assert.Equal(CheckedBrush, seg.Background);

        var changeCount = 0;
        seg.PropertyChangedInternal += (dp, _, _) =>
        {
            if (dp == Border.BackgroundProperty)
                changeCount++;
        };

        // hover 进入：旧实现会触发一次 PropertyChanged 把 background 从 Accent 改成 SubtleBg
        // (hover 残留 bug 的根因)。新实现按 WPF 语义抑制 hover trigger 的写入。
        radio.SetIsMouseOver(true);
        Assert.Equal(0, changeCount);
        Assert.Equal(CheckedBrush, seg.Background);

        // hover 退出：同样不能改 background
        radio.SetIsMouseOver(false);
        Assert.Equal(0, changeCount);
        Assert.Equal(CheckedBrush, seg.Background);
    }

    /// <summary>
    /// hover 先激活，后变 checked：hover 已经把 layer 写成 SubtleBg，但 IsChecked
    /// (collection 后) 激活时必须覆盖。后续 hover 退出，IsChecked 仍激活 — 必须保持 Accent。
    /// </summary>
    [Fact]
    public void HoverThenCheck_CheckedShouldOverride_AndSurviveHoverExit()
    {
        var radio = BuildSegmentedRadio();
        radio.Measure(new Size(100, 30));
        radio.Arrange(new Rect(0, 0, 100, 30));

        var seg = Assert.IsAssignableFrom<Border>(radio.GetVisualChild(0));

        radio.SetIsMouseOver(true);
        Assert.Equal(HoverBrush, seg.Background);

        radio.IsChecked = true;
        Assert.Equal(CheckedBrush, seg.Background);

        radio.SetIsMouseOver(false);
        Assert.Equal(CheckedBrush, seg.Background);
    }

    /// <summary>
    /// later sibling (IsChecked) 失活时，earlier sibling (IsMouseOver) 仍激活 —
    /// 应当 re-apply earlier 的 hover 值。
    /// </summary>
    [Fact]
    public void UncheckWhileHovering_ShouldFallBackToHoverBackground()
    {
        var radio = BuildSegmentedRadio();
        radio.Measure(new Size(100, 30));
        radio.Arrange(new Rect(0, 0, 100, 30));

        var seg = Assert.IsAssignableFrom<Border>(radio.GetVisualChild(0));

        radio.IsChecked = true;
        radio.SetIsMouseOver(true);
        // hover 被 checked 抑制，仍显示 checked 紫色
        Assert.Equal(CheckedBrush, seg.Background);

        // 取消 checked：layer 应回落到还激活的 hover trigger 的 SubtleBg
        radio.IsChecked = false;
        Assert.Equal(HoverBrush, seg.Background);
    }

    /// <summary>
    /// 构造 IdeSegmentedRadioBase 的等价：含 SegRoot Border 和 IsMouseOver / IsChecked 两个
    /// ControlTemplate.Triggers。背景色取自 OpenedProjectView 的 IdeLightSubtleBackground /
    /// IdeLightAccent。
    /// </summary>
    private static RadioButton BuildSegmentedRadio(bool withTransition = false)
    {
        var template = new ControlTemplate(typeof(RadioButton));
        template.SetVisualTree(() =>
        {
            var border = new Border
            {
                Name = "SegRoot",
                Background = new SolidColorBrush(Colors.Transparent)
            };
            if (withTransition)
            {
                border.TransitionProperty = new TransitionPropertyCollection(new[] { "Background" });
                border.SetValue(UIElement.TransitionDurationProperty, new Duration(TimeSpan.FromMilliseconds(120)));
            }
            return border;
        });

        template.Triggers.Add(new Trigger
        {
            Property = UIElement.IsMouseOverProperty,
            Value = true,
            Setters =
            {
                new Setter { TargetName = "SegRoot", Property = Border.BackgroundProperty, Value = HoverBrush }
            }
        });

        template.Triggers.Add(new Trigger
        {
            Property = ToggleButton.IsCheckedProperty,
            Value = true,
            Setters =
            {
                new Setter { TargetName = "SegRoot", Property = Border.BackgroundProperty, Value = CheckedBrush }
            }
        });

        return new RadioButton { Template = template };
    }
}

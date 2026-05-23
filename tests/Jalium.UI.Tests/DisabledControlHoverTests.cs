using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

/// <summary>
/// 验证「禁用控件不响应 hover」：IsMouseOver / IsPressed 这两个交互态对
/// IsEnabled=false 的控件根本不成立。修复前,几乎所有控件模板的单条件
/// &lt;Trigger Property="IsMouseOver"&gt; 会让禁用控件继续显示 hover 样式;
/// 修复在 UIElement.SetIsMouseOver / SetIsPressed / OnIsEnabledChanged 底层统一根治。
/// </summary>
public class DisabledControlHoverTests
{
    private static readonly Brush HoverBrush = new SolidColorBrush(Color.FromArgb(255, 0xF3, 0xF4, 0xF6));

    // ── 状态层:SetIsMouseOver / SetIsPressed 拒绝在禁用控件上置 true ──

    [Fact]
    public void DisabledElement_SetIsMouseOver_StaysFalse()
    {
        var border = new Border { IsEnabled = false };

        border.SetIsMouseOver(true);

        Assert.False(border.IsMouseOver);
    }

    [Fact]
    public void DisabledElement_SetIsPressed_StaysFalse()
    {
        var border = new Border { IsEnabled = false };

        border.SetIsPressed(true);

        Assert.False(border.IsPressed);
    }

    [Fact]
    public void EnabledElement_SetIsMouseOver_StillWorks()
    {
        var border = new Border();

        border.SetIsMouseOver(true);
        Assert.True(border.IsMouseOver);

        border.SetIsMouseOver(false);
        Assert.False(border.IsMouseOver);
    }

    // ── 残留清理:控件在 hover/pressed 状态下被禁用,交互态必须被清掉 ──

    [Fact]
    public void HoveredElement_WhenDisabled_ClearsIsMouseOver()
    {
        var border = new Border();
        border.SetIsMouseOver(true);
        Assert.True(border.IsMouseOver);

        border.IsEnabled = false;

        Assert.False(border.IsMouseOver);
    }

    [Fact]
    public void PressedElement_WhenDisabled_ClearsIsPressed()
    {
        var border = new Border();
        border.SetIsPressed(true);
        Assert.True(border.IsPressed);

        border.IsEnabled = false;

        Assert.False(border.IsPressed);
    }

    // ── 父链:有效 IsEnabled 走父链,祖先禁用对整个子树同样生效 ──

    [Fact]
    public void ChildOfDisabledAncestor_CannotEnterHover()
    {
        var parent = new Border { IsEnabled = false };
        var child = new Border();
        parent.Child = child;

        // child 本地 IsEnabled=true,但有效值因父而 false。
        child.SetIsMouseOver(true);

        Assert.False(child.IsMouseOver);
    }

    [Fact]
    public void DisablingAncestor_ClearsDescendantIsMouseOver()
    {
        var parent = new Border();
        var child = new Border();
        parent.Child = child;

        child.SetIsMouseOver(true);
        parent.SetIsMouseOver(true);
        Assert.True(child.IsMouseOver);
        Assert.True(parent.IsMouseOver);

        parent.IsEnabled = false;

        Assert.False(parent.IsMouseOver);
        Assert.False(child.IsMouseOver);
    }

    // ── 端到端:单条件 IsMouseOver 触发器在禁用控件上不再激活 ──

    [Fact]
    public void DisabledControl_Hover_DoesNotApplyHoverTrigger()
    {
        var radio = BuildHoverControl();
        radio.IsEnabled = false;
        radio.Measure(new Size(100, 30));
        radio.Arrange(new Rect(0, 0, 100, 30));

        var root = (Border)radio.GetVisualChild(0);
        var baseline = root.Background;

        radio.SetIsMouseOver(true);

        Assert.False(radio.IsMouseOver);
        Assert.Equal(baseline, root.Background);
    }

    [Fact]
    public void HoveredControl_WhenDisabled_RevertsHoverTrigger()
    {
        var radio = BuildHoverControl();
        radio.Measure(new Size(100, 30));
        radio.Arrange(new Rect(0, 0, 100, 30));

        var root = (Border)radio.GetVisualChild(0);
        var baseline = root.Background;

        radio.SetIsMouseOver(true);
        Assert.Equal(HoverBrush, root.Background);

        // 禁用 → IsMouseOver 被清 → hover 触发器失活 → 背景回落 baseline
        radio.IsEnabled = false;

        Assert.False(radio.IsMouseOver);
        Assert.Equal(baseline, root.Background);
    }

    /// <summary>
    /// 构造一个 ControlTemplate 只含单条件 &lt;Trigger Property="IsMouseOver" Value="True"&gt;,
    /// 等价于 Calendar / DataGrid / TextControls 等绝大多数控件主题的 hover 触发器写法。
    /// </summary>
    private static RadioButton BuildHoverControl()
    {
        var template = new ControlTemplate(typeof(RadioButton));
        template.SetVisualTree(() => new Border
        {
            Name = "Root",
            Background = new SolidColorBrush(Colors.Transparent)
        });

        template.Triggers.Add(new Trigger
        {
            Property = UIElement.IsMouseOverProperty,
            Value = true,
            Setters =
            {
                new Setter { TargetName = "Root", Property = Border.BackgroundProperty, Value = HoverBrush }
            }
        });

        return new RadioButton { Template = template };
    }
}

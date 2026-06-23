using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Shapes;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;
using Xunit;
using Path = Jalium.UI.Controls.Shapes.Path;

namespace Jalium.UI.Tests;

/// <summary>
/// 守护 ComboBox 下拉箭头修复（2026-06）。
///
/// 根因（渲染层埋点 PUSH#/GEOM# 实测）：箭头原用 0°→180° RenderTransform 翻转，180° 产生
/// (-1,0,0,-1) 负对角矩阵；managed 侧矩阵与几何坐标全对，但 native FillPath 对该矩阵光栅化
/// 有 bug —— 把路径整体平移一个 Offset，箭头飞到下方/消失。Vello/Impeller 两引擎共有。
///
/// 修复：不用 RenderTransform，改【逐帧把 chevron 几何旋转到当前角度并烘进坐标】，
/// 向 native 永远提交正常正定路径，既保留平滑翻转又绕开该 bug。_chevronBase 预拉伸到方框、
/// 居中（Stretch=None），绕中心旋转尺寸恒定。
/// </summary>
[Collection("Application")]
public class ComboBoxArrowRotationTests
{
    private static void ResetApplicationState()
    {
        typeof(Application).GetField("_current", BindingFlags.NonPublic | BindingFlags.Static)?.SetValue(null, null);
        typeof(ThemeManager).GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static)?.Invoke(null, null);
    }

    private static Path? GetArrow(ComboBox combo) =>
        typeof(ComboBox).GetField("_arrowPath", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(combo) as Path;

    // 同步设角度（绕开 DispatcherTimer —— 单测无消息循环，timer 不会 tick）。
    private static void ApplyAngle(ComboBox combo, double angle) =>
        typeof(ComboBox).GetMethod("ApplyArrowAngle", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(combo, new object[] { angle });

    private static string GetConst(string name) =>
        (string)typeof(ComboBox).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

    private static ComboBox BuildAndApplyTemplate()
    {
        var container = new StackPanel { Width = 240, Height = 80 };
        var combo = new ComboBox { Width = 200, MinHeight = 34 };
        container.Children.Add(combo);
        container.Measure(new Size(240, 80));
        container.Arrange(new Rect(0, 0, 240, 80));
        return combo;
    }

    /// <summary>修复后箭头不再持有 RotateTransform —— 180° 旋转矩阵会触发 native FillPath 的 bug。</summary>
    [Fact]
    public void ComboBox_Arrow_DoesNotUseRotateTransform()
    {
        ResetApplicationState();
        _ = new Application();
        try
        {
            var combo = BuildAndApplyTemplate();
            var arrow = GetArrow(combo);
            Assert.NotNull(arrow);
            Assert.False(arrow!.RenderTransform is RotateTransform,
                "箭头不应持有 RotateTransform；翻转改用逐帧旋转几何以绕开 native 180° 光栅化 bug。");
        }
        finally { ResetApplicationState(); }
    }

    /// <summary>翻转 = 逐帧旋转几何：0°/180° 尺寸相同、中心不动；90° 时 bbox 宽高互换（确为真旋转）。</summary>
    [Fact]
    public void ComboBox_ArrowFlip_RotatesGeometry_PreservingSizeAndCenter()
    {
        ResetApplicationState();
        _ = new Application();
        try
        {
            var combo = BuildAndApplyTemplate();
            var arrow = GetArrow(combo);
            Assert.NotNull(arrow);

            ApplyAngle(combo, 0);
            var b0 = (arrow!.Geometry as PathGeometry)!.Bounds;

            ApplyAngle(combo, 180);
            var b180 = (arrow.Geometry as PathGeometry)!.Bounds;

            // 旋转保尺寸 + bbox 中心不变。
            Assert.Equal(b0.Width, b180.Width, 1);
            Assert.Equal(b0.Height, b180.Height, 1);
            Assert.Equal(b0.X + b0.Width / 2, b180.X + b180.Width / 2, 1);
            Assert.Equal(b0.Y + b0.Height / 2, b180.Y + b180.Height / 2, 1);

            // 90° → bbox 宽高互换，证明是真旋转（而非原地不动）。
            ApplyAngle(combo, 90);
            var b90 = (arrow.Geometry as PathGeometry)!.Bounds;
            Assert.Equal(b0.Width, b90.Height, 1);
            Assert.Equal(b0.Height, b90.Width, 1);
        }
        finally { ResetApplicationState(); }
    }

    /// <summary>降级用的两套静态 chevron 常量都能解析成非空、等尺寸路径（朝上=朝下绕原点 180°）。</summary>
    [Fact]
    public void ComboBox_BothChevronGeometries_ParseToEqualSizedNonEmptyPaths()
    {
        var down = Geometry.Parse(GetConst("ArrowDownData")) as PathGeometry;
        var up = Geometry.Parse(GetConst("ArrowUpData")) as PathGeometry;

        Assert.NotNull(down);
        Assert.NotNull(up);
        Assert.True(down!.Figures.Count > 0);
        Assert.True(up!.Figures.Count > 0);
        Assert.Equal(down.Bounds.Width, up.Bounds.Width, 1);
        Assert.Equal(down.Bounds.Height, up.Bounds.Height, 1);
    }
}

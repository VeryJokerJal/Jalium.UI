using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

/// <summary>
/// <see cref="ThemeManager.ApplyAccent(Color)"/> 改成「就地更新 accent 字典 + 精确键通知」
/// 之后，必须证明它仍然把新颜色送到了活树上的每一类订阅者：顶层键、变体（ThemeDictionaries）
/// 键、以及经由合并字典冒泡的通知本身。这些断言就是那条快路径的正确性护栏。
/// </summary>
[Collection("Application")]
public class AccentInPlaceUpdateTests
{
    private static void ResetApplicationState()
    {
        var currentField = typeof(Application).GetField("_current", BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);

        var resetMethod = typeof(ThemeManager).GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static);
        resetMethod?.Invoke(null, null);
    }

    private static Color ColorOf(object? brush) => brush switch
    {
        SolidColorBrush solid => solid.Color,
        LinearGradientBrush gradient => gradient.GradientStops[0].Color,
        _ => throw new InvalidOperationException($"意外的画刷类型: {brush?.GetType().Name ?? "null"}"),
    };

    [Fact]
    public void ApplyAccent_UpdatesTopLevelAndVariantKeys_OnLiveTree()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            ThemeManager.Initialize(app);
            ResourceDictionary.CurrentThemeKey = ThemeVariant.Dark.ToString();

            var topLevelTarget = new Border();
            var variantTarget = new Border();
            var window = new Window
            {
                TitleBarStyle = WindowTitleBarStyle.Native,
                Width = 200,
                Height = 200,
            };
            var panel = new StackPanel();
            panel.Children.Add(topLevelTarget);
            panel.Children.Add(variantTarget);
            window.Content = panel;
            app.MainWindow = window;

            topLevelTarget.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            variantTarget.SetResourceReference(Border.BackgroundProperty, "AccentFillColorDefaultBrush");

            window.Measure(new Size(200, 200));
            window.Arrange(new Rect(0, 0, 200, 200));

            var beforeTop = ColorOf(topLevelTarget.Background);
            var beforeVariant = ColorOf(variantTarget.Background);

            ThemeManager.ApplyAccent(Color.FromRgb(0xFF, 0x00, 0x00));

            var afterTop = ColorOf(topLevelTarget.Background);
            var afterVariant = ColorOf(variantTarget.Background);

            Assert.NotEqual(beforeTop, afterTop);
            Assert.NotEqual(beforeVariant, afterVariant);

            // 顶层 AccentBrush 是围绕强调色的两级渐变，起点比强调色暗一点。
            Assert.True(afterTop.R > afterTop.G && afterTop.R > afterTop.B, $"顶层键没跟上红色强调色: {afterTop}");
            // Dark 变体的 AccentFillColorDefault 是 accent 掺白 34%，仍应保持红色主导。
            Assert.True(afterVariant.R > afterVariant.G && afterVariant.R > afterVariant.B,
                $"变体键没跟上红色强调色: {afterVariant}");

            // 再改一次，确认就地更新可以反复生效（不是只有第一次有效）。
            ThemeManager.ApplyAccent(Color.FromRgb(0x00, 0x00, 0xFF));
            var blueTop = ColorOf(topLevelTarget.Background);
            var blueVariant = ColorOf(variantTarget.Background);
            Assert.True(blueTop.B > blueTop.R, $"第二次更新没生效: {blueTop}");
            Assert.True(blueVariant.B > blueVariant.R, $"第二次变体更新没生效: {blueVariant}");
        }
        finally
        {
            ResourceDictionary.CurrentThemeKey = null;
            ResetApplicationState();
        }
    }

    [Fact]
    public void ApplyAccent_WithUnchangedColor_IsANoOp()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            ThemeManager.Initialize(app);

            var notifications = 0;
            app.Resources.ChangedWithKeys += (_, _) => notifications++;

            var accent = Color.FromRgb(0x12, 0x34, 0x56);
            ThemeManager.ApplyAccent(accent);
            var afterFirst = notifications;
            Assert.True(afterFirst > 0, "首次应用新强调色必须发出通知。");

            ThemeManager.ApplyAccent(accent);
            ThemeManager.ApplyAccent(accent);

            Assert.Equal(afterFirst, notifications);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    /// <summary>
    /// 合并字典里的单键变更必须带着键集合冒泡到宿主，而不是退化成「所有键都变了」。
    /// 这是整条定向刷新路径的前提。
    /// </summary>
    [Fact]
    public void MergedDictionary_KeyChange_BubblesWithKeys()
    {
        var host = new ResourceDictionary();
        var merged = new ResourceDictionary();
        host.MergedDictionaries.Add(merged);

        ResourceDictionary.ResourcesChangedEventArgs? last = null;
        host.ChangedWithKeys += (_, e) => last = e;

        merged["OnlyThisKey"] = "value";

        Assert.NotNull(last);
        Assert.NotNull(last!.ChangedKeys);
        Assert.Equal(["OnlyThisKey"], last.ChangedKeys!);

        // 结构性变化仍然是「全部键」。
        last = null;
        host.MergedDictionaries.Add(new ResourceDictionary());
        Assert.NotNull(last);
        Assert.Null(last!.ChangedKeys);
    }

    /// <summary>
    /// 嵌套两层合并字典时键信息也必须保持。ThemeManager 的 accent 字典就挂在
    /// Application.Resources 之下，而应用自己的字典还可能再嵌一层。
    /// </summary>
    [Fact]
    public void MergedDictionary_KeyChange_BubblesThroughNestedLevels()
    {
        var host = new ResourceDictionary();
        var middle = new ResourceDictionary();
        var leaf = new ResourceDictionary();
        host.MergedDictionaries.Add(middle);
        middle.MergedDictionaries.Add(leaf);

        ResourceDictionary.ResourcesChangedEventArgs? last = null;
        host.ChangedWithKeys += (_, e) => last = e;

        leaf["DeepKey"] = 42;

        Assert.NotNull(last);
        Assert.NotNull(last!.ChangedKeys);
        Assert.Equal(["DeepKey"], last.ChangedKeys!);
    }

    /// <summary>
    /// 被移除的合并字典不能再把变更推给旧宿主——否则每次替换都会留下一个幽灵订阅。
    /// </summary>
    [Fact]
    public void MergedDictionary_AfterRemoval_StopsNotifying()
    {
        var host = new ResourceDictionary();
        var merged = new ResourceDictionary();
        host.MergedDictionaries.Add(merged);

        var count = 0;
        host.ChangedWithKeys += (_, _) => count++;

        merged["A"] = 1;
        var afterFirst = count;

        host.MergedDictionaries.Remove(merged);
        var afterRemoval = count; // Remove 本身是结构性变化，会通知一次

        merged["B"] = 2;
        Assert.Equal(afterRemoval, count);
        Assert.True(afterFirst > 0);
    }

    /// <summary>
    /// ApplyBrandTheme 会在一个延迟通知作用域里同时改主题变体、强调色与字体。强调色现在走
    /// 「就地更新 + 精确键」，不再发出「所有键都变了」——所以必须证明**变体切换**本身仍然
    /// 传播到了活树：一个绑定到纯变体键（与强调色、字体都无关）的元素必须跟着换值。
    ///
    /// <para>这条用例是 <c>ApplyBrandTheme</c> 收尾不再补一次全树广播的依据；配套的
    /// <c>ApplyBrandTheme_ShouldReevaluateEachLiveRoot_ExactlyOnce</c> 从另一侧钉住
    /// 「也不能广播两次」。</para>
    /// </summary>
    [Fact]
    public void ApplyBrandTheme_VariantSwitch_ReachesLiveTree()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            ThemeManager.Initialize(app);
            ThemeManager.ApplyTheme(ThemeVariant.Dark);

            var target = new Border();
            var window = new Window { TitleBarStyle = WindowTitleBarStyle.Native, Content = target };
            app.MainWindow = window;

            // TextPrimary 是纯粹的变体键：Dark / Light 各一份，与强调色和字体都无关。
            target.SetResourceReference(Border.BackgroundProperty, "TextPrimary");
            window.Measure(new Size(200, 200));
            window.Arrange(new Rect(0, 0, 200, 200));

            var darkValue = ColorOf(target.Background);

            ThemeManager.ApplyBrandTheme(new BrandThemeOptions
            {
                Theme = ThemeVariant.Light,
                AccentColor = Color.FromRgb(0x40, 0xB8, 0x5A),
            });

            var lightValue = ColorOf(target.Background);
            Assert.NotEqual(darkValue, lightValue);
            Assert.Equal(ThemeVariant.Light, ThemeManager.CurrentTheme);
        }
        finally
        {
            ResourceDictionary.CurrentThemeKey = null;
            ResetApplicationState();
        }
    }
}

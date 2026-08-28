using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

/// <summary>
/// 带键的资源刷新走的是「资源键 → 订阅」反向索引，只碰真正引用了变更键的订阅。
/// 索引条目只增不删、靠使用时三重校验剔除失效项，所以这些用例覆盖的正是那些
/// 「订阅已经不是索引记录的那个了」的路径：属性被清除、键被换掉、元素被摘走。
/// </summary>
[Collection("Application")]
public class DynamicResourceKeyIndexTests
{
    private static void ResetApplicationState()
    {
        var currentField = typeof(Application).GetField("_current", BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);

        var resetMethod = typeof(ThemeManager).GetMethod("Reset", BindingFlags.NonPublic | BindingFlags.Static);
        resetMethod?.Invoke(null, null);
    }

    private static Color ColorOf(Brush? brush) =>
        Assert.IsType<SolidColorBrush>(brush).Color;

    [Fact]
    public void TargetedRefresh_UpdatesSubscriberOfChangedKey()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            app.Resources["ProbeBrush"] = new SolidColorBrush(Color.FromRgb(1, 2, 3));

            var target = new Border();
            var window = new Window { TitleBarStyle = WindowTitleBarStyle.Native, Content = target };
            app.MainWindow = window;
            target.SetResourceReference(Border.BackgroundProperty, "ProbeBrush");

            Assert.Equal(Color.FromRgb(1, 2, 3), ColorOf(target.Background));

            app.Resources["ProbeBrush"] = new SolidColorBrush(Color.FromRgb(9, 9, 9));
            Assert.Equal(Color.FromRgb(9, 9, 9), ColorOf(target.Background));
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void TargetedRefresh_IgnoresSubscribersOfOtherKeys()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            app.Resources["KeyA"] = new SolidColorBrush(Color.FromRgb(10, 0, 0));
            app.Resources["KeyB"] = new SolidColorBrush(Color.FromRgb(0, 10, 0));

            var a = new Border();
            var b = new Border();
            var panel = new StackPanel();
            panel.Children.Add(a);
            panel.Children.Add(b);
            var window = new Window { TitleBarStyle = WindowTitleBarStyle.Native, Content = panel };
            app.MainWindow = window;

            a.SetResourceReference(Border.BackgroundProperty, "KeyA");
            b.SetResourceReference(Border.BackgroundProperty, "KeyB");

            var bBefore = b.Background;
            app.Resources["KeyA"] = new SolidColorBrush(Color.FromRgb(200, 0, 0));

            Assert.Equal(Color.FromRgb(200, 0, 0), ColorOf(a.Background));
            Assert.Same(bBefore, b.Background);
        }
        finally
        {
            ResetApplicationState();
        }
    }

    /// <summary>
    /// 换键之后，旧键的变更不能再影响这个属性；新键的变更必须影响。
    /// 索引里旧条目还在，靠使用时校验剔除。
    /// </summary>
    [Fact]
    public void AfterRebinding_OldKeyNoLongerDrivesTheProperty()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            app.Resources["OldKey"] = new SolidColorBrush(Color.FromRgb(10, 0, 0));
            app.Resources["NewKey"] = new SolidColorBrush(Color.FromRgb(0, 0, 10));

            var target = new Border();
            var window = new Window { TitleBarStyle = WindowTitleBarStyle.Native, Content = target };
            app.MainWindow = window;

            target.SetResourceReference(Border.BackgroundProperty, "OldKey");
            Assert.Equal(Color.FromRgb(10, 0, 0), ColorOf(target.Background));

            target.SetResourceReference(Border.BackgroundProperty, "NewKey");
            Assert.Equal(Color.FromRgb(0, 0, 10), ColorOf(target.Background));

            // 旧键再变，不应该把属性拉回去。
            app.Resources["OldKey"] = new SolidColorBrush(Color.FromRgb(255, 0, 0));
            Assert.Equal(Color.FromRgb(0, 0, 10), ColorOf(target.Background));

            // 新键变，必须跟上。
            app.Resources["NewKey"] = new SolidColorBrush(Color.FromRgb(0, 0, 255));
            Assert.Equal(Color.FromRgb(0, 0, 255), ColorOf(target.Background));
        }
        finally
        {
            ResetApplicationState();
        }
    }

    [Fact]
    public void RepeatedRebinding_DoesNotAccumulateStaleWork()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            app.Resources["Churn"] = new SolidColorBrush(Color.FromRgb(1, 1, 1));

            var target = new Border();
            var window = new Window { TitleBarStyle = WindowTitleBarStyle.Native, Content = target };
            app.MainWindow = window;

            // 反复在同一槽位上换键：索引条目只增不删，必须靠作废 + 压缩收敛，
            // 否则模板反复重建的页面上索引会无限膨胀。
            for (var i = 0; i < 200; i++)
            {
                app.Resources[$"Churn{i}"] = new SolidColorBrush(Color.FromRgb((byte)i, 0, 0));
                target.SetResourceReference(Border.BackgroundProperty, $"Churn{i}");
            }

            target.SetResourceReference(Border.BackgroundProperty, "Churn");
            app.Resources["Churn"] = new SolidColorBrush(Color.FromRgb(7, 7, 7));
            Assert.Equal(Color.FromRgb(7, 7, 7), ColorOf(target.Background));

            var index = GetKeyIndex();
            for (var i = 0; i < 200; i++)
            {
                var entries = index[$"Churn{i}"];
                if (entries == null)
                {
                    continue;
                }

                // 每个被换掉的键最多留下一个待压缩的死条目，绝不该按次数累积。
                var count = ((System.Collections.ICollection)entries).Count;
                Assert.True(count <= 1, $"键 Churn{i} 的索引条目累积到了 {count} 个。");
            }
        }
        finally
        {
            ResetApplicationState();
        }
    }

    /// <summary>
    /// 元素被摘出树并回收后，索引里指向它的条目不能让刷新抛异常。
    /// </summary>
    [Fact]
    public void CollectedTargets_DoNotBreakTargetedRefresh()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            app.Resources["Ghost"] = new SolidColorBrush(Color.FromRgb(1, 1, 1));

            var survivor = new Border();
            var panel = new StackPanel();
            panel.Children.Add(survivor);
            var window = new Window { TitleBarStyle = WindowTitleBarStyle.Native, Content = panel };
            app.MainWindow = window;
            survivor.SetResourceReference(Border.BackgroundProperty, "Ghost");

            for (var i = 0; i < 50; i++)
            {
                var doomed = new Border();
                doomed.SetResourceReference(Border.BackgroundProperty, "Ghost");
            }

            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);

            app.Resources["Ghost"] = new SolidColorBrush(Color.FromRgb(5, 5, 5));
            Assert.Equal(Color.FromRgb(5, 5, 5), ColorOf(survivor.Background));
        }
        finally
        {
            ResetApplicationState();
        }
    }

    private static System.Collections.IDictionary GetKeyIndex()
    {
        var type = typeof(DependencyObject).Assembly.GetType("Jalium.UI.DynamicResourceBindingOperations")!;
        var field = type.GetField("KeyIndex", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        return (System.Collections.IDictionary)field!.GetValue(null)!;
    }

    /// <summary>
    /// 样式 / 模板应用会把 markup 上的 {DynamicResource} 订阅「提升」到样式层，也就是换掉
    /// 它在订阅表里的键。索引条目记的正是那个键，因此换层时必须跟着重新登记——否则这条
    /// 订阅会永久掉出定向刷新，主题调色板变化再也到不了它。
    /// </summary>
    [Fact]
    public void AfterPromotingToStyleLayer_TargetedRefreshStillReaches()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            app.Resources["PromotedKey"] = new SolidColorBrush(Color.FromRgb(1, 1, 1));

            var target = new Border();
            var window = new Window { TitleBarStyle = WindowTitleBarStyle.Native, Content = target };
            app.MainWindow = window;
            target.SetResourceReference(Border.BackgroundProperty, "PromotedKey");

            var operations = typeof(DependencyObject).Assembly
                .GetType("Jalium.UI.DynamicResourceBindingOperations")!;
            var promote = operations.GetMethod(
                "PromoteDynamicResourcesToLayer", BindingFlags.NonPublic | BindingFlags.Static)!;
            var layerType = promote.GetParameters()[1].ParameterType;
            var layer = Enum.Parse(layerType, "ParentTemplate");

            // 复刻 Control.PromoteTemplateLocalValuesRecursive 的两步：先把 Local 层的值搬到
            // 目标层（否则 Local 会一直遮蔽住新层的值），再把动态资源订阅搬过去。
            var promoteLocals = typeof(DependencyObject).GetMethod(
                "PromoteLocalValuesToLayer", BindingFlags.NonPublic | BindingFlags.Instance)!;
            promoteLocals.Invoke(target, [layer]);
            promote.Invoke(null, [target, layer]);

            app.Resources["PromotedKey"] = new SolidColorBrush(Color.FromRgb(3, 3, 3));
            Assert.Equal(Color.FromRgb(3, 3, 3), ColorOf(target.Background));
        }
        finally
        {
            ResetApplicationState();
        }
    }

    /// <summary>
    /// 同一个键上反复解绑再重绑：索引条目只增不删，必须靠移除路径上的作废把重复条目压下去，
    /// 否则同一条订阅会被刷新 N 次。
    /// </summary>
    [Fact]
    public void RepeatedClearAndSet_OnSameKey_DoesNotDuplicateIndexEntries()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            app.Resources["Sticky"] = new SolidColorBrush(Color.FromRgb(1, 1, 1));

            var target = new Border();
            var window = new Window { TitleBarStyle = WindowTitleBarStyle.Native, Content = target };
            app.MainWindow = window;

            var operations = typeof(DependencyObject).Assembly
                .GetType("Jalium.UI.DynamicResourceBindingOperations")!;
            var clear = operations.GetMethod(
                "ClearDynamicResource",
                BindingFlags.NonPublic | BindingFlags.Static,
                null,
                [typeof(FrameworkElement), typeof(DependencyProperty)],
                null)!;

            for (var i = 0; i < 50; i++)
            {
                target.SetResourceReference(Border.BackgroundProperty, "Sticky");
                clear.Invoke(null, [target, Border.BackgroundProperty]);
            }

            target.SetResourceReference(Border.BackgroundProperty, "Sticky");

            // 作废只是打标记，真正的压缩发生在下一次按键扫描时——所以先刷一次再数。
            app.Resources["Sticky"] = new SolidColorBrush(Color.FromRgb(9, 9, 9));
            Assert.Equal(Color.FromRgb(9, 9, 9), ColorOf(target.Background));

            var entries = GetKeyIndex()["Sticky"];
            var count = ((System.Collections.ICollection)entries!).Count;
            Assert.True(count <= 2, $"同键反复解绑重绑累积了 {count} 个索引条目。");
        }
        finally
        {
            ResetApplicationState();
        }
    }
}

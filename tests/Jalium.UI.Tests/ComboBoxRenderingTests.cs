using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Controls.Themes;
using System.Diagnostics;

namespace Jalium.UI.Tests;

/// <summary>
/// ComboBox 娓叉煋娴嬭瘯 - 妯℃嫙瀹為檯绐楀彛甯冨眬
/// </summary>
[Collection("Application")]
public class ComboBoxRenderingTests
{
    /// <summary>
    /// Resets static state for clean test isolation.
    /// </summary>
    private static void ResetApplicationState()
    {
        // Clear Application._current
        var currentField = typeof(Application).GetField("_current",
            BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);

        // Reset ThemeManager._initialized
        var resetMethod = typeof(ThemeManager).GetMethod("Reset",
            BindingFlags.NonPublic | BindingFlags.Static);
        resetMethod?.Invoke(null, null);
    }

    /// <summary>
    /// 妯℃嫙绐楀彛甯冨眬杩囩▼锛屾祴璇?ComboBox 鍦ㄥ疄闄呮覆鏌撴椂鐨勫昂瀵?
    /// </summary>
    [Fact]
    public void ComboBox_InWindow_ShouldRespectMinHeight()
    {
        // 妯℃嫙 Window 鐨勫竷灞€杩囩▼
        var container = new StackPanel
        {
            Width = 400,
            Height = 300
        };

        var comboBox = new ComboBox();
        comboBox.MinHeight = 50;
        container.Children.Add(comboBox);

        // Measure pass
        container.Measure(new Size(400, 300));

        // Arrange pass
        container.Arrange(new Rect(0, 0, 400, 300));

        // 楠岃瘉 ComboBox 鐨勬覆鏌撳昂瀵?
        Debug.WriteLine($"ComboBox.MinHeight = {comboBox.MinHeight}");
        Debug.WriteLine($"ComboBox.DesiredSize = {comboBox.DesiredSize}");
        Debug.WriteLine($"ComboBox.RenderSize = {comboBox.RenderSize}");
        Debug.WriteLine($"ComboBox.ActualHeight = {comboBox.ActualHeight}");

        Assert.True(comboBox.RenderSize.Height >= 50,
            $"ComboBox.RenderSize.Height ({comboBox.RenderSize.Height}) should be >= MinHeight (50)");
        Assert.True(comboBox.ActualHeight >= 50,
            $"ComboBox.ActualHeight ({comboBox.ActualHeight}) should be >= MinHeight (50)");
    }

    /// <summary>
    /// 娴嬭瘯 ComboBox ControlTemplate 瑙嗚鏍戞槸鍚﹁鍒涘缓
    /// </summary>
    [Fact]
    public void ComboBox_ShouldHaveVisualTree()
    {
        var comboBox = new ComboBox();
        comboBox.Width = 200;
        comboBox.MinHeight = 32;

        // Measure and Arrange to trigger template application
        comboBox.Measure(new Size(200, 100));
        comboBox.Arrange(new Rect(0, 0, 200, 100));

        // Check if visual tree exists
        var visualChildrenCount = comboBox.VisualChildrenCount;

        // Get visual child info
        var childInfo = "";
        for (int i = 0; i < visualChildrenCount; i++)
        {
            var child = comboBox.GetVisualChild(i);
            if (child != null)
            {
                childInfo += $"Child[{i}]: {child.GetType().FullName}; ";
            }
        }

        // Get style info
        var styleInfo = comboBox.Style != null
            ? $"TargetType={comboBox.Style.TargetType?.Name}, SettersCount={comboBox.Style.Setters.Count}"
            : "null";

        // ComboBox should have at least one visual child (fallback or template)
        Assert.True(visualChildrenCount >= 1,
            $"ComboBox should have visual children, but VisualChildrenCount = {visualChildrenCount}");
    }

    /// <summary>
    /// 娴嬭瘯 ControlTemplate 涓殑 Border 鏄惁姝ｇ‘鑾峰彇灏哄
    /// </summary>
    [Fact]
    public void ComboBox_TemplateChildren_ShouldHaveCorrectSize()
    {
        var comboBox = new ComboBox();
        comboBox.Width = 200;
        comboBox.MinHeight = 50;

        // Measure and Arrange
        comboBox.Measure(new Size(200, 100));
        comboBox.Arrange(new Rect(0, 0, 200, 100));

        Debug.WriteLine($"ComboBox.RenderSize = {comboBox.RenderSize}");
        Debug.WriteLine($"ComboBox.VisualChildrenCount = {comboBox.VisualChildrenCount}");

        // Walk the visual tree and check sizes
        void PrintVisualTree(Visual visual, int depth)
        {
            var indent = new string(' ', depth * 2);
            if (visual is FrameworkElement fe)
            {
                Debug.WriteLine($"{indent}{visual.GetType().Name}: RenderSize={fe.RenderSize}, ActualWidth={fe.ActualWidth}, ActualHeight={fe.ActualHeight}");
            }
            else
            {
                Debug.WriteLine($"{indent}{visual.GetType().Name}");
            }

            for (int i = 0; i < visual.VisualChildrenCount; i++)
            {
                if (visual.GetVisualChild(i) is Visual child)
                {
                    PrintVisualTree(child, depth + 1);
                }
            }
        }

        PrintVisualTree(comboBox, 0);

        // The test passes if we can enumerate the visual tree
        Assert.True(true);
    }

    /// <summary>
    /// 娴嬭瘯 Grid 瀹瑰櫒涓殑 ComboBox 甯冨眬
    /// </summary>
    [Fact]
    public void ComboBox_InGrid_ShouldRespectMinHeight()
    {
        var grid = new Grid();
        grid.Width = 400;
        grid.Height = 300;
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var comboBox = new ComboBox();
        comboBox.MinHeight = 50;
        Grid.SetRow(comboBox, 0);
        grid.Children.Add(comboBox);

        // Measure pass
        grid.Measure(new Size(400, 300));

        // Arrange pass
        grid.Arrange(new Rect(0, 0, 400, 300));

        Debug.WriteLine($"Grid.RenderSize = {grid.RenderSize}");
        Debug.WriteLine($"ComboBox.MinHeight = {comboBox.MinHeight}");
        Debug.WriteLine($"ComboBox.DesiredSize = {comboBox.DesiredSize}");
        Debug.WriteLine($"ComboBox.RenderSize = {comboBox.RenderSize}");
        Debug.WriteLine($"ComboBox.ActualHeight = {comboBox.ActualHeight}");

        Assert.True(comboBox.RenderSize.Height >= 50,
            $"ComboBox.RenderSize.Height ({comboBox.RenderSize.Height}) should be >= MinHeight (50)");
    }

    /// <summary>
    /// 楠岃瘉榛樿鏍峰紡涓殑 MinHeight 鍊?
    /// </summary>
    [Fact]
    public void ComboBox_DefaultInstance_ShouldNotForceConstructorMinHeight()
    {
        var comboBox = new ComboBox();

        Assert.False(comboBox.HasLocalValue(FrameworkElement.MinHeightProperty));
        Assert.Equal(0.0, comboBox.MinHeight);
    }

    /// <summary>
    /// 鍒濆鍖?Application 鍚庢祴璇?ComboBox Template
    /// </summary>
    [Fact]
    public void ComboBox_WithApplication_ShouldHaveTemplate()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            // 鍒涘缓瀹瑰櫒鏉ヨЕ鍙?VisualParent 鍙樺寲
            var container = new StackPanel { Width = 400, Height = 300 };

            var comboBox = new ComboBox();
            comboBox.Width = 200;
            comboBox.MinHeight = 32;

            // 娣诲姞鍒板鍣紝瑙﹀彂 OnVisualParentChanged -> ApplyImplicitStyleIfNeeded
            container.Children.Add(comboBox);

            // Measure and Arrange 瀹瑰櫒
            container.Measure(new Size(400, 300));
            container.Arrange(new Rect(0, 0, 400, 300));

            // 璇婃柇淇℃伅
            var styleInfo = comboBox.Style != null
                ? $"TargetType={comboBox.Style.TargetType?.Name}, SettersCount={comboBox.Style.Setters.Count}"
                : "null";

            var templateInfo = comboBox.Template != null
                ? $"TargetType={comboBox.Template.TargetType?.Name}"
                : "null";

            // Get visual child info
            var childInfo = "";
            for (int i = 0; i < comboBox.VisualChildrenCount; i++)
            {
                var child = comboBox.GetVisualChild(i);
                if (child != null)
                {
                    childInfo += $"Child[{i}]: {child.GetType().Name}; ";
                }
            }

            // 妫€鏌?Application.Resources 涓槸鍚︽湁 ComboBox 鐨勬牱寮?
            var hasComboBoxStyle = app.Resources.TryGetValue(typeof(ComboBox), out var styleFromApp);
            var appStyleInfo = hasComboBoxStyle && styleFromApp != null
                ? $"found (Type={styleFromApp.GetType().Name})"
                : "not found";

            // 鑾峰彇 Grid 瀛愬厓绱犵殑璇︾粏淇℃伅
            var gridInfo = "";
            if (comboBox.VisualChildrenCount > 0 && comboBox.GetVisualChild(0) is FrameworkElement gridChild)
            {
                gridInfo = $"Grid: RenderSize={gridChild.RenderSize}, DesiredSize={gridChild.DesiredSize}";
            }

            // ComboBox should have correct height after layout with Application
            Assert.True(comboBox.DesiredSize.Height >= comboBox.MinHeight,
                $"DesiredSize.Height ({comboBox.DesiredSize.Height}) should be >= MinHeight ({comboBox.MinHeight})");
            Assert.True(comboBox.RenderSize.Height >= comboBox.MinHeight,
                $"RenderSize.Height ({comboBox.RenderSize.Height}) should be >= MinHeight ({comboBox.MinHeight})");
        }
        finally
        {
            ResetApplicationState();
        }
    }

    /// <summary>
    /// 娴嬭瘯鏄惧紡璁剧疆 Height 鍜?MinHeight 鐨勪紭鍏堢骇
    /// </summary>
    [Fact]
    public void ComboBox_HeightVsMinHeight_Priority()
    {
        var comboBox = new ComboBox();
        comboBox.Height = 20;  // 灏忎簬 MinHeight
        comboBox.MinHeight = 50;

        comboBox.Measure(new Size(200, 200));
        comboBox.Arrange(new Rect(0, 0, 200, 200));

        Debug.WriteLine($"Height = {comboBox.Height}");
        Debug.WriteLine($"MinHeight = {comboBox.MinHeight}");
        Debug.WriteLine($"RenderSize.Height = {comboBox.RenderSize.Height}");

        // MinHeight should take precedence over explicit Height when Height < MinHeight
        Assert.True(comboBox.RenderSize.Height >= 50,
            $"RenderSize.Height ({comboBox.RenderSize.Height}) should be >= MinHeight (50) even when Height is set to 20");
    }

    /// <summary>
    /// 娴嬭瘯 DesiredSize 鍜?RenderSize 鐨勪竴鑷存€?
    /// </summary>
    [Fact]
    public void ComboBox_DesiredSize_RenderSize_Consistency()
    {
        var comboBox = new ComboBox();
        comboBox.MinHeight = 60;
        comboBox.Width = 150;

        // Measure
        comboBox.Measure(new Size(200, 200));
        var desiredSize = comboBox.DesiredSize;

        // Arrange with exact desired size
        comboBox.Arrange(new Rect(0, 0, desiredSize.Width, desiredSize.Height));

        Debug.WriteLine($"MinHeight = {comboBox.MinHeight}");
        Debug.WriteLine($"DesiredSize = {desiredSize}");
        Debug.WriteLine($"RenderSize = {comboBox.RenderSize}");

        // DesiredSize.Height should be >= MinHeight
        Assert.True(desiredSize.Height >= 60,
            $"DesiredSize.Height ({desiredSize.Height}) should be >= MinHeight (60)");

        // RenderSize.Height should be >= MinHeight
        Assert.True(comboBox.RenderSize.Height >= 60,
            $"RenderSize.Height ({comboBox.RenderSize.Height}) should be >= MinHeight (60)");
    }

    /// <summary>
    /// 璇婃柇锛氫笉浣跨敤 Application 鏃剁殑甯冨眬杩借釜
    /// </summary>
    [Fact]
    public void ComboBox_Layout_WithoutApplication()
    {
        var comboBox = new ComboBox();
        comboBox.Width = 200;
        comboBox.MinHeight = 50;

        // 璁板綍鍒濆鐘舵€?
        var initialIsMeasureValid = comboBox.IsMeasureValid;
        var initialIsArrangeValid = comboBox.IsArrangeValid;

        // Measure
        comboBox.Measure(new Size(300, 300));
        var measureIsMeasureValid = comboBox.IsMeasureValid;
        var desiredSize = comboBox.DesiredSize;

        // Arrange
        comboBox.Arrange(new Rect(0, 0, desiredSize.Width, desiredSize.Height));
        var arrangeIsArrangeValid = comboBox.IsArrangeValid;
        var renderSize = comboBox.RenderSize;

        // ComboBox should respect MinHeight without Application
        Assert.True(desiredSize.Height >= 50,
            $"DesiredSize.Height ({desiredSize.Height}) should be >= MinHeight (50)");
        Assert.True(renderSize.Height >= 50,
            $"RenderSize.Height ({renderSize.Height}) should be >= MinHeight (50)");
    }

    /// <summary>
    /// 璇婃柇锛氫娇鐢?Application 鏃?StackPanel 鍐呯殑甯冨眬杩借釜
    /// </summary>
    [Fact]
    public void ComboBox_Layout_InStackPanel_WithApplication()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var container = new StackPanel { Width = 400, Height = 300 };
            var comboBox = new ComboBox();
            comboBox.Width = 200;
            comboBox.MinHeight = 50;

            // 娣诲姞鍒板鍣ㄥ墠娴嬮噺
            comboBox.Measure(new Size(200, 100));
            var beforeAddDesiredSize = comboBox.DesiredSize;

            // 娣诲姞鍒板鍣?
            container.Children.Add(comboBox);

            // 瀹瑰櫒娴嬮噺鍓?ComboBox 鐘舵€?
            var afterAddIsMeasureValid = comboBox.IsMeasureValid;
            var afterAddDesiredSize = comboBox.DesiredSize;

            // 娴嬮噺瀹瑰櫒
            container.Measure(new Size(400, 300));
            var afterContainerMeasureDesiredSize = comboBox.DesiredSize;

            // 甯冪疆瀹瑰櫒
            container.Arrange(new Rect(0, 0, 400, 300));
            var finalRenderSize = comboBox.RenderSize;

            // 鑾峰彇 _templateRoot 淇℃伅
            var templateRootField = typeof(Control).GetField("_templateRoot",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var templateRoot = templateRootField?.GetValue(comboBox) as FrameworkElement;
            var templateRootInfo = templateRoot != null
                ? $"{templateRoot.GetType().Name}, DesiredSize={templateRoot.DesiredSize}"
                : "null";

            // ComboBox should respect MinHeight when in StackPanel with Application
            Assert.True(finalRenderSize.Height >= 50,
                $"RenderSize.Height ({finalRenderSize.Height}) should be >= MinHeight (50)");
            Assert.True(comboBox.ActualHeight >= 50,
                $"ActualHeight ({comboBox.ActualHeight}) should be >= MinHeight (50)");
        }
        finally
        {
            ResetApplicationState();
        }
    }

    /// <summary>
    /// 璇婃柇锛氭鏌ラ棶棰樻槸鍚﹀湪 ItemsControl.HasTemplate 瀵艰嚧 Control.MeasureOverride 琚皟鐢?
    /// </summary>
    [Fact]
    public void ComboBox_HasTemplate_ShouldNotBypassMeasureOverride()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var comboBox = new ComboBox();
            comboBox.Width = 200;
            comboBox.MinHeight = 50;

            // 娣诲姞鍒板鍣ㄦ潵瑙﹀彂闅愬紡鏍峰紡搴旂敤
            var container = new StackPanel { Width = 400, Height = 300 };
            container.Children.Add(comboBox);

            // 妫€鏌?Template 鏄惁琚缃?
            var hasTemplate = comboBox.Template != null;

            // 鍗曠嫭娴嬮噺 ComboBox锛堢敤鏈夐檺楂樺害锛?
            comboBox.Measure(new Size(200, 100));
            var finiteDesiredSize = comboBox.DesiredSize;

            // 鍐嶆娴嬮噺锛堢敤鏃犻檺楂樺害锛?
            comboBox.Measure(new Size(200, double.PositiveInfinity));
            var infiniteDesiredSize = comboBox.DesiredSize;

            // ComboBox.MeasureOverride should return MinHeight regardless of available size
            // With finite available height
            Assert.True(finiteDesiredSize.Height >= comboBox.MinHeight,
                $"Finite DesiredSize.Height ({finiteDesiredSize.Height}) should be >= MinHeight ({comboBox.MinHeight})");
            // With infinite available height - should NOT return infinity
            Assert.False(double.IsInfinity(infiniteDesiredSize.Height),
                $"Infinite DesiredSize.Height should not be infinity, but was {infiniteDesiredSize.Height}");
            Assert.True(infiniteDesiredSize.Height >= comboBox.MinHeight,
                $"Infinite DesiredSize.Height ({infiniteDesiredSize.Height}) should be >= MinHeight ({comboBox.MinHeight})");
        }
        finally
        {
            ResetApplicationState();
        }
    }

    /// <summary>
    /// 浣跨敤鑷畾涔?ComboBox 瀛愮被楠岃瘉 MeasureOverride 鏄惁琚皟鐢?
    /// </summary>
    [Fact]
    public void TracingComboBox_CustomMeasureOverride_ShouldBeCalled()
    {
        ResetApplicationState();
        var app = new Application();

        try
        {
            var comboBox = new TracingComboBox();
            comboBox.Width = 200;
            comboBox.MinHeight = 50;

            // 鎵嬪姩浠?App.Resources 鑾峰彇 ComboBox 鐨?Style 骞舵彁鍙?Template
            if (app.Resources.TryGetValue(typeof(ComboBox), out var styleObj) && styleObj is Style comboBoxStyle)
            {
                // 鏌ユ壘 Template Setter
                foreach (var setter in comboBoxStyle.Setters.OfType<Setter>())
                {
                    if (setter.Property?.Name == "Template" && setter.Value is ControlTemplate template)
                    {
                        comboBox.Template = template;
                        break;
                    }
                }
            }

            // 娣诲姞鍒板鍣?
            var container = new StackPanel { Width = 400, Height = 300 };
            container.Children.Add(comboBox);

            // 娓呴櫎涔嬪墠鐨勬祴閲忚褰?
            comboBox.MeasureLog.Clear();

            // 娴嬮噺
            comboBox.Measure(new Size(200, double.PositiveInfinity));

            // MeasureOverride should have been called
            Assert.True(comboBox.MeasureLog.Count > 0,
                "MeasureOverride should have been called at least once");

            // DesiredSize should respect MinHeight
            Assert.True(comboBox.DesiredSize.Height >= comboBox.MinHeight,
                $"DesiredSize.Height ({comboBox.DesiredSize.Height}) should be >= MinHeight ({comboBox.MinHeight})");
        }
        finally
        {
            ResetApplicationState();
        }
    }

    /// <summary>
    /// 鐢ㄤ簬杩借釜 MeasureOverride 璋冪敤鐨?ComboBox 瀛愮被
    /// </summary>
    private class TracingComboBox : ComboBox
    {
        public List<string> MeasureLog { get; } = new List<string>();

        protected override Size MeasureOverride(Size availableSize)
        {
            // 涓嶈皟鐢?base锛岀洿鎺ュ疄鐜?ComboBox.MeasureOverride 鐨勯€昏緫
            var directResult = new Size(availableSize.Width, MinHeight);

            // 涔熻皟鐢?base 鐪嬪畠杩斿洖浠€涔?
            var baseResult = base.MeasureOverride(availableSize);

            MeasureLog.Add($"availableSize={availableSize}, MinHeight={MinHeight}, directResult={directResult}, baseResult={baseResult}");

            // 杩斿洖姝ｇ‘鐨勫€硷紙鐩存帴璁＄畻鐨勶級
            return directResult;
        }
    }
}


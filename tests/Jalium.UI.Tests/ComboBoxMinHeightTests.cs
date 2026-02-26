using Jalium.UI;
using Jalium.UI.Controls;
using System.Reflection;

namespace Jalium.UI.Tests;

/// <summary>
/// ComboBox MinHeight 属性测试
/// </summary>
public class ComboBoxMinHeightTests
{
    /// <summary>
    /// Helper method to resolve DependencyProperty by searching up the inheritance chain
    /// </summary>
    private static DependencyProperty? ResolveDependencyProperty(string propertyName, Type targetType)
    {
        var dpFieldName = propertyName + "Property";
        var currentType = targetType;

        while (currentType != null && currentType != typeof(object))
        {
            var field = currentType.GetField(dpFieldName,
                BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);

            if (field != null && field.FieldType == typeof(DependencyProperty))
            {
                return field.GetValue(null) as DependencyProperty;
            }

            currentType = currentType.BaseType;
        }

        return null;
    }

    [Fact]
    public void MinHeight_DependencyProperty_ShouldExistInFrameworkElement()
    {
        // Verify MinHeightProperty exists in FrameworkElement
        var field = typeof(FrameworkElement).GetField("MinHeightProperty",
            BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(field);
        Assert.Equal(typeof(DependencyProperty), field.FieldType);

        var dp = field.GetValue(null) as DependencyProperty;
        Assert.NotNull(dp);
        Assert.Equal("MinHeight", dp.Name);
        Assert.Equal(typeof(double), dp.PropertyType);
    }

    [Fact]
    public void ResolveDependencyProperty_ShouldFindMinHeightInInheritanceChain()
    {
        // Test that we can find MinHeightProperty when searching from ComboBox
        // (which inherits from FrameworkElement)
        var dp = ResolveDependencyProperty("MinHeight", typeof(ComboBox));

        Assert.NotNull(dp);
        Assert.Equal("MinHeight", dp.Name);
        Assert.Equal(typeof(double), dp.PropertyType);
    }

    [Fact]
    public void ResolveDependencyProperty_ShouldFindMaxHeightInInheritanceChain()
    {
        var dp = ResolveDependencyProperty("MaxHeight", typeof(ComboBox));

        Assert.NotNull(dp);
        Assert.Equal("MaxHeight", dp.Name);
    }

    [Fact]
    public void ResolveDependencyProperty_ShouldFindMinWidthInInheritanceChain()
    {
        var dp = ResolveDependencyProperty("MinWidth", typeof(ComboBox));

        Assert.NotNull(dp);
        Assert.Equal("MinWidth", dp.Name);
    }

    [Fact]
    public void ResolveDependencyProperty_ShouldFindMaxWidthInInheritanceChain()
    {
        var dp = ResolveDependencyProperty("MaxWidth", typeof(ComboBox));

        Assert.NotNull(dp);
        Assert.Equal("MaxWidth", dp.Name);
    }

    [Fact]
    public void ComboBox_MinHeight_ShouldBeSettable()
    {
        // Arrange
        var comboBox = new ComboBox();

        // Act
        comboBox.MinHeight = 34;

        // Assert
        Assert.Equal(34, comboBox.MinHeight);
    }

    [Fact]
    public void ComboBox_MinHeight_ShouldBeAppliedViaSetter()
    {
        // Arrange
        var comboBox = new ComboBox();
        var style = new Style(typeof(ComboBox));

        // Create Setter with MinHeight
        var setter = new Setter
        {
            Property = FrameworkElement.MinHeightProperty,
            Value = 34.0
        };
        style.Setters.Add(setter);

        // Act
        comboBox.Style = style;

        // Assert
        Assert.Equal(34, comboBox.MinHeight);
    }

    [Fact]
    public void Setter_Property_ShouldResolveMinHeight()
    {
        // This tests the XAML parsing scenario
        // When XAML parser sees <Setter Property="MinHeight" Value="34"/>
        // it needs to resolve "MinHeight" to the DependencyProperty

        var setter = new Setter();

        // Simulate what XamlReader does - resolve the DP from inheritance chain
        var dp = ResolveDependencyProperty("MinHeight", typeof(ComboBox));
        setter.Property = dp;
        setter.Value = 34.0;

        Assert.NotNull(setter.Property);
        Assert.Equal("MinHeight", setter.Property.Name);
        Assert.Equal(34.0, setter.Value);
    }

    [Fact]
    public void Style_WithMinHeight_ShouldApplyToComboBox()
    {
        // Arrange
        var comboBox = new ComboBox();

        // Note: ComboBox may already have a default style applied with MinHeight set
        var initialMinHeight = comboBox.MinHeight;

        // Create style programmatically with a different MinHeight
        var style = new Style(typeof(ComboBox));
        var dp = ResolveDependencyProperty("MinHeight", typeof(ComboBox));
        Assert.NotNull(dp);

        style.Setters.Add(new Setter(dp, 50.0));

        // Act
        comboBox.Style = style;

        // Assert - MinHeight should now be our new value
        Assert.Equal(50, comboBox.MinHeight);
    }

    [Fact]
    public void ComboBox_DefaultInstance_ShouldNotForceLocalMinHeight()
    {
        // ComboBox should not force a constructor-level MinHeight local value.
        // Theme/style may set MinHeight later when the element enters the visual tree.
        var comboBox = new ComboBox();

        Assert.False(comboBox.HasLocalValue(FrameworkElement.MinHeightProperty));
        Assert.Equal(0.0, comboBox.MinHeight);
    }

    [Fact]
    public void ComboBox_MeasureWithMinHeight_ShouldRespectConstraint()
    {
        // Arrange
        var comboBox = new ComboBox();
        comboBox.MinHeight = 34;

        // Act - Measure with infinite available space
        comboBox.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        // Assert - DesiredSize.Height should be at least MinHeight
        Assert.True(comboBox.DesiredSize.Height >= 34,
            $"DesiredSize.Height ({comboBox.DesiredSize.Height}) should be >= MinHeight (34)");
    }

    [Fact]
    public void ComboBox_MeasureWithMinHeight_WhenContentIsSmaller()
    {
        // Arrange
        var comboBox = new ComboBox();
        comboBox.MinHeight = 50;  // Set a larger MinHeight

        // Act - Measure
        comboBox.Measure(new Size(200, 200));

        // Assert - Even without content, height should be at least MinHeight
        Assert.True(comboBox.DesiredSize.Height >= 50,
            $"DesiredSize.Height ({comboBox.DesiredSize.Height}) should be >= MinHeight (50)");
    }

    [Fact]
    public void FrameworkElement_MeasureCore_ShouldApplyMinHeightConstraint()
    {
        // Test with a simple FrameworkElement to isolate MinHeight behavior
        var element = new TestFrameworkElement();
        element.MinHeight = 100;

        // Measure with infinite space
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        // The DesiredSize should respect MinHeight
        Assert.True(element.DesiredSize.Height >= 100,
            $"DesiredSize.Height ({element.DesiredSize.Height}) should be >= MinHeight (100)");
    }

    [Fact]
    public void FrameworkElement_ArrangeCore_ShouldApplyMinHeightConstraint()
    {
        // Test that ArrangeCore respects MinHeight in RenderSize
        var element = new TestFrameworkElement();
        element.MinHeight = 100;

        // Measure first
        element.Measure(new Size(200, 200));

        // Arrange
        element.Arrange(new Rect(0, 0, 200, 200));

        // The RenderSize should respect MinHeight
        Assert.True(element.RenderSize.Height >= 100,
            $"RenderSize.Height ({element.RenderSize.Height}) should be >= MinHeight (100)");
    }

    [Fact]
    public void ComboBox_ArrangeWithMinHeight_ShouldRespectConstraint()
    {
        // Arrange
        var comboBox = new ComboBox();
        comboBox.MinHeight = 50;

        // Measure
        comboBox.Measure(new Size(200, 200));

        // Arrange
        comboBox.Arrange(new Rect(0, 0, 200, 200));

        // Assert - RenderSize.Height should be at least MinHeight
        Assert.True(comboBox.RenderSize.Height >= 50,
            $"RenderSize.Height ({comboBox.RenderSize.Height}) should be >= MinHeight (50)");
    }

    [Fact]
    public void ComboBox_Diagnostic_ShowCurrentValues()
    {
        // Diagnostic test to show what values are being used
        var comboBox = new ComboBox();

        // Log initial values
        var initialMinHeight = comboBox.MinHeight;
        var initialMinWidth = comboBox.MinWidth;
        var initialMaxHeight = comboBox.MaxHeight;
        var initialMaxWidth = comboBox.MaxWidth;

        // Measure
        comboBox.Measure(new Size(300, 300));
        var desiredSize = comboBox.DesiredSize;

        // Arrange
        comboBox.Arrange(new Rect(0, 0, 300, 300));
        var renderSize = comboBox.RenderSize;

        // Just output diagnostic info - this test always passes
        // Use Assert.True with message to output values
        Assert.True(true,
            $"MinHeight={initialMinHeight}, MinWidth={initialMinWidth}, " +
            $"MaxHeight={initialMaxHeight}, MaxWidth={initialMaxWidth}, " +
            $"DesiredSize={desiredSize}, RenderSize={renderSize}");
    }

    /// <summary>
    /// Simple test element to verify MinHeight behavior
    /// </summary>
    private class TestFrameworkElement : FrameworkElement
    {
        protected override Size MeasureOverride(Size availableSize)
        {
            // Return a small size to test MinHeight constraint
            return new Size(50, 10);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            // Return the final size we're given (respecting MinHeight from ArrangeCore)
            return finalSize;
        }
    }
}

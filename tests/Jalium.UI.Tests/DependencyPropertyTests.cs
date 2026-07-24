using Jalium.UI;
using System.Reflection;

namespace Jalium.UI.Tests;

/// <summary>
/// DependencyProperty 系统测试
/// </summary>
public class DependencyPropertyTests
{
    [Fact]
    public void ValueLayerStorage_ShouldRemainLazyAndReleaseTheLastLocalValue()
    {
        var obj = new TestDependencyObject();
        _ = obj.GetValue(TestDependencyObject.NameProperty);

        string[] layerFields =
        {
            "_localValues",
            "_parentTemplateValues",
            "_styleTriggerValues",
            "_templateTriggerValues",
            "_styleSetterValues",
            "_currentValues",
            "_bindings",
            "_animatedValues"
        };

        foreach (var fieldName in layerFields)
        {
            Assert.Null(GetValueLayer(obj, fieldName));
        }

        obj.SetValue(TestDependencyObject.NameProperty, "local");

        Assert.NotNull(GetValueLayer(obj, "_localValues"));
        foreach (var fieldName in layerFields[1..])
        {
            Assert.Null(GetValueLayer(obj, fieldName));
        }

        obj.ClearValue(TestDependencyObject.NameProperty);
        Assert.Null(GetValueLayer(obj, "_localValues"));
    }

    private static object? GetValueLayer(DependencyObject obj, string fieldName) =>
        typeof(DependencyObject)
            .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(obj);

    [Fact]
    public void Register_ShouldCreateNewProperty()
    {
        // Arrange & Act
        var property = DependencyProperty.Register(
            "TestProperty",
            typeof(string),
            typeof(TestDependencyObject),
            new PropertyMetadata("default"));

        // Assert
        Assert.NotNull(property);
        Assert.Equal("TestProperty", property.Name);
        Assert.Equal(typeof(string), property.PropertyType);
        Assert.Equal(typeof(TestDependencyObject), property.OwnerType);
    }

    [Fact]
    public void GetValue_ShouldReturnDefaultValue_WhenNotSet()
    {
        // Arrange
        var obj = new TestDependencyObject();

        // Act
        var value = obj.GetValue(TestDependencyObject.NameProperty);

        // Assert
        Assert.Equal("default", value);
    }

    [Fact]
    public void SetValue_ShouldChangeValue()
    {
        // Arrange
        var obj = new TestDependencyObject();

        // Act
        obj.SetValue(TestDependencyObject.NameProperty, "new value");
        var value = obj.GetValue(TestDependencyObject.NameProperty);

        // Assert
        Assert.Equal("new value", value);
    }

    [Fact]
    public void SetValue_ShouldInvokePropertyChangedCallback()
    {
        // Arrange
        var obj = new TestDependencyObject();
        var callbackInvoked = false;
        obj.PropertyChangedCallback = (d, e) => callbackInvoked = true;

        // Act
        obj.SetValue(TestDependencyObject.CountProperty, 42);

        // Assert
        Assert.True(callbackInvoked);
    }

    [Fact]
    public void CoerceValue_ShouldClampValue()
    {
        // Arrange
        var obj = new TestDependencyObject();

        // Act - 设置超出范围的值
        obj.SetValue(TestDependencyObject.BoundedValueProperty, 150);
        var value = (int)obj.GetValue(TestDependencyObject.BoundedValueProperty)!;

        // Assert - 值应该被强制转换为最大值100
        Assert.Equal(100, value);
    }

    [Fact]
    public void CoerceValue_ReentrantSameProperty_ShouldNotReenterCoercion()
    {
        var obj = new ReentrantCoerceDependencyObject();

        obj.SetValue(ReentrantCoerceDependencyObject.ReentrantValueProperty, 42);
        var value = (int)obj.GetValue(ReentrantCoerceDependencyObject.ReentrantValueProperty)!;

        Assert.Equal(42, value);
        Assert.Equal(3, obj.CoerceInvocationCount);
    }

    private class TestDependencyObject : DependencyObject
    {
        public static readonly DependencyProperty NameProperty =
            DependencyProperty.Register("Name", typeof(string), typeof(TestDependencyObject),
                new PropertyMetadata("default"));

        public static readonly DependencyProperty CountProperty =
            DependencyProperty.Register("Count", typeof(int), typeof(TestDependencyObject),
                new PropertyMetadata(0, OnCountChanged));

        public static readonly DependencyProperty BoundedValueProperty =
            DependencyProperty.Register("BoundedValue", typeof(int), typeof(TestDependencyObject),
                new PropertyMetadata(0, null, CoerceBoundedValue));

        public Action<DependencyObject, DependencyPropertyChangedEventArgs>? PropertyChangedCallback { get; set; }

        private static void OnCountChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TestDependencyObject obj)
            {
                obj.PropertyChangedCallback?.Invoke(d, e);
            }
        }

        private static object? CoerceBoundedValue(DependencyObject d, object? baseValue)
        {
            var value = (int)(baseValue ?? 0);
            return Math.Clamp(value, 0, 100);
        }
    }

    private class ReentrantCoerceDependencyObject : DependencyObject
    {
        public static readonly DependencyProperty ReentrantValueProperty =
            DependencyProperty.Register("ReentrantValue", typeof(int), typeof(ReentrantCoerceDependencyObject),
                new PropertyMetadata(0, null, CoerceReentrantValue));

        public int CoerceInvocationCount { get; private set; }

        private static object? CoerceReentrantValue(DependencyObject d, object? baseValue)
        {
            var obj = (ReentrantCoerceDependencyObject)d;
            obj.CoerceInvocationCount++;

            if (obj.CoerceInvocationCount < 5)
            {
                _ = obj.GetValue(ReentrantValueProperty);
            }

            return baseValue;
        }
    }
}

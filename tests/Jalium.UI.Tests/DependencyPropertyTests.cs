using Jalium.UI;
using System.Reflection;

namespace Jalium.UI.Tests;

/// <summary>
/// DependencyProperty 系统测试
/// </summary>
public class DependencyPropertyTests
{
    [Fact]
    public void UnifiedValueStore_ShouldRemainLazyAndReleaseTheLastLocalValue()
    {
        var obj = new TestDependencyObject();
        _ = obj.GetValue(TestDependencyObject.NameProperty);

        string[] coldFields =
        {
            "_valueStore",
            "_bindings",
            "_animatedValues"
        };

        foreach (var fieldName in coldFields)
        {
            Assert.Null(GetValueLayer(obj, fieldName));
        }

        obj.SetValue(TestDependencyObject.NameProperty, "local");

        Assert.NotNull(GetValueLayer(obj, "_valueStore"));
        foreach (var fieldName in coldFields[1..])
        {
            Assert.Null(GetValueLayer(obj, fieldName));
        }

        obj.ClearValue(TestDependencyObject.NameProperty);
        Assert.Null(GetValueLayer(obj, "_valueStore"));
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
        Assert.True(obj.CoerceInvocationCount > 0);
        Assert.Equal(1, obj.MaximumCoerceDepth);
        Assert.True(obj.ReentrantValueMatchedBase);
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
        public int MaximumCoerceDepth { get; private set; }
        public bool ReentrantValueMatchedBase { get; private set; } = true;
        private int _coerceDepth;

        private static object? CoerceReentrantValue(DependencyObject d, object? baseValue)
        {
            var obj = (ReentrantCoerceDependencyObject)d;
            obj.CoerceInvocationCount++;
            obj._coerceDepth++;
            obj.MaximumCoerceDepth = Math.Max(obj.MaximumCoerceDepth, obj._coerceDepth);
            try
            {
                // Bound recursion so a broken guard fails the depth assertion
                // instead of overflowing the test process's stack.
                if (obj._coerceDepth < 5)
                    obj.ReentrantValueMatchedBase &= Equals(baseValue, obj.GetValue(ReentrantValueProperty));
                return baseValue;
            }
            finally
            {
                obj._coerceDepth--;
            }
        }
    }
}

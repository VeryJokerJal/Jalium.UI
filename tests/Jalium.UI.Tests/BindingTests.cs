using Jalium.UI;
using System.ComponentModel;

namespace Jalium.UI.Tests;

/// <summary>
/// 数据绑定测试
/// </summary>
public class BindingTests
{
    [Fact]
    public void Binding_OneWay_ShouldUpdateTarget()
    {
        // Arrange
        var source = new TestViewModel { Name = "Initial" };
        var target = new TestFrameworkElement();
        var binding = new Binding("Name") { Source = source };

        // Act
        target.SetBinding(TestFrameworkElement.TextProperty, binding);

        // Assert
        Assert.Equal("Initial", target.GetValue(TestFrameworkElement.TextProperty));
    }

    [Fact]
    public void Binding_OneWay_ShouldUpdateOnSourceChange()
    {
        // Arrange
        var source = new TestViewModel { Name = "Initial" };
        var target = new TestFrameworkElement();
        var binding = new Binding("Name") { Source = source };
        target.SetBinding(TestFrameworkElement.TextProperty, binding);

        // Act
        source.Name = "Updated";

        // Assert
        Assert.Equal("Updated", target.GetValue(TestFrameworkElement.TextProperty));
    }

    [Fact]
    public void Binding_TwoWay_ShouldUpdateSource()
    {
        // Arrange
        var source = new TestViewModel { Name = "Initial" };
        var target = new TestFrameworkElement();
        var binding = new Binding("Name")
        {
            Source = source,
            Mode = BindingMode.TwoWay
        };
        target.SetBinding(TestFrameworkElement.TextProperty, binding);

        // Act
        target.SetValue(TestFrameworkElement.TextProperty, "FromTarget");

        // Assert
        Assert.Equal("FromTarget", source.Name);
    }

    [Fact]
    public void Binding_WithConverter_ShouldConvertValue()
    {
        // Arrange
        var source = new TestViewModel { Count = 42 };
        var target = new TestFrameworkElement();
        var binding = new Binding("Count")
        {
            Source = source,
            Converter = new IntToStringConverter()
        };

        // Act
        target.SetBinding(TestFrameworkElement.TextProperty, binding);

        // Assert
        Assert.Equal("42", target.GetValue(TestFrameworkElement.TextProperty));
    }

    [Fact]
    public void Binding_WithoutConverter_ShouldConvertNonStringSourceToStringTarget()
    {
        var source = new TestViewModel { Count = 42 };
        var target = new TestFrameworkElement();
        var binding = new Binding("Count") { Source = source };

        target.SetBinding(TestFrameworkElement.TextProperty, binding);

        Assert.Equal("42", target.GetValue(TestFrameworkElement.TextProperty));

        source.Count = 108;
        Assert.Equal("108", target.GetValue(TestFrameworkElement.TextProperty));
    }

    [Fact]
    public void Binding_TwoWay_WithoutConverter_ShouldConvertStringTargetBackToIntSource()
    {
        var source = new TestViewModel { Count = 1 };
        var target = new TestFrameworkElement();
        var binding = new Binding("Count")
        {
            Source = source,
            Mode = BindingMode.TwoWay
        };

        target.SetBinding(TestFrameworkElement.TextProperty, binding);
        target.SetValue(TestFrameworkElement.TextProperty, "256");

        Assert.Equal(256, source.Count);
    }

    private class TestViewModel : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private int _count;

        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }

        public int Count
        {
            get => _count;
            set
            {
                _count = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private class TestFrameworkElement : FrameworkElement
    {
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register("Text", typeof(string), typeof(TestFrameworkElement),
                new PropertyMetadata(string.Empty));
    }

    private class IntToStringConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            return value?.ToString();
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            if (value is string str && int.TryParse(str, out var result))
                return result;
            return 0;
        }
    }
}

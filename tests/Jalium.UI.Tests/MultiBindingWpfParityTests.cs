using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Data;

namespace Jalium.UI.Tests;

public class MultiBindingWpfParityTests
{
    [Fact]
    public void ConverterReturningUnsetValue_ShouldUseFallbackValue()
    {
        var target = new TextBlock
        {
            DataContext = new SampleViewModel { Name = "Alice" }
        };

        var multiBinding = new MultiBinding
        {
            Converter = new AlwaysUnsetConverter(),
            FallbackValue = "Fallback"
        };
        multiBinding.Bindings.Add(new Binding("Name"));

        target.SetBinding(TextBlock.TextProperty, multiBinding);

        Assert.Equal("Fallback", target.Text);
    }

    [Fact]
    public void NullResult_ShouldUseTargetNullValue()
    {
        var target = new TextBlock
        {
            DataContext = new SampleViewModel { Name = "Alice" }
        };

        var multiBinding = new MultiBinding
        {
            Converter = new NullConverter(),
            TargetNullValue = "(null)"
        };
        multiBinding.Bindings.Add(new Binding("Name"));

        target.SetBinding(TextBlock.TextProperty, multiBinding);

        Assert.Equal("(null)", target.Text);
    }

    [Fact]
    public void MultiBinding_WithoutConverter_ShouldConvertFirstValueToStringTarget()
    {
        var target = new TextBlock
        {
            DataContext = new SampleViewModel { Count = 42 }
        };

        var multiBinding = new MultiBinding();
        multiBinding.Bindings.Add(new Binding("Count"));

        target.SetBinding(TextBlock.TextProperty, multiBinding);

        Assert.Equal("42", target.Text);
    }

    private sealed class SampleViewModel
    {
        public string? Name { get; set; }

        public int Count { get; set; }
    }

    private sealed class AlwaysUnsetConverter : IMultiValueConverter
    {
        public object? Convert(object?[] values, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            return DependencyProperty.UnsetValue;
        }

        public object?[]? ConvertBack(object? value, Type[] targetTypes, object? parameter, System.Globalization.CultureInfo culture)
        {
            return null;
        }
    }

    private sealed class NullConverter : IMultiValueConverter
    {
        public object? Convert(object?[] values, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            return null;
        }

        public object?[]? ConvertBack(object? value, Type[] targetTypes, object? parameter, System.Globalization.CultureInfo culture)
        {
            return null;
        }
    }
}

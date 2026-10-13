using System.ComponentModel;
using System.Globalization;
using Jalium.UI.Controls;
using Jalium.UI.Data;

namespace Jalium.UI.Tests;

// Shared managed contracts: the Windows test project also compiles this file.
public class AttachedPropertyBindingTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void AttachedPathReadsAndTracksTheRegisteredProperty(int syntax)
    {
        var source = new Border();
        ScrollViewer.SetCanContentScroll(source, true);
        var target = new Target();
        target.SetBinding(Target.ValueProperty, new Binding { Source = source, Path = Path(syntax) });
        Assert.True(target.Value);
        ScrollViewer.SetCanContentScroll(source, false);
        Assert.False(target.Value);
        ScrollViewer.SetCanContentScroll(source, true);
        Assert.True(target.Value);
    }

    [Theory]
    [InlineData(UpdateSourceTrigger.Default)]
    [InlineData(UpdateSourceTrigger.PropertyChanged)]
    [InlineData(UpdateSourceTrigger.LostFocus)]
    [InlineData(UpdateSourceTrigger.Explicit)]
    public void AttachedWriteBackHonorsAutomaticTriggersAndManualUpdate(UpdateSourceTrigger trigger)
    {
        var source = new Border();
        var target = new Target();
        var expression = target.SetBinding(Target.ValueProperty, new Binding
        {
            Source = source, Path = Path(0), Mode = BindingMode.TwoWay, UpdateSourceTrigger = trigger
        });
        target.Value = true;
        Assert.Equal(trigger is UpdateSourceTrigger.Default or UpdateSourceTrigger.PropertyChanged,
            ScrollViewer.GetCanContentScroll(source));
        if (trigger == UpdateSourceTrigger.LostFocus)
        {
            target.RaiseEvent(new RoutedEventArgs(UIElement.LostFocusEvent, target));
            Assert.True(ScrollViewer.GetCanContentScroll(source));
            target.Value = false;
            Assert.True(ScrollViewer.GetCanContentScroll(source));
        }
        expression.UpdateSource();
        Assert.Equal(target.Value, ScrollViewer.GetCanContentScroll(source));
        Assert.Same(expression, target.GetBindingExpression(Target.ValueProperty));
    }

    [Theory]
    [InlineData(UpdateSourceTrigger.Default)]
    [InlineData(UpdateSourceTrigger.PropertyChanged)]
    [InlineData(UpdateSourceTrigger.LostFocus)]
    [InlineData(UpdateSourceTrigger.Explicit)]
    public void OrdinaryPropertyKeepsTheSameTriggerContract(UpdateSourceTrigger trigger)
    {
        var source = new Model();
        var target = new Target();
        var expression = target.SetBinding(Target.ValueProperty, new Binding(nameof(Model.Value))
        { Source = source, Mode = BindingMode.TwoWay, UpdateSourceTrigger = trigger });
        target.Value = true;
        Assert.Equal(trigger is UpdateSourceTrigger.Default or UpdateSourceTrigger.PropertyChanged, source.Value);
        expression.UpdateSource();
        Assert.True(source.Value);
    }

    [Fact]
    public void AttachedSourceChangesUsePropertyIdentityInsteadOfOnlyItsName()
    {
        var source = new Border();
        var converter = new CountingConverter();
        var target = new Target();
        target.SetBinding(Target.ValueProperty, new Binding
        { Source = source, Path = Path(0), Converter = converter });
        int initial = converter.Reads;
        source.SetValue(OtherCanContentScrollProperty, true);
        Assert.Equal(initial, converter.Reads);
        ScrollViewer.SetCanContentScroll(source, true);
        Assert.Equal(initial + 1, converter.Reads);
        Assert.True(target.Value);
    }

    [Fact]
    public void AttachedWriteBackUsesDependencyPropertyTypeForConversionAndDiagnostics()
    {
        var source = new Border();
        var target = new TextBlock();
        var converter = new BooleanTextConverter();
        var expression = target.SetBinding(TextBlock.TextProperty, new Binding
        { Source = source, Path = Path(1), Mode = BindingMode.TwoWay, Converter = converter });
        Assert.Equal("false", target.Text);
        target.Text = "true";
        Assert.True(ScrollViewer.GetCanContentScroll(source));
        Assert.Equal(typeof(bool), converter.BackType);
        Assert.Equal(nameof(ScrollViewer.CanContentScroll), Assert.IsType<BindingExpression>(expression).ResolvedSourcePropertyName);
    }

    [Fact]
    public void NestedAttachedPathTracksReplacementAndDetachesOldDependencyObject()
    {
        var first = new Border();
        var second = new Border();
        ScrollViewer.SetCanContentScroll(second, true);
        var source = new Model { Child = first };
        var target = new Target();
        var converter = new CountingConverter();
        var expression = target.SetBinding(Target.ValueProperty, new Binding("Child.(ScrollViewer.CanContentScroll)")
        { Source = source, Mode = BindingMode.TwoWay, Converter = converter });
        ScrollViewer.SetCanContentScroll(first, true);
        Assert.True(target.Value);
        ScrollViewer.SetCanContentScroll(first, false);
        Assert.False(target.Value);
        source.Child = second;
        Assert.True(target.Value);
        int before = converter.Reads;
        ScrollViewer.SetCanContentScroll(first, true);
        Assert.Equal(before, converter.Reads);
        target.Value = false;
        Assert.False(ScrollViewer.GetCanContentScroll(second));
        BindingOperations.ClearBinding(target, Target.ValueProperty);
        before = converter.Reads;
        ScrollViewer.SetCanContentScroll(second, true);
        Assert.Equal(before, converter.Reads);
        Assert.False(expression.IsActive);
    }

    [Fact]
    public void ParameterizedAttachedChainTracksRootAndIntermediateChanges()
    {
        var first = new Border();
        var second = new Border();
        var source = new Border();
        source.SetValue(ChildProperty, first);
        var target = new Target();
        target.SetBinding(Target.ValueProperty, new Binding
        {
            Source = source, Path = new PropertyPath("(0).(1)", ChildProperty, ScrollViewer.CanContentScrollProperty),
            Mode = BindingMode.TwoWay
        });
        ScrollViewer.SetCanContentScroll(first, true);
        Assert.True(target.Value);
        source.SetValue(ChildProperty, second);
        Assert.False(target.Value);
        target.Value = true;
        Assert.True(ScrollViewer.GetCanContentScroll(second));
        source.ClearValue(ChildProperty);
        Assert.False(target.Value);
    }

    [Fact]
    public void AttachedIntermediateCanContinueThroughClrAndIndexerSegments()
    {
        var source = new Border();
        var model = new Model { Value = true };
        source.SetValue(ModelProperty, new[] { model });
        var target = new Target();
        target.SetBinding(Target.ValueProperty, new Binding
        { Source = source, Path = new PropertyPath("(0)[0].Value", ModelProperty), Mode = BindingMode.TwoWay });
        Assert.True(target.Value);
        target.Value = false;
        Assert.False(model.Value);
        model.Value = true;
        Assert.True(target.Value);
        source.SetValue(ModelProperty, new[] { new Model() });
        Assert.False(target.Value);
    }

    [Fact]
    public void AttachedDataContextCanBeReplacedWithoutKeepingTheOldSource()
    {
        var first = new Border();
        var second = new Border();
        ScrollViewer.SetCanContentScroll(second, true);
        var target = new Target { DataContext = first };
        var expression = target.SetBinding(Target.ValueProperty, new Binding { Path = Path(2) });
        Assert.False(target.Value);
        target.DataContext = second;
        Assert.True(target.Value);
        ScrollViewer.SetCanContentScroll(first, true);
        ScrollViewer.SetCanContentScroll(second, false);
        Assert.False(target.Value);
        Assert.Same(expression, target.GetBindingExpression(Target.ValueProperty));
    }

    [Fact]
    public void AttachedWriteBackValidatesDataErrorsUsingThePropertyName()
    {
        var source = new ErrorSource();
        var target = new Target();
        var expression = target.SetBinding(Target.ValueProperty, new Binding
        { Source = source, Path = Path(0), Mode = BindingMode.TwoWay, ValidatesOnDataErrors = true });
        target.Value = true;
        Assert.True(ScrollViewer.GetCanContentScroll(source));
        Assert.True(expression.HasValidationError);
        Assert.Equal("内容滚动不可用", expression.ValidationError!.ErrorContent);
        Assert.Equal(nameof(ScrollViewer.CanContentScroll), source.LastRequestedProperty);
    }

    [Fact]
    public void AttachedNotifyDataErrorsCanBeRaisedAndCleared()
    {
        var source = new ErrorSource();
        var target = new Target();
        var expression = target.SetBinding(Target.ValueProperty, new Binding
        { Source = source, Path = Path(2), ValidatesOnNotifyDataErrors = true });
        Assert.False(expression.HasValidationError);
        source.PublishError(true);
        Assert.True(expression.HasValidationError);
        Assert.Equal(nameof(ScrollViewer.CanContentScroll), source.LastRequestedProperty);
        source.PublishError(false);
        Assert.False(expression.HasValidationError);
    }

    [Theory]
    [InlineData("()")]
    [InlineData("(0)")]
    [InlineData("(2)")]
    [InlineData("(MissingOwner.MissingProperty)")]
    [InlineData("(ScrollViewer.MissingProperty)")]
    [InlineData("(ScrollViewer.CanContentScroll")]
    public void InvalidAttachedPathsUseFallbackAndNeverWriteAnotherProperty(string path)
    {
        var source = new Border();
        var target = new TextBlock();
        target.SetBinding(TextBlock.TextProperty, new Binding
        { Source = source, Path = new PropertyPath(path), FallbackValue = "缺项", Mode = BindingMode.TwoWay });
        Assert.Equal("缺项", target.Text);
        target.Text = "true";
        Assert.False(ScrollViewer.GetCanContentScroll(source));
    }

    [Fact]
    public void ScrollViewerModeCanFollowAnAttachedPropertyOnAContentControl()
    {
        var content = new Border();
        var viewer = new ScrollViewer();
        viewer.SetBinding(ScrollViewer.CanContentScrollProperty, new Binding
        { Source = content, Path = Path(0), Mode = BindingMode.TwoWay });
        ScrollViewer.SetCanContentScroll(content, true);
        Assert.True(viewer.CanContentScroll);
        viewer.CanContentScroll = false;
        Assert.False(ScrollViewer.GetCanContentScroll(content));
    }

    private static PropertyPath Path(int syntax) => syntax switch
    {
        0 => new PropertyPath("(ScrollViewer.CanContentScroll)"),
        1 => new PropertyPath(ScrollViewer.CanContentScrollProperty),
        _ => new PropertyPath("(0)", ScrollViewer.CanContentScrollProperty)
    };

    private static readonly DependencyProperty OtherCanContentScrollProperty = DependencyProperty.RegisterAttached(
        "CanContentScroll", typeof(bool), typeof(AttachedPropertyBindingTests), new PropertyMetadata(false));
    private static readonly DependencyProperty ChildProperty = DependencyProperty.RegisterAttached(
        "Child", typeof(DependencyObject), typeof(AttachedPropertyBindingTests), new PropertyMetadata(null));
    private static readonly DependencyProperty ModelProperty = DependencyProperty.RegisterAttached(
        "Model", typeof(object), typeof(AttachedPropertyBindingTests), new PropertyMetadata(null));

    private sealed class Target : FrameworkElement
    {
        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            "Value", typeof(bool), typeof(Target), new PropertyMetadata(false));
        public bool Value { get => (bool)GetValue(ValueProperty)!; set => SetValue(ValueProperty, value); }
    }

    private sealed class Model : INotifyPropertyChanged
    {
        private bool _value;
        private DependencyObject? _child;
        public bool Value { get => _value; set { _value = value; PropertyChanged?.Invoke(this, new(nameof(Value))); } }
        public DependencyObject? Child { get => _child; set { _child = value; PropertyChanged?.Invoke(this, new(nameof(Child))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private sealed class CountingConverter : IValueConverter
    {
        public int Reads { get; private set; }
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) { Reads++; return value; }
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value;
    }

    private sealed class ErrorSource : Border, IDataErrorInfo, INotifyDataErrorInfo
    {
        public string? LastRequestedProperty { get; private set; }
        public string Error => string.Empty;
        public string this[string name]
        {
            get { LastRequestedProperty = name; return name == nameof(ScrollViewer.CanContentScroll) &&
                ScrollViewer.GetCanContentScroll(this) ? "内容滚动不可用" : string.Empty; }
        }
        public bool HasErrors { get; private set; }
        public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;
        public System.Collections.IEnumerable GetErrors(string? name)
        {
            LastRequestedProperty = name;
            return HasErrors && name == nameof(ScrollViewer.CanContentScroll) ? new[] { "内容滚动不可用" } : Array.Empty<string>();
        }
        public void PublishError(bool value) { HasErrors = value; ErrorsChanged?.Invoke(this, new(nameof(ScrollViewer.CanContentScroll))); }
    }

    private sealed class BooleanTextConverter : IValueConverter
    {
        public Type? BackType { get; private set; }
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is true ? "true" : "false";
        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        { BackType = targetType; return bool.Parse((string)value!); }
    }
}

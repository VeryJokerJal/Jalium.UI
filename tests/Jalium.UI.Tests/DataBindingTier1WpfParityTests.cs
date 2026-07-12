using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Data;

namespace Jalium.UI.Tests;

public sealed class DataBindingTier1WpfParityTests
{
    [Fact]
    public void BindingExpression_ExposesRootDataItemAndResolvedLeafPropertyName()
    {
        var model = new RootModel(new ChildModel("first"));
        var target = new Border { DataContext = model };

        var expression = Assert.IsType<BindingExpression>(
            BindingOperations.SetBinding(target, FrameworkElement.TagProperty, new Binding("Child.Name")));

        Assert.Same(model, expression.DataItem);
        Assert.Equal("Name", expression.ResolvedSourcePropertyName);
        Assert.Equal("first", target.Tag);

        var replacement = new RootModel(new ChildModel("second"));
        target.DataContext = replacement;

        Assert.Same(replacement, expression.DataItem);
        Assert.Equal("Name", expression.ResolvedSourcePropertyName);
        Assert.Equal("second", target.Tag);
    }

    [Fact]
    public void BindingExpression_ResolvedSourcePropertyName_IsNullForAnEmptyPath()
    {
        var model = new object();
        var target = new Border { DataContext = model };

        var expression = Assert.IsType<BindingExpression>(
            BindingOperations.SetBinding(target, FrameworkElement.TagProperty, new Binding()));

        Assert.Same(model, expression.DataItem);
        Assert.Null(expression.ResolvedSourcePropertyName);
    }

    [Fact]
    public void PriorityBinding_ShouldSerializeBindings_TracksCollectionContent()
    {
        var binding = new PriorityBinding();

        Assert.False(binding.ShouldSerializeBindings());

        binding.Bindings.Add(new Binding("Value"));
        Assert.True(binding.ShouldSerializeBindings());

        binding.Bindings.Clear();
        Assert.False(binding.ShouldSerializeBindings());

        var method = typeof(PriorityBinding).GetMethod(
            nameof(PriorityBinding.ShouldSerializeBindings),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.NotNull(method);
        Assert.Equal(
            EditorBrowsableState.Never,
            method!.GetCustomAttribute<EditorBrowsableAttribute>()?.State);
    }

    [Fact]
    public void PriorityBindingExpression_HasValidationError_ForwardsActiveExpressionState()
    {
        var model = new NumericModel { Value = 10 };
        var target = new Border { DataContext = model };
        var childBinding = new Binding("Value") { Mode = BindingMode.TwoWay };
        childBinding.ValidationRules.Add(new NonNegativeRule());

        var priorityBinding = new PriorityBinding();
        priorityBinding.Bindings.Add(childBinding);

        var expression = Assert.IsType<PriorityBindingExpression>(
            BindingOperations.SetBinding(target, FrameworkElement.TagProperty, priorityBinding));
        var activeExpression = Assert.IsType<BindingExpression>(expression.ActiveBindingExpression);

        Assert.False(activeExpression.HasValidationError);
        Assert.False(expression.HasValidationError);

        target.SetValue(activeExpression.TargetProperty, -1);
        activeExpression.UpdateSource();

        Assert.True(activeExpression.HasValidationError);
        Assert.True(expression.HasValidationError);
        Assert.Equal(10, model.Value);

        var property = typeof(PriorityBindingExpression).GetProperty(
            nameof(PriorityBindingExpression.HasValidationError),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        Assert.NotNull(property);
        Assert.Equal(
            typeof(BindingExpressionBase),
            property!.GetMethod!.GetBaseDefinition().DeclaringType);
    }

    [Fact]
    public void PropertyGroupDescription_NameComparers_UnwrapGroupsAndUseInvariantOrdering()
    {
        IComparer ascending = PropertyGroupDescription.CompareNameAscending;
        IComparer descending = PropertyGroupDescription.CompareNameDescending;
        var alpha = new TestGroup("alpha");
        var beta = new TestGroup("beta");
        var expected = Comparer.DefaultInvariant.Compare(alpha.Name, beta.Name);

        Assert.Same(ascending, PropertyGroupDescription.CompareNameAscending);
        Assert.Same(descending, PropertyGroupDescription.CompareNameDescending);
        Assert.Equal(expected, ascending.Compare(alpha, beta));
        Assert.Equal(-expected, descending.Compare(alpha, beta));
        Assert.Equal(0, ascending.Compare(alpha, "alpha"));
        Assert.Equal(typeof(IComparer), typeof(PropertyGroupDescription)
            .GetProperty(nameof(PropertyGroupDescription.CompareNameAscending))!.PropertyType);
    }

    [Fact]
    public void ValueConversionAttribute_TypeIdentityAndHashMatchWpfContract()
    {
        var attribute = new ValueConversionAttribute(typeof(string), typeof(int))
        {
            ParameterType = typeof(IFormatProvider)
        };
        var expectedHash = typeof(string).GetHashCode() + typeof(int).GetHashCode();

        Assert.Same(attribute, attribute.TypeId);
        Assert.Equal(expectedHash, attribute.GetHashCode());

        attribute.ParameterType = typeof(CultureInfo);
        Assert.Equal(expectedHash, attribute.GetHashCode());

        var typeIdProperty = typeof(ValueConversionAttribute).GetProperty(
            nameof(ValueConversionAttribute.TypeId),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        var getHashCode = typeof(ValueConversionAttribute).GetMethod(
            nameof(ValueConversionAttribute.GetHashCode),
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

        Assert.NotNull(typeIdProperty);
        Assert.NotNull(getHashCode);
    }

    private sealed record RootModel(ChildModel Child);

    private sealed record ChildModel(string Name);

    private sealed class NumericModel
    {
        public int Value { get; set; }
    }

    private sealed class NonNegativeRule : ValidationRule
    {
        public override ValidationResult Validate(object? value, CultureInfo cultureInfo) =>
            value is int number && number >= 0
                ? ValidationResult.ValidResult
                : new ValidationResult(false, "Value must be non-negative.");
    }

    private sealed class TestGroup(object name) : CollectionViewGroup(name)
    {
        public override bool IsBottomLevel => true;
    }
}

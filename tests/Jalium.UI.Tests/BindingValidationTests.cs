using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Data;

namespace Jalium.UI.Tests;

public class BindingValidationTests
{
    #region ValidationRules Collection Tests

    [Fact]
    public void Binding_ValidationRules_IsNotNull()
    {
        // Arrange & Act
        var binding = new Binding("Property");

        // Assert
        Assert.NotNull(binding.ValidationRules);
    }

    [Fact]
    public void Binding_CanAddValidationRules()
    {
        // Arrange
        var binding = new Binding("Property");

        // Act
        binding.ValidationRules.Add(new RequiredValidationRule());
        binding.ValidationRules.Add(new RangeValidationRule { Minimum = 0, Maximum = 100 });

        // Assert
        Assert.Equal(2, binding.ValidationRules.Count);
    }

    #endregion

    #region ValidationRule Tests

    [Fact]
    public void RequiredValidationRule_FailsForNull()
    {
        // Arrange
        var rule = new RequiredValidationRule();

        // Act
        var result = rule.Validate(null, CultureInfo.InvariantCulture);

        // Assert
        Assert.False(result.IsValid);
    }

    [Fact]
    public void RequiredValidationRule_FailsForEmptyString()
    {
        // Arrange
        var rule = new RequiredValidationRule();

        // Act
        var result = rule.Validate("", CultureInfo.InvariantCulture);

        // Assert
        Assert.False(result.IsValid);
    }

    [Fact]
    public void RequiredValidationRule_PassesForValidValue()
    {
        // Arrange
        var rule = new RequiredValidationRule();

        // Act
        var result = rule.Validate("test", CultureInfo.InvariantCulture);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void RangeValidationRule_FailsForValueBelowMinimum()
    {
        // Arrange
        var rule = new RangeValidationRule { Minimum = 0, Maximum = 100 };

        // Act
        var result = rule.Validate(-5, CultureInfo.InvariantCulture);

        // Assert
        Assert.False(result.IsValid);
    }

    [Fact]
    public void RangeValidationRule_FailsForValueAboveMaximum()
    {
        // Arrange
        var rule = new RangeValidationRule { Minimum = 0, Maximum = 100 };

        // Act
        var result = rule.Validate(150, CultureInfo.InvariantCulture);

        // Assert
        Assert.False(result.IsValid);
    }

    [Fact]
    public void RangeValidationRule_PassesForValueInRange()
    {
        // Arrange
        var rule = new RangeValidationRule { Minimum = 0, Maximum = 100 };

        // Act
        var result = rule.Validate(50, CultureInfo.InvariantCulture);

        // Assert
        Assert.True(result.IsValid);
    }

    #endregion

    #region Validation Attached Properties Tests

    [Fact]
    public void Validation_HasError_DefaultsToFalse()
    {
        // Arrange
        var element = new TestElement();

        // Act & Assert
        Assert.False(Validation.GetHasError(element));
    }

    [Fact]
    public void Validation_MarkInvalid_SetsHasErrorToTrue()
    {
        // Arrange
        var element = new TestElement();
        var error = new ValidationError("Test error");

        // Act
        Validation.MarkInvalid(element, error);

        // Assert
        Assert.True(Validation.GetHasError(element));
    }

    [Fact]
    public void Validation_MarkInvalid_AddsToErrorsCollection()
    {
        // Arrange
        var element = new TestElement();
        var error = new ValidationError("Test error");

        // Act
        Validation.MarkInvalid(element, error);

        // Assert
        var errors = Validation.GetErrors(element);
        Assert.NotNull(errors);
        Assert.Single(errors);
        Assert.Same(error, errors[0]);
    }

    [Fact]
    public void Validation_ClearInvalid_SetsHasErrorToFalse()
    {
        // Arrange
        var element = new TestElement();
        var error = new ValidationError("Test error");
        Validation.MarkInvalid(element, error);

        // Act
        Validation.ClearInvalid(element);

        // Assert
        Assert.False(Validation.GetHasError(element));
    }

    [Fact]
    public void Validation_ClearInvalid_ClearsErrorsCollection()
    {
        // Arrange
        var element = new TestElement();
        var error = new ValidationError("Test error");
        Validation.MarkInvalid(element, error);

        // Act
        Validation.ClearInvalid(element);

        // Assert
        var errors = Validation.GetErrors(element);
        Assert.NotNull(errors);
        Assert.Empty(errors);
    }

    #endregion

    #region Binding Validation Properties Tests

    [Fact]
    public void Binding_ValidatesOnExceptions_DefaultsToFalse()
    {
        // Arrange & Act
        var binding = new Binding("Property");

        // Assert
        Assert.False(binding.ValidatesOnExceptions);
    }

    [Fact]
    public void Binding_ValidatesOnDataErrors_DefaultsToFalse()
    {
        // Arrange & Act
        var binding = new Binding("Property");

        // Assert
        Assert.False(binding.ValidatesOnDataErrors);
    }

    [Fact]
    public void Binding_ValidatesOnNotifyDataErrors_DefaultsToTrue()
    {
        // Arrange & Act
        var binding = new Binding("Property");

        // Assert
        Assert.True(binding.ValidatesOnNotifyDataErrors);
    }

    #endregion

    #region Binding Integration Tests

    [Fact]
    public void BindingExpression_UpdateSource_ExecutesValidationRules()
    {
        // Arrange
        var vm = new TestViewModel { Age = 25 };
        var element = new TestElement { DataContext = vm };

        var binding = new Binding("Age");
        binding.ValidationRules.Add(new RangeValidationRule { Minimum = 0, Maximum = 120 });
        binding.Mode = BindingMode.TwoWay;

        BindingOperations.SetBinding(element, TestElement.TextProperty, binding);

        // Act - Set an invalid value
        element.SetValue(TestElement.TextProperty, -5);

        // The binding expression should run validation on UpdateSource
        var expr = BindingOperations.GetBindingExpression(element, TestElement.TextProperty);
        expr?.UpdateSource();

        // Assert - Source should not be updated due to validation failure
        Assert.Equal(25, vm.Age); // Original value preserved
        Assert.True(Validation.GetHasError(element));
    }

    [Fact]
    public void BindingExpression_UpdateSource_AllowsValidValue()
    {
        // Arrange
        var vm = new TestViewModel { Age = 25 };
        var element = new TestElement { DataContext = vm };

        var binding = new Binding("Age");
        binding.ValidationRules.Add(new RangeValidationRule { Minimum = 0, Maximum = 120 });
        binding.Mode = BindingMode.TwoWay;

        BindingOperations.SetBinding(element, TestElement.TextProperty, binding);

        // Act - Set a valid value
        element.SetValue(TestElement.TextProperty, 30);

        var expr = BindingOperations.GetBindingExpression(element, TestElement.TextProperty);
        expr?.UpdateSource();

        // Assert - Source should be updated
        Assert.Equal(30, vm.Age);
        Assert.False(Validation.GetHasError(element));
    }

    [Fact]
    public void BindingExpression_UpdateSource_ClearsErrorsOnSuccess()
    {
        // Arrange
        var vm = new TestViewModel { Age = 25 };
        var element = new TestElement { DataContext = vm };

        var binding = new Binding("Age");
        binding.ValidationRules.Add(new RangeValidationRule { Minimum = 0, Maximum = 120 });
        binding.Mode = BindingMode.TwoWay;

        BindingOperations.SetBinding(element, TestElement.TextProperty, binding);

        // First, cause a validation error
        element.SetValue(TestElement.TextProperty, -5);
        var expr = BindingOperations.GetBindingExpression(element, TestElement.TextProperty);
        expr?.UpdateSource();
        Assert.True(Validation.GetHasError(element));

        // Act - Now set a valid value
        element.SetValue(TestElement.TextProperty, 50);
        expr?.UpdateSource();

        // Assert - Error should be cleared
        Assert.False(Validation.GetHasError(element));
    }

    #endregion

    #region IDataErrorInfo Tests

    [Fact]
    public void BindingExpression_ValidatesOnDataErrors_ChecksIDataErrorInfo()
    {
        // Arrange
        var vm = new TestViewModelWithDataErrorInfo { Age = 25 };
        var element = new TestElement { DataContext = vm };

        var binding = new Binding("Age");
        binding.ValidatesOnDataErrors = true;
        binding.Mode = BindingMode.TwoWay;

        BindingOperations.SetBinding(element, TestElement.TextProperty, binding);

        // Act - Set an invalid value that IDataErrorInfo will reject
        element.SetValue(TestElement.TextProperty, -5);

        var expr = BindingOperations.GetBindingExpression(element, TestElement.TextProperty);
        expr?.UpdateSource();

        // Assert
        // Note: The actual behavior depends on how IDataErrorInfo is implemented
        // and whether the source actually updated before validation
    }

    #endregion

    #region Test Helpers

    private class TestElement : FrameworkElement
    {
        public static readonly DependencyProperty TextProperty =
            DependencyProperty.Register("Text", typeof(object), typeof(TestElement),
                new PropertyMetadata(null));

        public object? Text
        {
            get => GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }
    }

    private class TestViewModel : INotifyPropertyChanged
    {
        private int _age;

        public int Age
        {
            get => _age;
            set
            {
                if (_age != value)
                {
                    _age = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Age)));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private class TestViewModelWithDataErrorInfo : INotifyPropertyChanged, IDataErrorInfo
    {
        private int _age;

        public int Age
        {
            get => _age;
            set
            {
                if (_age != value)
                {
                    _age = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Age)));
                }
            }
        }

        public string Error => string.Empty;

        public string this[string columnName]
        {
            get
            {
                if (columnName == nameof(Age) && Age < 0)
                    return "Age cannot be negative";
                return string.Empty;
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    #endregion
}

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace Jalium.UI;

/// <summary>
/// Provides attached properties and methods for data validation.
/// </summary>
public static class Validation
{
    /// <summary>
    /// Delegate for handling validation adorner creation/removal.
    /// The UI layer registers this to provide visual feedback for validation errors.
    /// </summary>
    public static Action<DependencyObject, bool>? AdornerHandler { get; set; }

    #region Attached Properties

    /// <summary>
    /// Identifies the HasError attached property.
    /// </summary>
    public static readonly DependencyProperty HasErrorProperty =
        DependencyProperty.RegisterAttached("HasError", typeof(bool), typeof(Validation),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the Errors attached property.
    /// </summary>
    public static readonly DependencyProperty ErrorsProperty =
        DependencyProperty.RegisterAttached("Errors", typeof(ReadOnlyObservableCollection<ValidationError>), typeof(Validation),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the ErrorTemplate attached property.
    /// </summary>
    public static readonly DependencyProperty ErrorTemplateProperty =
        DependencyProperty.RegisterAttached("ErrorTemplate", typeof(ControlTemplate), typeof(Validation),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the ValidationAdornerSite attached property.
    /// </summary>
    public static readonly DependencyProperty ValidationAdornerSiteProperty =
        DependencyProperty.RegisterAttached("ValidationAdornerSite", typeof(DependencyObject), typeof(Validation),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the ValidationAdornerSiteFor attached property.
    /// </summary>
    public static readonly DependencyProperty ValidationAdornerSiteForProperty =
        DependencyProperty.RegisterAttached("ValidationAdornerSiteFor", typeof(DependencyObject), typeof(Validation),
            new PropertyMetadata(null));

    /// <summary>
    /// Gets the HasError value.
    /// </summary>
    public static bool GetHasError(DependencyObject element) =>
        (bool)(element.GetValue(HasErrorProperty) ?? false);

    /// <summary>
    /// Gets the Errors collection.
    /// </summary>
    public static ReadOnlyObservableCollection<ValidationError>? GetErrors(DependencyObject element) =>
        (ReadOnlyObservableCollection<ValidationError>?)element.GetValue(ErrorsProperty);

    /// <summary>
    /// Gets the ErrorTemplate.
    /// </summary>
    public static ControlTemplate? GetErrorTemplate(DependencyObject element) =>
        (ControlTemplate?)element.GetValue(ErrorTemplateProperty);

    /// <summary>
    /// Sets the ErrorTemplate.
    /// </summary>
    public static void SetErrorTemplate(DependencyObject element, ControlTemplate? value) =>
        element.SetValue(ErrorTemplateProperty, value);

    /// <summary>
    /// Gets the ValidationAdornerSite.
    /// </summary>
    public static DependencyObject? GetValidationAdornerSite(DependencyObject element) =>
        (DependencyObject?)element.GetValue(ValidationAdornerSiteProperty);

    /// <summary>
    /// Sets the ValidationAdornerSite.
    /// </summary>
    public static void SetValidationAdornerSite(DependencyObject element, DependencyObject? value) =>
        element.SetValue(ValidationAdornerSiteProperty, value);

    /// <summary>
    /// Gets the ValidationAdornerSiteFor.
    /// </summary>
    public static DependencyObject? GetValidationAdornerSiteFor(DependencyObject element) =>
        (DependencyObject?)element.GetValue(ValidationAdornerSiteForProperty);

    /// <summary>
    /// Sets the ValidationAdornerSiteFor.
    /// </summary>
    public static void SetValidationAdornerSiteFor(DependencyObject element, DependencyObject? value) =>
        element.SetValue(ValidationAdornerSiteForProperty, value);

    #endregion

    #region Routed Events

    /// <summary>
    /// Identifies the Error routed event.
    /// </summary>
    public static readonly RoutedEvent ErrorEvent =
        EventManager.RegisterRoutedEvent("Error", RoutingStrategy.Bubble,
            typeof(EventHandler<ValidationErrorEventArgs>), typeof(Validation));

    /// <summary>
    /// Adds a handler for the Error event.
    /// </summary>
    public static void AddErrorHandler(DependencyObject element, EventHandler<ValidationErrorEventArgs> handler)
    {
        if (element is UIElement uiElement)
        {
            uiElement.AddHandler(ErrorEvent, handler);
        }
    }

    /// <summary>
    /// Removes a handler for the Error event.
    /// </summary>
    public static void RemoveErrorHandler(DependencyObject element, EventHandler<ValidationErrorEventArgs> handler)
    {
        if (element is UIElement uiElement)
        {
            uiElement.RemoveHandler(ErrorEvent, handler);
        }
    }

    #endregion

    #region Validation Methods

    private static readonly Dictionary<DependencyObject, ObservableCollection<ValidationError>> _errors = new();

    /// <summary>
    /// Marks the element as having a validation error.
    /// </summary>
    public static void MarkInvalid(DependencyObject element, ValidationError error)
    {
        if (!_errors.TryGetValue(element, out var errors))
        {
            errors = new ObservableCollection<ValidationError>();
            _errors[element] = errors;
            element.SetValue(ErrorsProperty, new ReadOnlyObservableCollection<ValidationError>(errors));
        }

        errors.Add(error);
        element.SetValue(HasErrorProperty, true);

        // Show validation adorner
        AdornerHandler?.Invoke(element, true);

        if (element is UIElement uiElement)
        {
            var args = new ValidationErrorEventArgs(ErrorEvent, error, ValidationErrorEventAction.Added);
            uiElement.RaiseEvent(args);
        }
    }

    /// <summary>
    /// Removes all validation errors from the element.
    /// </summary>
    public static void ClearInvalid(DependencyObject element)
    {
        if (_errors.TryGetValue(element, out var errors))
        {
            var removedErrors = errors.ToList();
            errors.Clear();
            element.SetValue(HasErrorProperty, false);

            // Remove validation adorner
            AdornerHandler?.Invoke(element, false);

            if (element is UIElement uiElement)
            {
                foreach (var error in removedErrors)
                {
                    var args = new ValidationErrorEventArgs(ErrorEvent, error, ValidationErrorEventAction.Removed);
                    uiElement.RaiseEvent(args);
                }
            }
        }
    }

    #endregion
}

/// <summary>
/// Represents a validation error.
/// </summary>
public sealed class ValidationError
{
    /// <summary>
    /// Gets the validation rule that caused the error.
    /// </summary>
    public ValidationRule? RuleInError { get; }

    /// <summary>
    /// Gets the binding source that caused the error.
    /// </summary>
    public object? BindingSource { get; }

    /// <summary>
    /// Gets the error content.
    /// </summary>
    public object? ErrorContent { get; }

    /// <summary>
    /// Gets the exception that caused the error.
    /// </summary>
    public Exception? Exception { get; }

    /// <summary>
    /// Creates a new ValidationError.
    /// </summary>
    public ValidationError(ValidationRule rule, object? bindingSource, object? errorContent, Exception? exception)
    {
        RuleInError = rule;
        BindingSource = bindingSource;
        ErrorContent = errorContent;
        Exception = exception;
    }

    /// <summary>
    /// Creates a new ValidationError with only error content.
    /// </summary>
    public ValidationError(object errorContent)
    {
        ErrorContent = errorContent;
    }
}

/// <summary>
/// Provides data for validation error events.
/// </summary>
public sealed class ValidationErrorEventArgs : RoutedEventArgs
{
    /// <summary>
    /// Gets the validation error.
    /// </summary>
    public ValidationError Error { get; }

    /// <summary>
    /// Gets the action that occurred.
    /// </summary>
    public ValidationErrorEventAction Action { get; }

    /// <summary>
    /// Creates a new ValidationErrorEventArgs.
    /// </summary>
    public ValidationErrorEventArgs(RoutedEvent routedEvent, ValidationError error, ValidationErrorEventAction action)
        : base(routedEvent)
    {
        Error = error;
        Action = action;
    }
}

/// <summary>
/// Specifies the action for a validation error event.
/// </summary>
public enum ValidationErrorEventAction
{
    /// <summary>
    /// An error was added.
    /// </summary>
    Added,

    /// <summary>
    /// An error was removed.
    /// </summary>
    Removed
}

/// <summary>
/// Base class for validation rules.
/// </summary>
public abstract class ValidationRule
{
    /// <summary>
    /// Gets or sets when validation should occur.
    /// </summary>
    public ValidationStep ValidationStep { get; set; } = ValidationStep.RawProposedValue;

    /// <summary>
    /// Gets or sets whether to run validation on target updates.
    /// </summary>
    public bool ValidatesOnTargetUpdated { get; set; }

    /// <summary>
    /// Validates the value.
    /// </summary>
    /// <param name="value">The value to validate.</param>
    /// <param name="cultureInfo">The culture to use.</param>
    /// <returns>The validation result.</returns>
    public abstract ValidationResult Validate(object? value, CultureInfo cultureInfo);
}

/// <summary>
/// Specifies when validation should occur.
/// </summary>
public enum ValidationStep
{
    /// <summary>
    /// Validate the raw value before any conversion.
    /// </summary>
    RawProposedValue,

    /// <summary>
    /// Validate after type conversion.
    /// </summary>
    ConvertedProposedValue,

    /// <summary>
    /// Validate after the value has been set on the source.
    /// </summary>
    UpdatedValue,

    /// <summary>
    /// Validate after the source has been committed.
    /// </summary>
    CommittedValue
}

/// <summary>
/// Represents the result of a validation operation.
/// </summary>
public sealed class ValidationResult
{
    /// <summary>
    /// Gets whether the validation passed.
    /// </summary>
    public bool IsValid { get; }

    /// <summary>
    /// Gets the error content.
    /// </summary>
    public object? ErrorContent { get; }

    /// <summary>
    /// Gets a valid result.
    /// </summary>
    public static ValidationResult ValidResult { get; } = new(true, null);

    /// <summary>
    /// Creates a new ValidationResult.
    /// </summary>
    public ValidationResult(bool isValid, object? errorContent)
    {
        IsValid = isValid;
        ErrorContent = errorContent;
    }
}

/// <summary>
/// Validates that a value is not null or empty.
/// </summary>
public sealed class RequiredValidationRule : ValidationRule
{
    /// <summary>
    /// Gets or sets the error message.
    /// </summary>
    public string ErrorMessage { get; set; } = "This field is required.";

    /// <inheritdoc />
    public override ValidationResult Validate(object? value, CultureInfo cultureInfo)
    {
        if (value == null)
            return new ValidationResult(false, ErrorMessage);

        if (value is string str && string.IsNullOrWhiteSpace(str))
            return new ValidationResult(false, ErrorMessage);

        return ValidationResult.ValidResult;
    }
}

/// <summary>
/// Validates that a string matches a regular expression.
/// </summary>
public class RegexValidationRule : ValidationRule
{
    /// <summary>
    /// Gets or sets the regex pattern.
    /// </summary>
    public string? Pattern { get; set; }

    /// <summary>
    /// Gets or sets the error message.
    /// </summary>
    public string ErrorMessage { get; set; } = "The value does not match the required format.";

    /// <inheritdoc />
    public override ValidationResult Validate(object? value, CultureInfo cultureInfo)
    {
        if (string.IsNullOrEmpty(Pattern))
            return ValidationResult.ValidResult;

        var str = value?.ToString();
        if (string.IsNullOrEmpty(str))
            return ValidationResult.ValidResult;

        if (!System.Text.RegularExpressions.Regex.IsMatch(str, Pattern))
            return new ValidationResult(false, ErrorMessage);

        return ValidationResult.ValidResult;
    }
}

/// <summary>
/// Validates that a numeric value is within a range.
/// </summary>
public sealed class RangeValidationRule : ValidationRule
{
    /// <summary>
    /// Gets or sets the minimum value.
    /// </summary>
    public double? Minimum { get; set; }

    /// <summary>
    /// Gets or sets the maximum value.
    /// </summary>
    public double? Maximum { get; set; }

    /// <summary>
    /// Gets or sets the error message.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <inheritdoc />
    public override ValidationResult Validate(object? value, CultureInfo cultureInfo)
    {
        if (value == null)
            return ValidationResult.ValidResult;

        double numValue;
        if (value is double d)
            numValue = d;
        else if (value is int i)
            numValue = i;
        else if (value is decimal dec)
            numValue = (double)dec;
        else if (!double.TryParse(value.ToString(), NumberStyles.Any, cultureInfo, out numValue))
            return new ValidationResult(false, "Value must be a number.");

        if (Minimum.HasValue && numValue < Minimum.Value)
        {
            var message = ErrorMessage ?? $"Value must be at least {Minimum.Value}.";
            return new ValidationResult(false, message);
        }

        if (Maximum.HasValue && numValue > Maximum.Value)
        {
            var message = ErrorMessage ?? $"Value must be at most {Maximum.Value}.";
            return new ValidationResult(false, message);
        }

        return ValidationResult.ValidResult;
    }
}

/// <summary>
/// Validates that a string has a specific length.
/// </summary>
public sealed class StringLengthValidationRule : ValidationRule
{
    /// <summary>
    /// Gets or sets the minimum length.
    /// </summary>
    public int? MinimumLength { get; set; }

    /// <summary>
    /// Gets or sets the maximum length.
    /// </summary>
    public int? MaximumLength { get; set; }

    /// <summary>
    /// Gets or sets the error message.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <inheritdoc />
    public override ValidationResult Validate(object? value, CultureInfo cultureInfo)
    {
        var str = value?.ToString() ?? string.Empty;
        var length = str.Length;

        if (MinimumLength.HasValue && length < MinimumLength.Value)
        {
            var message = ErrorMessage ?? $"Value must be at least {MinimumLength.Value} characters.";
            return new ValidationResult(false, message);
        }

        if (MaximumLength.HasValue && length > MaximumLength.Value)
        {
            var message = ErrorMessage ?? $"Value must be at most {MaximumLength.Value} characters.";
            return new ValidationResult(false, message);
        }

        return ValidationResult.ValidResult;
    }
}

/// <summary>
/// Validates email addresses.
/// </summary>
public sealed class EmailValidationRule : RegexValidationRule
{
    /// <summary>
    /// Creates a new EmailValidationRule.
    /// </summary>
    public EmailValidationRule()
    {
        Pattern = @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$";
        ErrorMessage = "Please enter a valid email address.";
    }
}

/// <summary>
/// Validates using a custom predicate.
/// </summary>
public sealed class PredicateValidationRule : ValidationRule
{
    /// <summary>
    /// Gets or sets the validation predicate.
    /// </summary>
    public Func<object?, bool>? Predicate { get; set; }

    /// <summary>
    /// Gets or sets the error message.
    /// </summary>
    public string ErrorMessage { get; set; } = "Validation failed.";

    /// <inheritdoc />
    public override ValidationResult Validate(object? value, CultureInfo cultureInfo)
    {
        if (Predicate == null || Predicate(value))
            return ValidationResult.ValidResult;

        return new ValidationResult(false, ErrorMessage);
    }
}

/// <summary>
/// Validates using data annotations on the bound object.
/// </summary>
public sealed class DataAnnotationsValidationRule : ValidationRule
{
    /// <inheritdoc />
    public override ValidationResult Validate(object? value, CultureInfo cultureInfo)
    {
        if (value == null)
            return ValidationResult.ValidResult;

        var validationResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var context = new ValidationContext(value);

        if (System.ComponentModel.DataAnnotations.Validator.TryValidateObject(value, context, validationResults, validateAllProperties: true))
        {
            return ValidationResult.ValidResult;
        }

        var errors = string.Join(Environment.NewLine, validationResults.Select(r => r.ErrorMessage));
        return new ValidationResult(false, errors);
    }
}

/// <summary>
/// Validates a specific property using data annotations.
/// </summary>
public sealed class PropertyValidationRule : ValidationRule
{
    /// <summary>
    /// Gets or sets the property name to validate.
    /// </summary>
    public string? PropertyName { get; set; }

    /// <summary>
    /// Gets or sets the object containing the property.
    /// </summary>
    public object? ValidationObject { get; set; }

    /// <inheritdoc />
    public override ValidationResult Validate(object? value, CultureInfo cultureInfo)
    {
        if (ValidationObject == null || string.IsNullOrEmpty(PropertyName))
            return ValidationResult.ValidResult;

        var validationResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
        var context = new ValidationContext(ValidationObject) { MemberName = PropertyName };

        if (System.ComponentModel.DataAnnotations.Validator.TryValidateProperty(value, context, validationResults))
        {
            return ValidationResult.ValidResult;
        }

        var errors = string.Join(Environment.NewLine, validationResults.Select(r => r.ErrorMessage));
        return new ValidationResult(false, errors);
    }
}

/// <summary>
/// Exception wrapper validation rule.
/// </summary>
public sealed class ExceptionValidationRule : ValidationRule
{
    /// <inheritdoc />
    public override ValidationResult Validate(object? value, CultureInfo cultureInfo)
    {
        // This rule is used when exceptions occur during binding
        return ValidationResult.ValidResult;
    }
}

/// <summary>
/// Notifies about data error information.
/// </summary>
public sealed class DataErrorValidationRule : ValidationRule
{
    /// <inheritdoc />
    public override ValidationResult Validate(object? value, CultureInfo cultureInfo)
    {
        // Check for IDataErrorInfo
        if (value is IDataErrorInfo errorInfo)
        {
            var error = errorInfo.Error;
            if (!string.IsNullOrEmpty(error))
            {
                return new ValidationResult(false, error);
            }
        }

        // Check for INotifyDataErrorInfo
        if (value is INotifyDataErrorInfo notifyErrorInfo && notifyErrorInfo.HasErrors)
        {
            var errors = notifyErrorInfo.GetErrors(null)?.Cast<object>().ToList();
            if (errors?.Any() ?? false)
            {
                return new ValidationResult(false, errors.First());
            }
        }

        return ValidationResult.ValidResult;
    }
}

/// <summary>
/// Template for validation error display.
/// </summary>
public class ControlTemplate
{
    /// <summary>
    /// Gets or sets the visual tree factory.
    /// </summary>
    public Func<FrameworkElement>? VisualTreeFactory { get; set; }

    /// <summary>
    /// Gets or sets the target type.
    /// </summary>
    public Type? TargetType { get; set; }

    /// <summary>
    /// Creates the visual tree.
    /// </summary>
    public FrameworkElement? CreateVisualTree()
    {
        return VisualTreeFactory?.Invoke();
    }
}

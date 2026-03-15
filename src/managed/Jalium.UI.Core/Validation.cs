using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace Jalium.UI;

/// <summary>
/// Provides attached properties and methods for data validation.
/// </summary>
public static class Validation
{
    /// <summary>
    /// Gets or sets the adorner handler callback. Set by the Controls layer
    /// to connect validation errors with visual adorner feedback.
    /// </summary>
    public static Action<DependencyObject, bool>? AdornerHandler { get; set; }

    #region Attached Properties

    /// <summary>
    /// Identifies the HasError attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static readonly DependencyProperty HasErrorProperty =
        DependencyProperty.RegisterAttached("HasError", typeof(bool), typeof(Validation),
            new PropertyMetadata(false));

    /// <summary>
    /// Identifies the Errors attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty ErrorsProperty =
        DependencyProperty.RegisterAttached("Errors", typeof(ReadOnlyObservableCollection<ValidationError>), typeof(Validation),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the ErrorTemplate attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static readonly DependencyProperty ErrorTemplateProperty =
        DependencyProperty.RegisterAttached("ErrorTemplate", typeof(ControlTemplate), typeof(Validation),
            new PropertyMetadata(null));

    /// <summary>
    /// Gets the HasError value.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.State)]
    public static bool GetHasError(DependencyObject element) =>
        (bool)(element.GetValue(HasErrorProperty) ?? false);

    /// <summary>
    /// Gets the Errors collection.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static ReadOnlyObservableCollection<ValidationError>? GetErrors(DependencyObject element) =>
        (ReadOnlyObservableCollection<ValidationError>?)element.GetValue(ErrorsProperty);

    /// <summary>
    /// Gets the ErrorTemplate.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static ControlTemplate? GetErrorTemplate(DependencyObject element) =>
        (ControlTemplate?)element.GetValue(ErrorTemplateProperty);

    /// <summary>
    /// Sets the ErrorTemplate.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Other)]
    public static void SetErrorTemplate(DependencyObject element, ControlTemplate? value) =>
        element.SetValue(ErrorTemplateProperty, value);

    #endregion

    #region Routed Events

    /// <summary>
    /// Identifies the Error routed event.
    /// </summary>
    public static readonly RoutedEvent ErrorEvent =
        EventManager.RegisterRoutedEvent("Error", RoutingStrategy.Bubble,
            typeof(EventHandler<ValidationErrorEventArgs>), typeof(Validation));

    /// <summary>
    /// Adds an error event handler.
    /// </summary>
    public static void AddErrorHandler(DependencyObject element, EventHandler<ValidationErrorEventArgs> handler)
    {
        if (element is UIElement uiElement)
        {
            uiElement.AddHandler(ErrorEvent, handler);
        }
    }

    /// <summary>
    /// Removes an error event handler.
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

    private static readonly ConditionalWeakTable<DependencyObject, ObservableCollection<ValidationError>> _errors = new();

    /// <summary>
    /// Marks the element as having a validation error.
    /// </summary>
    public static void MarkInvalid(DependencyObject element, ValidationError error)
    {
        if (!_errors.TryGetValue(element, out var errors))
        {
            errors = new ObservableCollection<ValidationError>();
            _errors.Add(element, errors);
            element.SetValue(ErrorsProperty, new ReadOnlyObservableCollection<ValidationError>(errors));
        }

        errors.Add(error);
        element.SetValue(HasErrorProperty, true);

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
    public ValidationError(ValidationRule? rule, object? bindingSource, object? errorContent, Exception? exception)
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
/// A validation rule that checks for exceptions thrown during the update of the source value.
/// </summary>
public sealed class ExceptionValidationRule : ValidationRule
{
    /// <summary>
    /// Validates the value.
    /// </summary>
    /// <param name="value">The value to validate.</param>
    /// <param name="cultureInfo">The culture to use.</param>
    /// <returns>The validation result.</returns>
    public override ValidationResult Validate(object? value, CultureInfo cultureInfo)
    {
        // This rule is special - it's used by the binding engine to catch exceptions
        // during value conversion or source update. The actual validation is done
        // by the binding engine, not by calling this method directly.
        return ValidationResult.ValidResult;
    }

    /// <summary>
    /// Creates a validation error from an exception.
    /// </summary>
    internal static ValidationError CreateErrorFromException(Exception exception, object? bindingSource)
    {
        var errorContent = exception.InnerException?.Message ?? exception.Message;
        return new ValidationError(new ExceptionValidationRule(), bindingSource, errorContent, exception);
    }
}

/// <summary>
/// A validation rule that checks for errors raised by the IDataErrorInfo interface.
/// </summary>
public sealed class DataErrorValidationRule : ValidationRule
{
    /// <summary>
    /// Validates the value.
    /// </summary>
    /// <param name="value">The value to validate.</param>
    /// <param name="cultureInfo">The culture to use.</param>
    /// <returns>The validation result.</returns>
    public override ValidationResult Validate(object? value, CultureInfo cultureInfo)
    {
        // This rule is special - it works with IDataErrorInfo
        // The actual validation is done by checking the Error property
        return ValidationResult.ValidResult;
    }

    /// <summary>
    /// Validates a property on a data error info source.
    /// </summary>
    internal static ValidationResult ValidateProperty(System.ComponentModel.IDataErrorInfo source, string propertyName)
    {
        var error = source[propertyName];
        if (!string.IsNullOrEmpty(error))
        {
            return new ValidationResult(false, error);
        }
        return ValidationResult.ValidResult;
    }

    /// <summary>
    /// Validates the overall object error.
    /// </summary>
    internal static ValidationResult ValidateObject(System.ComponentModel.IDataErrorInfo source)
    {
        var error = source.Error;
        if (!string.IsNullOrEmpty(error))
        {
            return new ValidationResult(false, error);
        }
        return ValidationResult.ValidResult;
    }
}

/// <summary>
/// A validation rule that checks for errors raised by the INotifyDataErrorInfo interface.
/// </summary>
public sealed class NotifyDataErrorValidationRule : ValidationRule
{
    /// <summary>
    /// Validates the value.
    /// </summary>
    /// <param name="value">The value to validate.</param>
    /// <param name="cultureInfo">The culture to use.</param>
    /// <returns>The validation result.</returns>
    public override ValidationResult Validate(object? value, CultureInfo cultureInfo)
    {
        // This rule is special - it works with INotifyDataErrorInfo
        // The actual validation is done by subscribing to ErrorsChanged
        return ValidationResult.ValidResult;
    }

    /// <summary>
    /// Validates a property on a notify data error info source.
    /// </summary>
    internal static ValidationResult ValidateProperty(System.ComponentModel.INotifyDataErrorInfo source, string propertyName)
    {
        var errors = source.GetErrors(propertyName);
        if (errors != null)
        {
            var errorList = errors.Cast<object>().ToList();
            if (errorList.Count > 0)
            {
                var errorContent = string.Join(Environment.NewLine, errorList.Select(e => e?.ToString() ?? ""));
                return new ValidationResult(false, errorContent);
            }
        }
        return ValidationResult.ValidResult;
    }

    /// <summary>
    /// Checks if the source has any errors.
    /// </summary>
    internal static bool HasErrors(System.ComponentModel.INotifyDataErrorInfo source)
    {
        return source.HasErrors;
    }
}

/// <summary>
/// Validates that a string matches a regular expression pattern.
/// </summary>
public class RegexValidationRule : ValidationRule
{
    /// <summary>
    /// Gets or sets the regular expression pattern.
    /// </summary>
    public string? Pattern { get; set; }

    /// <summary>
    /// Gets or sets the error message.
    /// </summary>
    public string ErrorMessage { get; set; } = "Value does not match the required pattern.";

    /// <inheritdoc />
    public override ValidationResult Validate(object? value, CultureInfo cultureInfo)
    {
        if (string.IsNullOrEmpty(Pattern))
            return ValidationResult.ValidResult;

        if (value == null)
            return ValidationResult.ValidResult;

        var str = value.ToString();
        if (str == null)
            return ValidationResult.ValidResult;

        var regex = new System.Text.RegularExpressions.Regex(Pattern);
        if (!regex.IsMatch(str))
        {
            return new ValidationResult(false, ErrorMessage);
        }

        return ValidationResult.ValidResult;
    }
}

/// <summary>
/// Validates that a string has a minimum or maximum length.
/// </summary>
public sealed class StringLengthValidationRule : ValidationRule
{
    /// <summary>
    /// Gets or sets the minimum length.
    /// </summary>
    public int? MinLength { get; set; }

    /// <summary>
    /// Gets or sets the maximum length.
    /// </summary>
    public int? MaxLength { get; set; }

    /// <summary>
    /// Gets or sets the error message.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <inheritdoc />
    public override ValidationResult Validate(object? value, CultureInfo cultureInfo)
    {
        var str = value?.ToString() ?? string.Empty;
        var length = str.Length;

        if (MinLength.HasValue && length < MinLength.Value)
        {
            var message = ErrorMessage ?? $"Value must be at least {MinLength.Value} characters.";
            return new ValidationResult(false, message);
        }

        if (MaxLength.HasValue && length > MaxLength.Value)
        {
            var message = ErrorMessage ?? $"Value must be at most {MaxLength.Value} characters.";
            return new ValidationResult(false, message);
        }

        return ValidationResult.ValidResult;
    }
}

/// <summary>
/// A validation rule that uses a custom function for validation.
/// </summary>
public sealed class CustomValidationRule : ValidationRule
{
    private readonly Func<object?, CultureInfo, ValidationResult>? _validateFunc;

    /// <summary>
    /// Creates a new CustomValidationRule with the specified validation function.
    /// </summary>
    public CustomValidationRule(Func<object?, CultureInfo, ValidationResult> validateFunc)
    {
        _validateFunc = validateFunc;
    }

    /// <summary>
    /// Default constructor for XAML support.
    /// </summary>
    public CustomValidationRule()
    {
    }

    /// <inheritdoc />
    public override ValidationResult Validate(object? value, CultureInfo cultureInfo)
    {
        return _validateFunc?.Invoke(value, cultureInfo) ?? ValidationResult.ValidResult;
    }
}

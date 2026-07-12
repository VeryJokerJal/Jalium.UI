using System.Globalization;
using Jalium.UI.Controls;
using Xunit;

namespace Jalium.UI.Tests;

public sealed class ValidationErrorWpfParityTests
{
    [Fact]
    public void TwoArgumentConstructorExposesRuleAndBinding()
    {
        var rule = new TestRule();
        var binding = new object();

        var error = new ValidationError(rule, binding);

        Assert.Same(rule, error.RuleInError);
        Assert.Same(binding, error.BindingInError);
        Assert.Same(binding, error.BindingSource);
        Assert.Null(error.ErrorContent);
        Assert.Null(error.Exception);
    }

    [Fact]
    public void MutableWpfPropertiesCanBeUpdated()
    {
        var error = new ValidationError(new TestRule(), new object(), "old", null);
        var replacementRule = new TestRule();
        var exception = new InvalidOperationException("failure");

        error.RuleInError = replacementRule;
        error.ErrorContent = "new";
        error.Exception = exception;

        Assert.Same(replacementRule, error.RuleInError);
        Assert.Equal("new", error.ErrorContent);
        Assert.Same(exception, error.Exception);
    }

    private sealed class TestRule : ValidationRule
    {
        public override ValidationResult Validate(object? value, CultureInfo cultureInfo) =>
            ValidationResult.ValidResult;
    }
}

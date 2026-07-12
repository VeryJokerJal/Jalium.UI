using Jalium.UI.Controls;
using Jalium.UI.Data;
using Xunit;

namespace Jalium.UI.Tests;

public sealed class ValidationAttachedPropertiesWpfParityTests
{
    private static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        "ValidationAttachedValue",
        typeof(string),
        typeof(ValidationAttachedPropertiesWpfParityTests),
        new PropertyMetadata(string.Empty));

    [Fact]
    public void AdornerSiteAndReverseLinkStaySynchronized()
    {
        var target = new DependencyObject();
        var firstSite = new DependencyObject();
        var secondSite = new DependencyObject();

        Validation.SetValidationAdornerSite(target, firstSite);
        Assert.Same(firstSite, Validation.GetValidationAdornerSite(target));
        Assert.Same(target, Validation.GetValidationAdornerSiteFor(firstSite));

        Validation.SetValidationAdornerSite(target, secondSite);
        Assert.Null(Validation.GetValidationAdornerSiteFor(firstSite));
        Assert.Same(target, Validation.GetValidationAdornerSiteFor(secondSite));

        Validation.SetValidationAdornerSiteFor(secondSite, null);
        Assert.Null(Validation.GetValidationAdornerSite(target));
    }

    [Fact]
    public void BindingExpressionOverloadsOperateOnTheBindingTarget()
    {
        var target = new DependencyObject();
        BindingExpressionBase expression = target.SetBinding(ValueProperty, new Binding());
        var error = new ValidationError(errorContent: "invalid");

        Validation.MarkInvalid(expression, error);

        Assert.True(Validation.GetHasError(target));
        Assert.Contains(error, Validation.GetErrors(target)!);

        Validation.ClearInvalid(expression);

        Assert.False(Validation.GetHasError(target));
        Assert.Empty(Validation.GetErrors(target)!);
    }
}

using System.Globalization;
using Jalium.UI.Controls;
using Jalium.UI.Data;

namespace Jalium.UI.Tests;

public sealed class ValidationRuleParityTests
{
    [Fact]
    public void DefaultConstructor_UsesRawProposedValue()
    {
        var rule = new RecordingRule();

        Assert.Equal(ValidationStep.RawProposedValue, rule.ValidationStep);
        Assert.False(rule.ValidatesOnTargetUpdated);
    }

    [Fact]
    public void ParameterizedConstructor_StoresConfiguration()
    {
        var rule = new RecordingRule(ValidationStep.UpdatedValue, true);

        Assert.Equal(ValidationStep.UpdatedValue, rule.ValidationStep);
        Assert.True(rule.ValidatesOnTargetUpdated);
    }

    [Fact]
    public void BindingExpressionOverload_ForwardsRawValueBeforeSourceUpdateSteps()
    {
        var rule = new RecordingRule(ValidationStep.ConvertedProposedValue, false);
        var value = new object();

        ValidationResult result = rule.Validate(
            value,
            CultureInfo.InvariantCulture,
            (BindingExpressionBase)null!);

        Assert.Same(rule.Result, result);
        Assert.Same(value, rule.LastValue);
    }

    [Theory]
    [InlineData(ValidationStep.UpdatedValue)]
    [InlineData(ValidationStep.CommittedValue)]
    public void BindingExpressionOverload_UsesOwnerForSourceUpdateSteps(ValidationStep step)
    {
        var rule = new RecordingRule(step, false);

        rule.Validate(new object(), CultureInfo.InvariantCulture, (BindingExpressionBase)null!);

        Assert.Null(rule.LastValue);
    }

    [Fact]
    public void BindingGroupOverload_ValidatesTheOwner()
    {
        var rule = new RecordingRule();
        var group = new BindingGroup();

        ValidationResult result = rule.Validate(new object(), CultureInfo.InvariantCulture, group);

        Assert.Same(rule.Result, result);
        Assert.Same(group, rule.LastValue);
    }

    private sealed class RecordingRule : ValidationRule
    {
        public RecordingRule()
        {
        }

        public RecordingRule(ValidationStep validationStep, bool validatesOnTargetUpdated)
            : base(validationStep, validatesOnTargetUpdated)
        {
        }

        public object? LastValue { get; private set; }

        public ValidationResult Result { get; } = new(true, null);

        public override ValidationResult Validate(object? value, CultureInfo cultureInfo)
        {
            LastValue = value;
            return Result;
        }
    }
}

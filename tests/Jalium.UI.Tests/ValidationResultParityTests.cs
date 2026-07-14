using Jalium.UI.Controls;
using Xunit;

namespace Jalium.UI.Tests;

public sealed class ValidationResultParityTests
{
    [Fact]
    public void EqualityUsesValidityAndErrorContentIdentity()
    {
        var error = new object();
        var first = new ValidationResult(false, error);
        var equal = new ValidationResult(false, error);
        var distinctError = new ValidationResult(false, new object());

        Assert.True(first == equal);
        Assert.False(first != equal);
        Assert.Equal(first, equal);
        Assert.Equal(first.GetHashCode(), equal.GetHashCode());
        Assert.NotEqual(first, distinctError);
        Assert.False(first == distinctError);
    }

    [Fact]
    public void NullAndValidResultSemanticsMatchWpf()
    {
        ValidationResult? none = null;

        Assert.True(none == null);
        Assert.False(ValidationResult.ValidResult == null);
        Assert.True(ValidationResult.ValidResult.IsValid);
        Assert.Null(ValidationResult.ValidResult.ErrorContent);
        Assert.Equal(
            ValidationResult.ValidResult,
            new ValidationResult(true, null));
    }
}

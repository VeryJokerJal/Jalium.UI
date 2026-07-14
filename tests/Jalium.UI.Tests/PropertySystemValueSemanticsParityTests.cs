using Xunit;

namespace Jalium.UI.Tests;

public sealed class PropertySystemValueSemanticsParityTests
{
    private static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        "PropertySystemValueSemantics",
        typeof(object),
        typeof(PropertySystemValueSemanticsParityTests));

    [Fact]
    public void ValueSourceEqualityMatchesWpfAndIgnoresIsCurrent()
    {
        var current = new ValueSource(BaseValueSource.Local, true, false, true, isCurrent: true);
        var ordinary = new ValueSource(BaseValueSource.Local, true, false, true, isCurrent: false);
        var other = new ValueSource(BaseValueSource.Style, true, false, true, isCurrent: true);

        Assert.Equal(current, ordinary);
        Assert.True(current == ordinary);
        Assert.False(current != ordinary);
        Assert.Equal(current.GetHashCode(), ordinary.GetHashCode());
        Assert.NotEqual(current, other);
    }

    [Fact]
    public void DependencyPropertyChangedArgsUsePropertyAndValueIdentity()
    {
        var oldValue = new object();
        var newValue = new object();
        var first = new DependencyPropertyChangedEventArgs(ValueProperty, oldValue, newValue);
        var equal = new DependencyPropertyChangedEventArgs(ValueProperty, oldValue, newValue);
        var distinct = new DependencyPropertyChangedEventArgs(ValueProperty, oldValue, new object());

        Assert.True(first.Equals(equal));
        Assert.True(first == equal);
        Assert.False(first != equal);
        Assert.False(first.Equals(distinct));
        Assert.Throws<InvalidCastException>(() => first.Equals("not args"));
    }
}

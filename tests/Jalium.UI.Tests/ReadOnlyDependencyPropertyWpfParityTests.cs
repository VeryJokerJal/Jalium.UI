namespace Jalium.UI.Tests;

public sealed class ReadOnlyDependencyPropertyWpfParityTests
{
    private static readonly DependencyPropertyKey ValuePropertyKey =
        DependencyProperty.RegisterReadOnly(
            "Value",
            typeof(int),
            typeof(ReadOnlyOwner),
            new PropertyMetadata(1));

    private static readonly DependencyProperty ValueProperty = ValuePropertyKey.DependencyProperty;

    [Fact]
    public void PublicDependencyPropertyWritePaths_RejectReadOnlyProperties()
    {
        var owner = new ReadOnlyOwner();

        Assert.Throws<InvalidOperationException>(() => owner.SetValue(ValueProperty, 2));
        Assert.Throws<InvalidOperationException>(() => owner.SetCurrentValue(ValueProperty, 2));
        Assert.Throws<InvalidOperationException>(() => owner.ClearValue(ValueProperty));
        Assert.Equal(1, owner.Value);
    }

    [Fact]
    public void RegistrationKey_AuthorizesSetAndClear()
    {
        var owner = new ReadOnlyOwner();

        owner.SetAuthorizedValue(7);
        Assert.Equal(7, owner.Value);
        Assert.Equal(7, owner.ReadLocalValue(ValueProperty));

        owner.ClearAuthorizedValue();
        Assert.Equal(1, owner.Value);
        Assert.Same(DependencyProperty.UnsetValue, owner.ReadLocalValue(ValueProperty));
    }

    private sealed class ReadOnlyOwner : DependencyObject
    {
        public int Value => (int)GetValue(ValueProperty)!;

        public void SetAuthorizedValue(int value) => SetValue(ValuePropertyKey, value);

        public void ClearAuthorizedValue() => ClearValue(ValuePropertyKey);
    }
}

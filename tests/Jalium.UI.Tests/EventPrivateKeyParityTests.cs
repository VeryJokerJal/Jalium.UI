namespace Jalium.UI.Tests;

public sealed class EventPrivateKeyParityTests
{
    [Fact]
    public void InstancesUseReferenceIdentity()
    {
        var first = new EventPrivateKey();
        var second = new EventPrivateKey();

        Assert.Equal(first, first);
        Assert.NotEqual(first, second);
        Assert.False(first.Equals(second));
    }
}

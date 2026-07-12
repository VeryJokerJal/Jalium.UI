using System.Reflection;
using Jalium.UI.Data;

namespace Jalium.UI.Tests;

[Collection(nameof(WpfParityFoundationBehaviorCollection))]
public sealed class DataWeakEventsXmlMappingWpfParityTests
{
    [Fact]
    public void DataChangedEventManagerDeliversAndRemovesWeakListeners()
    {
        var provider = new ProbeDataSourceProvider();
        var listener = new ProbeWeakListener();

        DataChangedEventManager.AddListener(provider, listener);
        provider.Refresh();

        Assert.Equal(1, listener.Count);
        Assert.Equal(typeof(DataChangedEventManager), listener.ManagerType);
        Assert.Same(provider, listener.Sender);

        DataChangedEventManager.RemoveListener(provider, listener);
        provider.Refresh();
        Assert.Equal(1, listener.Count);

        Assert.Throws<ArgumentNullException>(
            () => DataChangedEventManager.AddListener(null!, listener));
        Assert.Throws<ArgumentNullException>(
            () => DataChangedEventManager.AddListener(provider, null!));

        Dispatcher.GetForCurrentThread().ProcessQueue();
    }

    [Fact]
    public void XmlNamespaceMappingsUseValueEqualityAndProtectedEnumeration()
    {
        var uri = new Uri("urn:jalium:test");
        var first = new XmlNamespaceMapping("j", uri);
        var equal = new XmlNamespaceMapping("j", new Uri("urn:jalium:test"));
        var different = new XmlNamespaceMapping("x", uri);

        Assert.True(first == equal);
        Assert.False(first != equal);
        Assert.True(first != different);
        Assert.True((XmlNamespaceMapping?)null == null);
        Assert.False(first == null);

        var collection = new XmlNamespaceMappingCollection { first, different };
        MethodInfo method = typeof(XmlNamespaceMappingCollection).GetMethod(
            "ProtectedGetEnumerator",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!;
        Assert.True(method.IsFamily);

        using var enumerator = (IEnumerator<XmlNamespaceMapping>)method.Invoke(collection, null)!;
        Assert.True(enumerator.MoveNext());
        Assert.Same(first, enumerator.Current);
        Assert.True(enumerator.MoveNext());
        Assert.Same(different, enumerator.Current);
        Assert.False(enumerator.MoveNext());
    }

    private sealed class ProbeDataSourceProvider : DataSourceProvider
    {
        protected override void BeginQuery() => OnQueryFinished(new object());
    }

    private sealed class ProbeWeakListener : IWeakEventListener
    {
        public int Count { get; private set; }

        public Type? ManagerType { get; private set; }

        public object? Sender { get; private set; }

        public bool ReceiveWeakEvent(Type managerType, object sender, EventArgs e)
        {
            Count++;
            ManagerType = managerType;
            Sender = sender;
            return true;
        }
    }
}

using System.Collections;
using System.ComponentModel;
using Jalium.UI.Data;

namespace Jalium.UI.Tests;

public sealed class ComponentModelFinalGapTests
{
    [Fact]
    public void CurrentEventManagersDeliverWeakAndDelegateHandlers()
    {
        var view = new CollectionView(new ArrayList { "one", "two" });
        var canonical = (System.ComponentModel.ICollectionView)view;
        var listener = new RecordingWeakListener();
        int changed = 0;
        int changing = 0;
        EventHandler<EventArgs> changedHandler = (_, _) => changed++;
        EventHandler<System.ComponentModel.CurrentChangingEventArgs> changingHandler = (_, _) => changing++;

        CurrentChangedEventManager.AddListener(canonical, listener);
        CurrentChangedEventManager.AddHandler(canonical, changedHandler);
        CurrentChangingEventManager.AddListener(canonical, listener);
        CurrentChangingEventManager.AddHandler(canonical, changingHandler);
        try
        {
            Assert.True(view.MoveCurrentToLast());
            Assert.Equal(2, listener.Deliveries);
            Assert.Equal(1, changed);
            Assert.Equal(1, changing);
        }
        finally
        {
            CurrentChangedEventManager.RemoveListener(canonical, listener);
            CurrentChangedEventManager.RemoveHandler(canonical, changedHandler);
            CurrentChangingEventManager.RemoveListener(canonical, listener);
            CurrentChangingEventManager.RemoveHandler(canonical, changingHandler);
        }
    }

    [Fact]
    public void ErrorsChangedManagerAndPropertyFilterAreFunctional()
    {
        var source = new ErrorSource();
        int deliveries = 0;
        EventHandler<DataErrorsChangedEventArgs> handler = (_, args) =>
        {
            Assert.Equal(nameof(ErrorSource.Value), args.PropertyName);
            deliveries++;
        };

        ErrorsChangedEventManager.AddHandler(source, handler);
        source.Raise();
        ErrorsChangedEventManager.RemoveHandler(source, handler);
        source.Raise();
        Assert.Equal(1, deliveries);

        var setValues = new PropertyFilterAttribute(PropertyFilterOptions.SetValues);
        var all = PropertyFilterAttribute.Default;
        Assert.True(setValues.Match(all));
        Assert.False(all.Match(setValues));
        Assert.Equal(15, (int)PropertyFilterOptions.All);
    }

    private sealed class RecordingWeakListener : IWeakEventListener
    {
        public int Deliveries { get; private set; }

        public bool ReceiveWeakEvent(Type managerType, object sender, EventArgs e)
        {
            Deliveries++;
            return true;
        }
    }

    private sealed class ErrorSource : INotifyDataErrorInfo
    {
        public string? Value { get; set; }
        public bool HasErrors => false;
        public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;
        public IEnumerable GetErrors(string? propertyName) => Array.Empty<object>();
        public void Raise() => ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(nameof(Value)));
    }
}

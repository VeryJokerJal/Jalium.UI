using System.Collections;
using Jalium.UI.Data;

namespace Jalium.UI.Tests;

public sealed class BindingOperationsWpfParityTests
{
    [Fact]
    public void BindingQueriesReturnTheExactParentBindingKinds()
    {
        var target = new BindingTarget();
        var binding = new Binding(nameof(BindingTarget.Source));

        BindingExpressionBase expression = BindingOperations.SetBinding(
            target,
            BindingTarget.ValueProperty,
            binding);

        Assert.IsType<BindingExpression>(expression);
        Assert.Same(binding, BindingOperations.GetBinding(target, BindingTarget.ValueProperty));
        Assert.Same(binding, BindingOperations.GetBindingBase(target, BindingTarget.ValueProperty));
        Assert.Same(expression, BindingOperations.GetBindingExpression(target, BindingTarget.ValueProperty));
        Assert.Same(expression, BindingOperations.GetBindingExpressionBase(target, BindingTarget.ValueProperty));
        Assert.True(BindingOperations.IsDataBound(target, BindingTarget.ValueProperty));

        BindingOperations.ClearBinding(target, BindingTarget.ValueProperty);
        Assert.False(BindingOperations.IsDataBound(target, BindingTarget.ValueProperty));
    }

    [Fact]
    public void CollectionSynchronizationUsesTheRegisteredContextAndCallback()
    {
        IEnumerable collection = new List<int> { 1, 2 };
        object context = new();
        bool callbackRan = false;
        bool accessRan = false;

        BindingOperations.EnableCollectionSynchronization(
            collection,
            context,
            (actualCollection, actualContext, accessMethod, writeAccess) =>
            {
                Assert.Same(collection, actualCollection);
                Assert.Same(context, actualContext);
                Assert.True(writeAccess);
                callbackRan = true;
                accessMethod();
            });

        BindingOperations.AccessCollection(collection, () => accessRan = true, writeAccess: true);

        Assert.True(callbackRan);
        Assert.True(accessRan);
        BindingOperations.DisableCollectionSynchronization(collection);
    }

    [Fact]
    public void RegistrationEventsAndDisconnectedSentinelAreStable()
    {
        bool collectionRaised = false;
        bool viewRaised = false;
        EventHandler<CollectionRegisteringEventArgs> collectionHandler = (_, _) => collectionRaised = true;
        EventHandler<CollectionViewRegisteringEventArgs> viewHandler = (_, _) => viewRaised = true;
        BindingOperations.CollectionRegistering += collectionHandler;
        BindingOperations.CollectionViewRegistering += viewHandler;
        try
        {
            _ = new CollectionView(new[] { 1, 2, 3 });
            Assert.True(collectionRaised);
            Assert.True(viewRaised);
            Assert.Same(BindingOperations.DisconnectedSource, BindingOperations.DisconnectedSource);
        }
        finally
        {
            BindingOperations.CollectionRegistering -= collectionHandler;
            BindingOperations.CollectionViewRegistering -= viewHandler;
        }
    }

    private sealed class BindingTarget : DependencyObject
    {
        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(string), typeof(BindingTarget));

        public string Source { get; set; } = "source";

        public string? Value
        {
            get => (string?)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }
    }
}

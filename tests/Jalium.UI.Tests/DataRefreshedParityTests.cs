using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using System.Xml;
using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Markup;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

[Collection(nameof(ParityFoundationBehaviorCollection))]
public sealed class DataRefreshedParityTests
{
    [Fact]
    public void BindingEnumsAndBaseSerializationMatchWpfContracts()
    {
        Assert.Equal(0, (int)BindingMode.TwoWay);
        Assert.Equal(1, (int)BindingMode.OneWay);
        Assert.Equal(2, (int)BindingMode.OneTime);
        Assert.Equal(3, (int)BindingMode.OneWayToSource);
        Assert.Equal(4, (int)BindingMode.Default);

        Assert.Equal(4, (int)BindingStatus.AsyncRequestPending);
        Assert.Equal(5, (int)BindingStatus.PathError);
        Assert.Equal(6, (int)BindingStatus.UpdateTargetError);
        Assert.Equal(7, (int)BindingStatus.UpdateSourceError);

        var binding = new Binding { BindingGroupName = "row" };
        Assert.False(binding.ShouldSerializeFallbackValue());
        Assert.False(binding.ShouldSerializeTargetNullValue());
        binding.FallbackValue = null;
        binding.TargetNullValue = "null";
        Assert.True(binding.ShouldSerializeFallbackValue());
        Assert.True(binding.ShouldSerializeTargetNullValue());
    }

    [Fact]
    public void RelativeSourceSupportsMarkupInitializationAndAllConstructors()
    {
        Assert.True(typeof(MarkupExtension).IsAssignableFrom(typeof(RelativeSource)));
        Assert.True(typeof(ISupportInitialize).IsAssignableFrom(typeof(RelativeSource)));
        Assert.NotNull(typeof(RelativeSource).GetConstructor(Type.EmptyTypes));
        Assert.NotNull(typeof(RelativeSource).GetConstructor(
            [typeof(RelativeSourceMode), typeof(Type), typeof(int)]));
        Assert.True(typeof(RelativeSource).GetProperty(nameof(RelativeSource.Mode))!.CanWrite);

        var source = new RelativeSource(
            RelativeSourceMode.FindAncestor,
            typeof(FrameworkElement),
            2);
        Assert.True(source.ShouldSerializeAncestorType());
        Assert.True(source.ShouldSerializeAncestorLevel());
        Assert.Same(source, source.ProvideValue(null!));
        Assert.Same(RelativeSource.Self, new RelativeSource(RelativeSourceMode.Self).ProvideValue(null!));

        var initialized = new RelativeSource();
        ISupportInitialize initializer = initialized;
        initializer.BeginInit();
        initialized.Mode = RelativeSourceMode.FindAncestor;
        initialized.AncestorType = typeof(FrameworkElement);
        initializer.EndInit();
    }

    [Fact]
    public void BindingExpressionsExposeParentGroupDirtyAndValidationContracts()
    {
        foreach (string propertyName in new[]
                 {
                     nameof(BindingExpressionBase.BindingGroup),
                     nameof(BindingExpressionBase.HasError),
                     nameof(BindingExpressionBase.IsDirty),
                     nameof(BindingExpressionBase.ParentBindingBase),
                     nameof(BindingExpressionBase.ValidationError),
                     nameof(BindingExpressionBase.ValidationErrors),
                 })
        {
            Assert.NotNull(typeof(BindingExpressionBase).GetProperty(propertyName));
        }

        Assert.NotNull(typeof(BindingExpressionBase).GetMethod(
            nameof(BindingExpressionBase.ValidateWithoutUpdate),
            Type.EmptyTypes));

        var multiBinding = new MultiBinding
        {
            NotifyOnValidationError = true,
            ValidatesOnDataErrors = true,
            ValidatesOnExceptions = true,
            ValidatesOnNotifyDataErrors = false,
            UpdateSourceExceptionFilter = static (_, exception) => exception.Message,
        };
        ((IAddChild)multiBinding).AddChild(new Binding("Name"));
        multiBinding.ValidationRules.Add(new RequiredValidationRule());
        Assert.True(multiBinding.ShouldSerializeBindings());
        Assert.True(multiBinding.ShouldSerializeValidationRules());
        Assert.True(typeof(IAddChild).IsAssignableFrom(typeof(MultiBinding)));
        Assert.True(typeof(IAddChild).IsAssignableFrom(typeof(PriorityBinding)));

        Assert.True(typeof(MultiBindingExpression)
            .GetProperty(nameof(MultiBindingExpression.HasError))!
            .GetMethod!
            .GetBaseDefinition()
            .DeclaringType == typeof(BindingExpressionBase));
    }

    [Fact]
    public void BindingGroupOwnsTargetAndReportsCanonicalCollectionsAndErrors()
    {
        var group = new BindingGroup { Name = "row" };
        var owner = new FrameworkElement { BindingGroup = group };
        Assert.Same(owner, group.Owner);
        Assert.Equal(typeof(Collection<BindingExpressionBase>),
            typeof(BindingGroup).GetProperty(nameof(BindingGroup.BindingExpressions))!.PropertyType);
        Assert.Equal(typeof(IList),
            typeof(BindingGroup).GetProperty(nameof(BindingGroup.Items))!.PropertyType);

        var item = new DataItem { Name = "source" };
        group.Items.Add(item);
        Assert.Equal("source", group.GetValue(item, nameof(DataItem.Name)));

        group.ValidationRules.Add(new AlwaysInvalidRule());
        Assert.False(group.ValidateWithoutUpdate());
        Assert.True(group.HasValidationError);
        Assert.Single(group.ValidationErrors);
    }

    [Fact]
    public void DataSourceProviderInitializationDispatcherAndCompletionAreWpfShaped()
    {
        const BindingFlags protectedInstance =
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        Assert.NotNull(typeof(DataSourceProvider).GetProperty("Dispatcher", protectedInstance));
        Assert.True(typeof(DataSourceProvider).GetMethod("BeginInit", protectedInstance)!.IsVirtual);
        Assert.True(typeof(DataSourceProvider).GetMethod("EndInit", protectedInstance)!.IsVirtual);
        Assert.True(typeof(DataSourceProvider).GetMethod("BeginQuery", protectedInstance)!.IsVirtual);
        Assert.NotNull(typeof(DataSourceProvider).GetMethod(
            "OnQueryFinished",
            protectedInstance,
            null,
            [typeof(object), typeof(Exception), typeof(DispatcherOperationCallback), typeof(object)],
            null));

        var provider = new ProbeProvider();
        ISupportInitialize initializer = provider;
        initializer.BeginInit();
        provider.Refresh();
        Assert.Equal(0, provider.QueryCount);
        initializer.EndInit();
        Assert.Equal(1, provider.QueryCount);
        provider.InitialLoad();
        Assert.Equal(1, provider.QueryCount);

        object callbackArgument = new();
        object? receivedArgument = null;
        provider.Complete("done", null, argument => receivedArgument = argument, callbackArgument);
        Assert.Equal("done", provider.Data);
        Assert.Same(callbackArgument, receivedArgument);
    }

    [Fact]
    public void XmlProviderUsesXmlDocumentNamespacesSerializerAndUriContext()
    {
        var document = new XmlDocument();
        document.LoadXml("<root xmlns='urn:test'><value>42</value></root>");
        var namespaces = new XmlNamespaceManager(document.NameTable);
        namespaces.AddNamespace("t", "urn:test");

        var provider = new XmlDataProvider
        {
            IsAsynchronous = false,
            XmlNamespaceManager = namespaces,
            XPath = "/t:root/t:value",
            Document = document,
        };

        Assert.Same(document, provider.Document);
        Assert.Single(Assert.IsAssignableFrom<XmlNodeList>(provider.Data).Cast<XmlNode>());
        Assert.NotNull(provider.XmlSerializer);
        Assert.True(provider.ShouldSerializeXPath());
        Assert.True(provider.ShouldSerializeXmlSerializer());
        Assert.True(typeof(IUriContext).IsAssignableFrom(typeof(XmlDataProvider)));
        Assert.Equal(typeof(XmlDocument),
            typeof(XmlDataProvider).GetProperty(nameof(XmlDataProvider.Document))!.PropertyType);
    }

    [Fact]
    public void CompositeCollectionAndContainerExposeWeakObservableViewContracts()
    {
        var source = new ObservableCollection<string> { "one" };
        var container = new CollectionContainer { Collection = source };
        Assert.NotNull(CollectionContainer.CollectionProperty);
        Assert.True(container.ShouldSerializeCollection());
        Assert.True(typeof(DependencyObject).IsAssignableFrom(typeof(CollectionContainer)));
        Assert.True(typeof(IWeakEventListener).IsAssignableFrom(typeof(CollectionContainer)));

        var composite = new CompositeCollection(4);
        composite.Add(container);
        Assert.Single(composite);
        Assert.Same(container, composite[0]);
        Assert.True(typeof(System.ComponentModel.ICollectionViewFactory)
            .IsAssignableFrom(typeof(CompositeCollection)));

        var view = ((System.ComponentModel.ICollectionViewFactory)composite).CreateView();
        Assert.Equal(["one"], view.Cast<string>());
        source.Add("two");
        Assert.Equal(["one", "two"], view.Cast<string>());
    }

    [Fact]
    public void ComponentModelCollectionViewMetadataUsesCanonicalTypeIdentities()
    {
        Type itemPropertiesType = typeof(ReadOnlyCollection<ItemPropertyInfo>);
        Assert.Equal(itemPropertiesType,
            typeof(ListCollectionView).GetProperty(nameof(ListCollectionView.ItemProperties))!.PropertyType);
        Assert.Equal(itemPropertiesType,
            typeof(BindingListCollectionView).GetProperty(nameof(BindingListCollectionView.ItemProperties))!.PropertyType);
        Assert.Equal(typeof(NewItemPlaceholderPosition),
            typeof(BindingListCollectionView).GetProperty(
                nameof(BindingListCollectionView.NewItemPlaceholderPosition))!.PropertyType);
        Assert.True(typeof(System.ComponentModel.ICollectionView)
            .IsAssignableFrom(typeof(CollectionView)));
        Assert.NotNull(typeof(CollectionViewSource).GetMethod(
            nameof(CollectionViewSource.IsDefaultView),
            [typeof(System.ComponentModel.ICollectionView)]));
        Assert.NotNull(typeof(DataChangedEventManager).GetMethod(
            nameof(DataChangedEventManager.AddHandler),
            [typeof(DataSourceProvider), typeof(EventHandler<EventArgs>)]));
    }

    [Fact]
    public void XmlNamespaceMappingsImplementInitializationAndNamespaceManagerCollection()
    {
        Assert.True(typeof(ISupportInitialize).IsAssignableFrom(typeof(XmlNamespaceMapping)));
        Assert.True(typeof(XmlNamespaceManager).IsAssignableFrom(typeof(XmlNamespaceMappingCollection)));
        Assert.True(typeof(ICollection<XmlNamespaceMapping>)
            .IsAssignableFrom(typeof(XmlNamespaceMappingCollection)));
        Assert.True(typeof(IAddChild).IsAssignableFrom(typeof(XmlNamespaceMappingCollection)));

        var mapping = new XmlNamespaceMapping("p", new Uri("urn:parity"));
        var mappings = new XmlNamespaceMappingCollection { mapping };
        Assert.Equal("urn:parity", mappings.LookupNamespace("p"));
        Assert.True(mappings.Remove(mapping));
        Assert.Null(mappings.LookupNamespace("p"));

        MethodInfo addChild = typeof(XmlNamespaceMappingCollection).GetMethod(
            "AddChild",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)!;
        Assert.True(addChild.IsFamily);
        Assert.True(addChild.IsVirtual);
    }

    private sealed class ProbeProvider : DataSourceProvider
    {
        public int QueryCount { get; private set; }

        protected override void BeginQuery()
        {
            QueryCount++;
            OnQueryFinished(QueryCount);
        }

        public void Complete(
            object? data,
            Exception? error,
            DispatcherOperationCallback callback,
            object? argument) =>
            OnQueryFinished(data, error, callback, argument);
    }

    private sealed class AlwaysInvalidRule : ValidationRule
    {
        public override ValidationResult Validate(object? value, CultureInfo cultureInfo) =>
            new(false, "invalid");
    }

    private sealed class DataItem
    {
        public string Name { get; set; } = string.Empty;
    }
}

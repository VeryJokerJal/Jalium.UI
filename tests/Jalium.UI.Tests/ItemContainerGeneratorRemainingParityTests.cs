using System.Collections.ObjectModel;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Tests;

public sealed class ItemContainerGeneratorRemainingParityTests
{
    [Fact]
    public void SurfaceMatchesWpfContracts()
    {
        var interfaceGenerateNext = typeof(IItemContainerGenerator).GetMethod(
            nameof(IItemContainerGenerator.GenerateNext),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        Assert.NotNull(interfaceGenerateNext);
        Assert.Equal(typeof(DependencyObject), interfaceGenerateNext!.ReturnType);

        var generatorForPanel = typeof(IItemContainerGenerator).GetMethod(
            nameof(IItemContainerGenerator.GetItemContainerGeneratorForPanel),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            null,
            new[] { typeof(Panel) },
            null);
        Assert.NotNull(generatorForPanel);
        Assert.Equal(typeof(ItemContainerGenerator), generatorForPanel!.ReturnType);

        var items = typeof(ItemContainerGenerator).GetProperty(
            nameof(ItemContainerGenerator.Items),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        Assert.NotNull(items);
        Assert.Equal(typeof(ReadOnlyCollection<object>), items!.PropertyType);
        Assert.True(items.GetMethod!.IsPublic);
        Assert.Null(items.SetMethod);

        var generateBatches = typeof(ItemContainerGenerator).GetMethod(
            nameof(ItemContainerGenerator.GenerateBatches),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            null,
            Type.EmptyTypes,
            null);
        Assert.NotNull(generateBatches);
        Assert.Equal(typeof(IDisposable), generateBatches!.ReturnType);

        var indexFromContainer = typeof(ItemContainerGenerator).GetMethod(
            nameof(ItemContainerGenerator.IndexFromContainer),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly,
            null,
            new[] { typeof(DependencyObject), typeof(bool) },
            null);
        Assert.NotNull(indexFromContainer);
        Assert.Equal(typeof(int), indexFromContainer!.ReturnType);
    }

    [Fact]
    public void ParameterlessGenerateNext_GeneratesUnrealizedItemsAndStopsAtRealizedOnes()
    {
        var control = new ListBox();
        control.Items.Add("zero");
        control.Items.Add("one");
        var generator = control.ItemContainerGenerator;
        var contract = (IItemContainerGenerator)generator;

        Assert.Throws<InvalidOperationException>(() => contract.GenerateNext());

        using (generator.StartAt(
                   generator.GeneratorPositionFromIndex(0),
                   GeneratorDirection.Forward,
                   allowStartAtRealizedItem: true))
        {
            Assert.NotNull(generator.GenerateNext(out var newlyRealized));
            Assert.True(newlyRealized);
        }

        using (contract.StartAt(
                   contract.GeneratorPositionFromIndex(0),
                   GeneratorDirection.Forward,
                   allowStartAtRealizedItem: true))
        {
            Assert.Null(contract.GenerateNext());
            Assert.Null(generator.GenerateNext(out _));
            Assert.Null(generator.ContainerFromIndex(1));
        }

        using (contract.StartAt(
                   contract.GeneratorPositionFromIndex(1),
                   GeneratorDirection.Forward,
                   allowStartAtRealizedItem: true))
        {
            Assert.NotNull(contract.GenerateNext());
            Assert.NotNull(generator.ContainerFromIndex(1));
        }
    }

    [Fact]
    public void GetItemContainerGeneratorForPanel_ValidatesHostAndResolvesPresenterOwner()
    {
        var owner = new ListBox();
        var requester = new ListBox();
        var requesterGenerator = requester.ItemContainerGenerator;
        var contract = (IItemContainerGenerator)requesterGenerator;

        var notAnItemsHost = new StackPanel();
        var error = Assert.Throws<ArgumentException>(() =>
            contract.GetItemContainerGeneratorForPanel(notAnItemsHost));
        Assert.Equal("panel", error.ParamName);

        var directlyTemplatedHost = new StackPanel { IsItemsHost = true };
        directlyTemplatedHost.SetTemplatedParent(requester);
        Assert.Same(
            requesterGenerator,
            contract.GetItemContainerGeneratorForPanel(directlyTemplatedHost));

        var presenter = new ItemsPresenter { Owner = owner };
        var presenterHost = new StackPanel { IsItemsHost = true };
        presenterHost.SetTemplatedParent(presenter);
        Assert.Same(
            owner.ItemContainerGenerator,
            contract.GetItemContainerGeneratorForPanel(presenterHost));

        var standaloneHost = new StackPanel { IsItemsHost = true };
        Assert.Null(contract.GetItemContainerGeneratorForPanel(standaloneHost));
    }

    [Fact]
    public void Items_IsCachedReadOnlyAndTracksDirectCollectionChanges()
    {
        var control = new ListBox();
        var generator = control.ItemContainerGenerator;
        var items = generator.Items;

        Assert.Same(items, generator.Items);
        Assert.Empty(items);

        control.Items.Add("alpha");
        control.Items.Add("beta");

        Assert.Equal(new object[] { "alpha", "beta" }, items);
        Assert.Throws<NotSupportedException>(() => ((IList<object>)items).Add("blocked"));

        control.Items.RemoveAt(0);

        Assert.Equal(new object[] { "beta" }, items);
    }

    [Fact]
    public void Items_TracksItemsSourceWithoutReplacingTheReadOnlyView()
    {
        var source = new ObservableCollection<object> { "first" };
        var control = new ListBox { ItemsSource = source };
        var generator = control.ItemContainerGenerator;
        var items = generator.Items;

        source.Add("second");

        Assert.Same(items, generator.Items);
        Assert.Equal(new object[] { "first", "second" }, items);

        source.RemoveAt(0);

        Assert.Equal(new object[] { "second" }, items);
    }

    [Fact]
    public void GenerateBatches_OwnsStatusAcrossGenerationSessions()
    {
        var control = new ListBox();
        control.Items.Add("item");
        var generator = control.ItemContainerGenerator;
        var statuses = new List<GeneratorStatus>();
        generator.StatusChanged += (_, _) => statuses.Add(generator.Status);

        var batch = generator.GenerateBatches();

        Assert.Equal(GeneratorStatus.GeneratingContainers, generator.Status);
        Assert.Equal(new[] { GeneratorStatus.GeneratingContainers }, statuses);
        Assert.Throws<InvalidOperationException>(() => generator.GenerateBatches());

        var position = generator.GeneratorPositionFromIndex(0);
        var session = generator.StartAt(position, GeneratorDirection.Forward, allowStartAtRealizedItem: true);
        Assert.NotNull(generator.GenerateNext(out _));
        session.Dispose();

        Assert.Equal(GeneratorStatus.GeneratingContainers, generator.Status);
        Assert.Equal(new[] { GeneratorStatus.GeneratingContainers }, statuses);

        batch.Dispose();

        Assert.Equal(GeneratorStatus.ContainersGenerated, generator.Status);
        Assert.Equal(
            new[] { GeneratorStatus.GeneratingContainers, GeneratorStatus.ContainersGenerated },
            statuses);

        batch.Dispose();
        Assert.Equal(2, statuses.Count);
    }

    [Fact]
    public void IndexFromContainer_ValidatesInputAndHonorsFlatLocalIndices()
    {
        var control = new ListBox();
        control.Items.Add("zero");
        control.Items.Add("one");
        var generator = control.ItemContainerGenerator;

        var error = Assert.Throws<ArgumentNullException>(() =>
            generator.IndexFromContainer(null!, returnLocalIndex: false));
        Assert.Equal("container", error.ParamName);

        var unknown = new ListBoxItem();
        Assert.Equal(-1, generator.IndexFromContainer(unknown));
        Assert.Equal(-1, generator.IndexFromContainer(unknown, returnLocalIndex: true));

        var position = generator.GeneratorPositionFromIndex(0);
        using var session = generator.StartAt(
            position,
            GeneratorDirection.Forward,
            allowStartAtRealizedItem: true);
        var first = generator.GenerateNext(out _);
        var second = generator.GenerateNext(out _);

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(0, generator.IndexFromContainer(first!));
        Assert.Equal(0, generator.IndexFromContainer(first!, returnLocalIndex: true));
        Assert.Equal(1, generator.IndexFromContainer(second!, returnLocalIndex: false));
        Assert.Equal(1, generator.IndexFromContainer(second!, returnLocalIndex: true));
    }
}

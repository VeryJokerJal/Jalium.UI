using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Data;

namespace Jalium.UI.Tests;

public class DataGridEventApiWpfParityTests
{
    [Fact]
    public void DelegateSignatures_MatchWpfContracts()
    {
        AssertDelegate<DataGridSortingEventHandler>(
            typeof(void), typeof(object), typeof(DataGridSortingEventArgs));
        AssertDelegate<InitializingNewItemEventHandler>(
            typeof(void), typeof(object), typeof(InitializingNewItemEventArgs));
        AssertDelegate<SelectedCellsChangedEventHandler>(
            typeof(void), typeof(object), typeof(SelectedCellsChangedEventArgs));
        AssertDelegate<GroupStyleSelector>(
            typeof(GroupStyle), typeof(CollectionViewGroup), typeof(int));
    }

    [Fact]
    public void DataGridEvents_ExposeWpfHandlerTypes()
    {
        AssertEventHandlerType<DataGridSortingEventHandler>(nameof(DataGrid.Sorting));
        AssertEventHandlerType<EventHandler<DataGridBeginningEditEventArgs>>(nameof(DataGrid.BeginningEdit));
        AssertEventHandlerType<EventHandler<DataGridCellEditEndingEventArgs>>(nameof(DataGrid.CellEditEnding));
        AssertEventHandlerType<EventHandler<DataGridPreparingCellForEditEventArgs>>(
            nameof(DataGrid.PreparingCellForEdit));
        AssertEventHandlerType<InitializingNewItemEventHandler>(nameof(DataGrid.InitializingNewItem));
        AssertEventHandlerType<SelectedCellsChangedEventHandler>(nameof(DataGrid.SelectedCellsChanged));
        AssertWpfHook("OnSorting", typeof(DataGridSortingEventArgs), isProtectedInternal: false);
        AssertWpfHook("OnBeginningEdit", typeof(DataGridBeginningEditEventArgs), isProtectedInternal: false);
        AssertWpfHook("OnCellEditEnding", typeof(DataGridCellEditEndingEventArgs), isProtectedInternal: false);
        AssertWpfHook(
            "OnPreparingCellForEdit",
            typeof(DataGridPreparingCellForEditEventArgs),
            isProtectedInternal: true);
        AssertWpfHook("OnInitializingNewItem", typeof(InitializingNewItemEventArgs), isProtectedInternal: false);
        AssertWpfHook("OnSelectedCellsChanged", typeof(SelectedCellsChangedEventArgs), isProtectedInternal: false);
    }

    [Fact]
    public void EditingEventArgs_ExposeWpfConstructorsAndReadOnlyState()
    {
        var column = new DataGridTextColumn();
        var row = new DataGridRow();
        var editingEventArgs = new RoutedEventArgs();
        var editingElement = new TextBox();

        Assert.NotNull(typeof(DataGridBeginningEditEventArgs).GetConstructor(
            [typeof(DataGridColumn), typeof(DataGridRow), typeof(RoutedEventArgs)]));
        Assert.NotNull(typeof(DataGridCellEditEndingEventArgs).GetConstructor(
            [typeof(DataGridColumn), typeof(DataGridRow), typeof(FrameworkElement), typeof(DataGridEditAction)]));
        Assert.NotNull(typeof(DataGridPreparingCellForEditEventArgs).GetConstructor(
            [typeof(DataGridColumn), typeof(DataGridRow), typeof(RoutedEventArgs), typeof(FrameworkElement)]));
        Assert.NotNull(typeof(DataGridSortingEventArgs).GetConstructor([typeof(DataGridColumn)]));

        var beginning = new DataGridBeginningEditEventArgs(column, row, editingEventArgs);
        Assert.Same(column, beginning.Column);
        Assert.Same(row, beginning.Row);
        Assert.Same(editingEventArgs, beginning.EditingEventArgs);
        AssertReadOnlyProperty<DataGridBeginningEditEventArgs>(nameof(beginning.Column), typeof(DataGridColumn));
        AssertReadOnlyProperty<DataGridBeginningEditEventArgs>(nameof(beginning.Row), typeof(DataGridRow));
        AssertReadOnlyProperty<DataGridBeginningEditEventArgs>(
            nameof(beginning.EditingEventArgs), typeof(RoutedEventArgs));

        var ending = new DataGridCellEditEndingEventArgs(
            column, row, editingElement, DataGridEditAction.Commit);
        Assert.Same(column, ending.Column);
        Assert.Same(row, ending.Row);
        Assert.Same(editingElement, ending.EditingElement);
        Assert.Equal(DataGridEditAction.Commit, ending.EditAction);
        AssertReadOnlyProperty<DataGridCellEditEndingEventArgs>(
            nameof(ending.EditingElement), typeof(FrameworkElement));

        var preparing = new DataGridPreparingCellForEditEventArgs(
            column, row, editingEventArgs, editingElement);
        Assert.Same(column, preparing.Column);
        Assert.Same(row, preparing.Row);
        Assert.Same(editingEventArgs, preparing.EditingEventArgs);
        Assert.Same(editingElement, preparing.EditingElement);
        AssertReadOnlyProperty<DataGridPreparingCellForEditEventArgs>(
            nameof(preparing.EditingEventArgs), typeof(RoutedEventArgs));
        AssertReadOnlyProperty<DataGridPreparingCellForEditEventArgs>(
            nameof(preparing.EditingElement), typeof(FrameworkElement));

        var sorting = new DataGridSortingEventArgs(column) { Handled = true };
        Assert.Same(column, sorting.Column);
        Assert.True(sorting.Handled);
        Assert.Equal(typeof(DataGridColumnEventArgs), typeof(DataGridSortingEventArgs).BaseType);
        Assert.Equal(typeof(EventArgs), typeof(DataGridPreparingCellForEditEventArgs).BaseType);
        AssertReadOnlyProperty<DataGridSortingEventArgs>(nameof(sorting.Column), typeof(DataGridColumn));
    }

    [Fact]
    public void WpfConstructedArgs_RaiseThroughExistingDataGridEventPaths()
    {
        var grid = new TestDataGrid();
        var column = new DataGridTextColumn();
        var row = new DataGridRow();
        var editingEventArgs = new RoutedEventArgs();
        var editingElement = new TextBox();

        DataGridSortingEventArgs? observedSorting = null;
        DataGridBeginningEditEventArgs? observedBeginning = null;
        DataGridCellEditEndingEventArgs? observedEnding = null;
        DataGridPreparingCellForEditEventArgs? observedPreparing = null;
        grid.Sorting += (_, e) => observedSorting = e;
        grid.BeginningEdit += (_, e) => observedBeginning = e;
        grid.CellEditEnding += (_, e) => observedEnding = e;
        grid.PreparingCellForEdit += (_, e) => observedPreparing = e;

        var sorting = new DataGridSortingEventArgs(column);
        var beginning = new DataGridBeginningEditEventArgs(column, row, editingEventArgs);
        var ending = new DataGridCellEditEndingEventArgs(
            column, row, editingElement, DataGridEditAction.Cancel);
        var preparing = new DataGridPreparingCellForEditEventArgs(
            column, row, editingEventArgs, editingElement);

        grid.RaiseSorting(sorting);
        grid.RaiseBeginningEdit(beginning);
        grid.RaiseCellEditEnding(ending);
        grid.RaisePreparingCellForEdit(preparing);

        Assert.Same(sorting, observedSorting);
        Assert.Same(beginning, observedBeginning);
        Assert.Same(ending, observedEnding);
        Assert.Same(preparing, observedPreparing);
    }

    [Fact]
    public void NewItemAndSelectedCellEvents_AreRaisedByTheirWpfHooks()
    {
        var grid = new TestDataGrid();
        var item = new object();
        var initializingArgs = new InitializingNewItemEventArgs(item);
        var selectedArgs = new SelectedCellsChangedEventArgs(
            new List<DataGridCellInfo>(), new List<DataGridCellInfo>());
        InitializingNewItemEventArgs? observedInitializing = null;
        SelectedCellsChangedEventArgs? observedSelected = null;
        grid.InitializingNewItem += (_, e) => observedInitializing = e;
        grid.SelectedCellsChanged += (_, e) => observedSelected = e;

        grid.RaiseInitializingNewItem(initializingArgs);
        grid.RaiseSelectedCellsChanged(selectedArgs);

        Assert.Same(initializingArgs, observedInitializing);
        Assert.Same(selectedArgs, observedSelected);
        Assert.Same(item, observedInitializing!.NewItem);
    }

    [Fact]
    public void GroupStyleSelector_UsesTheMappedCollectionViewGroupType()
    {
        var group = new TestCollectionViewGroup("group");
        var expected = new GroupStyle();
        GroupStyleSelector selector = (actualGroup, level) =>
        {
            Assert.Same(group, actualGroup);
            Assert.Equal(2, level);
            return expected;
        };

        Assert.Same(expected, selector(group, 2));
    }

    private static void AssertDelegate<TDelegate>(Type returnType, params Type[] parameterTypes)
        where TDelegate : Delegate
    {
        MethodInfo invoke = typeof(TDelegate).GetMethod(nameof(Action.Invoke))!;
        Assert.Equal(returnType, invoke.ReturnType);
        Assert.Equal(parameterTypes, invoke.GetParameters().Select(static parameter => parameter.ParameterType));
    }

    private static void AssertEventHandlerType<THandler>(string eventName)
        where THandler : Delegate
    {
        EventInfo eventInfo = typeof(DataGrid).GetEvent(eventName)!;
        Assert.Equal(typeof(THandler), eventInfo.EventHandlerType);
    }

    private static void AssertReadOnlyProperty<TDeclaringType>(string propertyName, Type propertyType)
    {
        PropertyInfo property = typeof(TDeclaringType).GetProperty(propertyName)!;
        Assert.Equal(propertyType, property.PropertyType);
        Assert.NotNull(property.GetMethod);
        Assert.Null(property.SetMethod);
    }

    private static void AssertWpfHook(string methodName, Type eventArgsType, bool isProtectedInternal)
    {
        MethodInfo method = typeof(DataGrid).GetMethod(
            methodName,
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [eventArgsType],
            modifiers: null)!;
        Assert.Equal(typeof(void), method.ReturnType);
        Assert.True(method.IsVirtual);
        Assert.Equal(isProtectedInternal, method.IsFamilyOrAssembly);
        Assert.Equal(!isProtectedInternal, method.IsFamily);
    }

    private sealed class TestDataGrid : DataGrid
    {
        public void RaiseSorting(DataGridSortingEventArgs e) => OnSorting(e);
        public void RaiseBeginningEdit(DataGridBeginningEditEventArgs e) => OnBeginningEdit(e);
        public void RaiseCellEditEnding(DataGridCellEditEndingEventArgs e) => OnCellEditEnding(e);
        public void RaisePreparingCellForEdit(DataGridPreparingCellForEditEventArgs e) => OnPreparingCellForEdit(e);
        public void RaiseInitializingNewItem(InitializingNewItemEventArgs e) => OnInitializingNewItem(e);
        public void RaiseSelectedCellsChanged(SelectedCellsChangedEventArgs e) => OnSelectedCellsChanged(e);
    }

    private sealed class TestCollectionViewGroup(object name) : CollectionViewGroup(name)
    {
        public override bool IsBottomLevel => true;
    }
}

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Reflection;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Input;
using Jalium.UI.Media;
using ControlsDataTemplateSelector = Jalium.UI.Controls.DataTemplateSelector;
using ControlsItemContainerTemplate = Jalium.UI.Controls.ItemContainerTemplate;
using ControlsItemsPanelTemplate = Jalium.UI.Controls.ItemsPanelTemplate;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class DataGridRowCellParityTests
{
    [Fact]
    public void CanonicalTemplateTypes_UseTheWpfControlsNamespace()
    {
        Assert.Equal("Jalium.UI.Controls.DataTemplateSelector", typeof(ControlsDataTemplateSelector).FullName);
        Assert.Equal("Jalium.UI.Controls.ItemContainerTemplate", typeof(ControlsItemContainerTemplate).FullName);
        Assert.Equal("Jalium.UI.Controls.ItemsPanelTemplate", typeof(ControlsItemsPanelTemplate).FullName);
        Assembly assembly = typeof(DataGridRow).Assembly;
        Assert.Null(assembly.GetType("Jalium.UI.DataTemplateSelector", throwOnError: false));
        Assert.Null(assembly.GetType("Jalium.UI.ItemContainerTemplate", throwOnError: false));
        Assert.Null(assembly.GetType("Jalium.UI.ItemsPanelTemplate", throwOnError: false));

        Assert.Equal(typeof(ControlsDataTemplateSelector),
            typeof(DataGridRow).GetProperty(nameof(DataGridRow.DetailsTemplateSelector))!.PropertyType);
        Assert.Equal(typeof(ControlsItemsPanelTemplate),
            typeof(DataGridRow).GetProperty(nameof(DataGridRow.ItemsPanel))!.PropertyType);
    }

    [Fact]
    public void RowSurface_DeclaresTheWpfPropertiesFieldsEventsAndHooks()
    {
        AssertProperty<DataGridRow, int>(nameof(DataGridRow.AlternationIndex), publicSetter: false);
        AssertProperty<DataGridRow, object>(nameof(DataGridRow.Header), publicSetter: true);
        AssertProperty<DataGridRow, Style>(nameof(DataGridRow.HeaderStyle), publicSetter: true);
        AssertProperty<DataGridRow, DataTemplate>(nameof(DataGridRow.HeaderTemplate), publicSetter: true);
        AssertProperty<DataGridRow, ControlsDataTemplateSelector>(nameof(DataGridRow.HeaderTemplateSelector), publicSetter: true);
        AssertProperty<DataGridRow, bool>(nameof(DataGridRow.IsEditing), publicSetter: false);
        AssertProperty<DataGridRow, bool>(nameof(DataGridRow.IsNewItem), publicSetter: false);
        AssertProperty<DataGridRow, object>(nameof(DataGridRow.Item), publicSetter: true);
        AssertProperty<DataGridRow, ControlsItemsPanelTemplate>(nameof(DataGridRow.ItemsPanel), publicSetter: true);
        AssertProperty<DataGridRow, ControlTemplate>(nameof(DataGridRow.ValidationErrorTemplate), publicSetter: true);

        foreach (var name in new[]
                 {
                     nameof(DataGridRow.AlternationIndex), nameof(DataGridRow.Header),
                     nameof(DataGridRow.HeaderStyle), nameof(DataGridRow.HeaderTemplate),
                     nameof(DataGridRow.HeaderTemplateSelector), nameof(DataGridRow.IsEditing),
                     nameof(DataGridRow.IsNewItem), nameof(DataGridRow.Item),
                     nameof(DataGridRow.ItemsPanel), nameof(DataGridRow.ValidationErrorTemplate)
                 })
        {
            Assert.Equal(typeof(DependencyProperty),
                typeof(DataGridRow).GetField(name + "Property", BindingFlags.Public | BindingFlags.Static)!.FieldType);
        }

        Assert.True(DataGridRow.AlternationIndexProperty.ReadOnly);
        Assert.True(DataGridRow.IsEditingProperty.ReadOnly);
        Assert.True(DataGridRow.IsNewItemProperty.ReadOnly);
        Assert.Same(ItemsControl.AlternationIndexProperty, DataGridRow.AlternationIndexProperty);
        Assert.Same(ItemsControl.ItemsPanelProperty, DataGridRow.ItemsPanelProperty);

        Assert.Equal(typeof(RoutedEventHandler), typeof(DataGridRow).GetEvent(nameof(DataGridRow.Selected))!.EventHandlerType);
        Assert.Equal(typeof(RoutedEventHandler), typeof(DataGridRow).GetEvent(nameof(DataGridRow.Unselected))!.EventHandlerType);
        Assert.Equal(typeof(RoutedEvent), typeof(DataGridRow).GetField(nameof(DataGridRow.SelectedEvent))!.FieldType);
        Assert.Equal(typeof(RoutedEvent), typeof(DataGridRow).GetField(nameof(DataGridRow.UnselectedEvent))!.FieldType);

        AssertMethod(typeof(DataGridRow), nameof(DataGridRow.GetIndex), typeof(int), isPublic: true, isStatic: false);
        AssertMethod(typeof(DataGridRow), nameof(DataGridRow.GetRowContainingElement), typeof(DataGridRow),
            isPublic: true, isStatic: true, typeof(FrameworkElement));
        AssertProtectedVirtual(typeof(DataGridRow), "OnColumnsChanged",
            typeof(ObservableCollection<DataGridColumn>), typeof(NotifyCollectionChangedEventArgs));
        AssertProtectedVirtual(typeof(DataGridRow), "OnHeaderChanged", typeof(object), typeof(object));
        AssertProtectedVirtual(typeof(DataGridRow), "OnItemChanged", typeof(object), typeof(object));
        AssertProtectedVirtual(typeof(DataGridRow), "OnSelected", typeof(RoutedEventArgs));
        AssertProtectedVirtual(typeof(DataGridRow), "OnUnselected", typeof(RoutedEventArgs));
    }

    [Fact]
    public void RowHooksAndSelectionEvents_RunForRealPropertyChanges()
    {
        var row = new ProbeRow();
        var selected = 0;
        var unselected = 0;
        row.Selected += (_, _) => selected++;
        row.Unselected += (_, _) => unselected++;

        var firstItem = new object();
        var secondItem = new object();
        row.Item = firstItem;
        row.Item = secondItem;
        row.Header = "header";
        row.IsSelected = true;
        row.IsSelected = false;

        Assert.Equal(2, row.ItemChanges);
        Assert.Equal(1, row.HeaderChanges);
        Assert.Equal(1, row.SelectedCalls);
        Assert.Equal(1, row.UnselectedCalls);
        Assert.Equal(1, selected);
        Assert.Equal(1, unselected);
    }

    [Fact]
    public void CellSurface_DeclaresReadOnlyColumnPolicyLayoutInputAndSelectionContracts()
    {
        AssertProperty<DataGridCell, DataGridColumn>(nameof(DataGridCell.Column), publicSetter: false);
        AssertProperty<DataGridCell, bool>(nameof(DataGridCell.IsReadOnly), publicSetter: false);
        Assert.True(DataGridCell.ColumnProperty.ReadOnly);
        Assert.True(DataGridCell.IsReadOnlyProperty.ReadOnly);

        foreach (var name in new[] { nameof(DataGridCell.Column), nameof(DataGridCell.IsReadOnly) })
        {
            Assert.Equal(typeof(DependencyProperty),
                typeof(DataGridCell).GetField(name + "Property", BindingFlags.Public | BindingFlags.Static)!.FieldType);
        }

        Assert.Equal(typeof(RoutedEventHandler), typeof(DataGridCell).GetEvent(nameof(DataGridCell.Selected))!.EventHandlerType);
        Assert.Equal(typeof(RoutedEventHandler), typeof(DataGridCell).GetEvent(nameof(DataGridCell.Unselected))!.EventHandlerType);
        Assert.Equal(typeof(RoutedEvent), typeof(DataGridCell).GetField(nameof(DataGridCell.SelectedEvent))!.FieldType);
        Assert.Equal(typeof(RoutedEvent), typeof(DataGridCell).GetField(nameof(DataGridCell.UnselectedEvent))!.FieldType);

        AssertOverride(typeof(DataGridCell), "MeasureOverride", typeof(Size), typeof(Size));
        AssertOverride(typeof(DataGridCell), "ArrangeOverride", typeof(Size), typeof(Size));
        AssertOverride(typeof(DataGridCell), "OnKeyDown", typeof(void), typeof(KeyEventArgs));
        AssertOverride(typeof(DataGridCell), "OnPreviewKeyDown", typeof(void), typeof(KeyEventArgs));
        AssertOverride(typeof(DataGridCell), "OnRender", typeof(void), typeof(DrawingContext));
        AssertOverride(typeof(DataGridCell), "OnTextInput", typeof(void), typeof(TextCompositionEventArgs));
        AssertProtectedVirtual(typeof(DataGridCell), "OnColumnChanged", typeof(DataGridColumn), typeof(DataGridColumn));
        AssertProtectedVirtual(typeof(DataGridCell), "OnIsEditingChanged", typeof(bool));
        AssertProtectedVirtual(typeof(DataGridCell), "OnSelected", typeof(RoutedEventArgs));
        AssertProtectedVirtual(typeof(DataGridCell), "OnUnselected", typeof(RoutedEventArgs));
    }

    [Fact]
    public void CellHooksReadOnlyPolicyAndSelectionEvents_AreFunctional()
    {
        var cell = new ProbeCell();
        var column = new DataGridTextColumn();
        var selected = 0;
        var unselected = 0;
        cell.Selected += (_, _) => selected++;
        cell.Unselected += (_, _) => unselected++;

        cell.SetColumn(column);
        Assert.Same(column, cell.Column);
        Assert.False(cell.IsReadOnly);
        Assert.Equal(1, cell.ColumnChanges);

        column.IsReadOnly = true;
        Assert.True(cell.IsReadOnly);

        cell.IsEditing = true;
        cell.IsEditing = false;
        cell.IsSelected = true;
        cell.IsSelected = false;

        Assert.Equal(2, cell.EditingChanges);
        Assert.Equal(1, cell.SelectedCalls);
        Assert.Equal(1, cell.UnselectedCalls);
        Assert.Equal(1, selected);
        Assert.Equal(1, unselected);
    }

    private static void AssertProperty<TDeclaring, TProperty>(string name, bool publicSetter)
    {
        var property = typeof(TDeclaring).GetProperty(name, BindingFlags.Public | BindingFlags.Instance)!;
        Assert.Equal(typeof(TProperty), property.PropertyType);
        Assert.Equal(publicSetter, property.SetMethod?.IsPublic == true);
    }

    private static void AssertMethod(
        Type declaringType,
        string name,
        Type returnType,
        bool isPublic,
        bool isStatic,
        params Type[] parameterTypes)
    {
        var flags = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static;
        var method = declaringType.GetMethod(name, flags, null, parameterTypes, null)!;
        Assert.Equal(returnType, method.ReturnType);
        Assert.Equal(isPublic, method.IsPublic);
        Assert.Equal(isStatic, method.IsStatic);
    }

    private static void AssertProtectedVirtual(Type declaringType, string name, params Type[] parameterTypes)
    {
        var method = declaringType.GetMethod(
            name,
            BindingFlags.DeclaredOnly | BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            parameterTypes,
            null)!;
        Assert.True(method.IsVirtual);
        Assert.True(method.IsFamily || method.IsFamilyOrAssembly);
    }

    private static void AssertOverride(
        Type declaringType,
        string name,
        Type returnType,
        params Type[] parameterTypes)
    {
        var method = declaringType.GetMethod(
            name,
            BindingFlags.DeclaredOnly | BindingFlags.NonPublic | BindingFlags.Instance,
            null,
            parameterTypes,
            null)!;
        Assert.Equal(returnType, method.ReturnType);
        Assert.True(method.IsVirtual);
        Assert.NotEqual(declaringType, method.GetBaseDefinition().DeclaringType);
    }

    private sealed class ProbeRow : DataGridRow
    {
        public int ItemChanges { get; private set; }

        public int HeaderChanges { get; private set; }

        public int SelectedCalls { get; private set; }

        public int UnselectedCalls { get; private set; }

        protected override void OnItemChanged(object? oldItem, object? newItem)
        {
            ItemChanges++;
            base.OnItemChanged(oldItem, newItem);
        }

        protected override void OnHeaderChanged(object? oldHeader, object? newHeader)
        {
            HeaderChanges++;
            base.OnHeaderChanged(oldHeader, newHeader);
        }

        protected override void OnSelected(RoutedEventArgs e)
        {
            SelectedCalls++;
            base.OnSelected(e);
        }

        protected override void OnUnselected(RoutedEventArgs e)
        {
            UnselectedCalls++;
            base.OnUnselected(e);
        }
    }

    private sealed class ProbeCell : DataGridCell
    {
        public int ColumnChanges { get; private set; }

        public int EditingChanges { get; private set; }

        public int SelectedCalls { get; private set; }

        public int UnselectedCalls { get; private set; }

        public void SetColumn(DataGridColumn column) => Column = column;

        protected override void OnColumnChanged(DataGridColumn oldColumn, DataGridColumn newColumn)
        {
            ColumnChanges++;
            base.OnColumnChanged(oldColumn, newColumn);
        }

        protected override void OnIsEditingChanged(bool isEditing)
        {
            EditingChanges++;
            base.OnIsEditingChanged(isEditing);
        }

        protected override void OnSelected(RoutedEventArgs e)
        {
            SelectedCalls++;
            base.OnSelected(e);
        }

        protected override void OnUnselected(RoutedEventArgs e)
        {
            UnselectedCalls++;
            base.OnUnselected(e);
        }
    }
}

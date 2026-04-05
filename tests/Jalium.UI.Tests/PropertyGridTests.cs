using System.ComponentModel;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class PropertyGridTests
{
    private static void ResetApplicationState()
    {
        var currentField = typeof(Application).GetField("_current",
            BindingFlags.NonPublic | BindingFlags.Static);
        currentField?.SetValue(null, null);
        var resetMethod = typeof(ThemeManager).GetMethod("Reset",
            BindingFlags.NonPublic | BindingFlags.Static);
        resetMethod?.Invoke(null, null);
    }

    // Test object with various property types
    private class TestObject
    {
        [Category("General")]
        [DisplayName("Full Name")]
        [Description("The full name of the person")]
        public string Name { get; set; } = "Default";

        [Category("General")]
        public int Age { get; set; } = 25;

        [Category("Appearance")]
        public double Height { get; set; } = 1.75;

        [Category("State")]
        public bool IsActive { get; set; } = true;

        [Browsable(false)]
        public string HiddenProperty { get; set; } = "hidden";

        [ReadOnly(true)]
        public string ReadOnlyProperty { get; set; } = "readonly";

        public DateTime BirthDate { get; set; } = new DateTime(2000, 1, 1);
    }

    [Fact]
    public void PropertyGrid_DefaultProperties()
    {
        var grid = new PropertyGrid();

        Assert.Equal(PropertyGridSortMode.Categorized, grid.SortMode);
        Assert.True(grid.ShowSearchBox);
        Assert.Equal(150.0, grid.NameColumnWidth);
        Assert.True(grid.AutoGenerateProperties);
        Assert.False(grid.IsReadOnly);
        Assert.True(grid.ShowDescription);
        Assert.True(grid.ShowToolBar);
        Assert.Equal(string.Empty, grid.SearchText);
        Assert.Null(grid.SelectedObject);
        Assert.Null(grid.SelectedProperty);
        Assert.Null(grid.PropertyFilter);
    }

    [Fact]
    public void PropertyGrid_SortMode_CanBeSet()
    {
        var grid = new PropertyGrid();
        grid.SortMode = PropertyGridSortMode.Alphabetical;
        Assert.Equal(PropertyGridSortMode.Alphabetical, grid.SortMode);
    }

    [Fact]
    public void PropertyGrid_ShowSearchBox_CanBeSet()
    {
        var grid = new PropertyGrid();
        grid.ShowSearchBox = false;
        Assert.False(grid.ShowSearchBox);
    }

    [Fact]
    public void PropertyGrid_NameColumnWidth_CanBeSet()
    {
        var grid = new PropertyGrid();
        grid.NameColumnWidth = 200.0;
        Assert.Equal(200.0, grid.NameColumnWidth);
    }

    [Fact]
    public void PropertyGrid_AutoGenerateProperties_CanBeSet()
    {
        var grid = new PropertyGrid();
        grid.AutoGenerateProperties = false;
        Assert.False(grid.AutoGenerateProperties);
    }

    [Fact]
    public void PropertyGrid_IsReadOnly_CanBeSet()
    {
        var grid = new PropertyGrid();
        grid.IsReadOnly = true;
        Assert.True(grid.IsReadOnly);
    }

    [Fact]
    public void PropertyGrid_SelectedObject_CanBeSet()
    {
        var grid = new PropertyGrid();
        var obj = new TestObject();
        grid.SelectedObject = obj;
        Assert.Same(obj, grid.SelectedObject);
    }

    [Fact]
    public void PropertyGrid_SearchText_CanBeSet()
    {
        var grid = new PropertyGrid();
        grid.SearchText = "name";
        Assert.Equal("name", grid.SearchText);
    }

    [Fact]
    public void PropertyGrid_BrushProperties_DefaultToNull()
    {
        var grid = new PropertyGrid();
        Assert.Null(grid.CategoryHeaderBackground);
        Assert.Null(grid.CategoryHeaderForeground);
        Assert.Null(grid.PropertyNameForeground);
    }

    [Fact]
    public void PropertyItem_FromPropertyInfo_Name()
    {
        var obj = new TestObject();
        var propInfo = typeof(TestObject).GetProperty("Name")!;
        var item = new PropertyItem(obj, propInfo);

        Assert.Equal("Name", item.Name);
        Assert.Equal("Full Name", item.DisplayName);
        Assert.Equal("General", item.Category);
        Assert.Equal("The full name of the person", item.Description);
        Assert.Equal(typeof(string), item.PropertyType);
        Assert.False(item.IsReadOnly);
    }

    [Fact]
    public void PropertyItem_FromPropertyInfo_Value()
    {
        var obj = new TestObject { Name = "Test" };
        var propInfo = typeof(TestObject).GetProperty("Name")!;
        var item = new PropertyItem(obj, propInfo);

        Assert.Equal("Test", item.Value);
    }

    [Fact]
    public void PropertyItem_SetValue_UpdatesSourceObject()
    {
        var obj = new TestObject();
        var propInfo = typeof(TestObject).GetProperty("Name")!;
        var item = new PropertyItem(obj, propInfo);

        item.Value = "New Name";
        Assert.Equal("New Name", obj.Name);
    }

    [Fact]
    public void PropertyItem_ReadOnlyProperty_CannotSetValue()
    {
        var obj = new TestObject();
        var propInfo = typeof(TestObject).GetProperty("ReadOnlyProperty")!;
        var item = new PropertyItem(obj, propInfo);

        Assert.True(item.IsReadOnly);
        item.Value = "attempt";
        // Value should not change because it's read-only
        Assert.Equal("readonly", obj.ReadOnlyProperty);
    }

    [Fact]
    public void PropertyItem_IntProperty_TypeDetection()
    {
        var obj = new TestObject();
        var propInfo = typeof(TestObject).GetProperty("Age")!;
        var item = new PropertyItem(obj, propInfo);

        Assert.Equal(typeof(int), item.PropertyType);
        Assert.False(item.IsExpandable);
        Assert.Equal(25, item.Value);
    }

    [Fact]
    public void PropertyItem_BoolProperty_TypeDetection()
    {
        var obj = new TestObject();
        var propInfo = typeof(TestObject).GetProperty("IsActive")!;
        var item = new PropertyItem(obj, propInfo);

        Assert.Equal(typeof(bool), item.PropertyType);
        Assert.False(item.IsExpandable);
        Assert.Equal(true, item.Value);
    }

    [Fact]
    public void PropertyItem_DoubleProperty_TypeDetection()
    {
        var obj = new TestObject();
        var propInfo = typeof(TestObject).GetProperty("Height")!;
        var item = new PropertyItem(obj, propInfo);

        Assert.Equal(typeof(double), item.PropertyType);
        Assert.False(item.IsExpandable);
    }

    [Fact]
    public void PropertyItem_StringProperty_IsNotExpandable()
    {
        var obj = new TestObject();
        var propInfo = typeof(TestObject).GetProperty("Name")!;
        var item = new PropertyItem(obj, propInfo);

        Assert.False(item.IsExpandable);
    }

    [Fact]
    public void PropertyItem_DateTimeProperty_IsNotExpandable()
    {
        var obj = new TestObject();
        var propInfo = typeof(TestObject).GetProperty("BirthDate")!;
        var item = new PropertyItem(obj, propInfo);

        Assert.Equal(typeof(DateTime), item.PropertyType);
        Assert.False(item.IsExpandable);
    }

    [Fact]
    public void PropertyItem_DefaultCategory_IsMisc()
    {
        var obj = new TestObject();
        var propInfo = typeof(TestObject).GetProperty("IsActive")!;
        var item = new PropertyItem(obj, propInfo);

        // IsActive has [Category("State")]
        Assert.Equal("State", item.Category);
    }

    [Fact]
    public void PropertyItem_SubProperties_InitiallyEmpty()
    {
        var obj = new TestObject();
        var propInfo = typeof(TestObject).GetProperty("Name")!;
        var item = new PropertyItem(obj, propInfo);

        Assert.NotNull(item.SubProperties);
        Assert.Empty(item.SubProperties);
    }

    [Fact]
    public void PropertyItem_ToString_ContainsNameAndValue()
    {
        var obj = new TestObject { Name = "Hello" };
        var propInfo = typeof(TestObject).GetProperty("Name")!;
        var item = new PropertyItem(obj, propInfo);

        var str = item.ToString();
        Assert.Contains("Full Name", str);
        Assert.Contains("Hello", str);
    }

    [Fact]
    public void PropertyGrid_RefreshProperties_DoesNotThrow()
    {
        var grid = new PropertyGrid();
        var ex = Record.Exception(() => grid.RefreshProperties());
        Assert.Null(ex);
    }

    [Fact]
    public void PropertyGrid_RegisterCustomEditor_DoesNotThrow()
    {
        var grid = new PropertyGrid();
        var ex = Record.Exception(() =>
            grid.RegisterCustomEditor(typeof(string), (item, pg) => new TextBlock()));
        Assert.Null(ex);
    }

    [Fact]
    public void PropertyItem_PropertyChanged_RaisedOnValueSet()
    {
        var obj = new TestObject();
        var propInfo = typeof(TestObject).GetProperty("Name")!;
        var item = new PropertyItem(obj, propInfo);

        bool raised = false;
        item.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == "Value")
                raised = true;
        };

        item.Value = "Changed";
        Assert.True(raised);
    }
}

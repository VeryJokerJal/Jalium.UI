using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Themes;

namespace Jalium.UI.Tests;

[Collection("Application")]
public class JsonTreeViewerTests
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

    [Fact]
    public void JsonTreeViewer_DefaultProperties()
    {
        var viewer = new JsonTreeViewer();

        Assert.False(viewer.IsEditable);
        Assert.Equal(2, viewer.ExpandDepth);
        Assert.Equal(100, viewer.MaxRenderDepth);
        Assert.True(viewer.ShowTypeIndicators);
        Assert.Equal(string.Empty, viewer.SearchText);
        Assert.Null(viewer.JsonText);
        Assert.Null(viewer.RootNode);
        Assert.Null(viewer.SelectedNode);
        Assert.Equal(20.0, viewer.IndentSize);
        Assert.True(viewer.ShowItemCount);
    }

    [Fact]
    public void JsonParser_Parse_SimpleObject()
    {
        var parseMethod = typeof(JsonParser).GetMethod("Parse",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        var root = (JsonTreeNode)parseMethod.Invoke(null, new object[] { "{\"name\": \"test\", \"value\": 42}" })!;

        Assert.Equal(JsonNodeType.Object, root.NodeType);
        Assert.Equal(2, root.Children.Count);

        var nameNode = root.Children[0];
        Assert.Equal("name", nameNode.Key);
        Assert.Equal(JsonNodeType.String, nameNode.NodeType);
        Assert.Equal("test", nameNode.Value);

        var valueNode = root.Children[1];
        Assert.Equal("value", valueNode.Key);
        Assert.Equal(JsonNodeType.Number, valueNode.NodeType);
        Assert.Equal(42L, valueNode.Value);
    }

    [Fact]
    public void JsonParser_Parse_SimpleArray()
    {
        var parseMethod = typeof(JsonParser).GetMethod("Parse",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        var root = (JsonTreeNode)parseMethod.Invoke(null, new object[] { "[1, 2, 3]" })!;

        Assert.Equal(JsonNodeType.Array, root.NodeType);
        Assert.Equal(3, root.Children.Count);

        Assert.Equal(JsonNodeType.Number, root.Children[0].NodeType);
        Assert.Equal(1L, root.Children[0].Value);
        Assert.Equal(2L, root.Children[1].Value);
        Assert.Equal(3L, root.Children[2].Value);
    }

    [Fact]
    public void JsonParser_Parse_NestedObject()
    {
        var parseMethod = typeof(JsonParser).GetMethod("Parse",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        var root = (JsonTreeNode)parseMethod.Invoke(null,
            new object[] { "{\"outer\": {\"inner\": \"deep\"}}" })!;

        Assert.Equal(JsonNodeType.Object, root.NodeType);
        var outerNode = root.Children[0];
        Assert.Equal("outer", outerNode.Key);
        Assert.Equal(JsonNodeType.Object, outerNode.NodeType);
        Assert.Single(outerNode.Children);

        var innerNode = outerNode.Children[0];
        Assert.Equal("inner", innerNode.Key);
        Assert.Equal(JsonNodeType.String, innerNode.NodeType);
        Assert.Equal("deep", innerNode.Value);
    }

    [Fact]
    public void JsonParser_Parse_NullValue()
    {
        var parseMethod = typeof(JsonParser).GetMethod("Parse",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        var root = (JsonTreeNode)parseMethod.Invoke(null,
            new object[] { "{\"key\": null}" })!;

        var node = root.Children[0];
        Assert.Equal(JsonNodeType.Null, node.NodeType);
        Assert.Null(node.Value);
    }

    [Fact]
    public void JsonParser_Parse_BooleanValues()
    {
        var parseMethod = typeof(JsonParser).GetMethod("Parse",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        var root = (JsonTreeNode)parseMethod.Invoke(null,
            new object[] { "{\"t\": true, \"f\": false}" })!;

        var trueNode = root.Children[0];
        Assert.Equal(JsonNodeType.Boolean, trueNode.NodeType);
        Assert.Equal(true, trueNode.Value);

        var falseNode = root.Children[1];
        Assert.Equal(JsonNodeType.Boolean, falseNode.NodeType);
        Assert.Equal(false, falseNode.Value);
    }

    [Fact]
    public void JsonParser_Parse_NumberValues()
    {
        var parseMethod = typeof(JsonParser).GetMethod("Parse",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        var root = (JsonTreeNode)parseMethod.Invoke(null,
            new object[] { "{\"int\": 100, \"float\": 3.14}" })!;

        var intNode = root.Children[0];
        Assert.Equal(JsonNodeType.Number, intNode.NodeType);
        Assert.Equal(100L, intNode.Value);

        var floatNode = root.Children[1];
        Assert.Equal(JsonNodeType.Number, floatNode.NodeType);
        Assert.Equal(3.14, floatNode.Value);
    }

    [Fact]
    public void JsonTreeNode_Path_RootIsDollar()
    {
        var node = new JsonTreeNode();
        Assert.Equal("$", node.Path);
    }

    [Fact]
    public void JsonTreeNode_Path_SimpleProperty()
    {
        var root = new JsonTreeNode { NodeType = JsonNodeType.Object };
        var child = new JsonTreeNode { Key = "name", Parent = root, NodeType = JsonNodeType.String };
        root.Children.Add(child);

        Assert.Equal("$.name", child.Path);
    }

    [Fact]
    public void JsonTreeNode_Path_ArrayIndex()
    {
        var root = new JsonTreeNode { NodeType = JsonNodeType.Array };
        var child0 = new JsonTreeNode { Parent = root, NodeType = JsonNodeType.Number };
        var child1 = new JsonTreeNode { Parent = root, NodeType = JsonNodeType.Number };
        root.Children.Add(child0);
        root.Children.Add(child1);

        Assert.Equal("$[0]", child0.Path);
        Assert.Equal("$[1]", child1.Path);
    }

    [Fact]
    public void JsonTreeNode_Path_NestedPath()
    {
        var root = new JsonTreeNode { NodeType = JsonNodeType.Object };
        var child = new JsonTreeNode { Key = "items", Parent = root, NodeType = JsonNodeType.Array };
        root.Children.Add(child);
        var grandchild = new JsonTreeNode { Parent = child, NodeType = JsonNodeType.Object };
        child.Children.Add(grandchild);
        var leaf = new JsonTreeNode { Key = "id", Parent = grandchild, NodeType = JsonNodeType.Number };
        grandchild.Children.Add(leaf);

        Assert.Equal("$.items[0].id", leaf.Path);
    }

    [Fact]
    public void JsonTreeNode_Path_SpecialCharacterKey_UsesBracketNotation()
    {
        var root = new JsonTreeNode { NodeType = JsonNodeType.Object };
        var child = new JsonTreeNode { Key = "my-key", Parent = root, NodeType = JsonNodeType.String };
        root.Children.Add(child);

        Assert.Equal("$[\"my-key\"]", child.Path);
    }

    [Fact]
    public void JsonTreeNode_DisplayValue_Object()
    {
        var node = new JsonTreeNode { NodeType = JsonNodeType.Object };
        node.Children.Add(new JsonTreeNode());
        node.Children.Add(new JsonTreeNode());

        Assert.Equal("{2 properties}", node.DisplayValue);
    }

    [Fact]
    public void JsonTreeNode_DisplayValue_Array()
    {
        var node = new JsonTreeNode { NodeType = JsonNodeType.Array };
        node.Children.Add(new JsonTreeNode());

        Assert.Equal("[1 items]", node.DisplayValue);
    }

    [Fact]
    public void JsonTreeNode_DisplayValue_String()
    {
        var node = new JsonTreeNode { NodeType = JsonNodeType.String, Value = "hello" };
        Assert.Equal("\"hello\"", node.DisplayValue);
    }

    [Fact]
    public void JsonTreeNode_DisplayValue_Null()
    {
        var node = new JsonTreeNode { NodeType = JsonNodeType.Null };
        Assert.Equal("null", node.DisplayValue);
    }

    [Fact]
    public void JsonTreeNode_DisplayValue_Boolean()
    {
        var nodeTrue = new JsonTreeNode { NodeType = JsonNodeType.Boolean, Value = true };
        Assert.Equal("true", nodeTrue.DisplayValue);

        var nodeFalse = new JsonTreeNode { NodeType = JsonNodeType.Boolean, Value = false };
        Assert.Equal("false", nodeFalse.DisplayValue);
    }

    [Fact]
    public void JsonTreeNode_ChildCount_Object()
    {
        var node = new JsonTreeNode { NodeType = JsonNodeType.Object };
        node.Children.Add(new JsonTreeNode());
        Assert.Equal(1, node.ChildCount);
    }

    [Fact]
    public void JsonTreeNode_ChildCount_Leaf_ReturnsZero()
    {
        var node = new JsonTreeNode { NodeType = JsonNodeType.String, Value = "test" };
        Assert.Equal(0, node.ChildCount);
    }

    [Fact]
    public void JsonTreeNode_IsExpanded_DefaultsFalse()
    {
        var node = new JsonTreeNode();
        Assert.False(node.IsExpanded);
    }

    [Fact]
    public void JsonTreeNode_IsVisible_DefaultsTrue()
    {
        var node = new JsonTreeNode();
        Assert.True(node.IsVisible);
    }

    [Fact]
    public void JsonTreeViewer_IsEditable_CanBeSet()
    {
        var viewer = new JsonTreeViewer();
        viewer.IsEditable = true;
        Assert.True(viewer.IsEditable);
    }

    [Fact]
    public void JsonTreeViewer_ExpandDepth_CanBeSet()
    {
        var viewer = new JsonTreeViewer();
        viewer.ExpandDepth = 5;
        Assert.Equal(5, viewer.ExpandDepth);
    }

    [Fact]
    public void JsonTreeViewer_MaxRenderDepth_CanBeSet()
    {
        var viewer = new JsonTreeViewer();
        viewer.MaxRenderDepth = 50;
        Assert.Equal(50, viewer.MaxRenderDepth);
    }

    [Fact]
    public void JsonTreeViewer_BrushProperties_DefaultToNull()
    {
        var viewer = new JsonTreeViewer();
        Assert.Null(viewer.ObjectBrush);
        Assert.Null(viewer.ArrayBrush);
        Assert.Null(viewer.StringBrush);
        Assert.Null(viewer.NumberBrush);
        Assert.Null(viewer.BooleanBrush);
        Assert.Null(viewer.NullBrush);
        Assert.Null(viewer.KeyBrush);
        Assert.Null(viewer.BracketBrush);
    }

    [Fact]
    public void JsonTreeViewer_ExpandAll_DoesNotThrow_WhenNoRoot()
    {
        var viewer = new JsonTreeViewer();
        var ex = Record.Exception(() => viewer.ExpandAll());
        Assert.Null(ex);
    }

    [Fact]
    public void JsonTreeViewer_CollapseAll_DoesNotThrow_WhenNoRoot()
    {
        var viewer = new JsonTreeViewer();
        var ex = Record.Exception(() => viewer.CollapseAll());
        Assert.Null(ex);
    }

    [Fact]
    public void JsonTreeViewer_SearchText_CanBeSet()
    {
        var viewer = new JsonTreeViewer();
        viewer.SearchText = "test";
        Assert.Equal("test", viewer.SearchText);
    }

    [Fact]
    public void JsonTreeNode_Depth_DefaultsToZero()
    {
        var node = new JsonTreeNode();
        Assert.Equal(0, node.Depth);
    }

    [Fact]
    public void JsonTreeNode_Depth_CanBeSet()
    {
        var node = new JsonTreeNode();
        node.Depth = 3;
        Assert.Equal(3, node.Depth);
    }

    [Fact]
    public void JsonParser_Parse_SetsDepthCorrectly()
    {
        var parseMethod = typeof(JsonParser).GetMethod("Parse",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        var root = (JsonTreeNode)parseMethod.Invoke(null,
            new object[] { "{\"a\": {\"b\": 1}}" })!;

        Assert.Equal(0, root.Depth);
        Assert.Equal(1, root.Children[0].Depth);
        Assert.Equal(2, root.Children[0].Children[0].Depth);
    }

    [Fact]
    public void JsonParser_Parse_SetsParentCorrectly()
    {
        var parseMethod = typeof(JsonParser).GetMethod("Parse",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        var root = (JsonTreeNode)parseMethod.Invoke(null,
            new object[] { "{\"a\": 1}" })!;

        Assert.Null(root.Parent);
        Assert.Equal(root, root.Children[0].Parent);
    }
}

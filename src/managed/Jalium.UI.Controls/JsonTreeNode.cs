using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Jalium.UI.Controls;

/// <summary>
/// Specifies the type of a JSON node.
/// </summary>
public enum JsonNodeType
{
    /// <summary>A JSON object ({...}).</summary>
    Object,
    /// <summary>A JSON array ([...]).</summary>
    Array,
    /// <summary>A JSON string value.</summary>
    String,
    /// <summary>A JSON number value.</summary>
    Number,
    /// <summary>A JSON boolean value.</summary>
    Boolean,
    /// <summary>A JSON null value.</summary>
    Null
}

/// <summary>
/// Represents a node in a JSON tree structure.
/// </summary>
public class JsonTreeNode : INotifyPropertyChanged
{
    private string? _key;
    private object? _value;
    private JsonNodeType _nodeType;
    private bool _isExpanded;
    private bool _isVisible = true;
    private bool _isMatchedBySearch;
    private int _depth;

    /// <summary>
    /// Gets or sets the key (property name) of this node. Null for array elements.
    /// </summary>
    public string? Key
    {
        get => _key;
        set => SetField(ref _key, value);
    }

    /// <summary>
    /// Gets or sets the value of this node. Null for objects and arrays.
    /// </summary>
    public object? Value
    {
        get => _value;
        set => SetField(ref _value, value);
    }

    /// <summary>
    /// Gets or sets the JSON node type.
    /// </summary>
    public JsonNodeType NodeType
    {
        get => _nodeType;
        set => SetField(ref _nodeType, value);
    }

    /// <summary>
    /// Gets the child nodes of this node.
    /// </summary>
    public ObservableCollection<JsonTreeNode> Children { get; } = new();

    /// <summary>
    /// Gets or sets the parent node.
    /// </summary>
    public JsonTreeNode? Parent { get; set; }

    /// <summary>
    /// Gets or sets whether this node is expanded in the tree view.
    /// </summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetField(ref _isExpanded, value);
    }

    /// <summary>
    /// Gets or sets whether this node is visible (not filtered out by search).
    /// </summary>
    public bool IsVisible
    {
        get => _isVisible;
        set => SetField(ref _isVisible, value);
    }

    /// <summary>
    /// Gets or sets whether this node is directly matched by a search query.
    /// </summary>
    public bool IsMatchedBySearch
    {
        get => _isMatchedBySearch;
        set => SetField(ref _isMatchedBySearch, value);
    }

    /// <summary>
    /// Gets or sets the depth of this node in the tree.
    /// </summary>
    public int Depth
    {
        get => _depth;
        set => SetField(ref _depth, value);
    }

    /// <summary>
    /// Gets the JSONPath string for this node (e.g., "$.foo.bar[0].baz").
    /// </summary>
    public string Path => BuildPath();

    /// <summary>
    /// Gets the number of children for object and array nodes.
    /// </summary>
    public int ChildCount => NodeType is JsonNodeType.Object or JsonNodeType.Array ? Children.Count : 0;

    /// <summary>
    /// Gets a display-friendly string for the node's value.
    /// </summary>
    public string DisplayValue
    {
        get
        {
            return NodeType switch
            {
                JsonNodeType.Object => $"{{{Children.Count} properties}}",
                JsonNodeType.Array => $"[{Children.Count} items]",
                JsonNodeType.String => $"\"{Value}\"",
                JsonNodeType.Null => "null",
                JsonNodeType.Boolean => Value?.ToString()?.ToLowerInvariant() ?? "false",
                _ => Value?.ToString() ?? ""
            };
        }
    }

    private string BuildPath()
    {
        if (Parent == null)
            return "$";

        var parentPath = Parent.Path;

        if (Parent.NodeType == JsonNodeType.Array)
        {
            var index = Parent.Children.IndexOf(this);
            return $"{parentPath}[{index}]";
        }

        if (Key != null)
        {
            // Use dot notation for simple keys, bracket notation for keys with special characters
            if (IsSimpleKey(Key))
                return $"{parentPath}.{Key}";
            return $"{parentPath}[\"{EscapeJsonString(Key)}\"]";
        }

        return parentPath;
    }

    private static bool IsSimpleKey(string key)
    {
        if (string.IsNullOrEmpty(key))
            return false;

        if (!char.IsLetter(key[0]) && key[0] != '_')
            return false;

        for (int i = 1; i < key.Length; i++)
        {
            if (!char.IsLetterOrDigit(key[i]) && key[i] != '_')
                return false;
        }

        return true;
    }

    private static string EscapeJsonString(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"");
    }

    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

/// <summary>
/// Parses JSON text into a <see cref="JsonTreeNode"/> tree using System.Text.Json.
/// </summary>
internal static class JsonParser
{
    /// <summary>
    /// Parses a JSON string into a tree of <see cref="JsonTreeNode"/> objects.
    /// </summary>
    /// <param name="json">The JSON string to parse.</param>
    /// <returns>The root <see cref="JsonTreeNode"/>.</returns>
    /// <exception cref="JsonException">The JSON is malformed.</exception>
    public static JsonTreeNode Parse(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });

        return ParseElement(doc.RootElement, null, null, 0);
    }

    private static JsonTreeNode ParseElement(JsonElement element, string? key, JsonTreeNode? parent, int depth)
    {
        var node = new JsonTreeNode
        {
            Key = key,
            Parent = parent,
            Depth = depth,
            NodeType = MapValueKind(element.ValueKind)
        };

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    var child = ParseElement(prop.Value, prop.Name, node, depth + 1);
                    node.Children.Add(child);
                }
                break;

            case JsonValueKind.Array:
                int index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    var child = ParseElement(item, null, node, depth + 1);
                    node.Children.Add(child);
                    index++;
                }
                break;

            case JsonValueKind.String:
                node.Value = element.GetString();
                break;

            case JsonValueKind.Number:
                // Preserve the numeric value as accurately as possible
                if (element.TryGetInt64(out long longVal))
                    node.Value = longVal;
                else if (element.TryGetDouble(out double doubleVal))
                    node.Value = doubleVal;
                else
                    node.Value = element.GetRawText();
                break;

            case JsonValueKind.True:
                node.Value = true;
                break;

            case JsonValueKind.False:
                node.Value = false;
                break;

            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                node.Value = null;
                break;
        }

        return node;
    }

    private static JsonNodeType MapValueKind(JsonValueKind kind)
    {
        return kind switch
        {
            JsonValueKind.Object => JsonNodeType.Object,
            JsonValueKind.Array => JsonNodeType.Array,
            JsonValueKind.String => JsonNodeType.String,
            JsonValueKind.Number => JsonNodeType.Number,
            JsonValueKind.True => JsonNodeType.Boolean,
            JsonValueKind.False => JsonNodeType.Boolean,
            JsonValueKind.Null => JsonNodeType.Null,
            _ => JsonNodeType.Null
        };
    }
}

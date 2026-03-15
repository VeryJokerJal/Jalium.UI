using System.Runtime.CompilerServices;

namespace Jalium.UI.Controls;

/// <summary>
/// Enables a user to search a list of items in an ItemsControl by typing text.
/// </summary>
public sealed class TextSearch : DependencyObject
{
    #region Attached Properties

    /// <summary>
    /// Identifies the TextPath attached property, which names the property on each data item
    /// that is used to do the text search.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty TextPathProperty =
        DependencyProperty.RegisterAttached("TextPath", typeof(string), typeof(TextSearch),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// Identifies the Text attached property, which is the string by which the item is known.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached("Text", typeof(string), typeof(TextSearch),
            new PropertyMetadata(string.Empty));

    #endregion

    #region Static Get/Set Methods

    /// <summary>
    /// Gets the value of the TextPath attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static string GetTextPath(DependencyObject element)
    {
        return (string)(element.GetValue(TextPathProperty) ?? string.Empty);
    }

    /// <summary>
    /// Sets the value of the TextPath attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static void SetTextPath(DependencyObject element, string value)
    {
        element.SetValue(TextPathProperty, value);
    }

    /// <summary>
    /// Gets the value of the Text attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static string GetText(DependencyObject element)
    {
        return (string)(element.GetValue(TextProperty) ?? string.Empty);
    }

    /// <summary>
    /// Sets the value of the Text attached property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public static void SetText(DependencyObject element, string value)
    {
        element.SetValue(TextProperty, value);
    }

    #endregion

    private readonly ItemsControl _itemsControl;
    private string _prefix = string.Empty;
    private DateTime _lastTime;
    private static readonly TimeSpan _timeout = TimeSpan.FromMilliseconds(1000);

    private TextSearch(ItemsControl itemsControl)
    {
        _itemsControl = itemsControl;
    }

    /// <summary>
    /// Gets the TextSearch instance for the specified ItemsControl, creating one if needed.
    /// </summary>
    internal static TextSearch EnsureInstance(ItemsControl itemsControl)
    {
        return _instances.GetValue(itemsControl, static control => new TextSearch(control));
    }

    private static readonly ConditionalWeakTable<ItemsControl, TextSearch> _instances = new();

    /// <summary>
    /// Performs a text search with the given character.
    /// </summary>
    internal bool DoSearch(string nextChar)
    {
        var now = DateTime.UtcNow;
        if (now - _lastTime > _timeout)
        {
            _prefix = string.Empty;
        }
        _lastTime = now;
        _prefix += nextChar;

        return FindAndSelect(_prefix);
    }

    /// <summary>
    /// Deletes the last character from the current search prefix.
    /// </summary>
    internal bool DeleteLastCharacter()
    {
        if (_prefix.Length == 0)
            return false;

        _prefix = _prefix[..^1];
        if (_prefix.Length == 0)
            return false;

        return FindAndSelect(_prefix);
    }

    /// <summary>
    /// Resets the text search state.
    /// </summary>
    internal void Reset()
    {
        _prefix = string.Empty;
    }

    private bool FindAndSelect(string prefix)
    {
        var items = _itemsControl.Items;
        var textPath = GetTextPath(_itemsControl);

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var text = GetPrimaryText(item, textPath);

            if (text != null && text.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase))
            {
                if (_itemsControl is Primitives.Selector selector)
                {
                    selector.SelectedIndex = i;
                }
                return true;
            }
        }

        return false;
    }

    private static string? GetPrimaryText(object? item, string textPath)
    {
        if (item == null)
            return null;

        if (item is string s)
            return s;

        // Check Text attached property on the item
        if (item is DependencyObject dObj)
        {
            var text = GetText(dObj);
            if (!string.IsNullOrEmpty(text))
                return text;
        }

        // Use TextPath to get text from a property
        if (!string.IsNullOrEmpty(textPath))
        {
            var prop = item.GetType().GetProperty(textPath);
            if (prop != null)
                return prop.GetValue(item)?.ToString();
        }

        return item.ToString();
    }

    /// <summary>
    /// Gets the primary text from an item in the specified items control.
    /// </summary>
    internal static string? GetPrimaryTextFromItem(ItemsControl itemsControl, object? item)
    {
        if (item == null)
            return null;

        var textPath = GetTextPath(itemsControl);
        return GetPrimaryText(item, textPath);
    }
}

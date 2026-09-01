namespace Jalium.UI.Styling;

/// <summary>
/// Attached-property surface of the CSS engine:
/// <c>Css.Style</c> — an inline declaration block ("background-color: red; margin: 4px 8px"),
/// <c>Css.Class</c> — space-separated class names for style-sheet selectors,
/// <c>Css.StyleSheet</c> — style-sheet text scoped to the element's subtree (the markup
/// equivalent of HTML's &lt;style&gt;), <c>Css.StyleSheets</c> — the same scope as a collection
/// of pre-parsed sheets.
/// </summary>
public static class Css
{
    public static readonly DependencyProperty StyleProperty =
        DependencyProperty.RegisterAttached(
            "Style", typeof(string), typeof(Css),
            new PropertyMetadata(string.Empty, OnStyleChanged));

    public static readonly DependencyProperty ClassProperty =
        DependencyProperty.RegisterAttached(
            "Class", typeof(string), typeof(Css),
            new PropertyMetadata(string.Empty, OnClassChanged));

    /// <summary>Reserved: template-internal parts never participate in page-level CSS matching (v1).</summary>
    public static readonly DependencyProperty IncludeTemplatePartsProperty =
        DependencyProperty.RegisterAttached(
            "IncludeTemplateParts", typeof(bool), typeof(Css),
            new PropertyMetadata(false));

    public static string GetStyle(DependencyObject element)
        => (string)(element.GetValue(StyleProperty) ?? string.Empty);

    public static void SetStyle(DependencyObject element, string value)
        => element.SetValue(StyleProperty, value);

    public static string GetClass(DependencyObject element)
        => (string)(element.GetValue(ClassProperty) ?? string.Empty);

    public static void SetClass(DependencyObject element, string value)
        => element.SetValue(ClassProperty, value);

    public static bool GetIncludeTemplateParts(DependencyObject element)
        => element.GetValue(IncludeTemplatePartsProperty) is true;

    public static void SetIncludeTemplateParts(DependencyObject element, bool value)
        => element.SetValue(IncludeTemplatePartsProperty, value);

    /// <summary>Style sheets scoped to this element's subtree. Created on demand.</summary>
    public static CssStyleSheetCollection GetStyleSheets(DependencyObject element)
    {
        if (element.GetValue(StyleSheetsProperty) is CssStyleSheetCollection existing)
        {
            return existing;
        }

        var collection = new CssStyleSheetCollection();
        element.SetValue(StyleSheetsProperty, collection);
        return collection;
    }

    public static void SetStyleSheets(DependencyObject element, CssStyleSheetCollection? value)
        => element.SetValue(StyleSheetsProperty, value);

    public static readonly DependencyProperty StyleSheetsProperty =
        DependencyProperty.RegisterAttached(
            "StyleSheets", typeof(CssStyleSheetCollection), typeof(Css),
            new PropertyMetadata(null, OnStyleSheetsChanged));

    /// <summary>
    /// Style-sheet text scoped to this element's subtree, declarable from markup:
    /// <c>Css.StyleSheet="Button { border-radius: 6px } Button:hover { background-color: #2563eb }"</c>.
    /// The text is parsed into a sheet appended to <see cref="StyleSheetsProperty"/>; re-setting
    /// the text replaces that sheet and leaves any other sheet in the collection alone.
    /// </summary>
    public static readonly DependencyProperty StyleSheetProperty =
        DependencyProperty.RegisterAttached(
            "StyleSheet", typeof(string), typeof(Css),
            new PropertyMetadata(string.Empty, OnStyleSheetTextChanged));

    public static string GetStyleSheet(DependencyObject element)
        => (string)(element.GetValue(StyleSheetProperty) ?? string.Empty);

    public static void SetStyleSheet(DependencyObject element, string value)
        => element.SetValue(StyleSheetProperty, value);

    private static void OnStyleSheetTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        var state = CssEngine.EnsureState(element);
        var sheets = GetStyleSheets(element);
        if (state.DeclaredStyleSheet is { } previous)
        {
            sheets.Remove(previous);
            state.DeclaredStyleSheet = null;
        }

        if (e.NewValue is string text && !string.IsNullOrWhiteSpace(text))
        {
            var sheet = CssStyleSheet.Parse(text, "Css.StyleSheet");
            state.DeclaredStyleSheet = sheet;
            sheets.Add(sheet);
        }
    }

    private static void OnStyleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is FrameworkElement element)
        {
            CssEngine.OnInlineStyleChanged(element, e.NewValue as string);
        }
    }

    private static void OnClassChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        var state = CssEngine.EnsureState(element);
        state.Classes = ParseClassList(e.NewValue as string);
        if (CssEngine.IsActive)
        {
            // The element may be an ancestor-position class in a combinator; refresh its subtree.
            CssEvaluationScheduler.InvalidateSubtree(element);
        }
    }

    private static void OnStyleSheetsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
        {
            return;
        }

        var state = CssEngine.EnsureState(element);
        state.ScopedStyleSheets = e.NewValue as CssStyleSheetCollection;
        if (state.ScopedStyleSheets is { } collection)
        {
            CssEngine.MarkActive();
            collection.Changed += () =>
            {
                CssEngine.MarkActive();
                CssEngine.NotifyCascadeChanged();
                CssEvaluationScheduler.InvalidateSubtree(element);
            };
        }

        CssEngine.NotifyCascadeChanged();
        CssEvaluationScheduler.InvalidateSubtree(element);
    }

    internal static string[] ParseClassList(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return CssElementState.EmptyClasses;
        }

        var parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length > 1)
        {
            Array.Sort(parts, StringComparer.Ordinal);
            var unique = 1;
            for (var i = 1; i < parts.Length; i++)
            {
                if (!string.Equals(parts[i], parts[unique - 1], StringComparison.Ordinal))
                {
                    parts[unique++] = parts[i];
                }
            }

            if (unique != parts.Length)
            {
                Array.Resize(ref parts, unique);
            }
        }

        return parts;
    }
}

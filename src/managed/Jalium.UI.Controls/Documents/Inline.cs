using Jalium.UI.Media;

namespace Jalium.UI.Documents;

/// <summary>
/// Abstract base class for inline flow content elements.
/// </summary>
public abstract class Inline : TextElement
{
    /// <summary>
    /// Gets or sets the next sibling inline.
    /// </summary>
    public Inline? NextInline { get; internal set; }

    /// <summary>
    /// Gets or sets the previous sibling inline.
    /// </summary>
    public Inline? PreviousInline { get; internal set; }
}

/// <summary>
/// A collection of inline elements.
/// </summary>
public class InlineCollection : List<Inline>
{
    private readonly TextElement _parent;

    /// <summary>
    /// Initializes a new instance of the <see cref="InlineCollection"/> class.
    /// </summary>
    public InlineCollection(TextElement parent)
    {
        _parent = parent;
    }

    /// <summary>
    /// Adds an inline element to the collection.
    /// </summary>
    public new void Add(Inline item)
    {
        item.Parent = _parent;
        if (Count > 0)
        {
            var last = this[Count - 1];
            last.NextInline = item;
            item.PreviousInline = last;
        }
        base.Add(item);
    }

    /// <summary>
    /// Adds a text string as a Run element.
    /// </summary>
    public void Add(string text)
    {
        Add(new Run(text));
    }

    /// <summary>
    /// Removes an inline element from the collection.
    /// </summary>
    public new bool Remove(Inline item)
    {
        var result = base.Remove(item);
        if (result)
        {
            item.Parent = null;
            if (item.PreviousInline != null)
                item.PreviousInline.NextInline = item.NextInline;
            if (item.NextInline != null)
                item.NextInline.PreviousInline = item.PreviousInline;
            item.NextInline = null;
            item.PreviousInline = null;
        }
        return result;
    }

    /// <summary>
    /// Clears all inline elements from the collection.
    /// </summary>
    public new void Clear()
    {
        foreach (var item in this)
        {
            item.Parent = null;
            item.NextInline = null;
            item.PreviousInline = null;
        }
        base.Clear();
    }
}

/// <summary>
/// An inline element that contains text.
/// </summary>
public class Run : Inline
{
    /// <summary>
    /// Gets or sets the text content.
    /// </summary>
    public string Text { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Run"/> class.
    /// </summary>
    public Run() : this(string.Empty) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="Run"/> class with the specified text.
    /// </summary>
    public Run(string text)
    {
        Text = text;
    }
}

/// <summary>
/// An inline element that displays bold text.
/// </summary>
public class Bold : Span
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Bold"/> class.
    /// </summary>
    public Bold()
    {
        FontWeight = FontWeights.Bold;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Bold"/> class with the specified inline.
    /// </summary>
    public Bold(Inline childInline) : this()
    {
        Inlines.Add(childInline);
    }
}

/// <summary>
/// An inline element that displays italic text.
/// </summary>
public class Italic : Span
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Italic"/> class.
    /// </summary>
    public Italic()
    {
        FontStyle = FontStyles.Italic;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Italic"/> class with the specified inline.
    /// </summary>
    public Italic(Inline childInline) : this()
    {
        Inlines.Add(childInline);
    }
}

/// <summary>
/// An inline element that displays underlined text.
/// </summary>
public class Underline : Span
{
    /// <summary>
    /// Gets or sets whether text is underlined.
    /// </summary>
    public bool IsUnderlined { get; set; } = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="Underline"/> class.
    /// </summary>
    public Underline() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="Underline"/> class with the specified inline.
    /// </summary>
    public Underline(Inline childInline)
    {
        Inlines.Add(childInline);
    }
}

/// <summary>
/// An inline element that groups other inlines.
/// </summary>
public class Span : Inline
{
    /// <summary>
    /// Gets the collection of inline elements.
    /// </summary>
    public InlineCollection Inlines { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Span"/> class.
    /// </summary>
    public Span()
    {
        Inlines = new InlineCollection(this);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Span"/> class with the specified inline.
    /// </summary>
    public Span(Inline childInline) : this()
    {
        Inlines.Add(childInline);
    }
}

/// <summary>
/// An inline element that represents a hyperlink.
/// </summary>
public class Hyperlink : Span
{
    /// <summary>
    /// Identifies the NavigateUri dependency property.
    /// </summary>
    public static readonly DependencyProperty NavigateUriProperty =
        DependencyProperty.Register(nameof(NavigateUri), typeof(Uri), typeof(Hyperlink),
            new PropertyMetadata(null));

    /// <summary>
    /// Gets or sets the URI to navigate to.
    /// </summary>
    public Uri? NavigateUri
    {
        get => (Uri?)GetValue(NavigateUriProperty);
        set => SetValue(NavigateUriProperty, value);
    }

    /// <summary>
    /// Occurs when the hyperlink is clicked.
    /// </summary>
    public event EventHandler? Click;

    /// <summary>
    /// Initializes a new instance of the <see cref="Hyperlink"/> class.
    /// </summary>
    public Hyperlink()
    {
        Foreground = new SolidColorBrush(Color.FromRgb(0, 102, 204));
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Hyperlink"/> class with the specified inline.
    /// </summary>
    public Hyperlink(Inline childInline) : this()
    {
        Inlines.Add(childInline);
    }

    /// <summary>
    /// Raises the Click event.
    /// </summary>
    protected internal void OnClick()
    {
        Click?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// An inline element that represents a line break.
/// </summary>
public class LineBreak : Inline
{
}

/// <summary>
/// An inline element that represents an inline UI element.
/// </summary>
public class InlineUIContainer : Inline
{
    /// <summary>
    /// Gets or sets the child UI element.
    /// </summary>
    public UIElement? Child { get; set; }
}

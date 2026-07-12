using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Windows.Input;
using Jalium.UI.Input;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using Jalium.UI.Navigation;

namespace Jalium.UI.Documents;

/// <summary>
/// Abstract base class for inline flow content elements.
/// </summary>
public abstract class Inline : TextElement
{
    internal InlineCollection? OwnerCollection { get; set; }

    /// <summary>Identifies the baseline-alignment dependency property.</summary>
    public static readonly DependencyProperty BaselineAlignmentProperty =
        DependencyProperty.Register(
            nameof(BaselineAlignment),
            typeof(BaselineAlignment),
            typeof(Inline),
            new PropertyMetadata(BaselineAlignment.Baseline));

    /// <summary>Identifies the inheritable flow-direction dependency property.</summary>
    public static readonly DependencyProperty FlowDirectionProperty =
        FrameworkElement.FlowDirectionProperty.AddOwner(
            typeof(Inline),
            new PropertyMetadata(FlowDirection.LeftToRight, null, null, inherits: true));

    /// <summary>Gets or sets how this inline is aligned with the text baseline.</summary>
    public BaselineAlignment BaselineAlignment
    {
        get => (BaselineAlignment)(GetValue(BaselineAlignmentProperty) ?? BaselineAlignment.Baseline);
        set => SetValue(BaselineAlignmentProperty, value);
    }

    /// <summary>Gets or sets the inline's content flow direction.</summary>
    public FlowDirection FlowDirection
    {
        get => (FlowDirection)(GetValue(FlowDirectionProperty) ?? FlowDirection.LeftToRight);
        set => SetValue(FlowDirectionProperty, value);
    }

    /// <summary>Gets the collection containing this inline, if it is attached.</summary>
    public InlineCollection? SiblingInlines => OwnerCollection;

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
public class InlineCollection : TextElementCollection<Inline>
{
    private readonly TextElement? _parent;
    private readonly Action _contentChanged;

    /// <summary>
    /// Initializes a new instance of the <see cref="InlineCollection"/> class.
    /// </summary>
    public InlineCollection(TextElement parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        _parent = parent;
        _contentChanged = parent.NotifyTextContentChanged;
    }

    internal InlineCollection(Action contentChanged)
    {
        ArgumentNullException.ThrowIfNull(contentChanged);
        _contentChanged = contentChanged;
    }

    internal TextElement? Parent => _parent;

    /// <summary>
    /// Gets the first inline in the collection, or <see langword="null"/> when it is empty.
    /// </summary>
    public Inline? FirstInline => Count == 0 ? null : this[0];

    /// <summary>
    /// Gets the last inline in the collection, or <see langword="null"/> when it is empty.
    /// </summary>
    public Inline? LastInline => Count == 0 ? null : this[Count - 1];

    /// <summary>
    /// Adds an inline element to the collection.
    /// </summary>
    public void Add(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Add(new Run(text));
    }

    /// <summary>
    /// Adds a UI element by wrapping it in an <see cref="InlineUIContainer"/>.
    /// </summary>
    /// <param name="uiElement">The UI element to add.</param>
    public void Add(UIElement uiElement)
    {
        ArgumentNullException.ThrowIfNull(uiElement);
        Add(new InlineUIContainer(uiElement));
    }

    protected override void InsertItem(int index, Inline item)
    {
        PrepareForInsert(item);
        base.InsertItem(index, item);
        Attach(item);
        RebuildSiblingLinks();
        _contentChanged();
    }

    protected override void SetItem(int index, Inline item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var oldItem = this[index];
        if (ReferenceEquals(oldItem, item))
        {
            return;
        }

        PrepareForInsert(item);
        Detach(oldItem);
        base.SetItem(index, item);
        Attach(item);
        RebuildSiblingLinks();
        _contentChanged();
    }

    protected override void RemoveItem(int index)
    {
        var item = this[index];
        Detach(item);
        base.RemoveItem(index);
        RebuildSiblingLinks();
        _contentChanged();
    }

    protected override void ClearItems()
    {
        if (Count == 0)
        {
            return;
        }

        foreach (var item in this)
        {
            Detach(item);
        }

        base.ClearItems();
        _contentChanged();
    }

    internal string GetText()
    {
        if (Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var inline in this)
        {
            AppendText(builder, inline);
        }

        return builder.ToString();
    }

    private static void AppendText(StringBuilder builder, Inline inline)
    {
        switch (inline)
        {
            case Run run:
                builder.Append(run.Text);
                break;
            case LineBreak:
                builder.Append('\n');
                break;
            case Span span:
                foreach (var child in span.Inlines)
                {
                    AppendText(builder, child);
                }
                break;
        }
    }

    private void PrepareForInsert(Inline item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.OwnerCollection != null)
        {
            throw new InvalidOperationException("The Inline already belongs to an InlineCollection.");
        }
    }

    private void Attach(Inline item)
    {
        item.OwnerCollection = this;
        item.Parent = _parent;
        _parent?.AddLogicalChild(item);
        item.TextContentChanged += OnItemTextContentChanged;
    }

    private void Detach(Inline item)
    {
        item.TextContentChanged -= OnItemTextContentChanged;
        _parent?.RemoveLogicalChild(item);
        item.OwnerCollection = null;
        item.Parent = null;
        item.NextInline = null;
        item.PreviousInline = null;
    }

    private void OnItemTextContentChanged(object? sender, EventArgs e)
    {
        _contentChanged();
    }

    private void RebuildSiblingLinks()
    {
        for (var index = 0; index < Count; index++)
        {
            var item = this[index];
            item.PreviousInline = index == 0 ? null : this[index - 1];
            item.NextInline = index + 1 < Count ? this[index + 1] : null;
        }
    }
}

/// <summary>
/// An inline element that contains text.
/// </summary>
public sealed class Run : Inline
{
    /// <summary>
    /// Identifies the Text dependency property.
    /// </summary>
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(Run),
            new PropertyMetadata(string.Empty, OnTextPropertyChanged, CoerceText));

    /// <summary>
    /// Gets or sets the text content.
    /// </summary>
    public string Text
    {
        get => (string?)GetValue(TextProperty) ?? string.Empty;
        set => SetValue(TextProperty, value ?? string.Empty);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Run"/> class.
    /// </summary>
    public Run() : this(null, null) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="Run"/> class with the specified text.
    /// </summary>
    public Run(string text) : this(text, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Run"/> class with optional text and insertion position.
    /// </summary>
    /// <param name="text">Optional initial text.</param>
    /// <param name="insertionPosition">An optional position at which to insert the run.</param>
    public Run(string? text, TextPointer? insertionPosition)
    {
        DocumentInsertion.InsertInline(this, insertionPosition);
        if (text != null)
        {
            Text = text;
        }
    }

    /// <summary>
    /// Indicates whether text content should be emitted by a designer serializer.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeText(XamlDesignerSerializationManager manager) =>
        manager != null && manager.XmlWriter == null;

    private static void OnTextPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        ((Run)d).NotifyTextContentChanged();
    }

    private static object CoerceText(DependencyObject d, object? baseValue) => baseValue ?? string.Empty;
}

/// <summary>
/// An inline element that displays bold text.
/// </summary>
public sealed class Bold : Span
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
    public Bold(Inline childInline) : this(childInline, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Bold"/> class, optionally inserting it at a text position.
    /// </summary>
    /// <param name="childInline">An optional initial inline.</param>
    /// <param name="insertionPosition">An optional position at which to insert the element.</param>
    public Bold(Inline? childInline, TextPointer? insertionPosition) : this()
    {
        DocumentInsertion.InsertInline(this, insertionPosition);
        if (childInline != null)
        {
            Inlines.Add(childInline);
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Bold"/> class around existing content.
    /// </summary>
    public Bold(TextPointer start, TextPointer end) : this()
    {
        DocumentInsertion.WrapRange(this, start, end);
    }
}

/// <summary>
/// An inline element that displays italic text.
/// </summary>
public sealed class Italic : Span
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
    public Italic(Inline childInline) : this(childInline, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Italic"/> class, optionally inserting it at a text position.
    /// </summary>
    /// <param name="childInline">An optional initial inline.</param>
    /// <param name="insertionPosition">An optional position at which to insert the element.</param>
    public Italic(Inline? childInline, TextPointer? insertionPosition) : this()
    {
        DocumentInsertion.InsertInline(this, insertionPosition);
        if (childInline != null)
        {
            Inlines.Add(childInline);
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Italic"/> class around existing content.
    /// </summary>
    public Italic(TextPointer start, TextPointer end) : this()
    {
        DocumentInsertion.WrapRange(this, start, end);
    }
}

/// <summary>
/// An inline element that displays underlined text.
/// </summary>
public sealed class Underline : Span
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Underline"/> class.
    /// </summary>
    public Underline()
    {
        TextDecorations = global::Jalium.UI.TextDecorations.Underline;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Underline"/> class with the specified inline.
    /// </summary>
    public Underline(Inline childInline) : this(childInline, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Underline"/> class, optionally inserting it at a text position.
    /// </summary>
    /// <param name="childInline">An optional initial inline.</param>
    /// <param name="insertionPosition">An optional position at which to insert the element.</param>
    public Underline(Inline? childInline, TextPointer? insertionPosition) : this()
    {
        DocumentInsertion.InsertInline(this, insertionPosition);
        if (childInline != null)
        {
            Inlines.Add(childInline);
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Underline"/> class around existing content.
    /// </summary>
    public Underline(TextPointer start, TextPointer end) : this()
    {
        DocumentInsertion.WrapRange(this, start, end);
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
    public Span(Inline childInline) : this(childInline, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Span"/> class with optional content and insertion position.
    /// </summary>
    /// <param name="childInline">An optional initial inline.</param>
    /// <param name="insertionPosition">An optional position at which to insert the span.</param>
    public Span(Inline? childInline, TextPointer? insertionPosition) : this()
    {
        DocumentInsertion.InsertInline(this, insertionPosition);
        if (childInline != null)
        {
            Inlines.Add(childInline);
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Span"/> class around existing content.
    /// </summary>
    public Span(TextPointer start, TextPointer end) : this()
    {
        DocumentInsertion.WrapRange(this, start, end);
    }

    /// <summary>
    /// Indicates whether inline content should be emitted by a designer serializer.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public bool ShouldSerializeInlines(XamlDesignerSerializationManager manager) =>
        manager != null && manager.XmlWriter == null;
}

/// <summary>
/// An inline element that represents a hyperlink.
/// </summary>
public class Hyperlink : Span, ICommandSource, IUriContext
{
    private Uri? _baseUri;

    public static readonly DependencyProperty NavigateUriProperty =
        DependencyProperty.Register(nameof(NavigateUri), typeof(Uri), typeof(Hyperlink), new PropertyMetadata(null));

    public static readonly DependencyProperty TargetNameProperty =
        DependencyProperty.Register(nameof(TargetName), typeof(string), typeof(Hyperlink), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(nameof(Command), typeof(ICommand), typeof(Hyperlink), new PropertyMetadata(null));

    public static readonly DependencyProperty CommandParameterProperty =
        DependencyProperty.Register(nameof(CommandParameter), typeof(object), typeof(Hyperlink), new PropertyMetadata(null));

    public static readonly DependencyProperty CommandTargetProperty =
        DependencyProperty.Register(nameof(CommandTarget), typeof(IInputElement), typeof(Hyperlink), new PropertyMetadata(null));

    public static readonly RoutedEvent ClickEvent =
        EventManager.RegisterRoutedEvent(nameof(Click), RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(Hyperlink));

    public static readonly RoutedEvent RequestNavigateEvent =
        EventManager.RegisterRoutedEvent(
            nameof(RequestNavigate), RoutingStrategy.Bubble, typeof(RequestNavigateEventHandler), typeof(Hyperlink));

    [DevToolsPropertyCategory(DevToolsPropertyCategory.Content)]
    public Uri? NavigateUri { get => (Uri?)GetValue(NavigateUriProperty); set => SetValue(NavigateUriProperty, value); }

    public string TargetName
    {
        get => (string?)GetValue(TargetNameProperty) ?? string.Empty;
        set => SetValue(TargetNameProperty, value ?? string.Empty);
    }

    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
    public object? CommandParameter { get => GetValue(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }
    public IInputElement? CommandTarget { get => (IInputElement?)GetValue(CommandTargetProperty); set => SetValue(CommandTargetProperty, value); }

    protected virtual Uri? BaseUri { get => _baseUri; set => _baseUri = value; }
    Uri? IUriContext.BaseUri { get => BaseUri; set => BaseUri = value; }

    public event RoutedEventHandler Click { add => AddHandler(ClickEvent, value); remove => RemoveHandler(ClickEvent, value); }
    public event RequestNavigateEventHandler RequestNavigate
    {
        add => AddHandler(RequestNavigateEvent, value);
        remove => RemoveHandler(RequestNavigateEvent, value);
    }

    public Hyperlink()
    {
        InitializeHyperlink();
    }

    public Hyperlink(Inline childInline) : this()
    {
        ArgumentNullException.ThrowIfNull(childInline);
        Inlines.Add(childInline);
    }

    public Hyperlink(Inline childInline, TextPointer insertionPosition) : base(childInline, insertionPosition)
    {
        InitializeHyperlink();
    }

    public Hyperlink(TextPointer start, TextPointer end) : base(start, end)
    {
        InitializeHyperlink();
    }

    public void DoClick() => OnClick();

    protected virtual void OnClick()
    {
        if (!IsEnabled)
        {
            return;
        }

        RaiseEvent(new RoutedEventArgs(ClickEvent, this));
        if (Command is RoutedCommand routedCommand)
        {
            var target = CommandTarget ?? this;
            if (routedCommand.CanExecute(CommandParameter, target))
            {
                routedCommand.Execute(CommandParameter, target);
            }
        }
        else if (Command?.CanExecute(CommandParameter) == true)
        {
            Command.Execute(CommandParameter);
        }

        if (NavigateUri is { } uri)
        {
            RaiseEvent(new RequestNavigateEventArgs(uri, TargetName)
            {
                RoutedEvent = RequestNavigateEvent,
                Source = this,
            });
        }
    }

    private void InitializeHyperlink()
    {
        Foreground = new SolidColorBrush(Color.FromRgb(0, 102, 204));
        Focusable = true;
        AddHandler(MouseLeftButtonUpEvent, new MouseButtonEventHandler(OnMouseLeftButtonUp));
        AddHandler(KeyDownEvent, new KeyEventHandler(OnKeyDown));
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        DoClick();
        e.Handled = true;
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Space)
        {
            DoClick();
            e.Handled = true;
        }
    }
}

/// <summary>
/// An inline element that represents a line break.
/// </summary>
public sealed class LineBreak : Inline
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LineBreak"/> class.
    /// </summary>
    public LineBreak()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="LineBreak"/> class at an optional text position.
    /// </summary>
    public LineBreak(TextPointer? insertionPosition)
    {
        DocumentInsertion.InsertInline(this, insertionPosition);
    }
}

/// <summary>
/// An inline element that represents an inline UI element.
/// </summary>
public sealed class InlineUIContainer : Inline
{
    private UIElement? _child;

    /// <summary>
    /// Initializes a new instance of the <see cref="InlineUIContainer"/> class.
    /// </summary>
    public InlineUIContainer()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InlineUIContainer"/> class with an optional child.
    /// </summary>
    public InlineUIContainer(UIElement? childUIElement) : this(childUIElement, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InlineUIContainer"/> class, optionally inserting it at a text position.
    /// </summary>
    public InlineUIContainer(UIElement? childUIElement, TextPointer? insertionPosition)
    {
        DocumentInsertion.InsertInline(this, insertionPosition);
        Child = childUIElement;
    }

    /// <summary>
    /// Gets or sets the child UI element.
    /// </summary>
    public UIElement? Child
    {
        get => _child;
        set
        {
            if (ReferenceEquals(_child, value))
            {
                return;
            }

            _child = value;
            NotifyTextContentChanged();
        }
    }
}

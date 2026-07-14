using System.Collections;
using System.ComponentModel;
using System.Globalization;
using Jalium.UI.Data;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI.Documents;

/// <summary>Supports paginators whose page map can evolve while pagination continues.</summary>
public abstract class DynamicDocumentPaginator : DocumentPaginator
{
    private bool _isBackgroundPaginationEnabled = true;

    protected DynamicDocumentPaginator()
    {
    }

    public virtual bool IsBackgroundPaginationEnabled
    {
        get => _isBackgroundPaginationEnabled;
        set => _isBackgroundPaginationEnabled = value;
    }

    public event GetPageNumberCompletedEventHandler? GetPageNumberCompleted;
    public event EventHandler? PaginationCompleted;
    public event PaginationProgressEventHandler? PaginationProgress;

    public virtual void GetPageNumberAsync(ContentPosition contentPosition) =>
        GetPageNumberAsync(contentPosition, null);

    public virtual void GetPageNumberAsync(ContentPosition contentPosition, object? userState)
    {
        ArgumentNullException.ThrowIfNull(contentPosition);
        try
        {
            OnGetPageNumberCompleted(new GetPageNumberCompletedEventArgs(
                contentPosition,
                GetPageNumber(contentPosition),
                null,
                cancelled: false,
                userState));
        }
        catch (Exception error)
        {
            OnGetPageNumberCompleted(new GetPageNumberCompletedEventArgs(
                contentPosition,
                -1,
                error,
                cancelled: false,
                userState));
        }
    }

    protected virtual void OnGetPageNumberCompleted(GetPageNumberCompletedEventArgs e) =>
        GetPageNumberCompleted?.Invoke(this, e);

    protected virtual void OnPaginationProgress(PaginationProgressEventArgs e) =>
        PaginationProgress?.Invoke(this, e);

    protected virtual void OnPaginationCompleted(EventArgs e) => PaginationCompleted?.Invoke(this, e);

    public abstract int GetPageNumber(ContentPosition contentPosition);
    public abstract ContentPosition GetPagePosition(DocumentPage page);
    public abstract ContentPosition GetObjectPosition(object value);
}

public class GetPageNumberCompletedEventArgs : AsyncCompletedEventArgs
{
    private readonly ContentPosition _contentPosition;
    private readonly int _pageNumber;

    public GetPageNumberCompletedEventArgs(
        ContentPosition contentPosition,
        int pageNumber,
        Exception? error,
        bool cancelled,
        object? userState)
        : base(error, cancelled, userState)
    {
        _contentPosition = contentPosition;
        _pageNumber = pageNumber;
    }

    public ContentPosition ContentPosition
    {
        get
        {
            RaiseExceptionIfNecessary();
            return _contentPosition;
        }
    }

    public int PageNumber
    {
        get
        {
            RaiseExceptionIfNecessary();
            return _pageNumber;
        }
    }
}

public delegate void GetPageNumberCompletedEventHandler(object sender, GetPageNumberCompletedEventArgs e);

public sealed class GetPageRootCompletedEventArgs : AsyncCompletedEventArgs
{
    private readonly FixedPage? _result;

    internal GetPageRootCompletedEventArgs(
        FixedPage? result,
        Exception? error,
        bool cancelled,
        object? userState)
        : base(error, cancelled, userState)
    {
        _result = result;
    }

    public FixedPage? Result
    {
        get
        {
            RaiseExceptionIfNecessary();
            return _result;
        }
    }
}

public delegate void GetPageRootCompletedEventHandler(object sender, GetPageRootCompletedEventArgs e);

public class PagesChangedEventArgs : EventArgs
{
    public PagesChangedEventArgs(int start, int count)
    {
        Start = start;
        Count = count;
    }

    public int Start { get; }
    public int Count { get; }
}

public delegate void PagesChangedEventHandler(object sender, PagesChangedEventArgs e);

public class PaginationProgressEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a WPF-compatible progress notification from the affected page range.
    /// </summary>
    public PaginationProgressEventArgs(int start, int count)
    {
        Start = start;
        Count = count;
    }

    /// <summary>
    /// Initializes a progress notification for the current total page count.
    /// This Jalium convenience overload preserves the former printing API while
    /// keeping the public type in the WPF-compatible Documents namespace.
    /// </summary>
    public PaginationProgressEventArgs(int pageCount)
        : this(start: 0, count: pageCount)
    {
    }

    public int Start { get; }
    public int Count { get; }

    /// <summary>
    /// Gets the current page count reported by Jalium's print pipeline.
    /// For range-based notifications this is the affected <see cref="Count"/>.
    /// </summary>
    public int PageCount => Count;
}

public delegate void PaginationProgressEventHandler(object sender, PaginationProgressEventArgs e);

/// <summary>Describes a named destination in a fixed-format page.</summary>
public sealed class LinkTarget
{
    public string? Name { get; set; }
}

/// <summary>Stores the named destinations defined by a fixed-format page.</summary>
public sealed class LinkTargetCollection : CollectionBase
{
    public LinkTarget this[int index]
    {
        get => (LinkTarget)List[index]!;
        set => List[index] = value ?? throw new ArgumentNullException(nameof(value));
    }

    public int Add(LinkTarget value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return List.Add(value);
    }

    public void Remove(LinkTarget value) => List.Remove(value);
    public bool Contains(LinkTarget value) => List.Contains(value);
    public void CopyTo(LinkTarget[] array, int index) => List.CopyTo(array, index);
    public int IndexOf(LinkTarget value) => List.IndexOf(value);
    public void Insert(int index, LinkTarget value) => List.Insert(index, value);
}

/// <summary>Represents document-framework IME composition state.</summary>
public class FrameworkTextComposition : TextComposition
{
    private readonly object _owner;
    private TextPointer? _resultStart;
    private TextPointer? _resultEnd;
    private TextPointer? _compositionStart;
    private TextPointer? _compositionEnd;

    internal FrameworkTextComposition(InputManager inputManager, IInputElement? source, object owner)
        : base(inputManager, source, string.Empty)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    public int ResultOffset => _resultStart?.DocumentOffset ?? 0;
    public int ResultLength => Math.Max(0, (_resultEnd?.DocumentOffset ?? ResultOffset) - ResultOffset);
    public int CompositionOffset => _compositionStart?.DocumentOffset ?? 0;
    public int CompositionLength =>
        Math.Max(0, (_compositionEnd?.DocumentOffset ?? CompositionOffset) - CompositionOffset);

    public override void Complete() => base.Complete();

    internal object Owner => _owner;

    internal void SetResultPositions(TextPointer? start, TextPointer? end)
    {
        _resultStart = start;
        _resultEnd = end;
    }

    internal void SetCompositionPositions(TextPointer? start, TextPointer? end)
    {
        _compositionStart = start;
        _compositionEnd = end;
    }

    protected TextPointer? ResultStartPointer => _resultStart;
    protected TextPointer? ResultEndPointer => _resultEnd;
    protected TextPointer? CompositionStartPointer => _compositionStart;
    protected TextPointer? CompositionEndPointer => _compositionEnd;
}

/// <summary>Exposes strongly typed text positions for rich-text composition.</summary>
public sealed class FrameworkRichTextComposition : FrameworkTextComposition
{
    internal FrameworkRichTextComposition(InputManager inputManager, IInputElement? source, object owner)
        : base(inputManager, source, owner)
    {
    }

    public TextPointer? ResultStart => ResultStartPointer;
    public TextPointer? ResultEnd => ResultEndPointer;
    public TextPointer? CompositionStart => CompositionStartPointer;
    public TextPointer? CompositionEnd => CompositionEndPointer;
}

/// <summary>Identifies editing behavior used when adjacent text elements are merged.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class TextElementEditingBehaviorAttribute : Attribute
{
    public bool IsMergeable { get; set; }
    public bool IsTypographicOnly { get; set; }
}

/// <summary>Resolves a text effect to concrete document elements.</summary>
public static class TextEffectResolver
{
    public static TextEffectTarget[] Resolve(
        TextPointer startPosition,
        TextPointer endPosition,
        TextEffect effect)
    {
        ArgumentNullException.ThrowIfNull(startPosition);
        ArgumentNullException.ThrowIfNull(endPosition);
        ArgumentNullException.ThrowIfNull(effect);
        if (!ReferenceEquals(startPosition.Document, endPosition.Document))
        {
            throw new ArgumentException("The text positions must belong to the same document.");
        }

        var targets = new List<TextEffectTarget>();
        var seen = new HashSet<DependencyObject>();
        if (startPosition.Parent is DependencyObject startElement && seen.Add(startElement))
        {
            targets.Add(new TextEffectTarget(startElement, effect));
        }
        if (endPosition.Parent is DependencyObject endElement && seen.Add(endElement))
        {
            targets.Add(new TextEffectTarget(endElement, effect));
        }
        return targets.ToArray();
    }
}

/// <summary>Controls one text effect applied to one dependency object.</summary>
public class TextEffectTarget
{
    private readonly DependencyObject _element;
    private readonly TextEffect _textEffect;

    internal TextEffectTarget(DependencyObject element, TextEffect effect)
    {
        _element = element ?? throw new ArgumentNullException(nameof(element));
        _textEffect = effect ?? throw new ArgumentNullException(nameof(effect));
    }

    public DependencyObject Element => _element;
    public TextEffect TextEffect => _textEffect;
    public bool IsEnabled { get; private set; }

    public void Enable()
    {
        if (IsEnabled) return;
        var effects = _element.GetValue(TextElement.TextEffectsProperty) as TextEffectCollection;
        if (effects == null)
        {
            effects = new TextEffectCollection();
            _element.SetValue(TextElement.TextEffectsProperty, effects);
        }
        if (!effects.Contains(_textEffect)) effects.Add(_textEffect);
        IsEnabled = true;
    }

    public void Disable()
    {
        if (!IsEnabled) return;
        if (_element.GetValue(TextElement.TextEffectsProperty) is TextEffectCollection effects)
        {
            effects.Remove(_textEffect);
        }
        IsEnabled = false;
    }
}

/// <summary>Converts numeric document zoom values to and from percentage text.</summary>
public sealed class ZoomPercentageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double zoom || targetType != typeof(string))
        {
            return DependencyProperty.UnsetValue;
        }
        return zoom.ToString("0.##", culture) + culture.NumberFormat.PercentSymbol;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string text || targetType != typeof(double))
        {
            return DependencyProperty.UnsetValue;
        }

        text = text.Trim();
        var symbol = culture.NumberFormat.PercentSymbol;
        if (text.EndsWith(symbol, StringComparison.Ordinal))
        {
            text = text[..^symbol.Length].TrimEnd();
        }
        return double.TryParse(text, NumberStyles.Float, culture, out var zoom)
            ? zoom
            : DependencyProperty.UnsetValue;
    }
}

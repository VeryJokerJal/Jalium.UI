using System.Collections;

namespace Jalium.UI.Media;

/// <summary>
/// Defines a text effect that can be applied to text objects.
/// Enables per-character transforms, foreground changes, and other visual effects.
/// </summary>
public sealed partial class TextEffect : Animation.Animatable
{
    public static readonly DependencyProperty TransformProperty =
        DependencyProperty.Register(nameof(Transform), typeof(Transform), typeof(TextEffect), new PropertyMetadata(null));

    public static readonly DependencyProperty ForegroundProperty =
        DependencyProperty.Register(nameof(Foreground), typeof(Brush), typeof(TextEffect), new PropertyMetadata(null));

    public static readonly DependencyProperty ClipProperty =
        DependencyProperty.Register(nameof(Clip), typeof(Geometry), typeof(TextEffect), new PropertyMetadata(null));

    public static readonly DependencyProperty PositionStartProperty =
        DependencyProperty.Register(nameof(PositionStart), typeof(int), typeof(TextEffect), new PropertyMetadata(0));

    public static readonly DependencyProperty PositionCountProperty =
        DependencyProperty.Register(nameof(PositionCount), typeof(int), typeof(TextEffect), new PropertyMetadata(0));

    public TextEffect()
    {
    }

    public TextEffect(Transform? transform, Brush? foreground, Geometry? clip, int positionStart, int positionCount)
    {
        Transform = transform;
        Foreground = foreground;
        Clip = clip;
        PositionStart = positionStart;
        PositionCount = positionCount;
    }

    /// <summary>
    /// Gets or sets the Transform to apply to the text effect.
    /// </summary>
    public Transform? Transform
    {
        get => (Transform?)GetValue(TransformProperty);
        set => SetValue(TransformProperty, value);
    }

    /// <summary>
    /// Gets or sets the Brush to apply to the text content.
    /// </summary>
    public Brush? Foreground
    {
        get => (Brush?)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public Geometry? Clip
    {
        get => (Geometry?)GetValue(ClipProperty);
        set => SetValue(ClipProperty, value);
    }

    /// <summary>
    /// Gets or sets the starting character position of the text effect.
    /// </summary>
    public int PositionStart
    {
        get => (int)(GetValue(PositionStartProperty) ?? 0);
        set => SetValue(PositionStartProperty, value);
    }

    /// <summary>
    /// Gets or sets the number of characters in the range.
    /// </summary>
    public int PositionCount
    {
        get => (int)(GetValue(PositionCountProperty) ?? 0);
        set => SetValue(PositionCountProperty, value);
    }

    /// <summary>
    /// Creates a copy of this TextEffect.
    /// </summary>
    public new TextEffect Clone() => (TextEffect)base.Clone();

    public new TextEffect CloneCurrentValue() => (TextEffect)base.CloneCurrentValue();

    protected override Freezable CreateInstanceCore() => new TextEffect();
}
/// <summary>
/// Represents a collection of TextEffect objects.
/// </summary>
public sealed class TextEffectCollection : Animation.Animatable, IList<TextEffect>, IList
{
    private readonly AnimatableListStorage<TextEffect> _items;

    public TextEffectCollection() => _items = CreateStorage();
    public TextEffectCollection(int capacity) => _items = CreateStorage(capacity);
    public TextEffectCollection(IEnumerable<TextEffect> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        _items = CreateStorage(collection is ICollection<TextEffect> source ? source.Count : 0);
        _items.AddRange(collection);
    }

    public TextEffect this[int index] { get => _items[index]; set => _items[index] = value; }
    object? IList.this[int index] { get => this[index]; set => this[index] = AnimatableListStorage<TextEffect>.Cast(value); }
    public int Count => _items.Count;
    bool ICollection<TextEffect>.IsReadOnly => _items.IsReadOnly;
    bool IList.IsReadOnly => _items.IsReadOnly;
    bool IList.IsFixedSize => _items.IsReadOnly;
    bool ICollection.IsSynchronized => _items.IsSynchronized;
    object ICollection.SyncRoot => this;
    public void Add(TextEffect item) => _items.Add(item);
    int IList.Add(object? value) { Add(AnimatableListStorage<TextEffect>.Cast(value)); return Count - 1; }
    public void AddRange(IEnumerable<TextEffect> items) => _items.AddRange(items);
    public void Clear() => _items.Clear();
    public bool Contains(TextEffect item) => _items.Contains(item);
    bool IList.Contains(object? value) => value is TextEffect item && Contains(item);
    public void CopyTo(TextEffect[] array, int arrayIndex) => _items.CopyTo(array, arrayIndex);
    void ICollection.CopyTo(Array array, int index) => _items.CopyTo(array, index);
    IEnumerator<TextEffect> IEnumerable<TextEffect>.GetEnumerator() => _items.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
    public int IndexOf(TextEffect item) => _items.IndexOf(item);
    int IList.IndexOf(object? value) => value is TextEffect item ? IndexOf(item) : -1;
    public void Insert(int index, TextEffect item) => _items.Insert(index, item);
    void IList.Insert(int index, object? value) => Insert(index, AnimatableListStorage<TextEffect>.Cast(value));
    public bool Remove(TextEffect item) => _items.Remove(item);
    void IList.Remove(object? value) { if (value is TextEffect item) Remove(item); }
    public void RemoveAt(int index) => _items.RemoveAt(index);

    public new TextEffectCollection Clone() => (TextEffectCollection)base.Clone();
    public new TextEffectCollection CloneCurrentValue() => (TextEffectCollection)base.CloneCurrentValue();
    public Enumerator GetEnumerator() => new(this);
    protected override Freezable CreateInstanceCore() => new TextEffectCollection();
    protected override bool FreezeCore(bool isChecking) => base.FreezeCore(isChecking) && _items.Freeze(isChecking);
    protected override void CloneCore(Freezable source) { base.CloneCore(source); _items.CopyFrom(((TextEffectCollection)source)._items, AnimatableListCloneMode.Clone); }
    protected override void CloneCurrentValueCore(Freezable source) { base.CloneCurrentValueCore(source); _items.CopyFrom(((TextEffectCollection)source)._items, AnimatableListCloneMode.CloneCurrentValue); }
    protected override void GetAsFrozenCore(Freezable source) { base.GetAsFrozenCore(source); _items.CopyFrom(((TextEffectCollection)source)._items, AnimatableListCloneMode.GetAsFrozen); }
    protected override void GetCurrentValueAsFrozenCore(Freezable source) { base.GetCurrentValueAsFrozenCore(source); _items.CopyFrom(((TextEffectCollection)source)._items, AnimatableListCloneMode.GetCurrentValueAsFrozen); }

    public struct Enumerator : IEnumerator<TextEffect>
    {
        private IEnumerator<TextEffect>? _inner;
        internal Enumerator(TextEffectCollection collection) =>
            _inner = ((IEnumerable<TextEffect>)collection).GetEnumerator();
        public TextEffect Current => _inner?.Current ?? throw new InvalidOperationException();
        object System.Collections.IEnumerator.Current => Current;
        public bool MoveNext() => _inner?.MoveNext() ?? false;
        public void Reset() => _inner?.Reset();
        public void Dispose()
        {
            _inner?.Dispose();
            _inner = null;
        }
    }

    private AnimatableListStorage<TextEffect> CreateStorage(int capacity = 0) => new(
        () => ReadPreamble(),
        () => WritePreamble(),
        () => WritePostscript(),
        (oldValue, newValue) => OnFreezablePropertyChanged(oldValue, newValue),
        () => IsFrozen,
        capacity);
}

/// <summary>
/// Provides attached properties and methods for performing OpenType typography on text elements.
/// </summary>
// The complete WPF-compatible attached-property surface is provided by
// Jalium.UI.Documents.Typography. Retain this early subset for in-assembly
// compatibility without advertising a second public Typography type.
internal static class Typography
{
    // Standard Ligatures
    public static readonly DependencyProperty StandardLigaturesProperty =
        DependencyProperty.RegisterAttached("StandardLigatures", typeof(bool), typeof(Typography),
            new PropertyMetadata(true));

    public static bool GetStandardLigatures(DependencyObject element) => (bool)element.GetValue(StandardLigaturesProperty)!;
    public static void SetStandardLigatures(DependencyObject element, bool value) => element.SetValue(StandardLigaturesProperty, value);

    // Contextual Ligatures
    public static readonly DependencyProperty ContextualLigaturesProperty =
        DependencyProperty.RegisterAttached("ContextualLigatures", typeof(bool), typeof(Typography),
            new PropertyMetadata(true));

    public static bool GetContextualLigatures(DependencyObject element) => (bool)element.GetValue(ContextualLigaturesProperty)!;
    public static void SetContextualLigatures(DependencyObject element, bool value) => element.SetValue(ContextualLigaturesProperty, value);

    // Discretionary Ligatures
    public static readonly DependencyProperty DiscretionaryLigaturesProperty =
        DependencyProperty.RegisterAttached("DiscretionaryLigatures", typeof(bool), typeof(Typography),
            new PropertyMetadata(false));

    public static bool GetDiscretionaryLigatures(DependencyObject element) => (bool)element.GetValue(DiscretionaryLigaturesProperty)!;
    public static void SetDiscretionaryLigatures(DependencyObject element, bool value) => element.SetValue(DiscretionaryLigaturesProperty, value);

    // Historical Ligatures
    public static readonly DependencyProperty HistoricalLigaturesProperty =
        DependencyProperty.RegisterAttached("HistoricalLigatures", typeof(bool), typeof(Typography),
            new PropertyMetadata(false));

    public static bool GetHistoricalLigatures(DependencyObject element) => (bool)element.GetValue(HistoricalLigaturesProperty)!;
    public static void SetHistoricalLigatures(DependencyObject element, bool value) => element.SetValue(HistoricalLigaturesProperty, value);

    // Kerning
    public static readonly DependencyProperty KerningProperty =
        DependencyProperty.RegisterAttached("Kerning", typeof(bool), typeof(Typography),
            new PropertyMetadata(true));

    public static bool GetKerning(DependencyObject element) => (bool)element.GetValue(KerningProperty)!;
    public static void SetKerning(DependencyObject element, bool value) => element.SetValue(KerningProperty, value);

    // Capital Spacing
    public static readonly DependencyProperty CapitalSpacingProperty =
        DependencyProperty.RegisterAttached("CapitalSpacing", typeof(bool), typeof(Typography),
            new PropertyMetadata(false));

    public static bool GetCapitalSpacing(DependencyObject element) => (bool)element.GetValue(CapitalSpacingProperty)!;
    public static void SetCapitalSpacing(DependencyObject element, bool value) => element.SetValue(CapitalSpacingProperty, value);

    // Numeral Style
    public static readonly DependencyProperty NumeralStyleProperty =
        DependencyProperty.RegisterAttached("NumeralStyle", typeof(FontNumeralStyle), typeof(Typography),
            new PropertyMetadata(FontNumeralStyle.Normal));

    public static FontNumeralStyle GetNumeralStyle(DependencyObject element) => (FontNumeralStyle)element.GetValue(NumeralStyleProperty)!;
    public static void SetNumeralStyle(DependencyObject element, FontNumeralStyle value) => element.SetValue(NumeralStyleProperty, value);

    // Numeral Alignment
    public static readonly DependencyProperty NumeralAlignmentProperty =
        DependencyProperty.RegisterAttached("NumeralAlignment", typeof(FontNumeralAlignment), typeof(Typography),
            new PropertyMetadata(FontNumeralAlignment.Normal));

    public static FontNumeralAlignment GetNumeralAlignment(DependencyObject element) => (FontNumeralAlignment)element.GetValue(NumeralAlignmentProperty)!;
    public static void SetNumeralAlignment(DependencyObject element, FontNumeralAlignment value) => element.SetValue(NumeralAlignmentProperty, value);

    // Variants
    public static readonly DependencyProperty VariantsProperty =
        DependencyProperty.RegisterAttached("Variants", typeof(FontVariants), typeof(Typography),
            new PropertyMetadata(FontVariants.Normal));

    public static FontVariants GetVariants(DependencyObject element) => (FontVariants)element.GetValue(VariantsProperty)!;
    public static void SetVariants(DependencyObject element, FontVariants value) => element.SetValue(VariantsProperty, value);

    // Capitals
    public static readonly DependencyProperty CapitalsProperty =
        DependencyProperty.RegisterAttached("Capitals", typeof(FontCapitals), typeof(Typography),
            new PropertyMetadata(FontCapitals.Normal));

    public static FontCapitals GetCapitals(DependencyObject element) => (FontCapitals)element.GetValue(CapitalsProperty)!;
    public static void SetCapitals(DependencyObject element, FontCapitals value) => element.SetValue(CapitalsProperty, value);

    // Slashed Zero
    public static readonly DependencyProperty SlashedZeroProperty =
        DependencyProperty.RegisterAttached("SlashedZero", typeof(bool), typeof(Typography),
            new PropertyMetadata(false));

    public static bool GetSlashedZero(DependencyObject element) => (bool)element.GetValue(SlashedZeroProperty)!;
    public static void SetSlashedZero(DependencyObject element, bool value) => element.SetValue(SlashedZeroProperty, value);
}

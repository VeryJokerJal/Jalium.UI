using System.Collections;
using Jalium.UI.Controls.Themes;
using Jalium.UI.Media;

namespace Jalium.UI.Documents;

using TextDecorationCollection = Jalium.UI.TextDecorationCollection;

/// <summary>
/// Abstract base class for all elements in a FlowDocument.
/// </summary>
public abstract class TextElement : FrameworkContentElement
{
    private Typography? _typography;
    private FlowDocument? _standaloneDocument;

    internal event EventHandler? TextContentChanged;

    /// <summary>
    /// Notifies the owning text container that this element's textual content changed.
    /// </summary>
    internal void NotifyTextContentChanged()
    {
        TextContentChanged?.Invoke(this, EventArgs.Empty);
    }

    #region Shared Typography Properties

    // These properties are registered as attached+inheritable so that Control, TextBlock,
    // and TextElement can share the same DependencyProperty instance via AddOwner.
    // This mirrors the WPF pattern where TextElement.ForegroundProperty is the canonical
    // source, and setting Foreground on a Control automatically propagates to child TextBlocks
    // through the property inheritance system.

    /// <summary>
    /// Identifies the FontFamily dependency property.
    /// Shared across Control, TextBlock, and TextElement via AddOwner.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty FontFamilyProperty =
        DependencyProperty.RegisterAttached("FontFamily", typeof(FontFamily), typeof(TextElement),
            new FrameworkPropertyMetadata(
                SystemFonts.MessageFontFamily,
                FrameworkPropertyMetadataOptions.AffectsMeasure |
                FrameworkPropertyMetadataOptions.AffectsRender |
                FrameworkPropertyMetadataOptions.Inherits),
            static value => value is FontFamily);

    /// <summary>
    /// Identifies the FontSize dependency property.
    /// Shared across Control, TextBlock, and TextElement via AddOwner.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty FontSizeProperty =
        DependencyProperty.RegisterAttached("FontSize", typeof(double), typeof(TextElement),
            new PropertyMetadata(14.0, null, null, inherits: true));

    /// <summary>
    /// Identifies the FontWeight dependency property.
    /// Shared across Control, TextBlock, and TextElement via AddOwner.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty FontWeightProperty =
        DependencyProperty.RegisterAttached("FontWeight", typeof(FontWeight), typeof(TextElement),
            new PropertyMetadata(FontWeights.Normal, null, null, inherits: true));

    /// <summary>
    /// Identifies the FontStyle dependency property.
    /// Shared across Control, TextBlock, and TextElement via AddOwner.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty FontStyleProperty =
        DependencyProperty.RegisterAttached("FontStyle", typeof(FontStyle), typeof(TextElement),
            new PropertyMetadata(FontStyles.Normal, null, null, inherits: true));

    /// <summary>
    /// Identifies the FontStretch dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty FontStretchProperty =
        DependencyProperty.RegisterAttached("FontStretch", typeof(FontStretch), typeof(TextElement),
            new PropertyMetadata(FontStretches.Normal, null, null, inherits: true));

    /// <summary>
    /// Identifies the Foreground dependency property.
    /// Shared across Control, TextBlock, and TextElement via AddOwner.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty ForegroundProperty =
        DependencyProperty.RegisterAttached("Foreground", typeof(Brush), typeof(TextElement),
            new PropertyMetadata(null, null, null, inherits: true));

    /// <summary>
    /// Identifies the Background dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public static readonly DependencyProperty BackgroundProperty =
        DependencyProperty.Register(nameof(Background), typeof(Brush), typeof(TextElement),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the TextDecorations dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty TextDecorationsProperty =
        DependencyProperty.Register(nameof(TextDecorations), typeof(TextDecorationCollection), typeof(TextElement),
            new PropertyMetadata(null));

    /// <summary>
    /// Identifies the TextEffects dependency property.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public static readonly DependencyProperty TextEffectsProperty =
        DependencyProperty.RegisterAttached(nameof(TextEffects), typeof(TextEffectCollection), typeof(TextElement),
            new PropertyMetadata(null, null, null, inherits: true));

    #endregion

    #region Attached Property Accessors

    public static void SetForeground(DependencyObject element, Brush? value) => element.SetValue(ForegroundProperty, value);
    public static Brush? GetForeground(DependencyObject element) => (Brush?)element.GetValue(ForegroundProperty);

    public static void SetFontFamily(DependencyObject element, FontFamily value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(FontFamilyProperty, value);
    }

    public static FontFamily GetFontFamily(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return (FontFamily)element.GetValue(FontFamilyProperty)!;
    }

    public static void SetFontSize(DependencyObject element, double value) => element.SetValue(FontSizeProperty, value);
    public static double GetFontSize(DependencyObject element) => (double)element.GetValue(FontSizeProperty)!;

    public static void SetFontWeight(DependencyObject element, FontWeight value) => element.SetValue(FontWeightProperty, value);
    public static FontWeight GetFontWeight(DependencyObject element) => element.GetValue(FontWeightProperty) is FontWeight fw ? fw : FontWeights.Normal;

    public static void SetFontStyle(DependencyObject element, FontStyle value) => element.SetValue(FontStyleProperty, value);
    public static FontStyle GetFontStyle(DependencyObject element) => element.GetValue(FontStyleProperty) is FontStyle fs ? fs : FontStyles.Normal;

    public static void SetFontStretch(DependencyObject element, FontStretch value)
    {
        ArgumentNullException.ThrowIfNull(element);
        element.SetValue(FontStretchProperty, value);
    }

    public static FontStretch GetFontStretch(DependencyObject element)
    {
        ArgumentNullException.ThrowIfNull(element);
        return element.GetValue(FontStretchProperty) is FontStretch stretch ? stretch : FontStretches.Normal;
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the font family.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty)!;
        set => SetValue(FontFamilyProperty, value);
    }

    /// <summary>
    /// Gets or sets the font size.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty)!;
        set => SetValue(FontSizeProperty, value);
    }

    /// <summary>
    /// Gets or sets the font weight.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public FontWeight FontWeight
    {
        get => GetValue(FontWeightProperty) is FontWeight fw ? fw : FontWeights.Normal;
        set => SetValue(FontWeightProperty, value);
    }

    /// <summary>
    /// Gets or sets the font style.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public FontStyle FontStyle
    {
        get => GetValue(FontStyleProperty) is FontStyle fs ? fs : FontStyles.Normal;
        set => SetValue(FontStyleProperty, value);
    }

    /// <summary>
    /// Gets or sets the font stretch.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public FontStretch FontStretch
    {
        get => GetValue(FontStretchProperty) is FontStretch stretch ? stretch : FontStretches.Normal;
        set => SetValue(FontStretchProperty, value);
    }

    /// <summary>
    /// Gets or sets the foreground brush.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? Foreground
    {
        get => (Brush?)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the background brush.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Appearance)]
    public Brush? Background
    {
        get => (Brush?)GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the text decorations.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public TextDecorationCollection? TextDecorations
    {
        get => (TextDecorationCollection?)GetValue(TextDecorationsProperty);
        set => SetValue(TextDecorationsProperty, value);
    }

    /// <summary>
    /// Gets or sets effects applied to the element's text.
    /// </summary>
    [DevToolsPropertyCategory(DevToolsPropertyCategory.Typography)]
    public TextEffectCollection TextEffects
    {
        get
        {
            if (GetValue(TextEffectsProperty) is TextEffectCollection effects)
            {
                return effects;
            }

            effects = [];
            SetValue(TextEffectsProperty, effects);
            return effects;
        }
        set => SetValue(TextEffectsProperty, value ?? throw new ArgumentNullException(nameof(value)));
    }

    /// <summary>
    /// Gets the OpenType typography settings for this element.
    /// </summary>
    public Typography Typography => _typography ??= new Typography(this);

    /// <summary>Gets a position immediately before the element's content.</summary>
    public TextPointer ContentStart => CreateTextPointer(0, LogicalDirection.Forward);

    /// <summary>Gets a position immediately after the element's content.</summary>
    public TextPointer ContentEnd => CreateTextPointer(GetContentLength(this), LogicalDirection.Backward);

    /// <summary>Gets a position at the element's opening boundary.</summary>
    public TextPointer ElementStart => CreateTextPointer(0, LogicalDirection.Backward);

    /// <summary>Gets a position at the element's closing boundary.</summary>
    public TextPointer ElementEnd => CreateTextPointer(GetContentLength(this), LogicalDirection.Forward);

    /// <inheritdoc />
    protected internal override IEnumerator LogicalChildren => base.LogicalChildren;

    /// <summary>
    /// Gets the parent element.
    /// </summary>
    public new TextElement? Parent { get; internal set; }

    #endregion

    #region Methods

    /// <summary>
    /// Gets the effective value of a property, considering inheritance.
    /// </summary>
    protected T GetEffectiveValue<T>(DependencyProperty property, T defaultValue)
    {
        var value = GetValue(property);
        if (value != null)
            return (T)value;

        if (Parent != null)
        {
            var parentValue = Parent.GetValue(property);
            return parentValue != null ? (T)parentValue : defaultValue;
        }

        return defaultValue;
    }

    /// <summary>
    /// Gets the effective font family.
    /// </summary>
    public FontFamily GetEffectiveFontFamily() =>
        GetEffectiveValue(FontFamilyProperty, (FontFamily)FontFamilyProperty.DefaultMetadata.DefaultValue!);

    /// <summary>
    /// Gets the effective font size.
    /// </summary>
    public double GetEffectiveFontSize() => GetEffectiveValue(FontSizeProperty, 14.0);

    /// <summary>
    /// Gets the effective font weight.
    /// </summary>
    public FontWeight GetEffectiveFontWeight() => GetEffectiveValue(FontWeightProperty, FontWeights.Normal);

    /// <summary>
    /// Gets the effective font style.
    /// </summary>
    public FontStyle GetEffectiveFontStyle() => GetEffectiveValue(FontStyleProperty, FontStyles.Normal);

    /// <summary>
    /// Gets the effective font stretch.
    /// </summary>
    public FontStretch GetEffectiveFontStretch() => GetEffectiveValue(FontStretchProperty, FontStretches.Normal);

    /// <summary>
    /// Gets the effective foreground brush.
    /// </summary>
    public Brush GetEffectiveForeground() =>
        GetEffectiveValue<Brush?>(ForegroundProperty, null) ?? new SolidColorBrush(ThemeColors.TextPrimary);

    /// <summary>
    /// Gets the effective background brush.
    /// </summary>
    public Brush? GetEffectiveBackground() => GetEffectiveValue<Brush?>(BackgroundProperty, null);

    /// <summary>
    /// Gets the effective text decorations.
    /// </summary>
    public TextDecorationCollection? GetEffectiveTextDecorations() => GetEffectiveValue<TextDecorationCollection?>(TextDecorationsProperty, null);

    internal FlowDocument? GetFlowDocument()
    {
        for (FrameworkContentElement? current = this;
             current is not null;
             current = current.Parent as FrameworkContentElement)
        {
            if (current is FlowDocument document)
            {
                return document;
            }
        }

        TextElement root = this;
        while (root.Parent is TextElement parent)
        {
            root = parent;
        }

        return root is Block block && block.OwnerCollection?.Parent is FlowDocument owner
            ? owner
            : null;
    }

    private TextPointer CreateTextPointer(int offset, LogicalDirection direction)
    {
        var document = GetFlowDocument() ?? (_standaloneDocument ??= new FlowDocument());
        return new TextPointer(document, this, offset, direction);
    }

    internal static int GetContentLength(TextElement element)
    {
        return element switch
        {
            Run run => run.Text.Length,
            Span span => span.Inlines.Sum(GetContentLength),
            Paragraph paragraph => paragraph.Inlines.Sum(GetContentLength),
            Section section => section.Blocks.Sum(GetContentLength),
            List list => list.ListItems.Sum(GetContentLength),
            ListItem item => item.Blocks.Sum(GetContentLength),
            Table table => table.RowGroups.Sum(GetContentLength),
            TableRowGroup rowGroup => rowGroup.Rows.Sum(GetContentLength),
            TableRow row => row.Cells.Sum(GetContentLength),
            TableCell cell => cell.Blocks.Sum(GetContentLength),
            LineBreak or InlineUIContainer or BlockUIContainer or AnchoredBlock => 1,
            _ => 0,
        };
    }

    #endregion
}


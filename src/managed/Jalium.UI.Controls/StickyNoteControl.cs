using Jalium.UI.Annotations;
using Jalium.UI.Media;

namespace Jalium.UI.Controls;

/// <summary>Displays an annotation as an expandable text or ink sticky note.</summary>
public sealed class StickyNoteControl : Control
{
    public static readonly DependencyProperty AuthorProperty =
        DependencyProperty.Register(nameof(Author), typeof(string), typeof(StickyNoteControl), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty IsExpandedProperty =
        DependencyProperty.Register(nameof(IsExpanded), typeof(bool), typeof(StickyNoteControl), new PropertyMetadata(true));

    public static readonly DependencyProperty PenWidthProperty =
        DependencyProperty.Register(nameof(PenWidth), typeof(double), typeof(StickyNoteControl), new PropertyMetadata(2d));

    public static readonly DependencyProperty StickyNoteTypeProperty =
        DependencyProperty.Register(
            nameof(StickyNoteType),
            typeof(StickyNoteType),
            typeof(StickyNoteControl),
            new PropertyMetadata(StickyNoteType.Text));

    private bool _isActive;
    private bool _isMouseOverAnchor;
    private IAnchorInfo? _anchorInfo;

    /// <summary>Initializes a text sticky note.</summary>
    public StickyNoteControl()
        : this(StickyNoteType.Text)
    {
    }

    /// <summary>Initializes a sticky note for the requested content type.</summary>
    internal StickyNoteControl(StickyNoteType stickyNoteType)
    {
        SetValue(StickyNoteTypeProperty, stickyNoteType);
        CaptionFontFamily = new FontFamily("Segoe UI");
        CaptionFontSize = 12d;
        CaptionFontStretch = FontStretches.Normal;
        CaptionFontStyle = FontStyles.Normal;
        CaptionFontWeight = FontWeights.Normal;
    }

    public string Author
    {
        get => (string?)GetValue(AuthorProperty) ?? string.Empty;
    }

    public bool IsExpanded
    {
        get => (bool)(GetValue(IsExpandedProperty) ?? true);
        set => SetValue(IsExpandedProperty, value);
    }

    public bool IsActive => _isActive;

    public bool IsMouseOverAnchor => _isMouseOverAnchor;

    public FontFamily CaptionFontFamily { get; set; }

    public double CaptionFontSize { get; set; }

    public FontStretch CaptionFontStretch { get; set; }

    public FontStyle CaptionFontStyle { get; set; }

    public FontWeight CaptionFontWeight { get; set; }

    public double PenWidth
    {
        get => (double)(GetValue(PenWidthProperty) ?? 2d);
        set
        {
            if (!double.IsFinite(value) || value <= 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }

            SetValue(PenWidthProperty, value);
        }
    }

    public StickyNoteType StickyNoteType
    {
        get => (StickyNoteType)(GetValue(StickyNoteTypeProperty) ?? StickyNoteType.Text);
    }

    public IAnchorInfo? AnchorInfo => _anchorInfo;

    internal void SetAnnotationState(
        IAnchorInfo? anchorInfo,
        string? author,
        bool isActive,
        bool isMouseOverAnchor)
    {
        _anchorInfo = anchorInfo;
        SetValue(AuthorProperty, author ?? string.Empty);
        _isActive = isActive;
        _isMouseOverAnchor = isMouseOverAnchor;
        InvalidateVisual();
    }

    /// <inheritdoc />
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        InvalidateVisual();
    }
}

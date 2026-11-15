using Jalium.UI.Automation.Peers;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;

namespace Jalium.UI.Styling;

/// <summary>Private formatting children never participate in document selectors or tab order.</summary>
internal interface ICssGeneratedContentBox
{
    FrameworkElement ContentOwner { get; }
}

internal sealed class CssGeneratedImage(FrameworkElement owner, Action changed, bool marker = false)
    : FrameworkElement, ICssGeneratedContentBox
{
    public FrameworkElement ContentOwner => owner;
    private CssImageValue? _definition;
    private Brush? _brush;
    private double _density = 1;
    private Size _default = new(300, 150);
    internal CssBackgroundImageResource? ActiveResource { get; private set; }
    private CssBackgroundImageResource[] _resources = [];
    internal CssBackgroundImageResource[] ActiveResources => _resources;
    internal bool UsesResource(CssBackgroundImageResource resource) => _resources.Contains(resource);
    internal bool Valid { get; private set; }
    internal Size NaturalSize { get; private set; }
    internal bool HasNaturalRatio { get; private set; }
    internal CssImageIntrinsicSize Intrinsic { get; private set; }
    internal Size DefaultObjectSize => _default;

    internal void Bind(CssImageValue definition)
    {
        if (_definition?.Equals(definition) != true) _definition = definition;
        Refresh();
    }

    internal void Retire() { _definition = null; _brush = null; ActiveResource = null; _resources = []; }

    private void Refresh()
    {
        var node = CssNode.Get(owner);
        var context = new CssApplyContext(node, CssEngine.BuildLengthContext(node), new());
        _default = marker ? new(context.Lengths.ElementFontSize, context.Lengths.ElementFontSize) : new(300, 150);
        _brush = null; ActiveResource = null; _density = 1;
        if (_definition?.Select(context) is { } selected)
        {
            var resources = new List<CssBackgroundImageResource>();
            _brush = selected.Image.Content(context, this, selected.Resolution, out var resource, out _density, resources);
            ActiveResource = resource;
            _resources = resources.ToArray();
            if (_brush is ImageBrush bitmap) bitmap.ScalingMode = CssImageRenderingProperties.Resolve(owner, BitmapScalingMode.Unspecified);
        }
        else _resources = [];
        UpdatePixels();
    }

    internal void ResourceChanged(bool pixelsOnly)
    {
        if (_definition is null) return;
        if (pixelsOnly) UpdatePixels(); else Refresh();
    }

    private void UpdatePixels()
    {
        var valid = _brush is not null;
        var intrinsic = CssImageIntrinsicSize.FromBrush(_brush, _default, _density);
        if (_brush?.CssImageLayout is { } generated)
        {
            valid = generated.IsValid;
        }
        else if (_brush is ImageBrush { ImageSource: { } source })
        {
            if (source is BitmapImage bitmap)
            {
                bitmap.RequestMetadata();
                var scale = VisualTreeHelper.GetDpi(VisualTreeHelper.GetRoot(owner) as Visual ?? owner).DpiScaleY;
                var target = RenderSize.Width > 0 && RenderSize.Height > 0 ? RenderSize : _default;
                bitmap.RequestDecode(Hint(target.Width * scale), Hint(target.Height * scale), false);
            }
            valid = CssImageIntrinsicSize.IsAvailable(source);
        }
        var natural = valid ? intrinsic.DefaultSize(_default) : default;
        var ratio = valid && intrinsic.Ratio > 0;
        var resized = Valid != valid || NaturalSize != natural || HasNaturalRatio != ratio || Intrinsic != intrinsic;
        Valid = valid; NaturalSize = natural; HasNaturalRatio = ratio; Intrinsic = intrinsic;
        if (resized) { InvalidateMeasure(); changed(); }
        InvalidateVisual();
    }

    private static int Hint(double value) => double.IsFinite(value) ? (int)Math.Clamp(Math.Ceiling(value), 1, 4096) : 1;
    protected override Size MeasureOverride(Size availableSize) => Valid ? NaturalSize : default;
    protected override void OnRender(DrawingContext context)
    {
        if (Valid && _brush is not null && RenderSize.Width > 0 && RenderSize.Height > 0)
            context.DrawRectangle(_brush, null, new(RenderSize));
    }
    protected override AutomationPeer? OnCreateAutomationPeer() => null;
}

internal partial class CssGeneratedText
{
    private List<UIElement>? _imageChildren;
    private CssResolvedContent[]? _imageParts;
    private bool _settingImages;
    private CssImageValue? _automaticImage;
    private string? _automaticFallback;
    private string? _automaticSpoken;
    private CssResolvedContent[]? _automaticParts;
    private CssGeneratedImage? _markerCandidate;
    internal bool HasImageContent => _imageChildren is not null;
    internal bool IsImageReplacement => _automaticImage is null && _imageParts is [{ Image: not null }];
    internal CssGeneratedImage? ReplacementImage => IsImageReplacement ? _imageChildren?.OfType<CssGeneratedImage>().FirstOrDefault() : null;
    internal bool IsListItemBox => Kind is CssPseudoElement.Before or CssPseudoElement.After && CssListProperties.IsItem(CssNode.Get(this));
    protected override bool SupportsCssContentLayout => HasImageContent && !IsImageReplacement || CssDisplayMode == CssDisplayMode.Contents || HasCssFirstLetter || HasCssFirstLine || HasInitialLetterTextScope || IsListItemBox;
    protected override bool SupportsCssInlineContentLayout => HasImageContent && !IsImageReplacement || HasCssFirstLetter || HasCssFirstLine || HasInitialLetterTextScope || IsListItemBox;
    protected override void PrepareCssContentLayout()
    {
        if ((CssDisplayMode == CssDisplayMode.Contents || HasCssFirstLetter || HasCssFirstLine || HasInitialLetterTextScope || IsListItemBox) && !HasImageContent) base.PrepareCssContentLayout();
    }
    protected override Size ArrangeOverride(Size size) => IsImageReplacement ? ArrangeImageReplacement(size) : base.ArrangeOverride(size);
    protected override int VisualChildrenCount => _imageChildren?.Count ?? base.VisualChildrenCount;
    protected override Visual? GetVisualChild(int index) => _imageChildren is { } children
        ? children[index] : base.GetVisualChild(index);

    private sealed class ContentRun(CssGeneratedText owner) : TextBlock, ICssGeneratedContentBox
    {
        public FrameworkElement ContentOwner => owner;
        protected override AutomationPeer? OnCreateAutomationPeer() => null;
    }

    private void SetImageContents(CssResolvedContent[]? contents)
    {
        if (_settingImages) return;
        _settingImages = true;
        try
        {
            if (contents?.Any(part => part.Image is not null) != true)
            {
                if (_imageChildren is null) return;
                foreach (var child in _imageChildren) { if (child is CssGeneratedImage image) image.Retire(); RemoveVisualChild(child); }
                _imageChildren = null; _imageParts = null;
                if (CssInlineLayoutOwner is { } owner) ReleaseCssInlineFrame(owner);
                InvalidateMeasure(); return;
            }
            if (_imageParts is null || !_imageParts.AsSpan().SequenceEqual(contents))
            {
                var previous = _imageChildren ?? [];
                var children = new List<UIElement>();
                for (var index = 0; index < contents.Length; index++)
                {
                    var part = contents[index]; UIElement child;
                    if (part.Image is { } definition)
                    {
                        var image = index < previous.Count && previous[index] is CssGeneratedImage retained ? retained :
                            new CssGeneratedImage(this, ImagesChanged, Kind.IsMarker());
                        image.CssInlineOuter = true; image.Bind(definition); child = image;
                    }
                    else
                    {
                        var run = index < previous.Count && previous[index] is ContentRun retained ? retained : new ContentRun(this);
                        run.CssInlineOuter = true; run.Text = part.Text; child = run;
                    }
                    children.Add(child);
                    if (!ReferenceEquals(child.VisualParent, this)) AddVisualChild(child);
                }
                foreach (var old in previous.Where(old => !children.Contains(old)))
                { if (old is CssGeneratedImage image) image.Retire(); RemoveVisualChild(old); }
                _imageChildren = children; _imageParts = contents;
                InvalidateMeasure();
            }
            else if (_imageChildren is { } retainedChildren)
                for (var index = 0; index < retainedChildren.Count; index++)
                    if (retainedChildren[index] is CssGeneratedImage image) image.Bind(contents[index].Image!);
        }
        finally { _settingImages = false; }
    }

    private void ImagesChanged()
    {
        if (_settingImages) return;
        if (_automaticImage is not null) UpdateImageMarker();
        InvalidateMeasure(); InvalidateOrigin();
    }

    private void ClearAutomaticImage()
    { _automaticImage = null; _automaticParts = null; _automaticSpoken = null; _markerCandidate?.Retire(); _markerCandidate = null; }

    internal void SetAutomaticImageMarker(string? fallback, CssImageValue? image, CssResolvedContent[]? symbolParts = null, string? spokenText = null)
    {
        _automaticSpoken = spokenText;
        _automaticParts = symbolParts;
        if (image is null)
        {
            _automaticImage = null; _markerCandidate?.Retire(); _markerCandidate = null;
            if (symbolParts is not null && fallback is not null)
            {
                _automaticFallback = fallback;
                _hasContent = true;
                SetResolvedContent(fallback, null, symbolParts, spokenText);
                CoerceValue(VisibilityProperty);
                return;
            }
            SetAutomaticMarker(fallback, spokenText); return;
        }
        _automaticFallback = fallback;
        if (_automaticImage?.Equals(image) != true) _automaticImage = image;
        _markerCandidate ??= new(this, ImagesChanged, marker: true);
        _settingImages = true;
        try { _markerCandidate.Bind(_automaticImage); }
        finally { _settingImages = false; }
        UpdateImageMarker();
    }

    private void UpdateImageMarker()
    {
        if (_automaticImage is null || _markerCandidate is null) return;
        _hasContent = _markerCandidate.Valid || _automaticFallback is not null;
        if (_markerCandidate.Valid)
            SetResolvedContent(" ", null, [new(Image: _automaticImage), new(" ")], _automaticSpoken);
        else SetResolvedContent(_automaticFallback ?? "", null, _automaticParts, _automaticSpoken);
        CoerceValue(VisibilityProperty);
    }

    internal Size MeasureImageReplacement(Size available)
    {
        var image = _imageChildren!.OfType<CssGeneratedImage>().First();
        return CssReplacedImageSizing.Measure(this, image, available);
    }
    internal Size ArrangeImageReplacement(Size size)
    {
        var image = _imageChildren!.OfType<CssGeneratedImage>().First();
        CssReplacedImageSizing.Arrange(this, image, size);
        return size;
    }
}

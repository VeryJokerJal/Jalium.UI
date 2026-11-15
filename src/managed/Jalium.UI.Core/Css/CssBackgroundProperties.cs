using System.Runtime.CompilerServices;
using Jalium.UI.Documents;
using Jalium.UI.Media;

namespace Jalium.UI.Styling;

/// <summary>
/// CSS Backgrounds Level 4 box extensions (<c>border-area</c> and combined <c>&lt;box&gt; text</c> clips)
/// plus inline compositing registration.
/// </summary>
internal static class CssBackgroundProperties
{
    internal const CssBackgroundBox BorderArea = (CssBackgroundBox)4;
    internal const CssBackgroundBox BorderAreaText = (CssBackgroundBox)5;
    internal const CssBackgroundBox BorderBoxText = (CssBackgroundBox)6;
    internal const CssBackgroundBox PaddingBoxText = (CssBackgroundBox)7;
    internal const CssBackgroundBox ContentBoxText = (CssBackgroundBox)8;

    private static int s_metadataRegistered;

    internal static bool IsTextClip(CssBackgroundBox clip) => clip is
        CssBackgroundBox.Text or
        BorderAreaText or
        BorderBoxText or
        PaddingBoxText or
        ContentBoxText;

    internal static CssBackgroundBox BoxPart(CssBackgroundBox clip) => clip switch
    {
        CssBackgroundBox.Text or BorderBoxText => CssBackgroundBox.Border,
        BorderAreaText => BorderArea,
        PaddingBoxText => CssBackgroundBox.Padding,
        ContentBoxText => CssBackgroundBox.Content,
        _ => clip,
    };

    internal static CssBackgroundBox CombineWithText(CssBackgroundBox box) => box switch
    {
        BorderArea => BorderAreaText,
        CssBackgroundBox.Padding => PaddingBoxText,
        CssBackgroundBox.Content => ContentBoxText,
        _ => BorderBoxText,
    };

    internal static void Register()
    {
        if (Interlocked.Exchange(ref s_metadataRegistered, 1) == 0)
        {
            static void OnInlineCompositingChanged(DependencyObject owner, DependencyPropertyChangedEventArgs _)
            {
                if (owner is not ContentElement) return;
                var node = CssNode.Get(owner);
                var createsGroup = (owner.GetValue(CssCompositingProperties.MixProperty) is CssBlendMode mix && mix != CssBlendMode.Normal)
                    || owner.GetValue(CssCompositingProperties.IsolationProperty) is true;
                if (createsGroup)
                    node.CssLayout ??= new();
                if (node.VisualOwner is UIElement visual)
                {
                    visual.InvalidateMeasure();
                    visual.InvalidateVisual();
                }
            }

            CssCompositingProperties.MixProperty.OverrideMetadata(
                typeof(ContentElement),
                new PropertyMetadata(CssBlendMode.Normal, OnInlineCompositingChanged));
            CssCompositingProperties.IsolationProperty.OverrideMetadata(
                typeof(ContentElement),
                new PropertyMetadata(false, OnInlineCompositingChanged));
        }
    }

    internal static bool TryReadClipValue(ref CssTokenReader reader, out CssBackgroundBox box)
    {
        var probe = reader;
        if (!TryReadSingleClipBox(ref probe, out var first))
        {
            box = default;
            return false;
        }

        var secondProbe = probe;
        if (TryReadSingleClipBox(ref secondProbe, out var second))
        {
            if (first == CssBackgroundBox.Text && second == BorderArea)
            {
                box = CombineWithText(second);
                reader = secondProbe;
                return true;
            }

            if (second == CssBackgroundBox.Text && first == BorderArea)
            {
                box = CombineWithText(first);
                reader = secondProbe;
                return true;
            }
        }

        box = first;
        reader = probe;
        return true;
    }

    internal static bool TryReadSingleClipBox(ref CssTokenReader reader, out CssBackgroundBox box)
    {
        var probe = reader;
        if (!probe.TryReadIdent(out var ident))
        {
            box = default;
            return false;
        }

        if (ident.Equals("border-box", StringComparison.OrdinalIgnoreCase))
        {
            box = CssBackgroundBox.Border;
            reader = probe;
            return true;
        }
        if (ident.Equals("padding-box", StringComparison.OrdinalIgnoreCase))
        {
            box = CssBackgroundBox.Padding;
            reader = probe;
            return true;
        }
        if (ident.Equals("content-box", StringComparison.OrdinalIgnoreCase))
        {
            box = CssBackgroundBox.Content;
            reader = probe;
            return true;
        }
        if (ident.Equals("border-area", StringComparison.OrdinalIgnoreCase))
        {
            box = BorderArea;
            reader = probe;
            return true;
        }
        if (ident.Equals("text", StringComparison.OrdinalIgnoreCase) ||
            ident.Equals("-webkit-text", StringComparison.OrdinalIgnoreCase))
        {
            box = CssBackgroundBox.Text;
            reader = probe;
            return true;
        }

        box = default;
        return false;
    }
}

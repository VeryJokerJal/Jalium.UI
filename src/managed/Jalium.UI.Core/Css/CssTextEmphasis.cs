using System.Globalization;
using System.Text;
using Jalium.UI.Media;

namespace Jalium.UI.Styling;

internal enum CssEmphasisShape : byte { None, Dot, Circle, DoubleCircle, Triangle, Sesame, Custom }
internal readonly record struct CssEmphasisStyle(CssEmphasisShape Shape,bool Open=false,string? Custom=null)
{
    internal string Mark => Shape switch
    {
        CssEmphasisShape.Dot => Open?"◦":"•",
        CssEmphasisShape.Circle => Open?"○":"●",
        CssEmphasisShape.DoubleCircle => Open?"◎":"◉",
        CssEmphasisShape.Triangle => Open?"△":"▲",
        CssEmphasisShape.Sesame => Open?"﹆":"﹅",
        CssEmphasisShape.Custom => Custom??"",
        _ => "",
    };
}
[Flags]
internal enum CssEmphasisPosition : byte { OverRight=0, Under=1, Left=2 }

internal static class CssTextEmphasis
{
    internal static readonly DependencyProperty StyleProperty=DependencyProperty.RegisterAttached(
        "CssTextEmphasisStyle",typeof(CssEmphasisStyle),typeof(CssTextEmphasis),
        new FrameworkPropertyMetadata(default(CssEmphasisStyle),FrameworkPropertyMetadataOptions.Inherits|
            FrameworkPropertyMetadataOptions.AffectsMeasure|FrameworkPropertyMetadataOptions.AffectsRender,MetricsChanged));
    internal static readonly DependencyProperty ColorProperty=DependencyProperty.RegisterAttached(
        "CssTextEmphasisColor",typeof(Brush),typeof(CssTextEmphasis),
        new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.Inherits|FrameworkPropertyMetadataOptions.AffectsRender,PaintChanged));
    internal static readonly DependencyProperty PositionProperty=DependencyProperty.RegisterAttached(
        "CssTextEmphasisPosition",typeof(CssEmphasisPosition),typeof(CssTextEmphasis),
        new FrameworkPropertyMetadata(CssEmphasisPosition.OverRight,FrameworkPropertyMetadataOptions.Inherits|
            FrameworkPropertyMetadataOptions.AffectsMeasure|FrameworkPropertyMetadataOptions.AffectsRender,MetricsChanged));
    // Keep symbolic currentColor through ordinary DP inheritance, including
    // descendants with no authored emphasis declaration of their own.
    internal static readonly DependencyProperty ColorExpressionProperty=DependencyProperty.RegisterAttached(
        "CssTextEmphasisColorExpression",typeof(string),typeof(CssTextEmphasis),
        new FrameworkPropertyMetadata("currentColor",FrameworkPropertyMetadataOptions.Inherits|FrameworkPropertyMetadataOptions.AffectsRender,PaintChanged));
    internal static CssEmphasisStyle Style(DependencyObject owner)=>(CssEmphasisStyle)owner.GetValue(StyleProperty)!;
    internal static CssEmphasisPosition Position(DependencyObject owner)=>(CssEmphasisPosition)owner.GetValue(PositionProperty)!;
    internal static Brush? Color(DependencyObject owner)
    {
        var brush=owner.GetValue(ColorProperty) as Brush;
        var expression=owner.GetValue(ColorExpressionProperty) as string;
        if(expression is null)return brush;
        var foreground=CssUsedForeground.Get(owner);
        if(expression.Equals("currentColor",StringComparison.OrdinalIgnoreCase))return foreground??brush;
        if(!CssColorParser.TryResolveCurrentColor(expression,(foreground as SolidColorBrush)?.Color??Colors.Black,out var color))return brush;
        if(brush is SolidColorBrush solid&&solid.Color==color)return brush;
        var resolved=new SolidColorBrush(color);resolved.Freeze();return resolved;
    }
    internal static bool Under(DependencyObject owner)=>(Position(owner)&CssEmphasisPosition.Under)!=0;
    private static void MetricsChanged(DependencyObject owner,DependencyPropertyChangedEventArgs _)
        => FontWidthRenderingSource.Invalidate(CssNode.Get(owner));
    private static void PaintChanged(DependencyObject owner,DependencyPropertyChangedEventArgs _)
    {
        if(owner is UIElement visual)visual.InvalidateVisual();
        else if(owner is Jalium.UI.Documents.TextElement text)text.NotifyTextContentChanged();
    }

    internal static bool ShouldMark(ReadOnlySpan<char> character)
    {
        if(character.IsEmpty||!Rune.TryGetRuneAt(character.ToString(),0,out var first))return false;
        var category=Rune.GetUnicodeCategory(first);
        if(category is UnicodeCategory.Control or UnicodeCategory.Format or UnicodeCategory.OtherNotAssigned)return false;
        if(category is UnicodeCategory.SpaceSeparator or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
        {
            foreach(var rune in character.EnumerateRunes())
                if(Rune.GetUnicodeCategory(rune) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)return true;
            return false;
        }
        if(category is UnicodeCategory.ConnectorPunctuation or UnicodeCategory.DashPunctuation or UnicodeCategory.OpenPunctuation or
            UnicodeCategory.ClosePunctuation or UnicodeCategory.InitialQuotePunctuation or UnicodeCategory.FinalQuotePunctuation or UnicodeCategory.OtherPunctuation)
        {
            // CSS Text Decoration 3 retains symbol-like punctuation, including
            // compatibility forms whose NFKD decomposition is one of these symbols.
            const string symbols="#%‰‱٪؉؊&⁊@§¶⁋⁓〽";
            foreach(var rune in first.ToString().Normalize(NormalizationForm.FormKD).EnumerateRunes())
                if(symbols.Contains(rune.ToString(),StringComparison.Ordinal))return true;
            return false;
        }
        return true;
    }
}

using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Controls;

internal static class CssTextEmphasisPainter
{
    internal sealed record Mark(FormattedText Text,double Height,double Ascent,double Descent,bool Under);

    internal static Mark? Create(DependencyObject source,double fallbackSize,Brush? fallbackBrush)
    {
        var value=CssTextEmphasis.Style(source).Mark;
        if(value.Length==0)return null;
        object? Property(string name)=>CssDependencyPropertyLookup.Find(source.GetType(),name) is { } property?source.GetValue(property):null;
        var size=Property("FontSize") is double authored?authored:fallbackSize;
        if(!double.IsFinite(size)||size<=0)return null;
        var family=(Property("FontFamily") as FontFamily)?.GetRenderingSource(source)??FrameworkElement.DefaultFontFamilyName;
        var ruby=new FontFeature(FontFeature.TagOf("ruby"),1);
        family=FontFeatureRenderingSource.TryUnwrap(family,out var features,out var baseFamily,out var caps,out var fontContext)
            ?FontFeatureRenderingSource.Wrap(baseFamily,FontFeatureRenderingSource.Merge(features,[ruby]),caps,fontContext)
            :FontFeatureRenderingSource.Wrap(family,[ruby]);
        var weight=Property("FontWeight") is FontWeight fw?fw.ToOpenTypeWeight():400;
        var style=Property("FontStyle") is FontStyle fs?fs.ToOpenTypeStyle():0;
        var text=new FormattedText(value,family,size*.5){FontWeight=weight,FontStyle=style,
            Foreground=CssTextEmphasis.Color(source)??Property("Foreground") as Brush??fallbackBrush??Brushes.Black};
        TextMeasurement.MeasureText(text);
        var metrics=TextMeasurement.GetFontMetrics((Property("FontFamily") as FontFamily)?.GetRenderingSource(source)??FrameworkElement.DefaultFontFamilyName,size,weight,style);
        return new(text,Math.Max(text.Height,text.Ascent+text.Descent),metrics.Ascent,metrics.Descent,CssTextEmphasis.Under(source));
    }

    internal static void Draw(DrawingContext context,Mark mark,double center,double baseline)
    {
        var top=mark.Under?baseline+mark.Descent:baseline-mark.Ascent-mark.Height;
        context.DrawText(mark.Text,new Point(center-mark.Text.WidthIncludingTrailingWhitespace/2,top));
    }
}

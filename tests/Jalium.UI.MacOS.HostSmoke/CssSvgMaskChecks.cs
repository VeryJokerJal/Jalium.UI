using System.Text;
using Jalium.UI.Controls;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;
using Jalium.UI.Threading;

namespace Jalium.UI.MacOS;

internal static partial class CssWritingToolsChecks
{
    private static MaskCase SvgCase(string name, string masks, string attributes, Func<double, double, int, double> alpha,
        string extra = "", string shared = "")
    {
        var xml = $"<svg xmlns='http://www.w3.org/2000/svg'><mask id='m' {attributes}>{masks}</mask></svg>";
        var url = "data:image/svg+xml;base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(xml)) + "#m";
        extra = extra.Replace("SVG_PLACEHOLDER", url);
        return new(name, $"mask-image:url('{url}');{extra}", alpha, SharedCss: shared, ExpandedReference: true);
    }
    private static double SvgInside(double x, double y, int width) => x>=0&&x<width&&y>=0&&y<60?1:0;
    private static MaskCase[] SvgMaskCases() => [
        SvgCase("default-luminance", "<rect width='100%' height='100%' fill='red'/>", "", (x,y,w)=>SvgInside(x,y,w)*54/255d),
        SvgCase("alpha-type", "<rect width='100%' height='100%' fill='black' opacity='.5'/>", "mask-type='alpha'", (x,y,w)=>SvgInside(x,y,w)*128/255d),
        SvgCase("inline-type", "<rect width='100%' height='100%' fill='black'/>", "mask-type='luminance' style='mask-type:alpha'", SvgInside),
        SvgCase("mode-override", "<rect width='100%' height='100%' fill='black'/>", "mask-type='luminance'", SvgInside, "mask-mode:alpha"),
        SvgCase("linear-rgb", "<rect width='100%' height='100%' fill='#808080'/>", "color-interpolation='linearRGB'", (x,y,w)=>SvgInside(x,y,w)*55/255d),
        SvgCase("content-box", "<rect width='.5' height='1' fill='white'/>", "maskContentUnits='objectBoundingBox'", (x,y,w)=>SvgInside(x,y,w)*(x<w/2d?1:0)),
        SvgCase("user-space", "<rect width='40' height='60' fill='white'/>", "", (x,y,w)=>SvgInside(x,y,w)*(x<40?1:0)),
        SvgCase("region-box", "<rect width='100%' height='100%' fill='white'/>", "maskUnits='objectBoundingBox' x='.25' y='0' width='.5' height='1'", (x,y,w)=>SvgInside(x,y,w)*(x>=w*.25&&x<w*.75?1:0)),
        SvgCase("region-user", "<rect width='100%' height='100%' fill='white'/>", "maskUnits='userSpaceOnUse' x='10' y='10' width='40' height='40'", (x,y,w)=>x>=10&&x<50&&y>=10&&y<50?1:0),
        SvgCase("tiling-ignored", "<rect width='.5' height='1' fill='white'/>", "maskContentUnits='objectBoundingBox'", (x,y,w)=>SvgInside(x,y,w)*(x<w/2d?1:0), "mask-position:right bottom;mask-size:1px;mask-repeat:space;mask-clip:content-box;padding:10px"),
        SvgCase("group-opacity", "<g opacity='.5'><rect width='100%' height='100%' fill='white'/><rect width='100%' height='100%' fill='white'/></g>", "", (x,y,w)=>SvgInside(x,y,w)*128/255d),
        SvgCase("gradient", "<defs><linearGradient id='g'><stop stop-color='white'/><stop offset='1' stop-color='black'/></linearGradient></defs><rect width='100%' height='100%' fill='url(#g)'/>", "", (x,y,w)=>SvgInside(x,y,w)*Math.Round(255*(1-x/w))/255),
        SvgCase("combined", "<rect width='100%' height='100%' fill='white' opacity='.5'/>", "", (x,y,w)=>SvgInside(x,y,w)*64/255d, "mask-image:url('SVG_PLACEHOLDER'),image(rgb(0 0 0 / .5));mask-composite:intersect"),
        SvgCase("filter", "<rect width='100%' height='100%' fill='white' opacity='.5'/>", "", (x,y,w)=>SvgInside(x,y,w)*128/255d, shared:"filter:brightness(.5)"),
        SvgCase("clip", "<rect width='100%' height='100%' fill='white' opacity='.5'/>", "", (x,y,w)=>SvgInside(x,y,w)*128/255d, shared:"clip-path:polygon(0 0,100% 0,0 100%)"),
        SvgCase("zero-region", "<rect width='100%' height='100%' fill='white'/>", "width='0'", (x,y,w)=>0),
        SvgCase("cycle", "<g mask='url(#m)'><rect width='100%' height='100%' fill='white'/></g>", "", (x,y,w)=>0),
        SvgCase("missing", "", "", (x,y,w)=>0),
    ];

    internal static int RunSvgMasks() => RunMasksCore(SvgMaskCases(), "svg-mask");

    private static void PrepareSvgMaskResources(FrameworkElement view)
    {
        var resources = new List<CssBackgroundImageResource>();
        void Visit(Visual visual)
        {
            if (visual.GetValue(CssMaskProperties.ImagesProperty) is CssMaskImages masks) resources.AddRange(masks.Resources);
            for (var i=0;i<visual.InternalVisualChildrenCount;i++) if(visual.InternalGetVisualChild(i) is {} child) Visit(child);
        }
        Visit(view);
        foreach(var resource in resources.Distinct())
        {
            var end=DateTime.UtcNow.AddSeconds(10);
            while(!resource.Ready.IsCompleted&&DateTime.UtcNow<end){Dispatcher.CurrentDispatcher.ProcessQueue();BitmapDecodeNotifier.FlushPendingForTests();Thread.Sleep(1);}
            if(!resource.Ready.IsCompleted)throw new TimeoutException("SVG mask resource did not become ready");
            Dispatcher.CurrentDispatcher.ProcessQueue();CssEvaluationScheduler.FlushIfPending(view.Dispatcher);
        }
    }
}

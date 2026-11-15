namespace Jalium.UI.MacOS;

internal static partial class CssWritingToolsChecks
{
    private static MaskCase[] SvgStyleCases() => [
        SvgCase("sheet-mask-type", "<style>mask{mask-type:alpha}rect{fill:black;opacity:.5}</style><rect width='100%' height='100%'/>", "", (x,y,w)=>SvgInside(x,y,w)*128/255d),
        SvgCase("sheet-variable", "<style>mask{--paint:white}rect{fill:var(--paint)}</style><rect width='100%' height='100%'/>", "", SvgInside),
        SvgCase("sheet-important", "<style>rect{fill:white!important}</style><rect width='100%' height='100%' fill='black' style='fill:red'/>", "", SvgInside),
        SvgCase("sheet-layer", "<style>@layer first,second;@layer first{rect{fill:white!important}}@layer second{rect{fill:red!important}}</style><rect width='100%' height='100%'/>", "", SvgInside),
        SvgCase("sheet-revert", "<style>@layer first{rect{fill:white}}@layer second{rect{fill:red;fill:var(--missing,revert-layer)}}</style><rect width='100%' height='100%'/>", "", SvgInside),
        SvgCase("sheet-geometry", "<style>rect{width:50%;height:100%;fill:white}</style><rect width='1' height='1'/>", "maskContentUnits='objectBoundingBox'", (x,y,w)=>SvgInside(x,y,w)*(x<w/2d?1:0)),
        SvgCase("sheet-selector", "<style>@namespace s url(http://www.w3.org/2000/svg);s|mask > s|rect:is(.stencil):not(.off){fill:white}</style><rect class='stencil' width='100%' height='100%' fill='black'/>", "", SvgInside),
        SvgCase("sheet-media", "<style>rect{fill:white}@media(min-width:100px){rect{fill:red}}</style><rect width='100%' height='100%'/>", "", (x,y,w)=>SvgInside(x,y,w)*(w<100?1:54/255d)),
        SvgCase("sheet-opacity-math", "<style>rect{fill:white;opacity:calc(.2 + .3)}</style><rect width='100%' height='100%'/>", "", (x,y,w)=>SvgInside(x,y,w)*128/255d),
        SvgCase("sheet-color-alpha", "<style>rect{fill:#fff8}</style><rect width='100%' height='100%'/>", "", (x,y,w)=>SvgInside(x,y,w)*136/255d),
        SvgCase("sheet-gradient", "<style>stop{stop-color:rgb(255 255 255 / 50%);stop-opacity:50%}</style><defs><linearGradient id='g'><stop/><stop offset='1'/></linearGradient></defs><rect width='100%' height='100%' fill='url(#g)'/>", "", (x,y,w)=>SvgInside(x,y,w)*64/255d),
        SvgCase("sheet-use", "<style>.paint{fill:var(--paint,currentColor)}</style><defs><rect id='half' class='paint' width='40' height='60'/></defs><use href='#half' style='--paint:white'/>", "", (x,y,w)=>SvgInside(x,y,w)*(x<40?1:0)),
        SvgCase("sheet-no-paint", "<style>rect{fill:var(--missing,none)}</style><rect width='100%' height='100%'/>", "", (x,y,w)=>0),
        SvgCase("sheet-registered", "<style>@property --fade{syntax:'&lt;number&gt;';inherits:false;initial-value:.25}rect{fill:white;opacity:var(--fade)}</style><rect width='100%' height='100%'/>", "", (x,y,w)=>SvgInside(x,y,w)*64/255d),
    ];
    internal static int RunSvgStyles() => RunMasksCore(SvgStyleCases(), "svg-style");
}

using System.Text.Json;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;
namespace Jalium.UI.MacOS;
internal static partial class CssWritingToolsChecks
{
    private sealed record EmojiCase(string Name, string Mode, string First, string Last, string PaintedFirst, string PaintedLast, string Extra = "");
    internal static int RunFontEmoji()
    {
        var pool=PushPool();var app=Send(Class("NSApplication"),Selector("sharedApplication"));
        Integer(app,Selector("setActivationPolicy:"),1);Send(app,Selector("finishLaunching"));Directory.CreateDirectory(Output);
        using var context=RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        var rows=new[]{
            new EmojiCase("emoji","emoji","♥⌚","☀©","♥\ufe0f⌚\ufe0f","☀\ufe0f©\ufe0f"),
            new EmojiCase("text","text","♥⌚","☀©","♥\ufe0e⌚\ufe0e","☀\ufe0e©\ufe0e"),
            new EmojiCase("unicode","unicode","♥⌚","☀©","♥\ufe0e⌚\ufe0f","☀\ufe0e©\ufe0e"),
            new EmojiCase("explicit-text","emoji","♥\ufe0e","⌚\ufe0e","♥\ufe0e","⌚\ufe0e"),
            new EmojiCase("explicit-emoji","text","♥\ufe0f","⌚\ufe0f","♥\ufe0f","⌚\ufe0f"),
            new EmojiCase("keycap-combining","emoji","1\u20e3","♥\u0301","1\ufe0f\u20e3","♥\ufe0f\u0301"),
            new EmojiCase("unaffected","text","A😀","𐐨","A😀","𐐨"),
            new EmojiCase("caps","emoji","aß","♥⌚","aß","♥\ufe0f⌚\ufe0f","font-variant-caps:all-small-caps;font-feature-settings:'smcp' off;"),
            new EmojiCase("caps-participating","emoji","ℹ","Ⓜ","ℹ\ufe0f","Ⓜ\ufe0f","font-variant-caps:all-small-caps;font-feature-settings:'smcp' off;"),
            new EmojiCase("caps-cased-emoji","emoji","ℹ","a","ℹ\ufe0f","a","font-variant-caps:all-small-caps;font-feature-settings:'smcp' off;"),
            new EmojiCase("combined","emoji","aß123","♥","aß1\ufe0f2\ufe0f3\ufe0f","♥\ufe0f","font-variant-caps:small-caps;font-variant-position:super;font-variant-numeric:oldstyle-nums;"),
            new EmojiCase("position","emoji","aß","♥⌚","aß","♥\ufe0f⌚\ufe0f","font-variant-position:super;"),
            new EmojiCase("cross-selector","emoji","♥","\ufe0e","♥","\ufe0e"),
            new EmojiCase("cross-normal","normal","⌚","\ufe0e","⌚","\ufe0e"),
            new EmojiCase("zwj","emoji","👁\u200d","🗨","👁\ufe0f\u200d","🗨\ufe0f"),
        };
        var records=new List<object>();int failed=0;
        try {
            foreach(var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })foreach(int dpi in new[]{1,2})foreach(int width in new[]{40,80,240})
            foreach(var row in rows)foreach(string path in new[]{"text","run","span","shared","edge"}) {
                context.DefaultRenderingEngine=engine;string label=$"font-emoji-{engine}-{dpi}-{width}-{row.Name}-{path}";
                var errors=new List<string>();int differentBytes=-1;
                try {
                    byte[] Capture(FrameworkElement view,int size) {
                        view.SetRootDpi(new(dpi,dpi));for(int i=0;i<3;++i){view.Measure(new(size,280));view.Arrange(new(0,0,size,280));view.UpdateLayout();}
                        var bitmap=MacOSWritingToolsBitmapCapture.Capture(size*dpi,280*dpi,dpi,dc=>view.Render(dc),()=>true)??throw new Exception("Emoji readback failed");
                        if(context.Backend!=RenderBackend.Metal||context.DefaultRenderingEngine!=engine||!ReferenceEquals(RenderContext.Current,context))throw new Exception("Emoji backend/engine changed");return Pixels(bitmap);
                    }
                    var sources=new List<(TextBlock Text,string Source)>();var runs=new List<(Run Run,string Source,DependencyObject? Parent)>();
                    FrameworkElement Build(bool reference) {
                        string first=reference?row.PaintedFirst:row.First,last=reference?row.PaintedLast:row.Last;
                        FrameworkElement root;
                        if(path is "shared" or "edge") {
                            var flow=new StackPanel();var a=new TextBlock{Text=first,IsTextSelectionEnabled=true};var b=new TextBlock{Text=last,IsTextSelectionEnabled=true};
                            flow.Children.Add(a);flow.Children.Add(b);Css.SetStyleSheet(flow,"TextBlock{display:inline}");root=flow;
                            if(path=="edge")Css.SetStyle(b,"padding-left:1px;");
                            if(!reference){sources.Add((a,first));sources.Add((b,last));}
                        } else {
                            var text=new TextBlock{IsTextSelectionEnabled=true};root=text;
                            if(path=="text")text.Text=first+last;
                            else {
                                var a=new Run(first);var b=new Run(last);text.Inlines.Add(a);if(path=="span")text.Inlines.Add(new Span(b));else text.Inlines.Add(b);
                                if(!reference){runs.Add((a,first,a.Parent));runs.Add((b,last,b.Parent));}
                            }
                            if(!reference)sources.Add((text,first+last));
                        }
                        Css.SetStyle(root,$"display:block;font-family:Arial;font-size:32px;line-height:44px;color:#146342;white-space:pre-wrap;word-break:break-all;text-autospace:no-autospace;font-variant-emoji:{(reference?"normal":row.Mode)};"+row.Extra);
                        return root;
                    }
                    var actual=Build(false);var reference=Build(true);
                    var changed=Capture(actual,width);var expected=Capture(reference,width);
                    differentBytes=changed.Zip(expected).Count(pair=>pair.First!=pair.Second);
                    Save(changed,width*dpi,280*dpi,label+"-changed.png");Save(expected,width*dpi,280*dpi,label+"-expected.png");
                    if(differentBytes!=0)throw new Exception($"Emoji paint differs from authored-selector reference ({differentBytes} bytes)");
                    foreach(var (text,source) in sources){text.SelectAll();if(text.SelectedText!=source)throw new Exception("Emoji changed selected source");text.Select(0,0);}
                    foreach(var (run,source,parent) in runs)if(run.Text!=source||!ReferenceEquals(run.Parent,parent))throw new Exception("Emoji replaced a run");
                    Capture(actual,400);if(!Capture(actual,width).SequenceEqual(changed))throw new Exception("Emoji resize retained stale context");
                    var css=Css.GetStyle(actual);Css.SetStyle(actual,css+"font-variant-emoji:normal;");Capture(actual,width);
                    Css.SetStyle(actual,css);if(!Capture(actual,width).SequenceEqual(changed))throw new Exception("Emoji repeated style change retained stale text");
                }catch(Exception error){errors.Add(error.ToString());failed++;}
                records.Add(new{label,backend=context.Backend.ToString(),engine=engine.ToString(),differentBytes,errors});Console.WriteLine((errors.Count==0?"PASS ":"FAIL ")+label+" "+string.Join(';',errors));
            }
            File.WriteAllText(Path.Combine(Output,"font-emoji.json"),JsonSerializer.Serialize(new{count=records.Count,failed,records,nativeLibraries=NativeLibraries(),physicalInput=false,voiceOver=false}));return failed==0?0:1;
        }finally{PopPool(pool);}
    }
}

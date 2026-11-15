using System.Text.Json;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Media.Effects;
using Jalium.UI.Styling;

namespace Jalium.UI.MacOS;

internal static partial class CssWritingToolsChecks
{
    private sealed record SelectionCase(string Name, string Text, int Start, int Length, string Ordinary = "", string Highlight = "color:#fff8de;background-color:#20537a;",
        Color? Background = null, string Selected = "color:#fff8de;", bool Rtl = false, bool Runs = false, bool OriginalShadow = false, bool HighlightShadow = false, bool Underline = false, double DecorationThickness = 1, Color? DecorationColor = null, bool Nested = false);
    private sealed record SelectionScene(StackPanel Root, TextBlock Text);
    private static readonly Color SelectionPaper = Color.FromRgb(0xf1, 0xf5, 0xf1);
    private static readonly Color SelectionBlue = Color.FromRgb(0x20, 0x53, 0x7a);
    private static SelectionCase[] SelectionCases() =>
    [
        new("all", "Office affinity, flowing selection and ordinary text.", 0, 500),
        new("partial", "Office affinity, flowing selection and ordinary text.", 2, 24),
        new("alpha", "Transparent selected glyphs preserve the original shaping.", 3, 31,
            Highlight: "color:rgba(255,248,222,.55);background-color:rgba(32,83,122,.45);", Background: Color.FromArgb(115,32,83,122), Selected: "color:rgba(255,248,222,.55);"),
        new("arabic", "العربية متصلة وتبقى الحروف في مواضعها office 123", 2, 30, Rtl: true),
        new("mixed-bidi", "אבגד office affinity 123 العربية זחט", 2, 27, Rtl: true),
        new("clusters", "A e\u0301 family 👨‍👩‍👧‍👦 office 🇨🇳 with 中文字形", 2, 30),
        new("runs", "Office affinity continues across source runs.", 1, 34, Runs: true),
        new("nested-highlight", "Office affinity continues across source runs.", 1, 34, Runs: true, Nested: true),
        new("first-line", "Office affinity continues through another line of ordinary text.", 0, 45,
            Ordinary: ".subject::first-line{color:#146342;font-size:20px;}"),
        new("first-letter", "A selection starts at the decorated first letter.", 0, 30,
            Ordinary: ".subject::first-letter{font-size:28px;color:#146342;}"),
        new("initial", "A selection flows around the dropped initial and through another line of text.", 0, 54,
            Ordinary: ".subject::first-letter{initial-letter:3;color:#146342;}"),
        new("underline", "Underline is added only to the selected portion.", 4, 30,
            Highlight: "color:#fff8de;background:#20537a;text-decoration:underline;text-decoration-skip-inset:none;", Selected: "color:#fff8de;", Underline: true),
        new("transparent-source", "Selection can reveal originally transparent text without retaining its alpha.", 0, 500,
            Ordinary: ".subject{color:rgba(35,60,50,.2);}"),
        new("decoration-color", "The highlight underline keeps its own author color.", 4, 30,
            Highlight: "color:#fff8de;background:#20537a;text-decoration:underline red;text-decoration-thickness:2px;text-decoration-skip-inset:none;",
            Underline: true, DecorationThickness: 2, DecorationColor: Colors.Red),
        new("original-shadow", "The original red shadow stays beneath the selected background.", 3, 34,
            Ordinary: ".subject{text-shadow:2px 4px 0 red;}", OriginalShadow: true),
        new("highlight-shadow", "The selected green shadow stays above the background and below the ink.", 3, 34,
            Highlight: "color:#fff8de;background:#20537a;text-shadow:3px 6px 0 lime;", HighlightShadow: true),
        new("both-shadows", "Both shadow layers preserve their order through selection.", 3, 34,
            Ordinary: ".subject{text-shadow:2px 4px 0 red;}", OriginalShadow: true,
            Highlight: "color:#fff8de;background:#20537a;text-shadow:3px 6px 0 lime;", HighlightShadow: true),
        new("background-only", "The ordinary ink survives when only the highlight background is authored.", 0, 500,
            Highlight: "background:#20537a;", Selected: "color:#233c32;")
    ];
    private static SelectionScene MakeSelectionScene(SelectionCase row, int width, bool actual, bool colored, bool nestedColors = true, string extra = "")
    {
        var root = new StackPanel { Width = width, Height = 180 };
        Css.SetStyle(root, "display:block;font-family:Georgia;font-size:16px;line-height:24px;color:#233c32;");
        var text = new TextBlock { IsTextSelectionEnabled = true, FlowDirection = row.Rtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight };
        Css.SetClass(text, "subject"); Css.SetStyle(text, "display:block;white-space:pre-wrap;"); root.Children.Add(text);
        if (row.Runs)
        {
            var first = row.Nested ? 7 : 3; var end = row.Nested ? 16 : 8;
            text.Inlines.Add(new Run(row.Text[..first])); var middle = new Run(row.Text[first..end]); Css.SetClass(middle,"emphasis");
            text.Inlines.Add(middle); text.Inlines.Add(new Run(row.Text[end..]));
        }
        else text.Text = row.Text;
        var rules = row.Ordinary;
        if (!actual && row.OriginalShadow) rules += ".subject{text-shadow:none;}";
        if (actual) rules += ".subject::selection{" + row.Highlight + "}" + (row.Nested ? ".subject Run.emphasis::selection{color:red;background:lime;}" : "");
        if (colored)
        {
            var subjects = ".subject, .subject Run";
            if (row.Ordinary.Contains("::first-line", StringComparison.Ordinal)) subjects += ", .subject::first-line";
            if (row.Ordinary.Contains("::first-letter", StringComparison.Ordinal)) subjects += ", .subject::first-letter";
            rules += subjects + "{" + row.Selected + "}";
            if (row.Nested && nestedColors) rules += ".subject Run.emphasis{color:red;}";
        }
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(rules + extra)); FirstLetterLayout(root, width, 180);
        if (actual) text.Select(row.Start, Math.Min(row.Length, row.Text.Length - row.Start));
        return new(root, text);
    }
    private static void SelectionReferenceShadow(DrawingContext context, TextBlock white, Geometry? clip, int width, Color color, double x, double y)
    {
        // Ordinary white glyphs and the established effect API form an
        // independent alpha-mask reference. No highlight paint/snapshot is used.
        var effect = new DropShadowEffect { Color = color, Opacity = 1, BlurRadius = 0, CssUsesAlphaMask = true, CssGaussianSigma = 0,
            ShadowDepth = Math.Sqrt(x*x+y*y), Direction = (Math.Atan2(-y,x)*180/Math.PI+360)%360 };
        var capture = (IEffectDrawingContext)context; var offset = (context as IOffsetDrawingContext)?.Offset ?? default;
        var left = (float)Math.Floor(offset.X - 12); var top = (float)Math.Floor(offset.Y - 12);
        capture.BeginEffectCapture(left,top,width+24,204);
        if (clip is not null) context.PushClip(clip);
        white.Render(context);
        if (clip is not null) context.Pop();
        capture.EndEffectCapture(); capture.ApplyCssTextShadowsOnly(effect,left,top,width+24,204,left,top);
    }
    private sealed record SelectionEditor(FrameworkElement Control, Action<bool> Select, Func<string> Source);
    private static SelectionEditor MakeSelectionEditor(string kind, int width, bool actual, bool colored)
    {
        const string value = "Office affinity continues through selected text.";
        var root = new StackPanel { Width = width, Height = 180 };
        SelectionEditor result;
        if (kind == "password")
        {
            var control = new PasswordBox { Password = value, Width = width, Height = 72, Background = null,
                BorderThickness = default, Padding = default, SelectionTextBrush = null, IsInactiveSelectionHighlightEnabled = true };
            result = new(control, active => control.Select(active ? 2 : 0, active ? 24 : 0), () => control.Password);
            if (!actual) control.SelectionBrush = Brushes.Black;
        }
        else if (kind == "text")
        {
            var control = new TextBox { Text = value, Width = width, Height = 72, Background = null,
                BorderThickness = default, Padding = default, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, IsInactiveSelectionHighlightEnabled = true };
            result = new(control, active => control.Select(active ? 2 : 0, active ? 24 : 0), () => control.Text);
            if (!actual) control.SelectionBrush = Brushes.Black;
        }
        else
        {
            var run = new Run(value); var paragraph = new Paragraph(run); var document = new FlowDocument(paragraph);
            var control = new RichTextBox { Document = document, Width = width, Height = 72, Background = null,
                BorderThickness = default, Padding = default, IsReadOnly = true };
            result = new(control, active => control.Selection.Select(run.ContentStart.GetPositionAtOffset(active ? 2 : 0)!, run.ContentStart.GetPositionAtOffset(active ? 26 : 0)!), () =>
            {
                if (!ReferenceEquals(run.Parent, paragraph) || !ReferenceEquals(control.Document, document)) throw new Exception("Rich selection source identity changed");
                return document.GetText().TrimEnd('\n');
            });
            if (!actual) control.SelectionBrush = Brushes.Black;
            if (kind == "rich-shared") Css.SetStyle(paragraph, "display:flex;");
        }
        if (result.Control is Control paintControl) paintControl.Foreground = new SolidColorBrush(colored ? Color.FromRgb(255,248,222) : Color.FromRgb(35,60,50));
        if (result.Control is RichTextBox rich)
        {
            rich.Document.Foreground = new SolidColorBrush(colored ? Color.FromRgb(255,248,222) : Color.FromRgb(35,60,50));
            foreach (var paragraph in rich.Document.Blocks.OfType<Paragraph>()) foreach (var run in paragraph.Inlines.OfType<Run>())
                run.Foreground = rich.Document.Foreground;
        }
        root.Children.Add(result.Control); Css.SetClass(result.Control, "subject");
        Css.SetStyle(root, "font-family:Georgia;font-size:16px;line-height:24px;color:#233c32;");
        var rules = ".subject{font-family:Georgia;font-size:16px;line-height:24px;color:#233c32;} .subject FlowDocument{font-family:Georgia;font-size:16px;line-height:24px;color:#233c32;}";
        if (actual) rules += ".subject::selection{color:#fff8de;background:#20537a;}";
        if (colored) rules += ".subject, .subject Run, .subject FlowDocument{color:#fff8de;}";
        Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(rules)); FirstLetterLayout(root, width, 180);
        if (actual) result.Select(true);
        // Keep the style/layout owner alive through its child's parent link.
        return result;
    }
    private static void SelectionEditors(RenderContext context, RenderBackend backend, RenderingEngine engine, List<object> records, ref int failures)
    {
        foreach (var kind in SelectionFilter(new[] { "text", "password", "rich-native", "rich-shared" }, "JALIUM_CSS_SELECTION_EDITORS",value=>value))
        foreach (var width in new[] { 312, 600 }) foreach (var dpi in new[] { 1, 2 })
        {
            GC.Collect(); GC.WaitForPendingFinalizers();
            var actual = MakeSelectionEditor(kind,width,true,false); var normal = MakeSelectionEditor(kind,width,false,false); var colored = MakeSelectionEditor(kind,width,false,true);
            normal.Select(true); var geometry = new SelectionReferenceGeometry(); normal.Control.Render(geometry); normal.Select(false);
            var area = SelectionReferenceArea(geometry.Rectangles); var clip = new RectangleGeometry(new Rect(0,0,width,180));
            foreach (var recorded in new[] { false, true })
            {
                var label = $"selection-editor-{backend}-{engine}-{kind}-{width}-{dpi}-{recorded}"; var errors = new List<string>(); var largest=0; var differences=0;
                try
                {
                    byte[] Capture(Action<DrawingContext> draw) => Pixels(MacOSWritingToolsBitmapCapture.Capture(width*dpi,180*dpi,dpi,dc =>
                    {
                        dc.DrawRectangle(new SolidColorBrush(SelectionPaper),null,new Rect(0,0,width,180));
                        if(recorded){var recorder=new DrawingRecorder();recorder.BindWholeFrame();draw(recorder);DrawingReplayer.Replay(recorder.Commit(),dc);}else draw(dc);
                    },()=>true,context) ?? throw new Exception("Editor capture unavailable"));
                    var pixels=Capture(dc=>actual.Control.Render(dc));var reference=Capture(dc=>
                    {
                        dc.DrawGeometry(new SolidColorBrush(SelectionBlue),null,area);
                        dc.PushClip(new CombinedGeometry(GeometryCombineMode.Exclude,clip,area));normal.Control.Render(dc);dc.Pop();
                        dc.PushClip(area);colored.Control.Render(dc);dc.Pop();
                    });
                    for(var index=0;index<pixels.Length;index++){var delta=Math.Abs(pixels[index]-reference[index]);largest=Math.Max(largest,delta);if(delta>3)differences++;}
                    if(largest>3)errors.Add($"Maximum channel difference {largest}; {differences} channels differ");
                    if(actual.Source()!=normal.Source())errors.Add("Editor source changed");
                    if(largest>3 || !recorded && width==312 && dpi==1){Save(pixels,width*dpi,180*dpi,label+".png");Save(reference,width*dpi,180*dpi,label+"-expected.png");}
                }
                catch(Exception error){errors.Add(error.ToString());}
                if(errors.Count>0)failures++;records.Add(new{label,largest,differences,errors});Console.WriteLine($"{(errors.Count==0?"PASS":"FAIL")} {label} {string.Join(" | ",errors)}");
            }
        }
    }
    private static T[] SelectionFilter<T>(T[] values,string variable,Func<T,string> name)
    {
        if(Environment.GetEnvironmentVariable(variable)is not{}filter)return values;
        var names=filter.Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
        if(names.Any(requested=>!values.Any(value=>name(value)==requested)))throw new ArgumentException("Unknown selection case in "+variable);
        return values.Where(value=>names.Contains(name(value))).ToArray();
    }
    private static (Rect[] Rectangles, string Text) OrdinarySelectionRectangles(TextBlock text, int start, int count)
    {
        // This tree has no highlight rules. Its established native selection
        // geometry supplies the reference clipping area; its ink is never used.
        text.SelectionBrush = Brushes.Black; text.Select(start, count);
        var selected = text.SelectedText; var geometry = new SelectionReferenceGeometry(); text.Render(geometry); text.Select(0, 0);
        return (geometry.Rectangles.ToArray(), selected);
    }
    private static Geometry SelectionReferenceArea(IEnumerable<Rect> rectangles)
    {
        var result = new PathGeometry { FillRule = FillRule.Nonzero };
        foreach (var rect in rectangles)
        {
            var figure = new PathFigure { StartPoint = rect.TopLeft, IsClosed = true };
            figure.Segments.Add(new LineSegment(rect.TopRight)); figure.Segments.Add(new LineSegment(rect.BottomRight)); figure.Segments.Add(new LineSegment(rect.BottomLeft));
            result.Figures.Add(figure);
        }
        result.Freeze(); return result;
    }
    internal static int RunSelection()
    {
        var pool = PushPool(); var app = Send(Class("NSApplication"), Selector("sharedApplication"));
        Integer(app, Selector("setActivationPolicy:"), 1); Send(app, Selector("finishLaunching")); Directory.CreateDirectory(Output);
        var backend = Environment.GetEnvironmentVariable("JALIUM_CSS_MASK_BACKEND") == "software" ? RenderBackend.Software : RenderBackend.Metal;
        using var context = RenderContext.GetOrCreateCurrent(backend); var records = new List<object>(); var failures = 0;
        try
        {
            var requestedEngine = Environment.GetEnvironmentVariable("JALIUM_CSS_SELECTION_ENGINE");
            foreach (var engine in (backend == RenderBackend.Metal ? new[] { RenderingEngine.Impeller, RenderingEngine.Vello } : new[] { RenderingEngine.Auto })
                .Where(engine => string.IsNullOrEmpty(requestedEngine) || engine.ToString().Equals(requestedEngine, StringComparison.OrdinalIgnoreCase)))
            {
                context.DefaultRenderingEngine = engine;
                foreach (var row in SelectionFilter(SelectionCases(),"JALIUM_CSS_SELECTION_CASES",row=>row.Name)) foreach (var width in new[] { 312, 600 }) foreach (var dpi in new[] { 1, 2 })
                {
                    GC.Collect(); GC.WaitForPendingFinalizers();
                    var actual = MakeSelectionScene(row, width, true, false); var normal = MakeSelectionScene(row, width, false, false); var colored = MakeSelectionScene(row, width, false, true, nestedColors: !row.Nested);
                    var count = Math.Min(row.Length, row.Text.Length - row.Start); var (rectangles, selected) = OrdinarySelectionRectangles(normal.Text, row.Start, count);
                    var area = SelectionReferenceArea(rectangles); var clip = new RectangleGeometry(new Rect(0,0,width,180));
                    var nestedArea = row.Nested ? SelectionReferenceArea(OrdinarySelectionRectangles(normal.Text,7,9).Rectangles) : null;
                    var nestedInk = row.Nested ? MakeSelectionScene(row with { Selected = "color:red;" }, width, false, true, nestedColors: false) : null;
                    var originalShadow = row.OriginalShadow ? MakeSelectionScene(row with { Selected = "color:white;" }, width, false, true) : null;
                    var highlightShadow = row.HighlightShadow ? MakeSelectionScene(row with { Selected = "color:white;" }, width, false, true) : null;
                    // A selected initial owns its whole glyph, including ink
                    // outside its advance box. Literal ordinary layers preserve
                    // that ink while the remaining selection is still partial.
                    var bodyInkArea = row.Name == "initial" ? SelectionReferenceArea(ContextSubtract(rectangles,
                        OrdinarySelectionRectangles(normal.Text,0,1).Rectangles)) : area;
                    foreach (var recorded in new[] { false, true })
                    {
                        var label = $"selection-{backend}-{engine}-{row.Name}-{width}-{dpi}-{recorded}";
                        var errors = new List<string>(); var largest = 0; var differences = 0;
                        try
                        {
                            byte[] Capture(Action<DrawingContext> draw) => Pixels(MacOSWritingToolsBitmapCapture.Capture(width*dpi,180*dpi,dpi,dc =>
                            {
                                dc.DrawRectangle(new SolidColorBrush(SelectionPaper), null, new Rect(0,0,width,180));
                                if (recorded) { var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); draw(recorder); DrawingReplayer.Replay(recorder.Commit(),dc); }
                                else draw(dc);
                            },()=>true,context) ?? throw new Exception("Selection capture unavailable"));
                            var pixels = Capture(dc => actual.Text.Render(dc));
                            // Literal ordinary-color controls provide the oracle. Neither
                            // tree has ::selection or calls the selection paint/snapshot path.
                            var reference = Capture(dc =>
                            {
                                if (originalShadow is not null) SelectionReferenceShadow(dc, originalShadow.Text, null, width, Colors.Red, 2, 4);
                                dc.DrawGeometry(new SolidColorBrush(row.Background ?? SelectionBlue),null,
                                    nestedArea is null ? area : new CombinedGeometry(GeometryCombineMode.Exclude,area,nestedArea));
                                if(nestedArea is not null)dc.DrawGeometry(Brushes.Lime,null,nestedArea);
                                if (highlightShadow is not null)
                                SelectionReferenceShadow(dc, highlightShadow.Text, area, width, Colors.Lime, 3, 6);
                                var normalClip = new CombinedGeometry(GeometryCombineMode.Exclude,clip,bodyInkArea);
                                var coloredClip = nestedArea is null ? bodyInkArea : new CombinedGeometry(GeometryCombineMode.Exclude,bodyInkArea,nestedArea);
                                if(row.Name == "initial")
                                {
                                    // The ordinary tree paints the floating initial
                                    // before the following body, including AA overlap.
                                    colored.Text.Render(new SelectionInitialInkContext(dc,true));
                                    normal.Text.Render(new SelectionInitialInkContext(dc,false,normalClip));
                                    colored.Text.Render(new SelectionInitialInkContext(dc,false,coloredClip));
                                }
                                else
                                {
                                    dc.PushClip(normalClip);normal.Text.Render(dc);dc.Pop();
                                    dc.PushClip(coloredClip);colored.Text.Render(dc);dc.Pop();
                                }
                                if (nestedInk is not null) { dc.PushClip(nestedArea!); nestedInk.Text.Render(dc); dc.Pop(); }
                                dc.PushClip(area);
                                if (row.Underline)
                                    foreach (var rect in rectangles)
                                    {
                                        var baseline = normal.Text.GetSourceRangeSegments(row.Start, count).OrderBy(segment => Math.Abs(segment.Baseline - rect.Top - 18)).First().Baseline;
                                        var y = baseline + Math.Max(1.28, row.DecorationThickness / 2);
                                        dc.DrawLine(new Pen(new SolidColorBrush(row.DecorationColor ?? Color.FromRgb(255,248,222)), row.DecorationThickness), new(rect.Left,y), new(rect.Right,y));
                                    }
                                dc.Pop();
                            });
                            for (var index=0; index<pixels.Length;index++) { var delta=Math.Abs(pixels[index]-reference[index]); largest=Math.Max(largest,delta); if(delta>3)differences++; }
                            if (largest>3) errors.Add($"Maximum channel difference {largest}; {differences} channels differ");
                            if (largest>3 || !recorded && dpi==1 && width==312)
                            { Save(pixels,width*dpi,180*dpi,label+".png"); Save(reference,width*dpi,180*dpi,label+"-expected.png"); }
                            if(actual.Text.SelectedText!=selected || actual.Text.Text!=row.Text)errors.Add("Selection/source text changed");
                            if(actual.Text.RenderSize!=normal.Text.RenderSize)errors.Add("Highlight changed layout");
                        }
                        catch(Exception error) { errors.Add(error.ToString()); }
                        if(errors.Count>0) failures++;
                        records.Add(new {label,largest,differences,errors}); Console.WriteLine($"{(errors.Count==0?"PASS":"FAIL")} {label} {string.Join(" | ",errors)}");
                    }
                }
                SelectionEditors(context, backend, engine, records, ref failures);
            }
            if(records.Count==0)throw new InvalidOperationException("No selection comparisons requested");
            File.WriteAllText(Path.Combine(Output,"selection.json"),JsonSerializer.Serialize(new{count=records.Count,failures,records,tolerance=3,
                nativeLibraries=NativeLibraries(),liveWindow=false,physicalInput=false,voiceOver=false,
                oracle="Literal ordinary-color controls clipped by the established selection geometry, with independent background composition; CSS Pseudo 4 sections 3.2, 3.5 and 3.6"}));
            return failures==0?0:1;
        }
        finally { context.Dispose(); PopPool(pool); }
    }
    internal static int RunSelectionGallery()
    {
        var pool=PushPool();var app=Send(Class("NSApplication"),Selector("sharedApplication"));Integer(app,Selector("setActivationPolicy:"),1);Send(app,Selector("finishLaunching"));Directory.CreateDirectory(Output);
        var backend=Environment.GetEnvironmentVariable("JALIUM_CSS_MASK_BACKEND")=="software"?RenderBackend.Software:RenderBackend.Metal;
        using var context=RenderContext.GetOrCreateCurrent(backend);var records=new List<object>();
        try
        {
            foreach(var engine in backend==RenderBackend.Metal?new[]{RenderingEngine.Impeller,RenderingEngine.Vello}:new[]{RenderingEngine.Auto})
            foreach(var width in new[]{375,720})foreach(var dpi in new[]{1,2})
            {
                context.DefaultRenderingEngine=engine;var wide=width>=600;var height=wide?650:1120;var cell=(width-48-(wide?16:0))/(wide?2d:1d);
                var root=new StackPanel{Width=width,Height=height,Background=new SolidColorBrush(SelectionPaper)};
                Css.SetStyle(root,"display:block;box-sizing:border-box;padding:24px;font-family:PingFang SC;font-size:14px;line-height:22px;color:#233c32;");
                Css.GetStyleSheets(root).Add(CssStyleSheet.Parse("@property --gallery-selection {syntax:'<color>';inherits:false;initial-value:#20537a;}"));
                root.Children.Add(new TextBlock{Text="CSS 选区",FontSize=28,LineHeight=36,FontWeight=FontWeights.SemiBold,Foreground=new SolidColorBrush(Color.FromRgb(20,99,66)),Margin=new Thickness(0,0,0,8)});
                root.Children.Add(new TextBlock{Text="文字保持原有排版，选区拥有独立颜色。",Foreground=new SolidColorBrush(Color.FromRgb(35,60,50)),Margin=new Thickness(0,0,0,24)});
                var grid=new Grid{Width=width-48};
                for(var column=0;column<(wide?2:1);column++)grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
                for(var row=0;row<(wide?2:4);row++)grid.RowDefinitions.Add(new RowDefinition{Height=new GridLength(232)});
                var names=new[]{"跨 Run · 保留连字","下沉首字 · 连续选区","双向文字 · 原始顺序","输入框 · 选区颜色"};
                for(var index=0;index<4;index++)
                {
                    var card=new Border{Background=Brushes.White,BorderBrush=new SolidColorBrush(Color.FromRgb(210,224,214)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Padding=new Thickness(16),Margin=new Thickness(0,0,wide&&index%2==0?16:0,16),ClipToBounds=true};
                    var content=new StackPanel();card.Child=content;
                    content.Children.Add(new TextBlock{Text=names[index],FontSize=14,FontWeight=FontWeights.SemiBold,Foreground=new SolidColorBrush(Color.FromRgb(20,99,66)),Margin=new Thickness(0,0,0,18)});
                    if(index<3)
                    {
                        var name=index==0?"runs":index==1?"initial":"mixed-bidi";
                        var scene=MakeSelectionScene(SelectionCases().Single(row=>row.Name==name),(int)(cell-32-(wide&&index%2==0?16:0)),true,false);
                        scene.Root.Height=138;content.Children.Add(scene.Root);
                    }
                    else
                    {
                        var editor=MakeSelectionEditor("text",(int)(cell-32),true,false);
                        Jalium.UI.Automation.AutomationProperties.SetName(editor.Control,"选区样例输入");
                        if(editor.Control is TextBox preview) { preview.Text="Office affinity stays intact."; preview.Select(2,18); }
                        content.Children.Add((FrameworkElement)VisualTreeHelper.GetParent(editor.Control)!);
                    }
                    Grid.SetColumn(card,wide?index%2:0);Grid.SetRow(card,wide?index/2:index);grid.Children.Add(card);
                }
                root.Children.Add(grid);root.Children.Add(new TextBlock{Text="选区变化不会改写原始文字。",Foreground=new SolidColorBrush(Color.FromRgb(70,98,80)),Margin=new Thickness(0,8,0,0)});
                VisualTreeHelper.SetRootDpi(root,new(dpi,dpi));FirstLetterLayout(root,width,height);
                var bitmap=MacOSWritingToolsBitmapCapture.Capture(width*dpi,height*dpi,dpi,dc=>root.Render(dc),()=>true,context)??throw new Exception("Selection gallery capture unavailable");
                var label=$"selection-gallery-{backend}-{engine}-{width}-{dpi}.png";Save(Pixels(bitmap),width*dpi,height*dpi,label);records.Add(new{label,width,height,dpi,layoutSettled=root.IsMeasureValid&&root.IsArrangeValid});
            }
            File.WriteAllText(Path.Combine(Output,"selection-gallery.json"),JsonSerializer.Serialize(new{records,liveWindow=false,physicalInput=false,voiceOver=false}));return 0;
        }
        finally{context.Dispose();PopPool(pool);}
    }
    private sealed class SelectionInitialInkContext(DrawingContext inner,bool initial,Geometry? clip = null) : DrawingContextAdapter,IOffsetDrawingContext,IClipBoundsDrawingContext
    {
        public Point Offset{get=>(inner as IOffsetDrawingContext)?.Offset??default;set{if(inner is IOffsetDrawingContext offset)offset.Offset=value;}}
        public Rect? CurrentClipBounds=>(inner as IClipBoundsDrawingContext)?.CurrentClipBounds;
        public override void DrawText(FormattedText text,Point point)
        {
            if((text.Text=="A")!=initial)return;
            if(clip is null){inner.DrawText(text,point);return;}
            var local=new GeometryGroup{Transform=new TranslateTransform(-Offset.X,-Offset.Y)};local.Children.Add(clip);
            inner.PushClip(local);try{inner.DrawText(text,point);}finally{inner.Pop();}
        }
        public override void DrawRectangle(Brush? brush,Pen? pen,Rect rect)=>inner.DrawRectangle(brush,pen,rect);
        public override void DrawGeometry(Brush? brush,Pen? pen,Geometry geometry)=>inner.DrawGeometry(brush,pen,geometry);
        public override void DrawLine(Pen pen,Point a,Point b)=>inner.DrawLine(pen,a,b);
        public override void DrawRoundedRectangle(Brush? brush,Pen? pen,Rect rect,double x,double y)=>inner.DrawRoundedRectangle(brush,pen,rect,x,y);
        public override void DrawEllipse(Brush? brush,Pen? pen,Point center,double x,double y)=>inner.DrawEllipse(brush,pen,center,x,y);
        public override void DrawImage(ImageSource source,Rect rect)=>inner.DrawImage(source,rect);
        public override void DrawBackdropEffect(Rect rect,IBackdropEffect effect,CornerRadius radii)=>inner.DrawBackdropEffect(rect,effect,radii);
        public override void PushClip(Geometry geometry)=>inner.PushClip(geometry);
        public override void PushTransform(Transform transform)=>inner.PushTransform(transform);
        public override void PushOpacity(double opacity)=>inner.PushOpacity(opacity);
        public override void Pop()=>inner.Pop();public override void Close() { }
    }
    private sealed class SelectionReferenceGeometry(Action<FormattedText,Point,Point>? text=null,Action<Pen,Point,Point,Point>? line=null) : DrawingContextAdapter, IOffsetDrawingContext
    {
        internal readonly List<Rect> Rectangles=[];
        public Point Offset { get; set; }
        private void Add(Rect rect) => Rectangles.Add(new(rect.X + Offset.X, rect.Y + Offset.Y, rect.Width, rect.Height));
        public override void DrawRectangle(Brush? brush,Pen? pen,Rect rect) { if(brush is SolidColorBrush {Color.R:0,Color.G:0,Color.B:0} && rect.Width>0&&rect.Height>0)Add(rect); }
        public override void DrawText(FormattedText value,Point point)=>text?.Invoke(value,point,Offset);
        public override void DrawGeometry(Brush? brush,Pen? pen,Geometry geometry)
        {
            if (brush is not SolidColorBrush {Color.R:0,Color.G:0,Color.B:0}) return;
            if (geometry is PathGeometry path)
                foreach (var figure in path.Figures)
                {
                    var points = figure.Segments.OfType<LineSegment>().Select(segment => segment.Point).Append(figure.StartPoint).ToArray();
                    Add(new Rect(new Point(points.Min(p => p.X), points.Min(p => p.Y)), new Point(points.Max(p => p.X), points.Max(p => p.Y))));
                }
            else Add(geometry.Bounds);
        }
        public override void DrawRoundedRectangle(Brush? brush,Pen? pen,Rect rect,double x,double y) { }
        public override void DrawEllipse(Brush? brush,Pen? pen,Point center,double x,double y) { }
        public override void DrawLine(Pen pen,Point a,Point b)=>line?.Invoke(pen,a,b,Offset);
        public override void DrawImage(ImageSource source,Rect rect) { }
        public override void DrawBackdropEffect(Rect rect,IBackdropEffect effect,CornerRadius radius) { }
        public override void PushClip(Geometry geometry) { }
        public override void PushOpacity(double opacity) { }
        public override void PushTransform(Transform transform) { }
        public override void Pop() { }
        public override void Close() { }
    }
}

using System.Globalization;
using System.Text.Json;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;

namespace Jalium.UI.MacOS;

internal static partial class CssWritingToolsChecks
{
    private sealed record EditorEmphasisCase(string Name, string Kind, string Text,
        bool Under = false, bool Wrap = false, bool Selected = false, TextAlignment Alignment = TextAlignment.Left, string Font = "", bool Hosted = false, bool CssBox = true);

    private static EditorEmphasisCase[] EditorEmphasisCases() =>
    [
        new("auto-native", "auto", "Office affinity", CssBox: false),
        new("auto-native-under", "auto", "Office affinity", true, CssBox: false),
        new("auto-native-selected", "auto", "Office affinity", Selected: true, CssBox: false),
        new("auto-plain", "auto", "Office affinity"),
        new("auto-under", "auto", "Office affinity", true),
        new("auto-bold", "auto", "Office affinity", Font: "font-weight:700;font-style:italic;"),
        new("auto-center", "auto", "Office affinity", Alignment: TextAlignment.Center),
        new("auto-right", "auto", "Office affinity", true, Alignment: TextAlignment.Right),
        new("auto-selected", "auto", "Office affinity", Selected: true),
        new("auto-selected-under", "auto", "Office affinity", true, Selected: true),
        new("auto-hosted", "auto", "Office affinity", Hosted: true),
        new("auto-hosted-selected", "auto", "Office affinity", true, Selected: true, Hosted: true),
        new("number-plain", "number", "12345"),
        new("number-under", "number", "12345", true),
        new("number-bold", "number", "12345", Font: "font-weight:700;font-style:italic;"),
        new("number-center", "number", "12345", Alignment: TextAlignment.Center),
        new("number-right", "number", "12345", true, Alignment: TextAlignment.Right),
        new("number-selected", "number", "12345", Selected: true),
        new("number-selected-under", "number", "12345", true, Selected: true),
        new("number-hosted", "number", "12345", Hosted: true),
        new("number-hosted-selected", "number", "12345", true, Selected: true, Hosted: true),
        new("auto-clusters", "auto", "A\u0308 😀 汉字"),
        new("auto-rtl", "auto", "ab אבג العربية cd"),
        new("auto-breaks", "auto", "A\r\nB"),
        new("plain", "text", "Office affinity"),
        new("text-bold", "text", "Office affinity", Font: "font-weight:700;font-style:italic;"),
        new("password-bold", "password", "ABCD", Font: "font-weight:700;font-style:italic;"),
        new("under", "text", "文字着重排版", true),
        new("wrapped", "text", "Office affinity flows through another ordinary wrapped line", false, true),
        new("wrapped-under", "text", "Office affinity flows through another ordinary wrapped line", true, true),
        new("rtl", "text", "ab אבג العربية cd"),
        new("center", "text", "Office affinity", false, false, false, TextAlignment.Center),
        new("right", "text", "Office affinity", true, false, false, TextAlignment.Right),
        new("clusters", "text", "A\u0308 😀 汉字"),
        new("selected", "text", "Office affinity", false, false, true),
        new("selected-wrap", "text", "Office affinity flows through another ordinary wrapped line", true, true, true),
        new("masked", "password", "A 、😀e\u0301"),
        new("revealed", "revealed", "A\u0308 😀 汉字", true),
    ];

    private static (StackPanel Root, Control Editor, Func<string> Source) MakeEditorEmphasisScene(
        EditorEmphasisCase row, int width, bool actual)
    {
        var root = new StackPanel { Width = width, Height = 220 };
        Css.SetStyle(root, "display:block;font-family:Georgia;font-size:24px;color:#233c32;");
        Control editor; Func<string> source;
        if (row.Kind is "auto" or "number")
        {
            Jalium.UI.Controls.Primitives.TextBoxBase input = row.Kind == "auto"
                ? new AutoCompleteBox { Text = row.Text }
                : new NumberBox { Value = 12345, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden };
            input.Width = width; input.Height = 200; input.Background = null;
            input.BorderThickness = default; input.Padding = default;
            if (row.CssBox) { input.ClearValue(Control.BorderThicknessProperty); input.ClearValue(Control.PaddingProperty); }
            input.IsInactiveSelectionHighlightEnabled = true;
            if (row.Hosted)
            {
                var template = new ControlTemplate(input.GetType());
                template.SetVisualTree(() => new Border { Name = "PART_ContentHost" }); input.Template = template;
            }
            editor = input; source = () => input is AutoCompleteBox auto ? auto.Text : ((NumberBox)input).Text;
            if (row.Selected) input.SelectAll();
        }
        else if (row.Kind is "password" or "revealed")
        {
            var password = new PasswordBox { Password = row.Text, Width = width, Height = 200,
                Background = null, BorderThickness = default, Padding = default,
                IsPasswordRevealed = row.Kind == "revealed", IsInactiveSelectionHighlightEnabled = true };
            editor = password; source = () => password.Password;
        }
        else
        {
            var input = new TextBox { Text = row.Text, Width = width, Height = 200,
                Background = null, BorderThickness = default, Padding = default,
                TextWrapping = row.Wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
                TextAlignment = row.Alignment, IsInactiveSelectionHighlightEnabled = true };
            editor = input; source = () => input.Text;
            if (row.Selected) input.SelectAll();
        }
        root.Children.Add(editor); Css.SetClass(editor, "subject");
        Css.SetStyle(editor, row.Font + (row.Kind is "auto" or "number" ? (row.CssBox ? "padding:0;border:0;" : "") + "text-align:" + row.Alignment.ToString().ToLowerInvariant() + ";" : "") + (actual ? "color:#233c32;text-emphasis:'※' #146342;text-emphasis-position:" +
            (row.Under ? "under left;" : "over right;") : "color:#233c32;text-emphasis:none;"));
        if (row.Selected)
        {
            if (actual) Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(
                ".subject::selection{color:#f6fffb;background:#146342;}"));
            else { ((Jalium.UI.Controls.Primitives.TextBoxBase)editor).SelectionBrush = new SolidColorBrush(Color.FromRgb(20,99,66));
                ((Jalium.UI.Controls.Primitives.TextBoxBase)editor).SelectionOpacity = 1; }
        }
        FirstLetterLayout(root, width, 220); return (root, editor, source);
    }

    internal static int RunEditorEmphasis()
    {
        var pool = PushPool(); var app = Send(Class("NSApplication"), Selector("sharedApplication"));
        Integer(app, Selector("setActivationPolicy:"), 1); Send(app, Selector("finishLaunching"));
        Directory.CreateDirectory(Output);
        var backend = Environment.GetEnvironmentVariable("JALIUM_CSS_MASK_BACKEND") == "software"
            ? RenderBackend.Software : RenderBackend.Metal;
        using var context = RenderContext.GetOrCreateCurrent(backend);
        var records = new List<object>(); var failures = 0;
        try
        {
            var requested = Environment.GetEnvironmentVariable("JALIUM_CSS_SELECTION_ENGINE");
            foreach (var engine in (backend == RenderBackend.Metal
                ? new[] { RenderingEngine.Impeller, RenderingEngine.Vello } : new[] { RenderingEngine.Auto })
                .Where(value => string.IsNullOrEmpty(requested) || value.ToString().Equals(requested, StringComparison.OrdinalIgnoreCase)))
            {
                context.DefaultRenderingEngine = engine;
                foreach (var row in SelectionFilter(EditorEmphasisCases(), "JALIUM_CSS_EDITOR_EMPHASIS_CASES", value => value.Name))
                foreach (var width in new[] { 312, 600 }) foreach (var dpi in new[] { 1, 2 })
                foreach (var recorded in new[] { false, true })
                {
                    var label = $"editor-emphasis-{backend}-{engine}-{row.Name}-{width}-{dpi}-{recorded}";
                    var errors = new List<string>(); var largest = 0; var differences = 0;
                    try
                    {
                        var actual = MakeEditorEmphasisScene(row, width, true);
                        var ordinary = MakeEditorEmphasisScene(row, width, false);
                        byte[] Capture(Action<DrawingContext> draw) => Pixels(MacOSWritingToolsBitmapCapture.Capture(
                            width * dpi, 220 * dpi, dpi, dc =>
                            {
                                dc.DrawRectangle(new SolidColorBrush(SelectionPaper), null, new(0,0,width,220));
                                if (recorded)
                                {
                                    var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); draw(recorder);
                                    var snapshot = recorder.Commit();
                                    if (row.Kind is "auto" or "number")
                                    {
                                        var local = actual.Editor.HasLocalValue(Control.FontSizeProperty);
                                        var size = actual.Editor.FontSize; actual.Editor.FontSize = size + 4;
                                        actual.Root.Render(new SelectionReferenceGeometry());
                                        if (local) actual.Editor.FontSize = size; else actual.Editor.ClearValue(Control.FontSizeProperty);
                                    }
                                    DrawingReplayer.Replay(snapshot, dc);
                                }
                                else draw(dc);
                            }, () => true, context) ?? throw new Exception("Editor native capture unavailable"));
                        var pixels = Capture(dc => actual.Root.Render(dc));
                        var expected = Capture(dc => ordinary.Root.Render(new EditorEmphasisReference(dc, row, context, width)));
                        for (var index = 0; index < pixels.Length; index++)
                        { var delta = Math.Abs(pixels[index] - expected[index]); largest = Math.Max(largest,delta); if(delta>3)differences++; }
                        if (largest > 3) errors.Add($"Maximum channel difference {largest}; {differences} channels differ");
                        if (largest > 3 || width == 312 && dpi == 1 && !recorded)
                        { Save(pixels,width*dpi,220*dpi,label+".png"); Save(expected,width*dpi,220*dpi,label+"-expected.png"); }
                        if (actual.Source() != row.Text || ordinary.Source() != row.Text) errors.Add("Original source changed");
                    }
                    catch (Exception error) { errors.Add(error.ToString()); }
                    if (errors.Count > 0) failures++;
                    records.Add(new {label,largest,differences,errors});
                    Console.WriteLine($"{(errors.Count==0?"PASS":"FAIL")} {label} {string.Join(" | ",errors)}");
                }
            }
            if (records.Count == 0) throw new Exception("No editor comparisons requested");
            File.WriteAllText(Path.Combine(Output,"editor-emphasis.json"),JsonSerializer.Serialize(new {
                count=records.Count,failures,records,tolerance=3,nativeLibraries=NativeLibraries(),
                liveWindow=false,physicalInput=false,voiceOver=false,
                oracle="Ordinary complete-paragraph drawing clipped to native body rows, independent annotation pitch and literal half-size ruby glyphs; no candidate painter or mark classifier"}));
            return failures == 0 ? 0 : 1;
        }
        finally { context.Dispose(); PopPool(pool); }
    }

    internal static int RunEditorEmphasisGallery()
    {
        var pool=PushPool();var app=Send(Class("NSApplication"),Selector("sharedApplication"));
        Integer(app,Selector("setActivationPolicy:"),1);Send(app,Selector("finishLaunching"));Directory.CreateDirectory(Output);
        var backend=Environment.GetEnvironmentVariable("JALIUM_CSS_MASK_BACKEND")=="software"?RenderBackend.Software:RenderBackend.Metal;
        using var context=RenderContext.GetOrCreateCurrent(backend);var records=new List<object>();
        try
        {
            foreach(var engine in backend==RenderBackend.Metal?new[]{RenderingEngine.Impeller,RenderingEngine.Vello}:new[]{RenderingEngine.Auto})
            foreach(var width in new[]{375,720})foreach(var dpi in new[]{1,2})
            {
                context.DefaultRenderingEngine=engine;const int height=840;
                var root=new StackPanel{Width=width,Height=height,Background=new SolidColorBrush(Color.FromRgb(243,247,241))};
                Css.SetStyle(root,"display:block;font-family:Georgia;font-size:16px;color:#233c32;");
                root.Children.Add(new TextBlock{Text="在编辑中标记重点",FontSize=28,FontWeight=FontWeights.SemiBold,Margin=new Thickness(18,26,18,8)});
                root.Children.Add(new TextBlock{Text="文字、选区和标记一起排版",FontSize=13,Foreground=new SolidColorBrush(Color.FromRgb(68,100,84)),Margin=new Thickness(18,0,18,18)});
                void Card(string title,Control editor,int cardHeight)
                {
                    editor.Width=width-64;editor.Background=null;editor.BorderThickness=default;editor.Padding=default;
                    editor.Foreground=new SolidColorBrush(Color.FromRgb(35,60,50));
                    Jalium.UI.Automation.AutomationProperties.SetName(editor,title);
                    var content=new StackPanel();content.Children.Add(new TextBlock{Text=title,FontSize=13,FontWeight=FontWeights.SemiBold,Foreground=new SolidColorBrush(Color.FromRgb(20,99,66)),Margin=new Thickness(0,0,0,10)});content.Children.Add(editor);
                    root.Children.Add(new Border{Child=content,Height=cardHeight,Background=Brushes.White,BorderBrush=new SolidColorBrush(Color.FromRgb(214,226,217)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Padding=new Thickness(13),Margin=new Thickness(18,5,18,5)});
                }
                var input=new TextBox{Text="文字着重帮助读者辨识重点。\n第二行仍可正常选择与编辑。",Height=92,TextWrapping=TextWrapping.Wrap,IsInactiveSelectionHighlightEnabled=true};
                Css.SetClass(input,"selected-editor");Css.SetStyle(input,"text-emphasis:filled dot #146342;");input.Select(2,4);
                Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(".selected-editor::selection{color:#f6fffb;background:#146342;}"));Card("正文编辑 · 选区包含上方着重号",input,156);
                var under=new TextBox{Text="A\u0308 与 😀 保持完整，标记位于下方。",Height=68,TextWrapping=TextWrapping.Wrap};Css.SetStyle(under,"text-emphasis:open sesame #146342;text-emphasis-position:under left;");Card("组合字符 · 标记随换行排列",under,132);
                var password=new PasswordBox{Password="私密 A 、😀e\u0301",Height=44};Css.SetStyle(password,"text-emphasis:'※' #146342;");Card("密码输入 · 保持统一掩码",password,108);
                var run=new Run("富文本保留原始内容与格式。\n替换、撤销后仍按正文排版。");var paragraph=new Paragraph(run);
                var rich=new RichTextBox{Document=new FlowDocument(paragraph),Height=112};Css.SetStyle(rich,"text-emphasis:open circle #146342;text-emphasis-position:under right;");Card("富文本编辑 · 保留内容与节点",rich,172);
                root.Children.Add(new TextBlock{Text="着重号不改写正文；密码仍以掩码显示。",FontSize=12,Foreground=new SolidColorBrush(Color.FromRgb(68,100,84)),Margin=new Thickness(18,14,18,0)});
                FirstLetterLayout(root,width,height);var label=$"editor-emphasis-gallery-{backend}-{engine}-{width}-{dpi}.png";
                Save(Pixels(MacOSWritingToolsBitmapCapture.Capture(width*dpi,height*dpi,dpi,dc=>root.Render(dc),()=>true,context)??throw new Exception("Editor gallery capture unavailable")),width*dpi,height*dpi,label);
                if(input.Text!="文字着重帮助读者辨识重点。\n第二行仍可正常选择与编辑。"||!ReferenceEquals(paragraph,run.Parent))throw new Exception("Gallery changed its source");
                records.Add(new{label,width,height,dpi,layoutSettled=root.IsMeasureValid&&root.IsArrangeValid});
            }
            File.WriteAllText(Path.Combine(Output,"editor-emphasis-gallery.json"),JsonSerializer.Serialize(new{records,nativeLibraries=NativeLibraries(),liveWindow=false,physicalInput=false,voiceOver=false}));return 0;
        }
        finally{context.Dispose();PopPool(pool);}
    }

    private sealed class EditorEmphasisReference(DrawingContext inner, EditorEmphasisCase row, RenderContext context, double viewportWidth)
        : DrawingContextAdapter, IOffsetDrawingContext
    {
        private readonly List<Rect> _selection = [];
        private Brush? _selectionBackground;
        private bool _selectionPainted;
        private Geometry? _selectionMask;
        public Point Offset { get => (inner as IOffsetDrawingContext)?.Offset ?? default;
            set { if (inner is IOffsetDrawingContext offset) offset.Offset = value; } }
        private (FormattedText Mark, double Height, TextMetrics Metrics) Annotation(string family, int weight = 400, int style = 0)
        {
            var ruby = new FontFeature(FontFeature.TagOf("ruby"),1);
            var source = FontFeatureRenderingSource.TryUnwrap(family,out var features,out var baseFamily,out var caps,out var fontContext)
                ? FontFeatureRenderingSource.Wrap(baseFamily,FontFeatureRenderingSource.Merge(features,[ruby]),caps,fontContext)
                : FontFeatureRenderingSource.Wrap(family,[ruby]);
            var mark = new FormattedText("※",source,12) { FontWeight=weight,FontStyle=style,
                Foreground = new SolidColorBrush(Color.FromRgb(20,99,66)) };
            TextMeasurement.MeasureText(mark);
            return (mark,Math.Ceiling(Math.Max(mark.Height,mark.Ascent+mark.Descent)),TextMeasurement.GetFontMetrics(family,24,weight,style));
        }
        private double NativeInputGrowth(TextMetrics metrics, double space)
            => row.Kind == "auto" && !row.CssBox
                ? Math.Max(32, Math.Round(metrics.LineHeight) + space) - Math.Max(32, Math.Round(metrics.LineHeight)) : 0;
        public override void DrawText(FormattedText text, Point point)
        {
            if (row.Selected && !_selectionPainted)
            { inner.DrawGeometry(_selectionBackground,null,SelectionMask()); _selectionPainted=true; }
            // The ordinary editor rounds its natural block height, which can
            // truncate the final row before this independent row clip runs.
            // Height does not change shaping or wrapping; retain all source ink.
            text=text.CreateRenderSnapshot(text.Foreground);text.MaxTextHeight=100000;
            var (mark, space, metrics) = Annotation(text.FontFamily,text.FontWeight,text.FontStyle);
            var pitch = Math.Round(metrics.LineHeight) + space;
            var above = row.Under ? 0 : space;
            using var format = TextMeasurement.CreateTextFormatFromFamilyList(context,text.FontFamily,24,text.FontWeight,text.FontStyle);
            format.SetTextAlignment(text.NativeTextAlignment ?? TextAlignment.Left); format.SetNoWrap(text.NativeNoWrap);
            var width = (float)Math.Min(100000,text.MaxTextWidth);
            uint position = 0; var number = 0;
            while (position < text.Text.Length && format.GetLineMetrics(text.Text,width,100000,position,false,out var native))
            {
                if (native.Length == 0) break;
                var bodyTop = row.Kind is "auto" or "number"
                    ? point.Y + above - space / 2 + NativeInputGrowth(metrics, space) / 2
                    : point.Y + number * pitch + above;
                // A single-line composite retains fallback glyph overhangs;
                // a Georgia-only row band would clip CJK and emoji descenders.
                var rowClip = row.Kind is not ("auto" or "number");
                if (rowClip) inner.PushClip(new RectangleGeometry(new Rect(point.X-48,bodyTop,
                    Math.Min(100000,text.MaxTextWidth)+96,native.Height+1)));
                try { DrawSelectedText(text,new Point(point.X,bodyTop-native.Y)); }
                finally { if (rowClip) inner.Pop(); }
                var units = StringInfo.ParseCombiningCharacters(text.Text);
                foreach (var start in units.Where(value => value >= native.TextPosition && value < native.TextPosition + native.Length))
                {
                    var end = units.FirstOrDefault(value => value > start,text.Text.Length);
                    // These fixtures contain letters, complete emoji and spaces.
                    // Masked text always annotates visible mask units.
                    if (row.Kind != "password" && text.Text[start] == ' ') continue;
                    if (!format.HitTestTextRange(text.Text,width,100000,(uint)start,(uint)(end-start),out var range))
                        throw new Exception("Reference source range unavailable");
                    var y = bodyTop + metrics.Ascent + (row.Under ? metrics.Descent : -metrics.Ascent-space);
                    // Placement uses the literal glyph's actual height, before ceil.
                    if (!row.Under) y += space - Math.Max(mark.Height,mark.Ascent+mark.Descent);
                    DrawSelectedText(mark,new Point(point.X+range.X+(range.Width-mark.WidthIncludingTrailingWhitespace)/2,y));
                }
                var next = native.TextPosition + native.Length; if(next<=position)break;position=next;number++;
            }
        }
        private void DrawSelectedText(FormattedText text, Point point)
        {
            if (!row.Selected) { inner.DrawText(text,point); return; }
            var selection = SelectionMask();
            inner.PushClip(new CombinedGeometry(GeometryCombineMode.Exclude,
                new RectangleGeometry(new Rect(0,0,viewportWidth,220)),selection));
            try { inner.DrawText(text,point); } finally { inner.Pop(); }
            inner.PushClip(selection);
            try { inner.DrawText(text.CreatePaintSnapshot(new SolidColorBrush(Color.FromRgb(246,255,251))),point); }
            finally { inner.Pop(); }
        }
        private Geometry SelectionMask()
        {
            if (_selectionMask is not null) return _selectionMask;
            var path=new PathGeometry { FillRule=FillRule.Nonzero };
            foreach(var rectangle in _selection)
            {
                var figure=new PathFigure { StartPoint=new Point(rectangle.Left,rectangle.Top),IsClosed=true,IsFilled=true };
                figure.Segments.Add(new LineSegment(new Point(rectangle.Right,rectangle.Top)));
                figure.Segments.Add(new LineSegment(new Point(rectangle.Right,rectangle.Bottom)));
                figure.Segments.Add(new LineSegment(new Point(rectangle.Left,rectangle.Bottom)));
                path.Figures.Add(figure);
            }
            path.Freeze();return _selectionMask=path;
        }
        public override void DrawRectangle(Brush? b,Pen? p,Rect r)
        {
            if(row.Selected && b is not null && p is null)
            {
                var family = FrameworkElement.DefaultFontFamilyName;
                // Selection rectangles are produced by the ordinary native
                // editor; its inherited family is Georgia in this fixture.
                family = "Georgia"; var (_,space,metrics)=Annotation(family);
                r = row.Kind is "auto" or "number"
                    ? new Rect(r.X, r.Y - space / 2 + NativeInputGrowth(metrics, space) / 2, r.Width, r.Height + space)
                    : new Rect(r.X,Math.Round(r.Y/Math.Max(1,r.Height))*(Math.Round(metrics.LineHeight)+space),
                        r.Width,Math.Round(metrics.LineHeight)+space);
                _selection.Add(r);
                _selectionBackground=b;
                return;
            }
            inner.DrawRectangle(b,p,r);
        }
        public override void DrawGeometry(Brush? b,Pen? p,Geometry g)=>inner.DrawGeometry(b,p,g);
        public override void DrawLine(Pen p,Point a,Point b)=>inner.DrawLine(p,a,b);
        public override void DrawRoundedRectangle(Brush? b,Pen? p,Rect r,double x,double y)=>inner.DrawRoundedRectangle(b,p,r,x,y);
        public override void DrawEllipse(Brush? b,Pen? p,Point q,double x,double y)=>inner.DrawEllipse(b,p,q,x,y);
        public override void DrawImage(ImageSource i,Rect r)=>inner.DrawImage(i,r);
        public override void DrawBackdropEffect(Rect r,IBackdropEffect e,CornerRadius c)=>inner.DrawBackdropEffect(r,e,c);
        public override void PushClip(Geometry g)
        {
            // The ordinary native input clips at its 32px minimum. Its annotated
            // reference grows by the independently measured annotation space.
            if (row.Kind == "auto" && !row.CssBox && g is RectangleGeometry rectangle &&
                Math.Abs(rectangle.Rect.Height - 32) < .01)
            {
                var (_, space, metrics) = Annotation("Georgia"); var bounds = rectangle.Rect;
                g = new RectangleGeometry(new Rect(bounds.X, bounds.Y, bounds.Width,
                    bounds.Height + NativeInputGrowth(metrics, space)));
            }
            inner.PushClip(g);
        }
        public override void PushTransform(Transform t)=>inner.PushTransform(t);
        public override void PushOpacity(double d)=>inner.PushOpacity(d); public override void Pop()=>inner.Pop(); public override void Close(){}
    }
}

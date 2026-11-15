using System.Text.Json;
using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;
using Jalium.UI.Styling;

namespace Jalium.UI.MacOS;

internal static partial class CssWritingToolsChecks
{
    private sealed record PlaceholderCase(string Name, string Kind, string Css, string Hint = "hint text");
    private static IEnumerable<PlaceholderCase> PlaceholderCases()
    {
        foreach (var kind in new[] { "text", "auto", "number", "password", "date", "time", "combo", "combo-editable", "tree" })
        {
            yield return new(kind + "-color", kind, "color:#b04440;");
            yield return new(kind + "-font", kind, "font-size:32px;font-weight:700;font-style:italic;color:#20537a;");
            yield return new(kind + "-spacing", kind, "letter-spacing:4px;word-spacing:6px;text-transform:uppercase;color:#233c32;");
            yield return new(kind + "-effects", kind, "color:#b04440;background-color:#fff0c0;opacity:.6;text-emphasis:dot;text-decoration:underline;text-shadow:1px 2px 0 #20537a;");
        }
    }

    private static (StackPanel Root, Control Input, Func<string> Read) MakePlaceholderScene(PlaceholderCase row, int width, bool actual)
    {
        Control input; Func<string> read;
        switch (row.Kind)
        {
            case "auto": var auto = new AutoCompleteBox { Text = "", PlaceholderText = row.Hint }; input = auto; read = () => auto.Text; break;
            case "number": var number = new NumberBox { Text = "", PlaceholderText = row.Hint, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Hidden }; input = number; read = () => number.Text; break;
            case "password": var password = new PasswordBox { Password = "", PlaceholderText = row.Hint }; input = password; read = () => password.Password; break;
            case "date": var date = new DatePicker { PlaceholderText = row.Hint }; input = date; read = () => date.Text; break;
            case "time": var time = new TimePicker { PlaceholderText = row.Hint }; input = time; read = () => time.SelectedTime?.ToString() ?? ""; break;
            case "combo":
            case "combo-editable":
                var combo = new ComboBox { PlaceholderText = row.Hint, IsEditable = row.Kind == "combo-editable" };
                var comboTemplate = new ControlTemplate(typeof(ComboBox));
                comboTemplate.SetVisualTree(() =>
                {
                    if (combo.IsEditable) return new TextBox { Name = "PART_EditableTextBox", Padding = default, BorderThickness = default, Background = null };
                    var presenter = new ContentPresenter { Name = "PART_SelectionPresenter" };
                    presenter.SetTemplateBinding(ContentPresenter.ContentProperty, ComboBox.SelectionBoxItemProperty); return presenter;
                });
                combo.Template = comboTemplate; input = combo; read = () => combo.SelectedItem?.ToString() ?? combo.Text; break;
            case "tree":
                var tree = new TreeSelector { PlaceholderText = row.Hint }; var treeTemplate = new ControlTemplate(typeof(TreeSelector));
                treeTemplate.SetVisualTree(() => new TextBlock { Name = "PART_PlaceholderText", Text = row.Hint }); tree.Template = treeTemplate;
                input = tree; read = () => tree.SearchText; break;
            default: var text = new TextBox { Text = "", PlaceholderText = row.Hint }; input = text; read = () => text.Text; break;
        }
        input.Width = width; input.Height = 96; input.FontFamily = new FontFamily("Georgia"); input.FontSize = 24;
        input.Foreground = new SolidColorBrush(Color.FromRgb(24, 60, 77)); input.Background = null; input.Padding = default; input.BorderThickness = default;
        Css.SetClass(input, "placeholder-subject"); var root = new StackPanel { Width = width, Height = 160 }; root.Children.Add(input);
        if (actual) Css.GetStyleSheets(root).Add(CssStyleSheet.Parse(".placeholder-subject::placeholder{" + row.Css + "}"));
        FirstLetterLayout(root, width, 160); return (root, input, read);
    }

    internal static int RunPlaceholders()
    {
        var pool = PushPool(); Send(Class("NSApplication"), Selector("sharedApplication")); Directory.CreateDirectory(Output);
        var backend = Environment.GetEnvironmentVariable("JALIUM_CSS_MASK_BACKEND") == "software" ? RenderBackend.Software : RenderBackend.Metal;
        using var context = RenderContext.GetOrCreateCurrent(backend); var records = new List<object>(); var failures = 0;
        try
        {
            foreach (var engine in backend == RenderBackend.Metal ? new[] { RenderingEngine.Impeller, RenderingEngine.Vello } : new[] { RenderingEngine.Auto })
            foreach (var row in SelectionFilter(PlaceholderCases().ToArray(), "JALIUM_CSS_PLACEHOLDER_CASES", item => item.Name))
            foreach (var width in new[] { 312, 600 }) foreach (var dpi in new[] { 1, 2 }) foreach (var recorded in new[] { false, true })
            {
                context.DefaultRenderingEngine = engine;
                var label = $"placeholder-{backend}-{engine}-{row.Name}-{width}-{dpi}-{recorded}"; var errors = new List<string>(); var largest = 0; var scenePool = PushPool();
                try
                {
                    var actual = MakePlaceholderScene(row, width, true); var ordinary = MakePlaceholderScene(row, width, false); var replaced = false;
                    byte[] Capture(Action<DrawingContext> draw) => Pixels(MacOSWritingToolsBitmapCapture.Capture(width * dpi, 160 * dpi, dpi, dc =>
                    {
                        dc.DrawRectangle(new SolidColorBrush(SelectionPaper), null, new(0, 0, width, 160));
                        if (recorded) { var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); draw(recorder); var drawing = recorder.Commit(); if (drawing.IsFullyRecordable) DrawingReplayer.Replay(drawing, dc); else draw(dc); }
                        else draw(dc);
                    }, () => true, context) ?? throw new Exception("Native placeholder capture unavailable"));
                    if (Environment.GetEnvironmentVariable("JALIUM_CSS_PLACEHOLDER_TRACE") == "1" && width == 312 && dpi == 1 && !recorded)
                    {
                        var first = new InputSpacingTrace(); actual.Root.Render(first);
                        var second = new InputSpacingTrace(); var traceReference = new PlaceholderReference(second, row, width); ordinary.Root.Render(traceReference); traceReference.Paint();
                        Console.WriteLine("TRACE " + JsonSerializer.Serialize(new { label, actual = first.Rows, expected = second.Rows }));
                    }
                    var pixels = Capture(dc => actual.Root.Render(dc));
                    if (Environment.GetEnvironmentVariable("JALIUM_CSS_PLACEHOLDER_TRACE") == "1" && row.Kind is "auto" or "number")
                    {
                        var styled = CssPlaceholder.Prepare(actual.Input, actual.Input, row.Hint, Brushes.Gray)!;
                        Console.WriteLine("LAYOUT " + JsonSerializer.Serialize(new { label, styled.DesiredSize, styled.VisualBounds, lineHeight = styled.LineHeight.ToString(), styled.LineStackingStrategy }));
                    }
                    var expected = Capture(dc => { var oracle = new PlaceholderReference(dc, row, width); ordinary.Root.Render(oracle); oracle.Paint(); replaced |= oracle.Replaced; });
                    if (!replaced) errors.Add("Reference did not observe the displayed native hint");
                    if (pixels.Distinct().Take(17).Count() < 17) errors.Add("No native hint glyph ink");
                    for (var i = 0; i < pixels.Length; i++) largest = Math.Max(largest, Math.Abs(pixels[i] - expected[i]));
                    if (largest > 3) errors.Add($"Maximum channel difference {largest}");
                    if (largest > 3 || width == 312 && dpi == 1 && !recorded) { Save(pixels, width * dpi, 160 * dpi, label + ".png"); Save(expected, width * dpi, 160 * dpi, label + "-expected.png"); }
                    if (actual.Read() != "" || ordinary.Read() != "") errors.Add("Placeholder styling wrote an input value");
                }
                catch (Exception error) { errors.Add(error.ToString()); }
                finally { PopPool(scenePool); }
                if (errors.Count > 0) failures++; records.Add(new { label, largest, errors }); Console.WriteLine((errors.Count == 0 ? "PASS " : "FAIL ") + label + " " + string.Join("; ", errors));
                if (records.Count % 8 == 0) { GC.Collect(); GC.WaitForPendingFinalizers(); }
            }
            File.WriteAllText(Path.Combine(Output, "placeholder.json"), JsonSerializer.Serialize(new { count = records.Count, failures, records, tolerance = 3, nativeLibraries = NativeLibraries(), liveWindow = false, physicalInput = false, voiceOver = false,
                oracle = "Ordinary native hint glyphs are replaced by a regular TextBlock with the same declarations; the control viewport and independently measured baseline font determine placement. Input values must remain empty." }));
            return failures == 0 ? 0 : 1;
        }
        finally { PopPool(pool); }
    }

    internal static int RunPlaceholderLifecycle()
    {
        var pool=PushPool();Send(Class("NSApplication"),Selector("sharedApplication"));Directory.CreateDirectory(Output);
        var backend=Environment.GetEnvironmentVariable("JALIUM_CSS_MASK_BACKEND")=="software"?RenderBackend.Software:RenderBackend.Metal;
        using var context=RenderContext.GetOrCreateCurrent(backend);var records=new List<object>();var failures=0;
        try
        {
            foreach(var engine in backend==RenderBackend.Metal?new[] { RenderingEngine.Impeller, RenderingEngine.Vello }:new[]{RenderingEngine.Auto})
            foreach(var kind in new[]{"text","auto","number","password","date","time","combo","combo-editable","tree"})
            foreach(var width in new[]{312,600})foreach(var dpi in new[]{1,2})
            {
                context.DefaultRenderingEngine=engine;var errors=new List<string>();var largest=0;var scenePool=PushPool();
                var label=$"placeholder-lifecycle-{backend}-{engine}-{kind}-{width}-{dpi}";
                try
                {
                    var scene=MakePlaceholderScene(new(label,kind,"font-size:var(--hint-size,32px);color:var(--hint-color,#b04440);background:#fff0c0;"),width,true);
                    byte[] Capture(Action<DrawingContext> draw)=>Pixels(MacOSWritingToolsBitmapCapture.Capture(width*dpi,160*dpi,dpi,dc=>
                    {dc.DrawRectangle(new SolidColorBrush(SelectionPaper),null,new(0,0,width,160));draw(dc);},()=>true,context)??throw new Exception("Placeholder lifecycle capture unavailable"));
                    var recorder=new DrawingRecorder();recorder.BindWholeFrame();scene.Root.Render(recorder);var drawing=recorder.Commit();
                    if(!drawing.IsFullyRecordable)errors.Add("Solid placeholder frame unexpectedly rejected recording");
                    var before=Capture(dc=>scene.Root.Render(dc));var replay=Capture(dc=>DrawingReplayer.Replay(drawing,dc));
                    for(var i=0;i<before.Length;i++)largest=Math.Max(largest,Math.Abs(before[i]-replay[i]));
                    for(var size=18;size<39;size++)
                    {Css.SetStyle(scene.Root,"--hint-size:"+size+"px;--hint-color:#20537a;");FirstLetterLayout(scene.Root,width,160);var discard=new DrawingRecorder();scene.Root.Render(discard);discard.Commit();}
                    var changed=Capture(dc=>scene.Root.Render(dc));if(before.SequenceEqual(changed))errors.Add("Updated hint reused stale text/font/color fragments");
                    Css.GetStyleSheets(scene.Root).Clear();FirstLetterLayout(scene.Root,width,160);scene.Root.Render(new DrawingRecorder());
                    GC.Collect();GC.WaitForPendingFinalizers();replay=Capture(dc=>DrawingReplayer.Replay(drawing,dc));
                    for(var i=0;i<before.Length;i++)largest=Math.Max(largest,Math.Abs(before[i]-replay[i]));
                    if(largest>3){errors.Add($"Retained placeholder changed after style/cache retirement: {largest}");Save(before,width*dpi,160*dpi,label+"-before.png");Save(replay,width*dpi,160*dpi,label+"-replay.png");}
                    if(scene.Read()!="")errors.Add("Placeholder lifecycle wrote an input value");
                }
                catch(Exception error){errors.Add(error.ToString());}
                finally{PopPool(scenePool);}
                if(errors.Count>0)failures++;records.Add(new{label,largest,errors});Console.WriteLine((errors.Count==0?"PASS ":"FAIL ")+label+" "+string.Join("; ",errors));
                if(records.Count%8==0){GC.Collect();GC.WaitForPendingFinalizers();}
            }
            File.WriteAllText(Path.Combine(Output,"placeholder-lifecycle.json"),JsonSerializer.Serialize(new{count=records.Count,failures,records,tolerance=3,nativeLibraries=NativeLibraries(),liveWindow=false,physicalInput=false,voiceOver=false}));return failures==0?0:1;
        }
        finally{PopPool(pool);}
    }

    internal static int RunPlaceholderGallery()
    {
        var pool=PushPool();Send(Class("NSApplication"),Selector("sharedApplication"));Directory.CreateDirectory(Output);
        var backend=Environment.GetEnvironmentVariable("JALIUM_CSS_MASK_BACKEND")=="software"?RenderBackend.Software:RenderBackend.Metal;
        using var context=RenderContext.GetOrCreateCurrent(backend);var records=new List<object>();
        try
        {
            foreach(var engine in backend==RenderBackend.Metal?new[] { RenderingEngine.Impeller, RenderingEngine.Vello }:new[]{RenderingEngine.Auto})
            foreach(var dark in new[]{false,true})foreach(var width in new[]{375,720})foreach(var dpi in new[]{1,2})
            {
                context.DefaultRenderingEngine=engine;const int height=1260;
                var ink=dark?Color.FromRgb(155,213,176):Color.FromRgb(20,99,66);
                var muted=dark?Color.FromRgb(175,208,188):Color.FromRgb(68,100,84);
                var paper=dark?Color.FromRgb(24,37,31):Color.FromRgb(243,247,241);
                var card=dark?Color.FromRgb(32,54,42):Colors.White;
                var border=dark?Color.FromRgb(70,103,83):Color.FromRgb(214,226,217);
                var root=new StackPanel{Width=width,Height=height,Background=new SolidColorBrush(paper)};
                root.Children.Add(new TextBlock{Text="占位文字，独立样式",FontSize=28,FontWeight=FontWeights.SemiBold,Foreground=new SolidColorBrush(ink),Margin=new Thickness(18,26,18,8)});
                root.Children.Add(new TextBlock{Text="提示可以变化，输入值保持原样",FontSize=13,Foreground=new SolidColorBrush(muted),Margin=new Thickness(18,0,18,18)});
                var kinds=new[]{"text","auto","number","password","date","time","combo","combo-editable","tree"};
                var labels=new[]{"文本输入 · 独立字体","自动补全 · 字间留白","数值输入 · 金额提示","密码输入 · 输入规则","日期选择 · 格式提示","时间选择 · 清晰示例","组合选择 · 模板提示","可编辑组合 · 继承规则","树选择器 · 空状态提示"};
                var hints=new[]{"输入 姓名…","选择 城市…","输入 金额…","至少 八位 字符…","YYYY / MM / DD","例如 09:30","选择 所属 项目…","输入或选择 项目…","选择 一个 分类…"};
                for(var index=0;index<kinds.Length;index++)
                {
                    var color=dark?"#afd0bc":"#446454";
                    var css="font-size:18px;color:"+color+";"+(index==0?"font-style:italic;":index==1?"letter-spacing:1px;word-spacing:4px;":"");
                    var scene=MakePlaceholderScene(new("gallery",kinds[index],css,hints[index]),width-64,true);
                    scene.Root.Height=46;scene.Input.Height=46;scene.Input.FontSize=18;scene.Input.Foreground=new SolidColorBrush(ink);
                    Jalium.UI.Automation.AutomationProperties.SetName(scene.Input,labels[index]);
                    var content=new StackPanel();content.Children.Add(new TextBlock{Text=labels[index],FontSize=13,FontWeight=FontWeights.SemiBold,Foreground=new SolidColorBrush(ink),Margin=new Thickness(0,0,0,9)});content.Children.Add(scene.Root);
                    root.Children.Add(new Border{Child=content,Height=108,Background=new SolidColorBrush(card),BorderBrush=new SolidColorBrush(border),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Padding=new Thickness(13),Margin=new Thickness(18,5,18,5)});
                }
                root.Children.Add(new TextBlock{Text="提示不是输入值，也不会进入键盘焦点顺序。",FontSize=12,Foreground=new SolidColorBrush(muted),Margin=new Thickness(18,14,18,0)});
                FirstLetterLayout(root,width,height);var label=$"placeholder-gallery-{backend}-{engine}-{(dark?"dark":"light")}-{width}-{dpi}.png";
                Save(Pixels(MacOSWritingToolsBitmapCapture.Capture(width*dpi,height*dpi,dpi,dc=>root.Render(dc),()=>true,context)??throw new Exception("Placeholder gallery capture unavailable")),width*dpi,height*dpi,label);
                records.Add(new{label,width,height,dpi,dark,layoutSettled=root.IsMeasureValid&&root.IsArrangeValid});
            }
            File.WriteAllText(Path.Combine(Output,"placeholder-gallery.json"),JsonSerializer.Serialize(new{records,nativeLibraries=NativeLibraries(),liveWindow=false,physicalInput=false,voiceOver=false}));return 0;
        }
        finally{PopPool(pool);}
    }

    private sealed class PlaceholderReference(DrawingContext inner, PlaceholderCase row, int width) : DrawingContextAdapter, IOffsetDrawingContext
    {
        private Point _offset = (inner as IOffsetDrawingContext)?.Offset ?? default;
        private Point _translation;
        private readonly Stack<(bool Forwarded, Point Translation, Rect? Clip)> _scopes = [];
        private Brush? _fallback;
        private Rect _viewport;
        internal bool Replaced;
        private double ViewportWidth => width - (row.Kind is "date" or "time" ? 32 : 0);
        public Point Offset { get => _offset; set { _offset = value; Sync(); } }
        private void Sync() { if (inner is IOffsetDrawingContext target) target.Offset = new(_offset.X + _translation.X, _offset.Y + _translation.Y); }
        public override void DrawText(FormattedText text, Point origin)
        {
            if (text.Text != row.Hint || Replaced) { inner.DrawText(text, origin); return; }
            Replaced = true; _fallback = text.Foreground;
            // Centered native controls expose their field height through the
            // ordinary glyph origin and independently measured font height.
            // Other controls add a glyph-band clip inside the field slot.
            var clips = _scopes.Where(scope => scope.Clip is not null).ToArray();
            _viewport = row.Kind is "auto" or "number" or "date" or "time"
                ? new Rect(_offset.X + _translation.X, _offset.Y + _translation.Y,
                    ViewportWidth, Math.Max(0, 2 * origin.Y + text.Height))
                : clips.Length >= 2 ? clips[1].Clip!.Value : new Rect(0, 0, width, 96);
        }
        internal void Paint()
        {
            if (!Replaced) return;
            var alias = row.Kind == "combo";
            var reference = new TextBlock { Text = row.Hint, TextWrapping = TextWrapping.NoWrap,
                TextTrimming = row.Kind is "text" or "auto" or "number" or "password" or "combo-editable" ? TextTrimming.CharacterEllipsis : TextTrimming.None };
            reference.SetLayerValue(TextBlock.FontFamilyProperty, new FontFamily("Georgia"), DependencyObject.LayerValueSource.StyleSetter);
            reference.SetLayerValue(TextBlock.FontSizeProperty, 24d, DependencyObject.LayerValueSource.StyleSetter);
            reference.SetLayerValue(TextBlock.ForegroundProperty, _fallback, DependencyObject.LayerValueSource.StyleSetter);
            reference.SetRootDpi(new DpiScale(1, 1));
            Css.SetStyle(reference, "line-height:normal;word-spacing:normal;" + row.Css);
            reference.Measure(new(alias ? width : _viewport.Width, double.PositiveInfinity));
            if (alias) _viewport = new(_viewport.X, _viewport.Y,
                Math.Min(width, reference.DesiredSize.Width), reference.DesiredSize.Height);
            var centered = row.Kind is "auto" or "number" or "date" or "time";
            var point = new Point(_viewport.X, _viewport.Y + (centered ? (_viewport.Height - reference.DesiredSize.Height) / 2 : 0));
            reference.Arrange(new(point.X, point.Y, Math.Min(_viewport.Width, Math.Ceiling(reference.DesiredSize.Width)), Math.Max(1, reference.DesiredSize.Height)));
            if (Environment.GetEnvironmentVariable("JALIUM_CSS_PLACEHOLDER_TRACE") == "1")
                Console.WriteLine("ORACLE " + JsonSerializer.Serialize(new { row.Name, _viewport, reference.DesiredSize, reference.VisualBounds, lineHeight = reference.LineHeight.ToString(), reference.LineStackingStrategy }));
            inner.PushClip(new RectangleGeometry(_viewport));
            try { new Border().RenderChildVisualInline(inner, reference); }
            finally { inner.Pop(); }
        }
        public override void PushClip(Geometry clip)
        {
            var bounds = clip.Bounds;
            _scopes.Push((true, _translation, new Rect(bounds.X + _offset.X + _translation.X,
                bounds.Y + _offset.Y + _translation.Y, bounds.Width, bounds.Height)));
            inner.PushClip(clip);
        }
        public override void PushTransform(Transform transform)
        {
            _scopes.Push((transform is not TranslateTransform, _translation, null));
            if (transform is TranslateTransform move) { _translation = new(_translation.X + move.X, _translation.Y + move.Y); Sync(); }
            else inner.PushTransform(transform);
        }
        public override void PushOpacity(double opacity) { _scopes.Push((true, _translation, null)); inner.PushOpacity(opacity); }
        public override void Pop() { var scope = _scopes.Pop(); if (scope.Forwarded) inner.Pop(); _translation = scope.Translation; Sync(); }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rect) => inner.DrawRectangle(brush, pen, rect);
        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry) => inner.DrawGeometry(brush, pen, geometry);
        public override void DrawLine(Pen pen, Point start, Point end) => inner.DrawLine(pen, start, end);
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rect, double x, double y) => inner.DrawRoundedRectangle(brush, pen, rect, x, y);
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double x, double y) => inner.DrawEllipse(brush, pen, center, x, y);
        public override void DrawImage(ImageSource image, Rect rect) => inner.DrawImage(image, rect);
        public override void DrawBackdropEffect(Rect rect, IBackdropEffect effect, CornerRadius radius) => inner.DrawBackdropEffect(rect, effect, radius);
        public override void Close() { }
    }
}

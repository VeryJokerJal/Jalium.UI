using AppKit;
using CoreGraphics;
using Jalium.UI;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;
using Jalium.UI.Styling;
using Jalium.UI.Threading;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text.Json;

namespace Jalium.UI.MacOS;
[SupportedOSPlatform("macos15.0")]
internal static class CssInlineBoxChecks
{
    private const string Font = "font-family:'Courier New';font-size:16px;line-height:24px;white-space:normal;width:180px;color:#146342;";
    private static readonly string[] Modes = ["inline", "block", "inline-block", "flow-root", "flex", "inline-flex", "grid", "inline-grid", "table", "inline-table", "table-row", "table-cell", "contents", "none"];
    private static readonly string[] Styles = ["display:inline;padding:3px 5px;border:2px solid #146342;margin:0 4px;background:#eef5f0;border-radius:4px;", "display:inline;box-decoration-break:clone;padding:2px 4px;border:1px solid green;", "display:inline-block;width:50%;padding:3px;border:2px solid green;box-sizing:content-box;", "display:block;width:50%;padding:3px;border:2px solid green;box-sizing:border-box;", "display:grid;width:140px;height:64px;grid-template-columns:40% 60%;align-items:end;gap:3px;padding:4px;border:2px solid green;", "display:flex;width:130px;gap:7px;justify-content:space-between;align-items:center;padding:4px;", "display:table;width:120px;border-spacing:6px;border:1px solid green;", "display:contents;width:120px;padding:10px;border:4px solid green;background:red;", "display:inline;position:relative;left:7px;top:3px;", "display:inline;font-size:21px;line-height:1.5;letter-spacing:1px;word-spacing:2px;", "display:inline;vertical-align:super;line-height:18px;", "display:inline;white-space:pre;"];
    internal static int RunAll()
    {
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        Keyboard.Initialize();
        var baseline = Environment.GetEnvironmentVariable("JALIUM_CSS_INLINE_BOXES_BASELINE") == "1";
        var records = new List<object>();
        var filter = Environment.GetEnvironmentVariable("JALIUM_CSS_INLINE_BOXES_CASES")?.Split(',');
        var failed = 0;
        foreach (var engine in new[]
        {
            RenderingEngine.Impeller,
            RenderingEngine.Vello
        }
        )
            foreach (var document in new[]
            {
                false,
                true
            }
            )
                foreach (var target in new[]
                {
                    "run",
                    "span"
                }
                )
                {
                    foreach (var mode in Modes)
                    {
                        using var scene = new Scene(document, target);
                        scene.Style($"display:{mode};gap:6px;grid-template-columns:24px 36px;");
                        Compare(engine, document + "-" + target + "-" + mode, scene);
                    }

                    for (var i = 0; i < Styles.Length; i++)
                    {
                        using var scene = new Scene(document, target);
                        scene.Style(Styles[i]);
                        Compare(engine, document + "-" + target + "-style-" + i, scene);
                    }

                    foreach (var kind in new[]
                    {
                        "selection",
                        "unicode",
                        "columns",
                        "clamp",
                        "wrap",
                        "restore",
                        "decorations"
                    }
                    )
                    {
                        using var scene = new Scene(document, target);
                        scene.Style("display:inline;box-decoration-break:clone;padding:2px;border:1px solid green;background:#eef5f0;");
                        if (kind == "selection")
                        {
                            scene.Style("display:inline-grid;grid-template-columns:36px 48px;gap:5px;");
                            scene.Layout();
                            scene.Select(2, 7);
                        }

                        if (kind == "unicode")
                            scene.Words(["甲乙e\u0301 ", "丙丁", "👨‍👩‍👧‍👦 ", "戊己"]);
                        if (kind is "columns" or "clamp" or "wrap")
                        {
                            scene.Words(["AA BB CC ", "DD EE FF GG ", "HH II JJ KK ", "LL MM NN OO PP QQ RR"]);
                            scene.RootStyle(kind == "columns" ? "columns:2;column-gap:12px;height:48px;column-fill:auto;" : kind == "clamp" ? "line-clamp:2 no-ellipsis;width:90px;" : "width:90px;");
                        }

                        if (kind == "restore")
                        {
                            scene.Style("display:grid;width:120px;padding:10px;grid-template-columns:40px 60px;");
                            scene.Layout();
                            scene.Style("");
                        }

                        if (kind == "decorations")
                            scene.Decorations();
                        Compare(engine, document + "-" + target + "-" + kind, scene);
                    }
                }

        if (filter is null)
            Reader(context, baseline);
        Write("native.json", new { baseline, count = records.Count, failed, cases = records });
        return baseline ? 0 : failed == 0 ? 0 : 1;
        void Compare(RenderingEngine engine, string kind, Scene scene)
        {
            if (filter is not null && !filter.Any(value => kind.Contains(value, StringComparison.Ordinal)))
                return;
            var canvas = CanvasFor(scene.Root);
            var expectedCanvas = CanvasFor(scene.Reference);
            Layout(canvas, 280, 280);
            Layout(expectedCanvas, 280, 280);
            var actual = Capture(context, canvas, engine, 280, 280, 1);
            var expected = Capture(context, expectedCanvas, engine, 280, 280, 1);
            var same = actual.SequenceEqual(expected);
            var differentPixels = 0;
            var maxChannelDelta = 0;
            for (var pixel = 0; pixel < actual.Length; pixel += 4)
            {
                var differs = false;
                for (var channel = 0; channel < 4; channel++)
                {
                    var delta = Math.Abs(actual[pixel + channel] - expected[pixel + channel]);
                    differs |= delta != 0;
                    maxChannelDelta = Math.Max(maxChannelDelta, delta);
                }

                if (differs)
                    differentPixels++;
            }

            var height = scene.Body?.ExtentHeight ?? scene.Root.DesiredSize.Height;
            var passed = same && Math.Abs(height - scene.Reference.DesiredSize.Height) < .0001;
            if (!passed)
                failed++;
            Save(actual, 280, 280, $"formatted-{engine}-{kind}.png");
            Save(expected, 280, 280, $"reference-{engine}-{kind}.png");
            scene.OriginalSource();
            records.Add(new { engine = engine.ToString(), kind, samePixels = same, differentPixels, maxChannelDelta, passed, height, referenceHeight = scene.Reference.DesiredSize.Height });
        }
    }

    private sealed class Scene : IDisposable
    {
        internal readonly Run[] Runs = [new("AA "), new("BB "), new("CC "), new("DD")];
        internal readonly Span Inner, Span;
        internal readonly TextBlock Text = new();
        internal readonly FlowDocument? Document;
        internal readonly FlowDocumentPresentation? Body;
        internal readonly StackPanel Reference = new();
        internal FrameworkElement Root => (FrameworkElement? )Body ?? Text;

        private readonly Inline _target;
        private readonly Dictionary<Inline, FrameworkElement> _boxes = [];
        private readonly Dictionary<Inline, DependencyObject?> _parents = [];
        private readonly TextBlock[] _texts = new TextBlock[4];
        private string _raw;
        internal Scene(bool document, string target)
        {
            var selectionBrush = new SolidColorBrush(Color.FromArgb(96, 20, 99, 66));
            Text.SelectionBrush = selectionBrush;
            Inner = new Span(Runs[2]);
            Span = new Span(Runs[1]);
            Span.Inlines.Add(Inner);
            Inline[] originals = [Runs[0], Span, Runs[3]];
            _target = target == "run" ? Runs[1] : Span;
            if (document)
            {
                var paragraph = new Paragraph();
                foreach (var inline in originals)
                    paragraph.Inlines.Add(inline);
                Document = CssInlineBoxChecks.Document(paragraph);
                Body = new()
                {
                    Width = 180
                };
                Body.SetDocument(Document);
                Body.SelectionBrush = selectionBrush;
            }
            else
            {
                foreach (var inline in originals)
                    Text.Inlines.Add(inline);
                Css.SetStyle(Text, Font + "display:block;");
            }

            foreach (var inline in originals)
                Reference.Children.Add(Build(inline));
            Css.SetStyle(Reference, Font + "display:block;");
            _raw = string.Concat(Runs.Select(run => run.Text));
            FrameworkElement Build(Inline inline)
            {
                _parents[inline] = ((FrameworkContentElement)inline).Parent;
                FrameworkElement result;
                if (inline is Run run)
                {
                    var text = new TextBlock
                    {
                        Text = run.Text,
                        SelectionBrush = selectionBrush
                    };
                    _texts[Array.IndexOf(Runs, run)] = text;
                    result = text;
                }
                else
                {
                    var panel = new StackPanel();
                    foreach (var child in ((Span)inline).Inlines)
                        panel.Children.Add(Build(child));
                    result = panel;
                }

                Css.SetStyle(result, "display:inline;");
                _boxes[inline] = result;
                return result;
            }
        }

        internal void Decorations()
        {
            const string decoration = "text-decoration-line:underline;text-decoration-style:double;text-decoration-color:red;text-decoration-thickness:2px;";
            Style("display:inline;" + decoration);
            // The ordinary text references carry the propagated decoration on
            // their text leaves, independently of the source inline tree.
            if (_target is Span)
                foreach (var text in _texts.Skip(1).Take(2))
                    Css.SetStyle(text, "display:inline;" + decoration);
        }

        internal void Style(string value)
        {
            Css.SetStyle(_target, value);
            Css.SetStyle(_boxes[_target], string.IsNullOrEmpty(value) ? "display:inline;" : value);
        }

        internal void RootStyle(string value)
        {
            Css.SetStyle((DependencyObject? )Document ?? Text, Font + "display:block;" + value);
            Css.SetStyle(Reference, Font + "display:block;" + value);
        }

        internal void Layout()
        {
            CssInlineBoxChecks.Layout(Root, 180, 280);
            CssInlineBoxChecks.Layout(Reference, 180, 280);
        }

        internal void Words(string[] values)
        {
            for (var i = 0; i < 4; i++)
            {
                Runs[i].Text = values[i];
                _texts[i].Text = values[i];
            }

            _raw = string.Concat(values);
        }

        internal void Select(int start, int length)
        {
            if (Body is not null)
            {
                var selection = new TextSelection(Document!.ContentStart, Document.ContentStart);
                selection.SelectOffsets(Document, start, length);
                Body.SetSelection(selection);
            }
            else
                Text.Select(start, length);
            var offset = 0;
            foreach (var text in _texts)
            {
                var from = Math.Clamp(start - offset, 0, text.Text.Length);
                var to = Math.Clamp(start + length - offset, 0, text.Text.Length);
                text.Select(from, Math.Max(0, to - from));
                offset += text.Text.Length;
            }
        }

        internal void OriginalSource()
        {
            if (_raw != (Body?.Paragraphs[0] ?? Text).Text || _parents.Any(pair => !ReferenceEquals(pair.Value, ((FrameworkContentElement)pair.Key).Parent)) || !ReferenceEquals(Runs[1], Span.Inlines[0]) || !ReferenceEquals(Runs[2], Inner.Inlines[0]))
                throw new InvalidOperationException("Original inline source changed");
        }

        public void Dispose() => Body?.Dispose();
    }

    private static FlowDocument Document(params Block[] blocks)
    {
        var doc = new FlowDocument
        {
            PagePadding = new(0),
            FontFamily = "Courier New",
            FontSize = 16,
            LineHeight = 24
        };
        foreach (var block in blocks)
            doc.Blocks.Add(block);
        Css.SetStyle(doc, Font);
        Css.SetStyleSheet(doc, "Paragraph,Section{margin:0;padding:0;border:0;}");
        return doc;
    }

    private static Canvas CanvasFor(FrameworkElement body)
    {
        var canvas = new Canvas
        {
            Background = Brushes.White
        };
        canvas.Children.Add(body);
        Canvas.SetLeft(body, 16);
        Canvas.SetTop(body, 16);
        return canvas;
    }

    private static void Reader(RenderContext context, bool baseline)
    {
        var records = new List<object>();
        foreach (var engine in baseline ? new[]
        {
            RenderingEngine.Impeller
        }
        : new[]
        {
            RenderingEngine.Impeller,
            RenderingEngine.Vello
        }
        )
            foreach (var width in baseline ? new[]
            {
                375
            }
            : new[]
            {
                320,
                375,
                720
            }
            )
            {
                var height = width == 320 ? 420 : 640;
                const string original = "留一段给山风，也留一段给自己。";
                var run = new Run(original);
                var emphasis = new Span(run);
                var first = new Paragraph(new Run("沿途的发现值得慢慢记录。"));
                first.Inlines.Add(emphasis);
                first.Inlines.Add(new Run("再继续往前走。"));
                var second = new Paragraph(new Run("记录光影的变化，也记录走过的路。"));
                var third = new Paragraph(new Run("把小小的发现留在这里，下一次还可以继续补写。"));
                var details = new Section(first);
                details.Blocks.Add(second);
                details.Blocks.Add(third);
                Css.SetClass(details, "details");
                var doc = Document(details);
                Css.SetStyle(doc, "font-family:'PingFang SC';font-size:18px;line-height:28px;white-space:normal;color:#233c32;");
                Css.SetStyleSheet(doc, "Paragraph,Section{margin:0;padding:0;border:0;}.details{margin:0;}");
                using var body = new FlowDocumentPresentation();
                body.SetDocument(doc);
                body.SelectionBrush = new SolidColorBrush(Color.FromArgb(96, 20, 99, 66));
                Css.SetClass(body, "document");
                var root = new StackPanel
                {
                    Background = Brushes.White
                };
                Css.SetClass(root, "root");
                root.Children.Add(Label("让重点随阅读展开", "title"));
                root.Children.Add(Label("切换行内与分段，原文和选择位置保持不变。", "description"));
                root.Children.Add(body);
                var actions = new StackPanel();
                Css.SetClass(actions, "actions");
                root.Children.Add(actions);
                var toggle = Action("展开重点", 0);
                var edit = Action("补写说明", 1);
                var restore = Action("恢复原文", 2);
                actions.Children.Add(toggle);
                actions.Children.Add(edit);
                actions.Children.Add(restore);
                var status = Label("", "status");
                AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
                root.Children.Add(status);
                Css.GetStyleSheets(root).Add(CssStyleSheet.Parse("""
                .root{display:block;box-sizing:border-box;padding:20px;font-family:"PingFang SC";font-size:16px;line-height:24px;color:#233c32;background:white;}
                .title{display:block;font-size:26px;line-height:36px;font-weight:600;margin:0 0 8px;}
                .description{display:block;margin:0 0 20px;}
                .document{display:block;max-width:440px;margin:0 0 20px;}
                .actions{display:flex;flex-wrap:wrap;gap:8px;margin:0 0 12px;}
                .action{display:block;box-sizing:border-box;min-height:44px;padding:8px 10px;border:2px solid transparent;background:#dceee4;color:#146342;}
                .action:hover{background:#c5e2d1;}.action:active{background:#b4d7c2;}.action:focus-visible{border-color:#146342;}
                .status{display:block;font-size:16px;line-height:24px;}
                """));
                var shown = false;
                void Apply()
                {
                    Css.SetStyle(details, "display:block;");
                    foreach (var paragraph in details.Blocks)
                        Css.SetStyle(paragraph, "display:block;margin:0 0 12px;");
                    Css.SetStyle(emphasis, shown ? "display:block;margin:10px 0;padding:12px;border:1px solid #aac6b6;background:#eef5f0;" : "display:inline;background:#eef5f0;padding:2px 4px;border-bottom:1px solid #aac6b6;");
                    ((TextBlock)toggle.Child!).Text = shown ? "收起重点" : "展开重点";
                    AutomationProperties.SetName(toggle, shown ? "收起重点" : "展开重点");
                    status.Text = shown ? "重点已展开，原文和选择位置保持不变。" : "重点随正文连读，也可以单独展开。";
                }

                toggle.Activate = () =>
                {
                    shown = !shown;
                    Apply();
                };
                edit.Activate = () =>
                {
                    run.Text += "再补写一句，留给下一次阅读。";
                    Apply();
                };
                restore.Activate = () =>
                {
                    run.Text = original;
                    shown = false;
                    body.SetSelection(new TextSelection(doc.ContentStart, doc.ContentStart));
                    Apply();
                };
                foreach (var action in new[]
                {
                    toggle,
                    edit,
                    restore
                }
                )
                {
                    action.GotKeyboardFocus += (_, _) => action.BringIntoView();
                    action.MouseLeftButtonUp += (_, _) => action.Activate?.Invoke();
                    action.PreviewKeyDown += (_, e) =>
                    {
                        if (e.Key is Key.Enter or Key.Space)
                        {
                            action.Activate?.Invoke();
                            e.Handled = true;
                        }
                    };
                }

                Apply();
                var viewport = new ScrollViewer
                {
                    Content = root,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
                };
                byte[]? initial = null;
                foreach (var phase in baseline ? new[]
                {
                    "initial"
                }
                : new[]
                {
                    "initial",
                    "selected",
                    "segmented",
                    "edited",
                    "joined",
                    "joined-edited",
                    "2x",
                    "restored"
                }
                )
                {
                    if (phase == "segmented")
                        Press(toggle, Key.Enter);
                    if (phase == "joined")
                        Press(toggle, Key.Space);
                    if (phase is "edited" or "joined-edited")
                        ((IInvokeProvider)edit.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke)!).Invoke();
                    if (phase == "restored")
                        ((IInvokeProvider)restore.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke)!).Invoke();
                    Layout(viewport, width, height);
                    if (!baseline && phase == "initial")
                    {
                        if (root.Children.OfType<TextBlock>().First().GetAutomationPeer()!.GetHeadingLevel() != AutomationHeadingLevel.Level1)
                            throw new InvalidOperationException("Reader heading semantics differ");
                        if (!toggle.Focus() || !toggle.MoveFocus(FocusNavigationDirection.Next) || !edit.IsFocused || !edit.MoveFocus(FocusNavigationDirection.Next) || !restore.IsFocused)
                            throw new InvalidOperationException("Managed focus traversal differs");
                    }

                    if (!baseline && phase == "initial")
                    {
                        toggle.Focus();
                        Layout(viewport, width, height);
                        var at = toggle.TransformToAncestor(viewport);
                        if (at.Y < 0 || at.Y + toggle.ActualHeight > height)
                            throw new InvalidOperationException("Focused action outside viewport");
                        Save(Capture(context, viewport, engine, width, height, 1), width, height, $"reader-{engine}-{width}-focused.png");
                    }

                    Keyboard.ClearFocus();
                    viewport.ScrollToTop();
                    Layout(viewport, width, height);
                    if (phase == "selected")
                    {
                        var textProvider = (ITextProvider)body.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!;
                        textProvider.DocumentRange.FindText("留一段给山风", false, false)!.Select();
                        if (textProvider.GetSelection().Single().GetText(-1) != "留一段给山风")
                            throw new InvalidOperationException("Reader selection differs");
                    }

                    var scale = phase == "2x" ? 2 : 1;
                    var pixels = Capture(context, viewport, engine, width, height, scale);
                    Save(pixels, width * scale, height * scale, $"reader-{engine}-{width}-{phase}.png");
                    var provider = (ITextProvider)body.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!;
                    var exposed = provider.DocumentRange.GetText(-1);
                    var matches = exposed.Contains(run.Text) && exposed.Contains("记录光影") && exposed.Contains("再继续往前走");
                    if (!baseline)
                    {
                        if (!matches || body.Paragraphs.Count != 3 || !ReferenceEquals(run, emphasis.Inlines[0]) || !ReferenceEquals(emphasis, first.Inlines[1]) || !ReferenceEquals(first, details.Blocks[0]) || !ReferenceEquals(second, details.Blocks[1]))
                            throw new InvalidOperationException("Display state or original source differs");
                        foreach (var action in new[]
                        {
                            toggle,
                            edit,
                            restore
                        }
                        )
                        {
                            var at = action.TransformToAncestor(root);
                            if (action.ActualHeight < 44 || at.X < 19 || at.X + action.ActualWidth > width - 19 || action.GetAutomationPeer()!.GetAutomationControlType() != AutomationControlType.Button || string.IsNullOrEmpty(action.GetAutomationPeer()!.GetName()))
                                throw new InvalidOperationException("Reader action layout or semantics differ");
                        }

                        if (phase == "initial")
                            initial = pixels;
                        if (phase == "restored" && !pixels.SequenceEqual(initial!))
                            throw new InvalidOperationException("Restored pixels differ");
                    }

                    if (!baseline && viewport.ExtentHeight > height)
                    {
                        viewport.ScrollToBottom();
                        Layout(viewport, width, height);
                        var at = restore.TransformToAncestor(viewport);
                        if (at.Y < 0 || at.Y + restore.ActualHeight > height)
                            throw new InvalidOperationException("Expanded reader action unreachable");
                        Save(Capture(context, viewport, engine, width, height, 1), width, height, $"reader-{engine}-{width}-{phase}-bottom.png");
                        viewport.ScrollToTop();
                        Layout(viewport, width, height);
                    }

                    records.Add(new { engine = engine.ToString(), width, height, phase, shown, exposureMatches = matches, viewport.ExtentHeight, viewport.ViewportHeight });
                }

                viewport.ScrollToBottom();
                Layout(viewport, width, height);
                if (!baseline)
                {
                    var at = restore.TransformToAncestor(viewport);
                    if (at.Y < 0 || at.Y + restore.ActualHeight > height)
                        throw new InvalidOperationException("Scrolled action unreachable");
                }

                Save(Capture(context, viewport, engine, width, height, 1), width, height, $"reader-{engine}-{width}-scrolled.png");
            }

        Write("reader.json", new { baseline, count = records.Count, cases = records });
    }

    private static void Press(DisplayAction action, Key key)
    {
        if (!action.Focus())
            throw new InvalidOperationException("Reader action could not receive managed focus");
        var args = (KeyEventArgs)typeof(KeyEventArgs).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(RoutedEvent), typeof(Key), typeof(ModifierKeys), typeof(bool), typeof(bool), typeof(int)], null)!.Invoke([UIElement.PreviewKeyDownEvent, key, ModifierKeys.None, true, false, 0]);
        action.RaiseEvent(args);
        if (!args.Handled)
            throw new InvalidOperationException("Display action key unhandled");
    }

    private sealed class DisplayAction : Border
    {
        internal System.Action? Activate;
        protected override AutomationPeer OnCreateAutomationPeer() => new DisplayActionPeer(this);
    }

    private sealed class DisplayActionPeer(DisplayAction owner) : FrameworkElementAutomationPeer(owner), IInvokeProvider
    {
        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button;
        protected override string GetClassNameCore() => "DisplayAction";
        protected override object? GetPatternCore(PatternInterface pattern) => pattern == PatternInterface.Invoke ? this : base.GetPatternCore(pattern);
        public void Invoke() => owner.Activate?.Invoke();
    }

    private static TextBlock Label(string text, string css)
    {
        var result = new TextBlock
        {
            Text = text
        };
        Css.SetClass(result, css);
        if (css == "title")
            AutomationProperties.SetHeadingLevel(result, AutomationHeadingLevel.Level1);
        return result;
    }

    private static DisplayAction Action(string text, int tabIndex)
    {
        var result = new DisplayAction
        {
            Child = Label(text, ""),
            Focusable = true
        };
        Css.SetClass(result, "action");
        KeyboardNavigation.SetTabIndex(result, tabIndex);
        AutomationProperties.SetName(result, text);
        return result;
    }

    private static byte[] Capture(RenderContext context, FrameworkElement root, RenderingEngine engine, int width, int height, int scale)
    {
        using var view = new NSView(new CGRect(0, 0, width, height));
        using var target = context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle), width * scale, height * scale);
        target.SetDpi(96 * scale, 96 * scale);
        target.SetRenderingEngine(engine);
        if (target.RenderingEngine != engine || target.RequestReadback() != JaliumResult.Ok)
            throw new InvalidOperationException("Requested paragraph renderer unavailable");
        using var drawing = new RenderTargetDrawingContext(target, context);
        target.SetFullInvalidation();
        target.BeginDraw();
        target.Clear(1, 1, 1, 1);
        drawing.PushClip(new RectangleGeometry(new Rect(0, 0, width, height)));
        try
        {
            root.Render(drawing);
        }
        finally
        {
            drawing.Pop();
            target.EndDraw();
        }

        var pixels = new byte[width * height * scale * scale * 4];
        if (target.FetchReadback(pixels, (uint)(width * scale * 4), out var w, out var h) != JaliumResult.Ok || w != width * scale || h != height * scale)
            throw new InvalidOperationException("Paragraph readback failed");
        return pixels;
    }

    private static void Write(string name, object value)
    {
        if (Environment.GetEnvironmentVariable("JALIUM_CSS_INLINE_BOXES_EVIDENCE_DIR") is { Length: > 0 } path)
        {
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, name), JsonSerializer.Serialize(value));
        }
    }

    private static void Save(byte[] pixels, int width, int height, string name)
    {
        if (Environment.GetEnvironmentVariable("JALIUM_CSS_INLINE_BOXES_EVIDENCE_DIR") is not { Length: > 0 } path)
            return;
        Directory.CreateDirectory(path);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4)));
        using var file = File.Create(Path.Combine(path, name));
        encoder.Save(file);
    }

    private static void Layout(FrameworkElement root, int width, int height)
    {
        for (var pass = 0; pass < 4; pass++)
        {
            typeof(Dispatcher).GetMethod("ProcessQueue", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(Dispatcher.CurrentDispatcher, null);
            root.Measure(new(width, height));
            root.Arrange(new(0, 0, width, height));
            root.UpdateLayout();
        }
    }
}

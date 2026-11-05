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

namespace Jalium.UI.MacOS;

[SupportedOSPlatform("macos15.0")]
internal static class CssDocumentRootBoxChecks
{
    internal static int RunAll()
    {
        NSApplication.SharedApplication.ActivationPolicy = NSApplicationActivationPolicy.Prohibited;
        using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        var baseline = Environment.GetEnvironmentVariable("JALIUM_CSS_DOCUMENT_ROOT_BOXES_BASELINE") == "1"; var passed = 0;
        foreach (var engine in baseline ? new[] { RenderingEngine.Impeller } : new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (var width in baseline ? new[] { 375 } : new[] { 375, 720 })
        {
            const int height = 760;
            var root = new StackPanel { Background = Brushes.White }; Css.SetClass(root, "root");
            root.Children.Add(Label("文档根盒，尺寸与留白", "title"));
            root.Children.Add(Label("根盒尺寸、页边距和原文保持一致。", "description"));
            root.Children.Add(Label("连续阅读 · 同一份原文", "section"));
            var source = P(string.Join('\n', Enumerable.Range(0, 4).Select(index => $"第{index:00}行"))); var article = new Section(source); Css.SetClass(article, "article"); Css.SetClass(source, "copy");
            var spacer = new Section(); Css.SetClass(spacer, "spacer"); var footer = P("末段原文\n仍可选择"); Css.SetClass(footer, "footer");
            var document = new FlowDocument { FontFamily = "PingFang SC", FontSize = 16, LineHeight = 24, PagePadding = new(8), PageWidth = width - 60, PageHeight = 160,
                Foreground = new SolidColorBrush(Color.FromRgb(35, 60, 50)), Background = Brushes.White };
            document.Blocks.Add(article); document.Blocks.Add(spacer); document.Blocks.Add(footer);
            Css.SetStyleSheet(document, """
                FlowDocument{white-space:pre-wrap;text-align:start;width:var(--root-width,80%);height:var(--root-height,240px);margin:8px auto;padding:10px;border:2px solid #146342;box-sizing:border-box;}
                Paragraph,Section{margin:0;}.article{height:50%;background:#f1f7f3;}.copy{background:#dceee4;}
                .spacer{height:36px;box-sizing:border-box;border:1px solid #9ab8a8;background:#f8fbf9;}
                .footer{padding:0;background:#dceee4;}
                """);
            var selectionBrush = new SolidColorBrush(Color.FromRgb(181, 222, 198));
            var scroll = new FlowDocumentScrollViewer { Document = document, Height = 200, IsToolBarVisible = false, IsInactiveSelectionHighlightEnabled = true,
                SelectionBrush = selectionBrush, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled }; root.Children.Add(scroll);
            var pageLabel = Label("", "section pagelabel"); root.Children.Add(pageLabel);
            var pageHost = new Border { Background = Brushes.White }; Css.SetClass(pageHost, "page"); root.Children.Add(pageHost);
            var actions = new StackPanel(); Css.SetClass(actions, "actions"); root.Children.Add(actions);
            var previous = Action("上一页"); var next = Action("下一页"); var resize = Action("增高根盒"); actions.Children.Add(previous); actions.Children.Add(next); actions.Children.Add(resize);
            var status = Label("", "status"); root.Children.Add(status);
            Css.SetStyleSheet(root, """
                .root{display:block;padding:20px;font-family:'PingFang SC',sans-serif;font-size:16px;color:#233c32;}
                .title{display:block;font-size:26px;line-height:1.3;color:#146342;margin-bottom:8px;}
                .description{display:block;line-height:1.6;margin-bottom:14px;}.section{display:block;font-size:18px;line-height:1.5;color:#146342;margin-bottom:8px;}
                .pagelabel{margin-top:16px;}.page{padding:8px;border:1px solid #9ab8a8;border-radius:8px;}
                .actions{display:flex;gap:12px;margin-top:12px;}.action{min-height:44px;box-sizing:border-box;padding:8px 12px;background:#dceee4;border:1px solid #9ab8a8;border-radius:8px;}
                .action:hover{background:#c9e4d5;}.action:focus{outline:2px solid #146342;outline-offset:2px;}.status{display:block;line-height:1.5;color:#146342;margin-top:10px;}
                """);
            AutomationProperties.SetName(scroll, "可选择的文档原文"); AutomationProperties.SetName(pageHost, "保留根盒尺寸的分页内容");
            var original = document.GetText(); var sources = new[] { source, footer }; var parents = sources.Select(p => p.Parent).ToArray(); var runs = sources.Select(p => p.Inlines[0]).ToArray();
            UIElement? pageVisual = null; var pageIndex = 0; var taller = false; var wider = false; var rtl = false;
            void SetArticle()
            {
                Css.SetStyle(document, $"--root-height:{(taller ? 320 : 240)}px;--root-width:{(wider ? 100 : 80)}%;direction:{(rtl ? "rtl" : "ltr")};");
                ((TextBlock)resize.Child!).Text = taller ? "还原高度" : "增高根盒"; AutomationProperties.SetName(resize, taller ? "还原高度" : "增高根盒");
            }
            void Page(int index)
            {
                var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator; pageIndex = Math.Clamp(index, 0, Math.Max(0, paginator.PageCount - 1));
                pageHost.Child = null; (pageVisual as IDisposable)?.Dispose(); pageVisual = (UIElement)paginator.GetPage(pageIndex).Visual!; pageHost.Child = pageVisual;
                pageVisual.GetType().GetProperty("SelectionBrush", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(pageVisual, selectionBrush);
                if (scroll.Selection is { } selection) pageVisual.GetType().GetMethod("SetSelection", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(pageVisual, [selection, true]);
                pageLabel.Text = $"分页阅读 · 第 {pageIndex + 1} 页，共 {paginator.PageCount} 页";
            }
            void Wire(PageAction button, System.Action activate)
            {
                button.Activate = activate; button.MouseLeftButtonUp += (_, _) => activate();
                button.PreviewKeyDown += (_, e) => { if (e.Key is Key.Enter or Key.Space) { activate(); e.Handled = true; } };
            }
            Wire(previous, () => Page(pageIndex - 1)); Wire(next, () => Page(pageIndex + 1)); Wire(resize, () => { taller = !taller; SetArticle(); });
            byte[]? initial = null;
            foreach (var phase in baseline ? new[] { "initial" } : new[] { "initial", "taller", "wider", "page-2", "rtl-2x", "restored" })
            {
                rtl = phase == "rtl-2x"; wider = phase == "wider"; taller = false; var scale = rtl ? 2 : 1; SetArticle(); VisualTreeHelper.SetRootDpi(root, new(scale, scale));
                if (phase == "taller") Press(resize, Key.Enter);
                Layout(root, width, height); var selected = phase == "page-2" ? "末段" : "第00行";
                if (!scroll.Find(selected)) throw new InvalidOperationException("Original source search failed"); Page(0);
                if (phase == "page-2") { Press(next, Key.Enter); if (pageIndex != 1) throw new InvalidOperationException("Page key action failed"); }
                if (rtl)
                {
                    Press(next, Key.Space); if (pageIndex != 1) throw new InvalidOperationException("RTL page key action failed");
                    Press(previous, Key.Space); if (pageIndex != 0) throw new InvalidOperationException("RTL page restoration failed");
                }
                status.Text = phase switch { "taller" => "根盒增高到 320px，子段按 50% 排版", "wider" => "根盒宽度 100%，原文随内容区重排", "page-2" => "第二页续读原文，根留白只消耗一次", "rtl-2x" => "RTL · 双倍像素密度 · 原文可选", "restored" => "已恢复：尺寸、原文顺序和选择", _ => "根盒宽度 80%，内边距 10px" }; Layout(root, width, height);
                using var view = new NSView(new CGRect(0, 0, width, height)); using var target = context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle), width * scale, height * scale);
                target.SetDpi(96 * scale, 96 * scale); target.SetRenderingEngine(engine); if (target.RenderingEngine != engine) throw new InvalidOperationException("Renderer unavailable");
                using var drawing = new RenderTargetDrawingContext(target, context); if (target.RequestReadback() != JaliumResult.Ok) throw new InvalidOperationException("Readback request failed");
                target.SetFullInvalidation(); target.BeginDraw(); target.Clear(1, 1, 1, 1); try { root.Render(drawing); } finally { target.EndDraw(); }
                var pixels = new byte[width * scale * height * scale * 4]; if (target.FetchReadback(pixels, (uint)(width * scale * 4), out var w, out var h) != JaliumResult.Ok || w != width * scale || h != height * scale) throw new InvalidOperationException("Readback failed");
                if (Environment.GetEnvironmentVariable("JALIUM_CSS_DOCUMENT_ROOT_BOXES_EVIDENCE_DIR") is { Length: > 0 } path)
                {
                    Directory.CreateDirectory(path); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create((int)w, (int)h, 96 * scale, 96 * scale, PixelFormats.Bgra32, null, pixels, (int)w * 4)));
                    using var file = File.Create(Path.Combine(path, $"document-root-boxes-{engine}-{width}-{phase}.png")); encoder.Save(file);
                }
                if (baseline) { Console.WriteLine("Before-increment document root box screenshot captured."); return 0; }
                if (document.GetText() != original || scroll.Selection?.Text != selected || !sources.Select(p => p.Parent).SequenceEqual(parents) || !sources.Select(p => p.Inlines[0]).SequenceEqual(runs)) throw new InvalidOperationException("Original text, parents or selection changed");
                var body = Descendants(scroll).First(item => item.GetType().Name == "FlowDocumentPresentation");
                var paragraphs = (IReadOnlyList<TextBlock>)body.GetType().GetProperty("Paragraphs", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(body)!;
                var contentWidth = ((ScrollViewer)Descendants(scroll).Single(item => item is ScrollViewer)).ViewportWidth - 16;
                var expectedWidth = contentWidth * (wider ? 1 : .8) - 24;
                if (Math.Abs(paragraphs[0].ActualWidth - expectedWidth) > .02 || Math.Abs(paragraphs[1].TransformToAncestor(body).Y - (28 + ((taller ? 320 : 240) - 24) / 2 + 36)) > .02)
                    throw new InvalidOperationException("Root box or child percentage geometry differs");
                var originalBoxes = (IReadOnlyList<Rect>)typeof(TextBlock).GetMethod("DocumentTextBounds", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(paragraphs[0], [0, paragraphs[0].Text.Length])!;
                if (originalBoxes.Count != 4) throw new InvalidOperationException("Root geometry lost original source lines");
                var provider = (ITextProvider)body.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!;
                var foundBoxes = provider.DocumentRange.FindText(selected, false, false)!.GetBoundingRectangles();
                if (foundBoxes.Length != 4 || Math.Abs(foundBoxes[3] - 24) > .02) throw new InvalidOperationException("Found source geometry was clipped by its specified box height");
                var count = ((IDocumentPaginatorSource)document).DocumentPaginator.PageCount; if (count < 2) throw new InvalidOperationException("Specified blank space was dropped from pagination");
                foreach (var element in new FrameworkElement[] { scroll, pageHost, actions, status })
                { var at = element.TransformToAncestor(root); if (at.X < 18 || at.X + element.ActualWidth > width - 18 || at.Y + element.ActualHeight > height - 8) throw new InvalidOperationException("Content outside viewport"); }
                if (phase is "initial" or "restored")
                {
                    var top = scroll.TransformToAncestor(root).Y; var bottom = pageHost.TransformToAncestor(root).Y + pageHost.ActualHeight; var crop = new List<byte>();
                    for (var y = (int)Math.Floor(top); y < Math.Min(height, Math.Ceiling(bottom)); y++) for (var x = 20; x < width - 20; x++) for (var component = 0; component < 4; component++) crop.Add(pixels[(y * width + x) * 4 + component]);
                    if (phase == "initial") initial = crop.ToArray(); else if (!crop.SequenceEqual(initial!)) throw new InvalidOperationException("Restored document pixels differ");
                }
                Console.WriteLine($"Document root boxes {engine}/{width}/{phase}: footerTop={paragraphs[1].TransformToAncestor(body).Y:0.###}, pages={count}, selected={selected}"); passed++;
            }
            foreach (var button in new[] { previous, next, resize })
            { var peer = button.GetAutomationPeer()!; if (peer.GetAutomationControlType() != AutomationControlType.Button || string.IsNullOrWhiteSpace(peer.GetName()) || peer.GetPattern(PatternInterface.Invoke) is not IInvokeProvider) throw new InvalidOperationException("Custom action semantics missing"); }
            ((IInvokeProvider)next.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke)!).Invoke(); if (pageIndex != 1) throw new InvalidOperationException("Accessible paging failed");
            ((IInvokeProvider)previous.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke)!).Invoke(); if (pageIndex != 0) throw new InvalidOperationException("Accessible paging restoration failed");
            ((IInvokeProvider)resize.GetAutomationPeer()!.GetPattern(PatternInterface.Invoke)!).Invoke(); Layout(root, width, height); if (!taller) throw new InvalidOperationException("Accessible height action failed");
            pageHost.Child = null; (pageVisual as IDisposable)?.Dispose(); scroll.Document = null;
        }
        foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
            foreach (var pageHeight in new[] { 72, 144 }) VerifyPagedInk(context, engine, pageHeight);
        Console.WriteLine($"macOS CSS document root box GPU checks: {passed}/24 passed; paginated ink 4/4 passed"); return passed == 24 ? 0 : 1;
    }
    private static void VerifyPagedInk(RenderContext context, RenderingEngine engine, int pageHeight)
    {
        FlowDocument Document(bool constrained)
        {
            var source = P(string.Join('\n', Enumerable.Range(0, 12).Select(index => $"第{index:00}行")));
            var section = new Section(source);
            var document = new FlowDocument(section) { FontFamily = "PingFang SC", FontSize = 16, LineHeight = 24, Foreground = Brushes.Black,
                Background = Brushes.White, PagePadding = new(0), PageWidth = 400, PageHeight = constrained ? pageHeight : 400 };
            document.Blocks.Add(P("末段"));
            Css.SetStyleSheet(document, "FlowDocument{white-space:pre-wrap;text-align:start;}Paragraph,Section{margin:0;}" +
                (constrained ? "FlowDocument{width:100%;height:120px;columns:2;column-gap:20px;margin:10px 0;padding:10px;border:2px solid green;}" : "")); return document;
        }
        int Ink(UIElement page, int height)
        {
            const int width = 400;
            using var view = new NSView(new CGRect(0, 0, width, height)); using var target = context.CreateRenderTarget(NativeSurfaceDescriptor.ForMacOSView(view.Handle), width, height);
            target.SetDpi(96, 96); target.SetRenderingEngine(engine); if (target.RenderingEngine != engine) throw new InvalidOperationException("Ink renderer unavailable");
            using var drawing = new RenderTargetDrawingContext(target, context); if (target.RequestReadback() != JaliumResult.Ok) throw new InvalidOperationException("Ink readback request failed");
            target.SetFullInvalidation(); target.BeginDraw(); target.Clear(1, 1, 1, 1); try { page.Render(drawing); } finally { target.EndDraw(); }
            var pixels = new byte[width * height * 4]; if (target.FetchReadback(pixels, width * 4, out var actualWidth, out var actualHeight) != JaliumResult.Ok || actualWidth != width || actualHeight != height) throw new InvalidOperationException("Ink readback failed");
            return Enumerable.Range(0, width * height).Count(index => pixels[index * 4] == 0 && pixels[index * 4 + 1] == 0 && pixels[index * 4 + 2] == 0 && pixels[index * 4 + 3] == 255);
        }
        var reference = Document(false); var referencePaginator = ((IDocumentPaginatorSource)reference).DocumentPaginator;
        using var referencePage = (IDisposable)referencePaginator.GetPage(0).Visual!;
        var expected = Ink((UIElement)referencePage, 400); var actual = 0;
        var constrained = Document(true); var paginator = ((IDocumentPaginatorSource)constrained).DocumentPaginator;
        for (var index = 0; index < paginator.PageCount; index++)
        { using var page = (IDisposable)paginator.GetPage(index).Visual!; actual += Ink((UIElement)page, pageHeight); }
        if (expected == 0 || actual != expected) throw new InvalidOperationException($"Paged root glyph ink changed for {engine}/{pageHeight}: expected={expected}, actual={actual}");
        Console.WriteLine($"Paged root ink {engine}/{pageHeight}: expected={expected}, actual={actual}, pages={paginator.PageCount}");
    }
    private static void Press(PageAction button, Key key)
    {
        var args = (KeyEventArgs)typeof(KeyEventArgs).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(RoutedEvent), typeof(Key), typeof(ModifierKeys), typeof(bool), typeof(bool), typeof(int)], null)!.Invoke([UIElement.PreviewKeyDownEvent, key, ModifierKeys.None, true, false, 0]);
        button.RaiseEvent(args); if (!args.Handled) throw new InvalidOperationException("Custom action did not handle its key");
    }
    private sealed class PageAction : Border
    { internal System.Action? Activate; protected override AutomationPeer OnCreateAutomationPeer() => new PageActionPeer(this); }
    private sealed class PageActionPeer(PageAction owner) : FrameworkElementAutomationPeer(owner), IInvokeProvider
    { protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Button; protected override string GetClassNameCore() => "PageAction"; protected override object? GetPatternCore(PatternInterface pattern) => pattern == PatternInterface.Invoke ? this : base.GetPatternCore(pattern); public void Invoke() => owner.Activate?.Invoke(); }
    private static Paragraph P(string text) => new(new Run(text));
    private static TextBlock Label(string text, string css) { var result = new TextBlock { Text = text }; Css.SetClass(result, css); return result; }
    private static PageAction Action(string text) { var result = new PageAction { Child = Label(text, ""), Focusable = true }; Css.SetClass(result, "action"); AutomationProperties.SetName(result, text); return result; }
    private static IEnumerable<FrameworkElement> Descendants(FrameworkElement source)
    { for (var i = 0; i < VisualTreeHelper.GetChildrenCount(source); i++) if (VisualTreeHelper.GetChild(source, i) is FrameworkElement child) { yield return child; foreach (var nested in Descendants(child)) yield return nested; } }
    private static void Layout(FrameworkElement root, int width, int height)
    { for (var i = 0; i < 4; i++) { typeof(Dispatcher).GetMethod("ProcessQueue", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!.Invoke(Dispatcher.CurrentDispatcher, null); root.Measure(new(width, height)); root.Arrange(new(0, 0, width, height)); root.UpdateLayout(); } }
}

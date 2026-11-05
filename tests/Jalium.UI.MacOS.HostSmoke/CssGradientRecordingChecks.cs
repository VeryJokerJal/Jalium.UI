using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text.Json;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Media.Rendering;

namespace Jalium.UI.MacOS;

internal static partial class CssWritingToolsChecks
{
    private sealed record GradientRecordingRow(string Label, bool FullyRecordable, int Largest,
        string? BeforeHash, string? RetainedHash, string? ChangedHash, List<string> Errors);

    internal static int RunGradientRecording()
    {
        var pool = PushPool(); Send(Class("NSApplication"), Selector("sharedApplication"));
        Directory.CreateDirectory(Output);
        var backend = Environment.GetEnvironmentVariable("JALIUM_CSS_MASK_BACKEND") == "software" ? RenderBackend.Software : RenderBackend.Metal;
        using var context = RenderContext.GetOrCreateCurrent(backend);
        var records = new List<GradientRecordingRow>();
        try
        {
            foreach (var engine in backend == RenderBackend.Metal ? new[] { RenderingEngine.Impeller, RenderingEngine.Vello } : new[] { RenderingEngine.Auto })
            foreach (var radial in new[] { false, true })
            foreach (var words in new[] { "Office A\u0308", "色彩 中文 \U0001F642", "אבג العربية" })
            foreach (var dpi in new[] { 1, 2 })
            foreach (var css in new[] { false, true })
            foreach (var transformation in new[] { 0, 1, 2 })
            foreach (var mode in new[] { "frame", "visual", "nested" })
            {
                context.DefaultRenderingEngine = engine;
                var errors = new List<string>(); var largest = 0; var fullyRecordable = false;
                string? beforeHash = null, retainedHash = null, changedHash = null;
                var label = $"gradient-recording-{backend}-{engine}-{radial}-{words}-{dpi}-{css}-{transformation}-{mode}";
                try
                {
                    var stops = new GradientStopCollection { new(Color.FromArgb(117, 173, 45, 38), 0), new(Color.FromRgb(20, 115, 74), .45), new(Color.FromArgb(215, 38, 83, 155), 1) };
                    GradientBrush brush = radial
                        ? new RadialGradientBrush { Center = new(.45, .5), GradientOrigin = new(.3, .4), RadiusX = .6, RadiusY = .8 }
                        : new LinearGradientBrush { StartPoint = new(.05, .1), EndPoint = new(.9, .8) };
                    Jalium.UI.Controls.StackPanel? cssRoot = null;
                    if (css)
                    {
                        var target = new Jalium.UI.Controls.Border(); cssRoot = new Jalium.UI.Controls.StackPanel();
                        cssRoot.Children.Add(new Jalium.UI.Controls.Border()); cssRoot.Children.Add(target);
                        var lengths = new Jalium.UI.Styling.CssLengthContext(28, 28, 14, 800, 600, element: Jalium.UI.Styling.CssNode.Get(target));
                        var arguments = radial ? "ellipse at 30% 40%, #ad2d26 calc(12px * sibling-index()), #14734a 60%, #26539b 100%"
                            : "35deg, #ad2d26 calc(12px * sibling-index()), #14734a 60%, #26539b 100%";
                        brush.CssImageLayout = new Jalium.UI.Styling.CssGradientLayout(radial ? "radial-gradient" : "linear-gradient", arguments, lengths);
                    }
                    brush.GradientStops = stops; brush.SpreadMethod = GradientSpreadMethod.Reflect;
                    brush.Opacity = .79;
                    var paintTransform = new TranslateTransform(5, 3); brush.Transform = paintTransform;
                    brush.RelativeTransform = new ScaleTransform(.95, 1.08);
                    var transform = new MatrixTransform(transformation == 1 ? new Matrix(.94, .15, -.11, 1.02, 20, 3) : transformation == 2 ? new Matrix(1.22, 0, 0, 1.22, 4, 3) : Matrix.Identity);
                    var text = new FormattedText(words, "Georgia", 28) { Foreground = brush, MaxTextWidth = 270, MaxTextHeight = 120 };
                    void Draw(DrawingContext dc)
                    {
                        dc.PushTransform(transform); dc.PushClip(new RectangleGeometry(new Rect(4, 4, 300, 160)));
                        dc.PushOpacity(.73); dc.DrawText(text, new Point(14.375, 18.625));
                        dc.Pop(); dc.Pop(); dc.Pop();
                    }
                    RecordedDrawing Record()
                    {
                        var recorder = new DrawingRecorder();
                        if (mode == "frame") recorder.BindWholeFrame(); else recorder.Bind(new object());
                        Draw(recorder); var drawing = recorder.Commit();
                        if (mode != "nested") return drawing;
                        var outer = new DrawingRecorder(); outer.BindWholeFrame();
                        if (!outer.TryAppendRecordedDrawing(drawing)) throw new InvalidOperationException("Scene graph append failed");
                        return outer.Commit();
                    }
                    byte[] Capture(Action<DrawingContext> paint, bool worker) => CaptureGradientRecording(context, engine, dpi, paint, worker);
                    void Compare(byte[] expected, byte[] actual, string failure)
                    {
                        var delta = 0; for (var i = 0; i < actual.Length; i++) delta = Math.Max(delta, Math.Abs(actual[i] - expected[i]));
                        largest = Math.Max(largest, delta); if (delta > 3) errors.Add(failure + ": " + delta);
                    }
                    var before = Capture(Draw, false); beforeHash = Convert.ToHexString(SHA256.HashData(before));
                    var recording = Record(); fullyRecordable = recording.IsFullyRecordable;
                    if (!fullyRecordable) throw new InvalidOperationException("Gradient capture requires direct fallback");
                    Compare(before, Capture(dc => DrawingReplayer.Replay(recording, dc), true), "Initial worker replay differs from direct paint");
                    if (text.IsMeasured) errors.Add("Recording or painting mutated caller measurement state");
                    brush.GradientStops[0].Color = Color.FromRgb(55, 27, 180);
                    brush.GradientStops[1].Offset = .72; brush.Opacity = .43;
                    brush.SpreadMethod = GradientSpreadMethod.Repeat;
                    brush.ColorInterpolationMode = ColorInterpolationMode.ScRgbLinearInterpolation;
                    paintTransform.X = 29; transform.Matrix = new Matrix(.85, -.08, .17, 1.05, 10, 16);
                    if (brush is RadialGradientBrush r) { r.Center = new(.7, .3); r.RadiusX = .9; }
                    else ((LinearGradientBrush)brush).EndPoint = new(.35, .65);
                    text.SetFontSize(34); text.MaxTextWidth = 210;
                    cssRoot?.Children.Insert(0, new Jalium.UI.Controls.Border());
                    DrawingObjectPool.Clear();
                    if (records.Count % 12 == 0) { GC.Collect(); GC.WaitForPendingFinalizers(); }
                    var changed = Capture(Draw, false); changedHash = Convert.ToHexString(SHA256.HashData(changed));
                    if (before.SequenceEqual(changed)) errors.Add("Source mutation produced stale direct paint");
                    var retained = Capture(dc => DrawingReplayer.Replay(recording, dc), true);
                    retainedHash = Convert.ToHexString(SHA256.HashData(retained)); Compare(before, retained, "Retained worker paint observed source mutation");
                    var next = Record(); if (!next.IsFullyRecordable) errors.Add("Changed gradient became unrecordable");
                    Compare(changed, Capture(dc => DrawingReplayer.Replay(next, dc), true), "New recording reused stale gradient paint");
                }
                catch (Exception error) { errors.Add(error.ToString()); }
                records.Add(new(label, fullyRecordable, largest, beforeHash, retainedHash, changedHash, errors));
                Console.WriteLine((errors.Count == 0 ? "PASS " : "FAIL ") + label + " " + string.Join("; ", errors));
            }
            return SaveGradientRecording(records, "gradient-recording.json");
        }
        finally { PopPool(pool); }
    }

    internal static int RunGradientRecordingInputs()
    {
        var pool = PushPool(); Send(Class("NSApplication"), Selector("sharedApplication")); Directory.CreateDirectory(Output);
        var backend = Environment.GetEnvironmentVariable("JALIUM_CSS_MASK_BACKEND") == "software" ? RenderBackend.Software : RenderBackend.Metal;
        using var context = RenderContext.GetOrCreateCurrent(backend); var records = new List<GradientRecordingRow>();
        try
        {
            foreach (var engine in backend == RenderBackend.Metal ? new[] { RenderingEngine.Impeller, RenderingEngine.Vello } : new[] { RenderingEngine.Auto })
            foreach (var kind in new[] { "auto", "number", "text", "password", "revealed", "rich", "rich-shared", "text-wrap" })
            foreach (var radial in new[] { false, true }) foreach (var width in new[] { 312, 600 }) foreach (var dpi in new[] { 1, 2 })
            {
                context.DefaultRenderingEngine = engine; var errors = new List<string>(); var largest = 0; var fullyRecordable = false;
                string? beforeHash = null, retainedHash = null, changedHash = null;
                var label = $"gradient-input-recording-{backend}-{engine}-{kind}-{radial}-{width}-{dpi}";
                var scenePool = PushPool();
                try
                {
                    var wrapped = kind == "text-wrap";
                    var row = new InputSpacingCase(label, wrapped ? "text" : kind, kind == "number" ? "121" : wrapped ? "A A A A A A A A A A A A A A A A A A A A A A A A" : "A A", Wrap: wrapped, Effects: kind == "auto", Gradient: true);
                    var scene = MakeInputSpacingScene(row, width, true);
                    GradientBrush brush = radial ? new RadialGradientBrush(Color.FromRgb(35, 60, 50), Color.FromRgb(63, 89, 160))
                        : new LinearGradientBrush(Color.FromRgb(35, 60, 50), Color.FromRgb(63, 89, 160), 0);
                    scene.Input.Foreground = brush; FirstLetterLayout(scene.Root, width, 220);
                    byte[] Capture(Action<DrawingContext> paint, bool worker) => CaptureGradientRecording(context, engine, dpi, paint, worker, width, 220);
                    RecordedDrawing Record()
                    {
                        var recorder = new DrawingRecorder(); recorder.BindWholeFrame(); scene.Root.Render(recorder); return recorder.Commit();
                    }
                    void Compare(byte[] expected, byte[] actual, string message)
                    {
                        var delta = 0; for (var i = 0; i < actual.Length; i++) delta = Math.Max(delta, Math.Abs(expected[i] - actual[i]));
                        largest = Math.Max(largest, delta); if (delta > 3) errors.Add(message + ": " + delta);
                    }
                    var before = Capture(dc => scene.Root.Render(dc), false); beforeHash = Convert.ToHexString(SHA256.HashData(before));
                    var recording = Record(); fullyRecordable = recording.IsFullyRecordable;
                    if (!fullyRecordable) throw new InvalidOperationException("Gradient input requires direct fallback");
                    Compare(before, Capture(dc => DrawingReplayer.Replay(recording, dc), true), "Initial input worker replay differs");
                    var original = scene.Source();
                    switch (scene.Input)
                    {
                        case Jalium.UI.Controls.AutoCompleteBox input: input.Text += "Q"; break;
                        case Jalium.UI.Controls.NumberBox input: input.Value = 212; break;
                        case Jalium.UI.Controls.TextBox input: input.Text += "Q"; break;
                        case Jalium.UI.Controls.PasswordBox input: input.Password += "Q"; break;
                        case Jalium.UI.Controls.RichTextBox input: ((Jalium.UI.Documents.Run)((Jalium.UI.Documents.Paragraph)input.Document.Blocks[0]).Inlines[0]).Text += "Q"; break;
                    }
                    if (scene.Source() == original) errors.Add("Input mutation did not change source");
                    brush.GradientStops[0].Color = Color.FromRgb(130, 35, 98); brush.Opacity = .67;
                    Jalium.UI.Styling.Css.SetStyle(scene.Input, "padding:0;border:0;letter-spacing:8px;word-spacing:2px;");
                    for (var font = 20; font < 39; font++)
                    {
                        scene.Input.FontSize = font; FirstLetterLayout(scene.Root, width, 220);
                        if (scene.Input is Jalium.UI.IImeSupport ime) ime.GetImeCaretRectangle();
                    }
                    var changed = Capture(dc => scene.Root.Render(dc), false); changedHash = Convert.ToHexString(SHA256.HashData(changed));
                    if (before.SequenceEqual(changed)) errors.Add("Changed input reused stale paint");
                    var source = scene.Source(); DrawingObjectPool.Clear();
                    if (records.Count % 16 == 0) { GC.Collect(); GC.WaitForPendingFinalizers(); }
                    var retained = Capture(dc => DrawingReplayer.Replay(recording, dc), true); retainedHash = Convert.ToHexString(SHA256.HashData(retained));
                    Compare(before, retained, "Retained input worker paint observed source or cache mutation");
                    var next = Record(); if (!next.IsFullyRecordable) errors.Add("Changed input became unrecordable");
                    Compare(changed, Capture(dc => DrawingReplayer.Replay(next, dc), true), "New input recording reused stale paint");
                    if (scene.Source() != source) errors.Add("Worker replay changed input source");
                }
                catch (Exception error) { errors.Add(error.ToString()); }
                finally { PopPool(scenePool); }
                records.Add(new(label, fullyRecordable, largest, beforeHash, retainedHash, changedHash, errors));
                Console.WriteLine((errors.Count == 0 ? "PASS " : "FAIL ") + label + " " + string.Join("; ", errors));
            }
            return SaveGradientRecording(records, "gradient-input-recording.json");
        }
        finally { PopPool(pool); }
    }

    private static int SaveGradientRecording(List<GradientRecordingRow> records, string file)
    {
            var failures = records.Count(row => row.Errors.Count != 0);
            using var stream = File.Create(Path.Combine(Output, file)); using var writer = new Utf8JsonWriter(stream);
            writer.WriteStartObject(); writer.WriteNumber("count", records.Count); writer.WriteNumber("failures", failures); writer.WriteNumber("tolerance", 3);
            writer.WriteBoolean("workerReplay", true); writer.WriteBoolean("targetLifecycleOnMain", true);
            writer.WriteBoolean("liveWindow", false); writer.WriteBoolean("physicalInput", false); writer.WriteBoolean("voiceOver", false);
            writer.WriteStartArray("records");
            foreach (var row in records)
            {
                writer.WriteStartObject(); writer.WriteString("label", row.Label); writer.WriteBoolean("fullyRecordable", row.FullyRecordable); writer.WriteNumber("largest", row.Largest);
                writer.WriteString("beforeHash", row.BeforeHash); writer.WriteString("retainedHash", row.RetainedHash); writer.WriteString("changedHash", row.ChangedHash);
                writer.WriteStartArray("errors"); foreach (var error in row.Errors) writer.WriteStringValue(error); writer.WriteEndArray(); writer.WriteEndObject();
            }
            writer.WriteEndArray(); writer.WriteStartObject("nativeLibraries");
            var paths = new Dictionary<string, string>();
            for (uint i = 0; i < ImageCount(); i++)
            {
                var path = System.Runtime.InteropServices.Marshal.PtrToStringUTF8(ImageName(i));
                if (path is null || !Path.GetFileName(path).StartsWith("libjalium.native.", StringComparison.Ordinal)) continue;
                var name = Path.GetFileName(path); path = Path.GetFullPath(path);
                if (paths.TryGetValue(name, out var previous) && previous != path) throw new InvalidOperationException("Multiple loaded native payloads: " + name);
                paths[name] = path;
            }
            foreach (var entry in paths)
            {
                writer.WriteStartObject(entry.Key); writer.WriteString("path", entry.Value);
                writer.WriteString("sha256", Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(entry.Value))).ToLowerInvariant()); writer.WriteEndObject();
            }
            writer.WriteEndObject(); writer.WriteEndObject(); return failures == 0 ? 0 : 1;
    }

    private static byte[] CaptureGradientRecording(RenderContext context, RenderingEngine engine, int dpi, Action<DrawingContext> paint, bool worker, int width = 312, int height = 180, bool paintPaper = true)
    {
        var view = Send(Send(Class("NSView"), Selector("alloc")), Selector("init"));
        if (view == 0) throw new InvalidOperationException("Private capture view unavailable");
        try
        {
            using var target = context.CreateRenderTargetForComposition(NativeSurfaceDescriptor.ForMacOSView(view, composition: true), width * dpi, height * dpi);
            if (context.Backend == RenderBackend.Metal && target.RenderingEngine != engine) throw new InvalidOperationException("Actual engine differs from requested engine");
            target.SetDpi(96 * dpi, 96 * dpi);
            if (target.RequestReadback() != JaliumResult.Ok) throw new InvalidOperationException("Readback request failed");
            Exception? failure = null; var owner = Environment.CurrentManagedThreadId;
            void Draw()
            {
                var threadPool = PushPool();
                try
                {
                    if (worker && owner == Environment.CurrentManagedThreadId) throw new InvalidOperationException("Replay stayed on the owner thread");
                    using var dc = new RenderTargetDrawingContext(target, context);
                    try { if (paintPaper) dc.DrawRectangle(new SolidColorBrush(SelectionPaper), null, new Rect(0, 0, width, height)); paint(dc); }
                    finally { dc.Close(); dc.ClearCache(); }
                }
                catch (Exception error) { failure = error; }
                finally { PopPool(threadPool); }
            }
            target.BeginDraw(); target.Clear(0, 0, 0, 0);
            try { if (worker) { var thread = new Thread(Draw) { IsBackground = true }; thread.Start(); thread.Join(); } else Draw(); }
            finally { target.EndDraw(); }
            if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
            var pixels = new byte[width * height * dpi * dpi * 4];
            if (target.FetchReadback(pixels, (uint)(width * dpi * 4), out var w, out var h) != JaliumResult.Ok || w != width * dpi || h != height * dpi)
                throw new InvalidOperationException("Native capture dimensions differ");
            return pixels;
        }
        finally { Send(view, Selector("release")); }
    }
}

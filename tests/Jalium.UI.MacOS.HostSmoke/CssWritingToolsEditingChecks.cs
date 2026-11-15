using System.Runtime.InteropServices;
using System.Text.Json;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Input;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Styling;
using Jalium.UI.Threading;
using DocList = Jalium.UI.Documents.List;

namespace Jalium.UI.MacOS;

internal static partial class CssWritingToolsChecks
{
    internal static int RunEditing()
    {
        var pool = PushPool(); var app = Send(Class("NSApplication"), Selector("sharedApplication"));
        Integer(app, Selector("setActivationPolicy:"), 1); Send(app, Selector("finishLaunching"));
        Directory.CreateDirectory(Output); using var context = RenderContext.GetOrCreateCurrent(RenderBackend.Metal);
        var records = new List<object>(); int failures = 0;
        foreach (var engine in new[] { RenderingEngine.Impeller, RenderingEngine.Vello })
        foreach (int scale in new[] { 1, 2 })
        foreach (string kind in new[] { "prefix", "disjoint", "multiline", "list", "table", "cell", "hidden-siblings", "cancelled" })
        {
            context.DefaultRenderingEngine = engine;
            var errors = new List<string>(); string label = $"{engine}-{scale}-{kind}";
            try
            {
                if (context.DefaultRenderingEngine != engine) throw new Exception("Requested engine unavailable");
                using var scene = new EditingScene(kind, false, scale); scene.Layout();
                using var expected = new EditingScene(kind, kind != "cancelled", scale); expected.Layout();
                string beforeText = scene.Source.Text, beforeRaw = scene.Document.GetText();
                byte[] Paint(EditingScene value) => Pixels(MacOSWritingToolsBitmapCapture.Capture(380 * scale, 300 * scale, scale,
                    value.Editor.Render, () => true) ?? throw new Exception("Native readback failed"));
                byte[] before = Paint(scene), oracle = Paint(expected);
                int start = kind is "list" or "table" or "hidden-siblings" ? 0 : beforeText.IndexOf(scene.Match, StringComparison.Ordinal);
                int length = kind is "list" or "table" or "hidden-siblings" ? beforeText.Length : scene.Match.Length;
                var input = new TextCompositionEventArgs(UIElement.TextInputEvent, scene.Replacement, 0)
                {
                    ImeReplacementRange = (start, length), NativeTextServicesPayload = EditingPayload(scene.Replacement, kind),
                    NativeEditLifetime = scene.Current,
                };
                scene.Editor.RaiseEvent(input); scene.Layout(); byte[] edited = Paint(scene); var editedTree = EditingTexts(scene.Editor).ToArray();
                Check(input.Handled == (kind != "cancelled"), "unexpected request acceptance");
                Check(scene.Source.Text == expected.Source.Text, "exposed replacement text differs");
                Check(edited.SequenceEqual(oracle), "edited pixels differ from a fresh independent document");
                Check(scene.HiddenRun.Text == "隐藏🙂" && ReferenceEquals(scene.HiddenRun.GetFlowDocument(), scene.Document), "hidden source was lost");
                Check(JsonSerializer.Serialize(scene.Editor.GetWritingToolsStructure()) == JsonSerializer.Serialize(expected.Editor.GetWritingToolsStructure()), "projected structure differs");
                byte[] undone = edited, redone = edited; string[] undoTree = editedTree, redoTree = editedTree;
                if (kind != "cancelled")
                {
                    Check(scene.Editor.Undo(), "undo unavailable"); scene.Layout(); undone = Paint(scene); undoTree = EditingTexts(scene.Editor).ToArray();
                    Check(scene.Document.GetText() == beforeRaw && scene.Source.Text == beforeText, "undo text differs");
                    Check(undone.SequenceEqual(before), "undo did not restore exact native pixels");
                    Check(scene.Editor.Redo(), "redo unavailable"); scene.Layout(); redone = Paint(scene); redoTree = EditingTexts(scene.Editor).ToArray();
                    Check(redone.SequenceEqual(edited), "redo did not restore exact native pixels");
                }
                else Check(scene.Document.GetText() == beforeRaw && !scene.Editor.CanUndo, "retired edit changed source/history");
                Save(before, 380 * scale, 300 * scale, label + "-before.png"); Save(edited, 380 * scale, 300 * scale, label + "-edited.png");
                Save(oracle, 380 * scale, 300 * scale, label + "-oracle.png"); Save(undone, 380 * scale, 300 * scale, label + "-undo.png");
                Save(redone, 380 * scale, 300 * scale, label + "-redo.png");
                records.Add(new { label, errors, beforeText, afterText = scene.Source.Text, start, length,
                    differentBytes = edited.Zip(oracle).Count(pair => pair.First != pair.Second), accepted = input.Handled, editedTree, undoTree, redoTree });
            }
            catch (Exception exception) { errors.Add(exception.ToString()); records.Add(new { label, errors }); }
            if (errors.Count > 0) ++failures;
            Console.WriteLine((errors.Count == 0 ? "PASS " : "FAIL ") + label + " " + string.Join("; ", errors));
            void Check(bool value, string message) { if (!value) errors.Add(message); }
        }
        File.WriteAllText(Path.Combine(Output, "editing.json"), JsonSerializer.Serialize(new { count = records.Count, failures, records,
            physicalInput = false, appleIntelligence = false, nativeLibraries = NativeLibraries() }));
        PopPool(pool); return failures == 0 ? 0 : 1;
    }

    private static IEnumerable<string> EditingTexts(Visual root)
    {
        if (root is TextBlock text) yield return text.Text;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (VisualTreeHelper.GetChild(root, i) is Visual child)
                foreach (var value in EditingTexts(child)) yield return value;
    }

    private sealed class EditingScene : IDisposable
    {
        internal readonly Run HiddenRun = new("隐藏🙂");
        internal readonly FlowDocument Document;
        internal readonly RichTextBox Editor;
        internal readonly string Match, Replacement;
        private readonly bool _cancelled;
        private int _checks;
        internal IAutomationTextProviderSource Source => ((AutomationTextProvider)Editor.GetAutomationPeer()!.GetPattern(PatternInterface.Text)!).Source;
        internal bool Current() => !_cancelled || ++_checks <= 4;
        internal EditingScene(string kind, bool after, int scale)
        {
            _cancelled = kind == "cancelled";
            Match = kind is "disjoint" or "cancelled" ? "AABB" : "AA";
            Replacement = kind == "multiline" ? "改写\n第二行" : kind is "list" or "table" ? "one\ntwo\n" : kind == "hidden-siblings" ? "result\n" : "改写🙂";
            var hidden = new Paragraph(HiddenRun); Css.SetStyle(hidden, "display:none;");
            var section = new Section();
            Document = new(hidden) { PagePadding = new(0), FontFamily = "Helvetica Neue", FontSize = 26, LineHeight = 40 };
            Document.Blocks.Add(section);
            Css.SetStyle(Document, "display:block;font-family:'Helvetica Neue';font-size:26px;line-height:40px;color:#146342;" + Frame);
            Css.SetStyleSheet(Document, "Paragraph,Section{margin:0;padding:0;border:0;}");
            if (kind is "list" or "table")
            {
                if (!after) section.Blocks.Add(new Paragraph(new Run("replace")));
                else if (kind == "list")
                {
                    var list = new DocList { StartIndex = 3, MarkerStyle = TextMarkerStyle.Decimal };
                    list.ListItems.Add(new ListItem(new Paragraph(Rich("one")))); list.ListItems.Add(new ListItem(new Paragraph(Rich("two"))));
                    section.Blocks.Add(list);
                }
                else
                {
                    var table = new Table(); table.Columns.Add(new TableColumn()); table.Columns.Add(new TableColumn());
                    var row = new TableRow(); row.Cells.Add(new TableCell(new Paragraph(Rich("one")))); row.Cells.Add(new TableCell(new Paragraph(Rich("two"))));
                    var group = new TableRowGroup(); group.Rows.Add(row); table.RowGroups.Add(group); section.Blocks.Add(table);
                }
            }
            else if (kind == "hidden-siblings")
            {
                var secret = new Paragraph(new Run("private")); Css.SetStyle(secret, "display:none;");
                section.Blocks.Add(after ? new Paragraph(Rich("result")) : new Paragraph(new Run("one"))); section.Blocks.Add(secret);
                if (!after) section.Blocks.Add(new Paragraph(new Run("two")));
            }
            else
            {
                var first = new Paragraph(new Run("前文 ")); var last = first;
                if (after)
                {
                    if (kind == "multiline")
                    {
                        first.Inlines.Add(Rich("改写")); last = new Paragraph(Rich("第二行"));
                    }
                    else first.Inlines.Add(Rich(Replacement));
                    if (kind != "disjoint") last.Inlines.Add(new Run("BB"));
                }
                else first.Inlines.Add(new Run("AA"));
                if (kind is "disjoint" or "cancelled")
                {
                    var secret = new Span(new Run("private")); Css.SetStyle(secret, "display:none;"); first.Inlines.Add(secret);
                }
                if (!after) first.Inlines.Add(new Run("BB"));
                last.Inlines.Add(new Run(" 后文"));
                if (kind == "cell")
                {
                    var cell = new TableCell(first); var row = new TableRow(); row.Cells.Add(cell);
                    var group = new TableRowGroup(); group.Rows.Add(row); var table = new Table(); table.RowGroups.Add(group); section.Blocks.Add(table);
                }
                else
                {
                    section.Blocks.Add(first); if (!ReferenceEquals(last, first)) section.Blocks.Add(last);
                }
            }
            Editor = new(Document) { Template = null, Padding = new(3), BorderThickness = new(0), Background = Brushes.White };
            Editor.SetRootDpi(new DpiScale(scale, scale)); Editor.IsUndoEnabled = false; Editor.IsUndoEnabled = true;
        }
        internal void Layout()
        {
            for (int i = 0; i < 3; i++)
            { Dispatcher.CurrentDispatcher.ProcessQueue(); CssEvaluationScheduler.FlushIfPending(Dispatcher.CurrentDispatcher); Editor.Measure(new(360, 280)); Editor.Arrange(new(0, 0, 360, 280)); }
        }
        public void Dispose() { MacOSWritingToolsPresentation.Clear(Editor); Keyboard.ClearFocus(); }
    }

    private static Run Rich(string text) => new(text) { FontFamily = "Helvetica Neue", FontSize = 23,
        FontWeight = FontWeights.Bold, FontStyle = FontStyles.Italic, Foreground = new SolidColorBrush(Color.FromArgb(255, 34, 85, 255)),
        TextDecorations = new TextDecorationCollection { new TextDecoration { Location = TextDecorationLocation.Underline } } };

    private static string EditingPayload(string text, string kind)
    {
        var rows = new List<object>();
        for (int at = 0, index = 0; at < text.Length; index++)
        {
            int next = text.IndexOf('\n', at), end = next < 0 ? text.Length : next + 1;
            rows.Add(new { start = at, length = end - at, alignment = 0, direction = 0,
                lists = kind == "list" ? new object[] { new { id = "list", format = "{decimal}", start = 3 } } : [],
                tables = kind == "table" ? new object[] { new { id = "table", row = 0, column = index, rows = 1, columns = 1, tableColumns = 2 } } : [] });
            at = end;
        }
        return JsonSerializer.Serialize(new { proposal = new { version = 1,
            text16 = Convert.ToBase64String(MemoryMarshal.AsBytes(text.AsSpan())),
            runs = new[] { new { start = 0, length = text.Length, family = "Helvetica Neue", size = 23, weight = 700, italic = true,
                underline = 1, strike = 0, foreground = 0xff2255ffu } }, paragraphs = rows } });
    }
}

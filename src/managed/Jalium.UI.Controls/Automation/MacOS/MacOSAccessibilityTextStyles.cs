using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Interop;

namespace Jalium.UI.Controls.Automation.MacOS;

internal sealed unsafe partial class MacOSAccessibilityTree
{
    private const int MaximumStyleTextLength = 16 * 1024 * 1024;
    private const int MaximumStylePayloadLength = 64 * 1024 * 1024;

    private bool ReadTextStyles(AutomationPeer peer, ref MacOSAXRequest request)
    {
        if (peer.IsPassword() || GetTextSource(peer) is not { } source
            || source is not IAutomationTextStyleSource styled) return false;
        string text = source.Text;
        int start = request.TextStart, length = request.TextLength;
        if (text.Length > MaximumStyleTextLength || start < 0 || start > text.Length
            || length < 0 || length > text.Length - start) return false;
        var runs = styled.GetTextStyles();
        // Highlighting/brush resolution can call user code. Reject a snapshot
        // whose owner is detached, hidden, closed, or edited during that call.
        if (_window.IsClosedForPlatformTermination || !IsVisible(peer)
            || peer.IsPassword() || source.Text != text || !ValidStyleRuns(runs, text.Length)) return false;
        if (request.Operation == MacOSAXOperation.TextStyleRange)
        {
            // NSTextView has no character/style at EOF, including an empty document.
            if (start == text.Length) { request.TextStart = 0; request.TextLength = 0; return true; }
            if (AutomationTextStyles.At(runs, start, text.Length) is not { } run) return false;
            request.TextStart = run.Start; request.TextLength = run.Length; return true;
        }
        var fonts = new TextMeasurement.RenderingFont?[runs.Count];
        for (int index = 0; index < runs.Count; index++)
        {
            var run = runs[index];
            fonts[index] = TextMeasurement.GetRenderingFont(run.Style.FontFamily,
                text.Substring(run.Start, run.Length), run.Style.FontSize, run.Style.FontWeight, run.Style.FontStyle);
        }
        // Materializing a CSS font can dispatch user callbacks as well.
        if (_window.IsClosedForPlatformTermination || !IsVisible(peer)
            || peer.IsPassword() || source.Text != text) return false;
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject(); writer.WriteNumber("version", 1);
            // JSON and Encoding.Unicode replace lone surrogates. Transport the
            // raw UTF-16 code units so an AX substring never changes offsets.
            writer.WriteBase64String("text16", MemoryMarshal.AsBytes(text.AsSpan(start, length)));
            writer.WriteStartArray("runs");
            for (int index = 0; index < runs.Count; index++)
            {
                var run = runs[index];
                int from = Math.Max(start, run.Start), to = Math.Min(start + length, run.Start + run.Length);
                if (to <= from) continue;
                var style = run.Style;
                writer.WriteStartObject(); writer.WriteNumber("start", from - start); writer.WriteNumber("length", to - from);
                var font = fonts[index];
                writer.WriteString("family", font?.Family ?? style.FontFamily); writer.WriteNumber("size", font is { } sized ? sized.Size : style.FontSize);
                writer.WriteNumber("weight", font?.Weight ?? style.FontWeight); writer.WriteNumber("italic", font?.Style ?? style.FontStyle);
                if (font is { } resolved) writer.WriteNumber("width", resolved.Width);
                WriteColor("foreground", style.Foreground); writer.WriteNumber("underline", style.Underline);
                WriteColor("underlineColor", style.UnderlineColor); writer.WriteNumber("strike", style.Strikethrough);
                WriteColor("strikeColor", style.StrikethroughColor); writer.WriteNumber("alignment", style.Alignment);
                writer.WriteNumber("direction", style.Direction); writer.WriteString("language", style.Language);
                writer.WriteEndObject(); writer.Flush();
                if (stream.Length > MaximumStylePayloadLength) return false;
            }
            writer.WriteEndArray(); writer.WriteEndObject(); writer.Flush();
            void WriteColor(string name, uint? color) { if (color is { } value) writer.WriteNumber(name, value); }
        }
        if (stream.Length > MaximumStylePayloadLength) return false;
        return WriteString(Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length), ref request);
    }

    private static bool ValidStyleRuns(IReadOnlyList<AutomationTextStyleSpan> runs, int textLength)
    {
        int through = 0;
        foreach (var run in runs)
        {
            var style = run.Style;
            if (run.Start != through || run.Length < 0 || run.Length > textLength - through
                || run.Length == 0 && (textLength != 0 || runs.Count != 1)
                || string.IsNullOrEmpty(style.FontFamily) || style.FontFamily.Length > 4 * 1024 * 1024
                || !double.IsFinite(style.FontSize) || style.FontSize <= 0 || style.FontSize > 65536
                || style.FontWeight is < 1 or > 1000 || style.FontStyle is < 0 or > 2
                || style.Alignment is < 0 or > 3 || style.Direction is < -1 or > 1
                || style.Language?.Length > 1024) return false;
            through += run.Length;
        }
        return runs.Count != 0 && through == textLength;
    }
}

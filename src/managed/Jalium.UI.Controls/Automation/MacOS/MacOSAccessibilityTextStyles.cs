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
        string? payload = CreateTextStyleSnapshot(source, start, length,
            () => !_window.IsClosedForPlatformTermination && IsVisible(peer) && !peer.IsPassword() && source.Text == text,
            runs);
        return payload is not null && WriteString(payload, ref request);
    }

    // Shared by native accessibility and Writing Tools. Both use the same font
    // resolution and raw UTF-16 representation as the actual editor rendering.
    internal static string? CreateTextStyleSnapshot(IAutomationTextProviderSource source,
        int start, int length, Func<bool> isCurrent, IReadOnlyList<AutomationTextStyleSpan>? capturedRuns = null)
    {
        if (source is not IAutomationTextStyleSource styled || !isCurrent()) return null;
        string text = source.Text;
        if (text.Length > MaximumStyleTextLength || start < 0 || start > text.Length ||
            length < 0 || length > text.Length - start) return null;
        var runs = capturedRuns ?? styled.GetTextStyles();
        if (!isCurrent() || source.Text != text || !ValidStyleRuns(runs, text.Length)) return null;
        // Validate the entire source above, but only materialize fonts and text
        // for the requested characters. AX asks for both size and copy, so a
        // single-character read must not copy every run of a large document.
        // Capture value spans before font callbacks can change the source list.
        var selected = new List<AutomationTextStyleSpan>();
        foreach (var run in runs)
        {
            int from = Math.Max(start, run.Start), to = Math.Min(start + length, run.Start + run.Length);
            if (to > from) selected.Add(new(from - start, to - from, run.Style));
        }
        if (!isCurrent() || source.Text != text) return null;
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject(); writer.WriteNumber("version", 1);
            // JSON and Encoding.Unicode replace lone surrogates. Transport the
            // raw UTF-16 code units so an AX substring never changes offsets.
            writer.WriteBase64String("text16", MemoryMarshal.AsBytes(text.AsSpan(start, length)));
            writer.WriteStartArray("runs");
            foreach (var run in selected)
            {
                var style = run.Style;
                var font = TextMeasurement.GetRenderingFont(style.FontFamily,
                    text.Substring(start + run.Start, run.Length), style.FontSize, style.FontWeight, style.FontStyle);
                // A CSS font callback can edit or retire the owner. Stop before
                // resolving the next font or publishing a partial snapshot.
                if (!isCurrent() || source.Text != text) return null;
                writer.WriteStartObject(); writer.WriteNumber("start", run.Start); writer.WriteNumber("length", run.Length);
                writer.WriteString("family", font?.Family ?? style.FontFamily); writer.WriteNumber("size", font is { } sized ? sized.Size : style.FontSize);
                writer.WriteNumber("weight", font?.Weight ?? style.FontWeight); writer.WriteNumber("italic", font?.Style ?? style.FontStyle);
                if (font is { } resolved) writer.WriteNumber("width", resolved.Width);
                WriteColor("foreground", style.Foreground); WriteColor("background", style.Background);
                writer.WriteNumber("underline", style.Underline);
                WriteColor("underlineColor", style.UnderlineColor); writer.WriteNumber("strike", style.Strikethrough);
                WriteColor("strikeColor", style.StrikethroughColor); writer.WriteNumber("alignment", style.Alignment);
                writer.WriteNumber("direction", style.Direction); writer.WriteString("language", style.Language);
                writer.WriteEndObject(); writer.Flush();
                if (stream.Length > MaximumStylePayloadLength) return null;
            }
            writer.WriteEndArray(); writer.WriteEndObject(); writer.Flush();
            void WriteColor(string name, uint? color) { if (color is { } value) writer.WriteNumber(name, value); }
        }
        if (stream.Length > MaximumStylePayloadLength) return null;
        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
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

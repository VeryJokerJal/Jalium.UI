using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Automation.MacOS;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Input;
using Jalium.UI.Media;

namespace Jalium.UI;

public partial class Window
{
    private UIElement? _macOSTextServicesTarget;
    private IAutomationTextProviderSource? _macOSTextServicesSource;
    private long _macOSTextServicesEditor;
    private string? _macOSTextServicesSession;
    private int _macOSTextServicesRefresh;
    private int _macOSTextServicesDepth;

    private bool UpdateMacOSTextServicesTarget()
    {
        int refresh = unchecked(++_macOSTextServicesRefresh);
        UIElement? target = GetTextInputTarget();
        IAutomationTextProviderSource? source = null;
        if (IsActive && !_isClosing && !_managedTeardownStarted && IsEnabled &&
            target is TextBox or RichTextBox or EditControl && IsMacOSInputTargetValid(target))
        {
            var current = CaptureMacOSInputLifetime(target, textInput: true);
            var peer = UIElementAutomationPeer.FromElement(target) ?? UIElementAutomationPeer.CreatePeerForElement(target);
            if (peer is { } && !peer.IsPassword())
                source = peer.GetPattern(PatternInterface.Text) switch
                {
                    AutomationTextProvider provider => provider.Source,
                    IAutomationTextProviderSource text => text,
                    _ => null
                };
            if (refresh != _macOSTextServicesRefresh || !current()) return false;
        }
        if (source is not IAutomationTextViewSource or not IAutomationTextStyleSource) target = null;
        if (target is null) source = null;
        if (ReferenceEquals(target, _macOSTextServicesTarget) && ReferenceEquals(source, _macOSTextServicesSource))
            return false;
        if (_macOSTextServicesTarget is { } previous) MacOSWritingToolsPresentation.Clear(previous);
        _macOSTextServicesTarget = target;
        _macOSTextServicesSource = source;
        _macOSTextServicesSession = null;
        unchecked { ++_macOSTextServicesEditor; }
        return true;
    }

    private void HandleMacOSTextServicesRequest(PlatformTextServicesRequest request)
    {
        if (!OperatingSystem.IsMacOS() || !Dispatcher.CheckAccess() || request.Kind is < 0 or > 8 ||
            _macOSTextServicesDepth >= 32) return;
        ++_macOSTextServicesDepth;
        try
        {
            using var document = JsonDocument.Parse(request.Payload);
            JsonElement payload = document.RootElement;
            string editor = _macOSTextServicesEditor.ToString(CultureInfo.InvariantCulture);
            string? session = payload.TryGetProperty("session", out var sessionValue) &&
                sessionValue.ValueKind == JsonValueKind.String ? sessionValue.GetString() : null;
            // Cleanup belongs to the captured editor, even after deactivation or
            // disabling. A newer editor must never inherit an older presentation.
            if (request.Kind == 8 && payload.TryGetProperty("stopped", out var stop) && stop.ValueKind == JsonValueKind.True)
            {
                if (session is not null && session == _macOSTextServicesSession &&
                    payload.TryGetProperty("editor", out var old) && old.GetString() == editor &&
                    _macOSTextServicesTarget is { } previous)
                {
                    _macOSTextServicesSession = null;
                    MacOSWritingToolsPresentation.Clear(previous);
                }
                Reply(new MacOSTextServicesResponse { Editor = editor, Session = session });
                return;
            }
            UpdateMacOSTextServicesTarget();
            editor = _macOSTextServicesEditor.ToString(CultureInfo.InvariantCulture);
            UIElement? target = _macOSTextServicesTarget;
            var source = _macOSTextServicesSource;
            if (target is null || source is not IAutomationTextViewSource view)
            {
                if (request.Kind == 0) Reply(new MacOSTextServicesResponse { Editor = editor, CanRead = false });
                return;
            }
            if (request.Kind != 0)
            {
                if (!payload.TryGetProperty("editor", out var requestedEditor) || requestedEditor.GetString() != editor ||
                    !Guid.TryParseExact(session, "D", out _)) return;
                if (session != _macOSTextServicesSession)
                {
                    if (request.Kind is not (1 or 8)) return;
                    MacOSWritingToolsPresentation.Clear(target);
                    _macOSTextServicesSession = session;
                }
            }
            var lifetime = CaptureMacOSInputLifetime(target, textInput: true);
            bool IsCurrent() => IsActive && !_isClosing && !_managedTeardownStarted && lifetime() &&
                _macOSTextServicesEditor.ToString(CultureInfo.InvariantCulture) == editor &&
                (request.Kind == 0 || _macOSTextServicesSession == session) &&
                ReferenceEquals(target, _macOSTextServicesTarget) && ReferenceEquals(source, _macOSTextServicesSource);
            if (!IsCurrent() || InputMethod.IsComposing) return;
            var response = new MacOSTextServicesResponse { Editor = editor, Session = session };
            string before = source.Text;
            if (!IsCurrent()) return;
            MacOSWritingToolsPresentation.ObserveText(target, before);
            switch (request.Kind)
            {
                case 0:
                    response.CanRead = true;
                    response.CanEdit = !source.IsReadOnly;
                    response.ResultOptions = target is RichTextBox ? 15 : 1;
                    break;
                case 1:
                {
                    string? styles = MacOSAccessibilityTree.CreateTextStyleSnapshot(source, 0, before.Length, IsCurrent);
                    if (styles is null || source.Text != before || !IsCurrent()) return;
                    var visible = AutomationTextNavigation.VisibleRanges(source, view);
                    int first = visible.Count == 0 ? 0 : visible.Min(range => range.Start);
                    int last = visible.Count == 0 ? 0 : visible.Max(range => range.Start + range.Length);
                    response.Text16 = EncodeText(before);
                    response.Styles = styles;
                    if (target is RichTextBox rich) response.Structure = rich.GetWritingToolsStructure();
                    response.Selection = new[] { source.SelectionStart, source.SelectionLength };
                    response.Visible = new[] { first, last - first };
                    response.UndoRedo = MacOSWritingToolsPresentation.IsUndoRedo;
                    break;
                }
                case 2:
                {
                    if (source.IsReadOnly || !ValidRange(before, request.Start, request.Length)) return;
                    string proposed = DecodeText(payload.GetProperty("text16").GetString()!);
                    var preview = new TextCompositionEventArgs(UIElement.PreviewTextInputEvent, proposed, Environment.TickCount)
                    {
                        ImeReplacementRange = (request.Start, request.Length),
                        NativeTextServicesPayload = request.Payload,
                        NativeEditLifetime = IsCurrent
                    };
                    bool CanBeginEdit() => IsCurrent() && source.Text == before;
                    InputRoute.Raise(target, preview, CanBeginEdit);
                    if (!IsCurrent()) return;
                    bool handled = preview.Handled;
                    if (!handled)
                    {
                        if (!CanBeginEdit()) return;
                        var input = new TextCompositionEventArgs(UIElement.TextInputEvent, proposed, Environment.TickCount)
                        {
                            ImeReplacementRange = (request.Start, request.Length),
                            NativeTextServicesPayload = request.Payload,
                            NativeEditLifetime = IsCurrent
                        };
                        InputRoute.Raise(target, input, CanBeginEdit);
                        handled = input.Handled;
                    }
                    if (!handled || !IsCurrent()) return;
                    string after = source.Text;
                    int suffix = before.Length - request.Start - request.Length;
                    if (after.Length < request.Start + suffix ||
                        !after.AsSpan(0, request.Start).SequenceEqual(before.AsSpan(0, request.Start)) ||
                        !after.AsSpan(after.Length - suffix).SequenceEqual(before.AsSpan(before.Length - suffix))) return;
                    int acceptedLength = after.Length - request.Start - suffix;
                    string? actualStyles = MacOSAccessibilityTree.CreateTextStyleSnapshot(source, request.Start, acceptedLength, IsCurrent);
                    if (actualStyles is null || !IsCurrent() || source.Text != after) return;
                    response.Applied = true;
                    response.Text16 = EncodeText(after.Substring(request.Start, acceptedLength));
                    response.Styles = actualStyles;
                    if (target is RichTextBox rich) response.Structure = rich.GetWritingToolsStructure(request.Start, acceptedLength);
                    MacOSWritingToolsPresentation.ObserveText(target, after);
                    _hasLastPlatformImeContext = false;
                    RefreshLinuxImeContext();
                    break;
                }
                case 3:
                {
                    var ranges = payload.GetProperty("ranges");
                    // The framework's current text providers expose a single
                    // selection. Never silently union disjoint system selections.
                    if (ranges.GetArrayLength() != 1) return;
                    int start = ranges[0][0].GetInt32(), length = ranges[0][1].GetInt32();
                    if (!ValidRange(before, start, length)) return;
                    source.Select(start, length);
                    if (!IsCurrent()) return;
                    source.ScrollIntoView(start, length);
                    response.Applied = IsCurrent();
                    break;
                }
                case 4:
                {
                    if (request.Flags == 1)
                    {
                        var point = payload.GetProperty("point");
                        var transform = GetMacOSInputTransform(target);
                        if (!transform.TryInvert(out var inverse) || !view.TryGetInsertionIndex(
                            inverse.Transform(new Point(point[0].GetDouble() / _dpiScale, point[1].GetDouble() / _dpiScale)), out int index))
                            return;
                        response.Index = index;
                    }
                    else
                    {
                        if (!ValidRange(before, request.Start, request.Length)) return;
                        response.Rects = GetWritingToolsRectangles(target, source, request.Start, request.Length);
                    }
                    break;
                }
                case 5:
                {
                    if (!ValidRange(before, request.Start, request.Length)) return;
                    Rect? requested = null;
                    if (payload.TryGetProperty("rect", out var rect))
                        requested = new Rect(rect[0].GetDouble() / _dpiScale, rect[1].GetDouble() / _dpiScale,
                            rect[2].GetDouble() / _dpiScale, rect[3].GetDouble() / _dpiScale);
                    var preview = MacOSWritingToolsPresentation.CreatePreview(target, source,
                        request.Start, request.Length, GetMacOSInputTransform(target), _dpiScale, requested, IsCurrent);
                    if (preview is null) return;
                    response.Png = Convert.ToBase64String(preview.Png);
                    response.Frame = ToPixels(preview.Frame);
                    response.Rects = preview.Rectangles.Select(ToPixels).ToArray();
                    break;
                }
                case 6:
                    if (!ValidRange(before, request.Start, request.Length)) return;
                    MacOSWritingToolsPresentation.Prepare(target, source, AnimationKey(payload),
                        request.Start, request.Length, request.Flags, IsCurrent);
                    break;
                case 7:
                    MacOSWritingToolsPresentation.Finish(target, AnimationKey(payload));
                    break;
                case 8:
                    MacOSWritingToolsPresentation.SetState(target, source, request.Flags, payload, IsCurrent);
                    break;
            }
            if (IsCurrent() && (request.Kind == 2 || source.Text == before)) Reply(response);

            void Reply(MacOSTextServicesResponse value) => request.Response = JsonSerializer.SerializeToUtf8Bytes(
                value, MacOSTextServicesJsonContext.Default.MacOSTextServicesResponse);
        }
        finally { --_macOSTextServicesDepth; }
    }

    private double[][] GetWritingToolsRectangles(UIElement target, IAutomationTextProviderSource source, int start, int length)
    {
        Matrix transform = GetMacOSInputTransform(target);
        return source.GetBoundingRectangles(start, length).Select(rect => TransformImeRectangle(rect, transform))
            .Where(IsFiniteImeRectangle).Select(ToPixels).ToArray();
    }

    private double[] ToPixels(Rect rect) => new[]
        { rect.X * _dpiScale, rect.Y * _dpiScale, rect.Width * _dpiScale, rect.Height * _dpiScale };
    private static string AnimationKey(JsonElement payload) =>
        payload.GetProperty("context").GetString() + ":" + payload.GetProperty("animationKey").GetString();
    private static string EncodeText(string text) => Convert.ToBase64String(MemoryMarshal.AsBytes(text.AsSpan()));
    private static string DecodeText(string encoded)
    {
        byte[] bytes = Convert.FromBase64String(encoded);
        if (bytes.Length % 2 != 0 || bytes.Length > 32 * 1024 * 1024) throw new FormatException("Invalid UTF-16 text service payload.");
        return new string(MemoryMarshal.Cast<byte, char>(bytes));
    }
    private static bool ValidRange(string text, int start, int length) =>
        ImeTextEncoding.TryNormalizeUtf16Range(text, start, length, out int normalizedStart, out int normalizedLength) &&
        start == normalizedStart && length == normalizedLength;
}


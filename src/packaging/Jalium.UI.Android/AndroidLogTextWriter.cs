using System.Runtime.Versioning;
using System.Text;

namespace Jalium.UI;

/// <summary>
/// Line-buffered <see cref="TextWriter"/> that forwards console output to the
/// Android log. Android discards a managed process's stdout/stderr, while the
/// framework reports its rendering and lifecycle diagnostics (render-target
/// bails, backend fallbacks, frame skips, bridge callback failures) exclusively
/// through <c>Console.Error</c>/<c>Console.Out</c> — without this redirect the
/// entire evidence chain for a black-screen device is invisible in logcat.
/// Installed once per process by <see cref="JaliumActivity.OnCreate"/>.
/// </summary>
[SupportedOSPlatform("android24.0")]
internal sealed class AndroidLogTextWriter : TextWriter
{
    private const string Tag = "JaliumUI";
    // logcat truncates entries near 4 KiB of UTF-8 payload, but this buffer
    // counts UTF-16 chars — CJK text encodes at ~3 bytes per char, so a
    // char-counted threshold near 4000 would still let an all-CJK line be
    // truncated. Flush at 1300 chars (conservative 3-bytes-per-char estimate,
    // 1300 × 3 = 3900 < 4 KiB) so even worst-case lines land intact.
    private const int MaxBufferedLineLength = 1300;

    private readonly object _gate = new();
    private readonly StringBuilder _line = new();
    private readonly Android.Util.LogPriority _priority;

    public AndroidLogTextWriter(Android.Util.LogPriority priority)
    {
        _priority = priority;
    }

    public override Encoding Encoding => Encoding.UTF8;

    public override void Write(char value)
    {
        lock (_gate)
        {
            AppendLocked(value);
        }
    }

    public override void Write(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return;
        }

        lock (_gate)
        {
            AppendLocked(value);
        }
    }

    // Kept atomic under one lock so lines from concurrent writers (UI thread,
    // render worker, timer threads) never interleave inside a logcat entry.
    public override void WriteLine(string? value)
    {
        lock (_gate)
        {
            if (!string.IsNullOrEmpty(value))
            {
                AppendLocked(value);
            }

            FlushLineLocked();
        }
    }

    public override void Flush()
    {
        lock (_gate)
        {
            FlushLineLocked();
        }
    }

    private void AppendLocked(string value)
    {
        foreach (char c in value)
        {
            AppendLocked(c);
        }
    }

    private void AppendLocked(char value)
    {
        if (value == '\n')
        {
            FlushLineLocked();
            return;
        }

        if (value == '\r')
        {
            return;
        }

        _line.Append(value);
        if (_line.Length >= MaxBufferedLineLength)
        {
            FlushLineLocked();
        }
    }

    private void FlushLineLocked()
    {
        // Blank lines (including a Flush with nothing buffered) are dropped —
        // they carry no diagnostic value and would only pad logcat.
        if (_line.Length == 0)
        {
            return;
        }

        string text = _line.ToString();
        _line.Clear();
        Android.Util.Log.WriteLine(_priority, Tag, text);
    }
}

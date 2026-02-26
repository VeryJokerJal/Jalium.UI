namespace Jalium.UI.Controls.Editor;

/// <summary>
/// Manages the caret position, blinking animation, and desired column for vertical navigation.
/// </summary>
internal sealed class CaretManager
{
    private int _offset;
    private int _desiredColumn = -1;
    private bool _isBlinking;
    private DateTime _lastResetTime;

    private const double BlinkIntervalMs = 530;

    /// <summary>
    /// Gets or sets the document offset of the caret.
    /// </summary>
    public int Offset
    {
        get => _offset;
        set
        {
            if (_offset != value)
            {
                _offset = value;
                OffsetChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>
    /// Gets or sets the desired column for vertical navigation (preserved across up/down moves).
    /// Set to -1 to recalculate from current position.
    /// </summary>
    public int DesiredColumn
    {
        get => _desiredColumn;
        set => _desiredColumn = value;
    }

    /// <summary>
    /// Gets the current blink opacity (1.0 = visible, 0.0 = hidden).
    /// </summary>
    public double Opacity
    {
        get
        {
            if (!_isBlinking)
                return 1.0;

            var elapsed = (DateTime.UtcNow - _lastResetTime).TotalMilliseconds;
            var cyclePos = elapsed % (BlinkIntervalMs * 2);
            return cyclePos < BlinkIntervalMs ? 1.0 : 0.0;
        }
    }

    /// <summary>
    /// Gets the preferred timer cadence for caret blinking.
    /// </summary>
    public TimeSpan BlinkInterval => TimeSpan.FromMilliseconds(BlinkIntervalMs);

    /// <summary>
    /// Gets whether the caret is visible (after focus gain, always show for a bit).
    /// </summary>
    public bool IsVisible => true; // Controlled by focus state in EditControl

    /// <summary>
    /// Occurs when the caret offset changes.
    /// </summary>
    public event EventHandler? OffsetChanged;

    /// <summary>
    /// Resets the blink timer (shows caret immediately after movement).
    /// </summary>
    public void ResetBlink()
    {
        _lastResetTime = DateTime.UtcNow;
    }

    /// <summary>
    /// Starts the blinking animation.
    /// </summary>
    public void StartBlinking()
    {
        _isBlinking = true;
        _lastResetTime = DateTime.UtcNow;
    }

    /// <summary>
    /// Stops the blinking animation (caret stays visible).
    /// </summary>
    public void StopBlinking()
    {
        _isBlinking = false;
    }

    /// <summary>
    /// Gets the line and column of the caret within the document.
    /// </summary>
    public (int line, int column) GetLineColumn(TextDocument document)
    {
        if (document.TextLength == 0)
            return (1, 0);

        int clampedOffset = Math.Clamp(_offset, 0, document.TextLength);
        var docLine = document.GetLineByOffset(clampedOffset);
        return (docLine.LineNumber, clampedOffset - docLine.Offset);
    }

    /// <summary>
    /// Clamps caret state to valid document bounds.
    /// </summary>
    public void CoerceToDocument(TextDocument document)
    {
        Offset = Math.Clamp(_offset, 0, document.TextLength);
        if (_desiredColumn < -1)
            _desiredColumn = -1;
    }
}

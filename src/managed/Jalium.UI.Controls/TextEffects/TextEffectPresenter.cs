using System;
using System.Collections.Generic;
using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.TextEffects.Effects;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Threading;
using Jalium.UI.Interop;

namespace Jalium.UI.Controls.TextEffects;

/// <summary>
/// A text surface that runs a per-character animation pipeline. Mutations
/// (<see cref="AppendText"/>, <see cref="InsertText"/>, <see cref="RemoveText"/>,
/// <see cref="ClearText"/>, assignment to <see cref="Text"/>) drive grapheme-level
/// cells through an enter / shift / exit state machine, and the attached
/// <see cref="ITextEffect"/> decides the per-frame visuals.
/// </summary>
/// <remarks>
/// <para>
/// The default <see cref="TextEffect"/> is <see cref="RiseSettleEffect"/>, which
/// raises new cells from below with an overshoot-and-settle curve while they
/// come into focus from a soft blur, drifts old cells upwards as they dissipate,
/// and slides unchanged neighbours to new positions with a plain ease-out.
/// </para>
/// <para>
/// <b>Two animation layers, cleanly separated:</b>
/// </para>
/// <list type="bullet">
///   <item>
///     <term><see cref="TextEffect"/> (this property)</term>
///     <description>Per-cell CPU pipeline. Controls the enter / shift / exit
///     animation of each grapheme independently. Sees cell identity, phase, and
///     stagger index.</description>
///   </item>
///   <item>
///     <term><see cref="UIElement.Effect"/> (inherited)</term>
///     <description>Whole-element GPU pass. Captures the fully-animated text to
///     an offscreen texture and runs a pixel shader over it. Use
///     <see cref="Jalium.UI.Media.Effects.BlurEffect"/> for a GPU gaussian blur,
///     <see cref="Jalium.UI.Media.Effects.DropShadowEffect"/> for a shadow, or
///     subclass <see cref="Jalium.UI.Media.Effects.ShaderEffect"/> with your own
///     HLSL DXBC bytecode for arbitrary filters (glow, scanline, chromatic
///     aberration, etc.). The shader sees the final composited frame; it cannot
///     receive per-cell uniforms.</description>
///   </item>
/// </list>
/// <para>
/// The two layers compose: per-cell motion first, then the GPU shader on top of
/// the result. Setting one never touches the other.
/// </para>
/// </remarks>
public partial class TextEffectPresenter : FrameworkElement
{

    /// <summary>
    /// Identifies the <see cref="Text"/> dependency property. Assigning replaces
    /// all current content: every existing cell is exited, every new grapheme
    /// is entered.
    /// </summary>
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(TextEffectPresenter),
            new PropertyMetadata(string.Empty, OnTextPropertyChanged));

    /// <summary>
    /// Identifies the <see cref="FontFamily"/> dependency property. Inherits from
    /// ancestors and is shared with <see cref="Jalium.UI.Documents.TextElement"/>.
    /// </summary>
    public static readonly DependencyProperty FontFamilyProperty =
        Jalium.UI.Documents.TextElement.FontFamilyProperty.AddOwner(typeof(TextEffectPresenter),
            new FrameworkPropertyMetadata(
                SystemFonts.MessageFontFamily,
                FrameworkPropertyMetadataOptions.Inherits,
                OnFontPropertyChanged));

    /// <summary>
    /// Identifies the <see cref="FontSize"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty FontSizeProperty =
        Jalium.UI.Documents.TextElement.FontSizeProperty.AddOwner(typeof(TextEffectPresenter),
            new PropertyMetadata(20.0, OnFontPropertyChanged, null, inherits: true));

    /// <summary>
    /// Identifies the <see cref="FontStyle"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty FontStyleProperty =
        Jalium.UI.Documents.TextElement.FontStyleProperty.AddOwner(typeof(TextEffectPresenter),
            new PropertyMetadata(FontStyles.Normal, OnFontPropertyChanged, null, inherits: true));

    /// <summary>
    /// Identifies the <see cref="FontWeight"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty FontWeightProperty =
        Jalium.UI.Documents.TextElement.FontWeightProperty.AddOwner(typeof(TextEffectPresenter),
            new PropertyMetadata(FontWeights.Normal, OnFontPropertyChanged, null, inherits: true));

    /// <summary>
    /// Identifies the <see cref="Foreground"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty ForegroundProperty =
        Jalium.UI.Documents.TextElement.ForegroundProperty.AddOwner(typeof(TextEffectPresenter),
            new PropertyMetadata(new SolidColorBrush(Colors.White), OnVisualPropertyChanged, null, inherits: true));

    /// <summary>
    /// Identifies the <see cref="TextAlignment"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty TextAlignmentProperty =
        DependencyProperty.Register(nameof(TextAlignment), typeof(TextAlignment), typeof(TextEffectPresenter),
            new PropertyMetadata(TextAlignment.Left, OnVisualPropertyChanged));

    /// <summary>
    /// Identifies the <see cref="LineHeight"/> dependency property. When NaN, the
    /// line height is derived from the font metrics.
    /// </summary>
    public static readonly DependencyProperty LineHeightProperty =
        DependencyProperty.Register(nameof(LineHeight), typeof(double), typeof(TextEffectPresenter),
            new PropertyMetadata(double.NaN, OnFontPropertyChanged));

    /// <summary>
    /// Identifies the <see cref="TextWrapping"/> dependency property.
    /// <see cref="TextWrapping.NoWrap"/> (default) lays every grapheme in a single
    /// line; <see cref="TextWrapping.Wrap"/> inserts line breaks at whitespace and
    /// at CJK boundaries when the line would exceed the available width.
    /// </summary>
    public static readonly DependencyProperty TextWrappingProperty =
        DependencyProperty.Register(nameof(TextWrapping), typeof(TextWrapping), typeof(TextEffectPresenter),
            new PropertyMetadata(TextWrapping.NoWrap, OnLayoutPropertyChanged));

    /// <summary>
    /// Identifies the <see cref="TextEffect"/> dependency property. Drives the
    /// per-character (grapheme-level) animation pipeline. Setting to null disables
    /// all animation — mutations take effect instantly.
    /// </summary>
    /// <remarks>
    /// This is deliberately <b>separate</b> from <see cref="UIElement.Effect"/>:
    /// <see cref="UIElement.Effect"/> is a whole-element GPU filter (e.g.
    /// <see cref="Jalium.UI.Media.Effects.BlurEffect"/>,
    /// <see cref="Jalium.UI.Media.Effects.ShaderEffect"/>), while
    /// <see cref="TextEffect"/> is the per-cell animation driver. The two compose:
    /// first <see cref="TextEffect"/> positions and fades each glyph on CPU, then
    /// <see cref="UIElement.Effect"/> captures the whole rendered surface and
    /// pipes it through a GPU shader pass.
    /// </remarks>
    public static readonly DependencyProperty TextEffectProperty =
        DependencyProperty.Register(nameof(TextEffect), typeof(ITextEffect), typeof(TextEffectPresenter),
            new PropertyMetadata(null, OnTextEffectPropertyChanged));

    /// <summary>
    /// Identifies the <see cref="MaxCells"/> dependency property. When exceeded,
    /// the oldest non-exiting cells are force-exited, protecting long-running
    /// lyric / log scenarios from unbounded memory growth.
    /// </summary>
    public static readonly DependencyProperty MaxCellsProperty =
        DependencyProperty.Register(nameof(MaxCells), typeof(int), typeof(TextEffectPresenter),
            new PropertyMetadata(4096, OnMaxCellsChanged));

    /// <summary>
    /// Identifies the <see cref="AnimationSpeedRatio"/> dependency property.
    /// Multiplies all phase durations. 1.0 = default, 2.0 = twice as fast, 0.5 = half.
    /// </summary>
    public static readonly DependencyProperty AnimationSpeedRatioProperty =
        DependencyProperty.Register(nameof(AnimationSpeedRatio), typeof(double), typeof(TextEffectPresenter),
            new PropertyMetadata(1.0));

    /// <summary>
    /// Identifies the <see cref="IsAnimationEnabled"/> dependency property. When
    /// false, all mutations skip straight to their final state — useful for
    /// reduced-motion scenarios and unit tests.
    /// </summary>
    public static readonly DependencyProperty IsAnimationEnabledProperty =
        DependencyProperty.Register(nameof(IsAnimationEnabled), typeof(bool), typeof(TextEffectPresenter),
            new PropertyMetadata(true, OnAnimationEnabledChanged));



    /// <summary>
    /// Identifies the <see cref="TextChanged"/> routed event.
    /// </summary>
    public static readonly RoutedEvent TextChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(TextChanged), RoutingStrategy.Bubble,
            typeof(RoutedEventHandler), typeof(TextEffectPresenter));

    /// <summary>
    /// Identifies the <see cref="CellPhaseChanged"/> routed event, raised when
    /// any cell transitions between lifecycle phases.
    /// </summary>
    public static readonly RoutedEvent CellPhaseChangedEvent =
        EventManager.RegisterRoutedEvent(nameof(CellPhaseChanged), RoutingStrategy.Direct,
            typeof(RoutedEventHandler), typeof(TextEffectPresenter));



    private readonly List<TextEffectCell> _cells = new();
    private readonly List<TextEffectCell> _exitingCells = new();
    private readonly Dictionary<string, double> _widthCache = new(StringComparer.Ordinal);
    private long _nextCellId;
    private int _nextBatchId;
    private int _batchDepth;
    private bool _layoutDirty = true;
    private bool _renderingSubscribed;
    private bool _isAttached;
    private double _elapsedMs;
    private long _lastTickUtc;



    /// <summary>
    /// Initializes a new instance of the <see cref="TextEffectPresenter"/> class.
    /// </summary>
    public TextEffectPresenter()
    {
        Focusable = false;
        KeyboardNavigation.SetIsTabStop(this, false);
        TextEffect = new RiseSettleEffect();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <inheritdoc />
    protected override Jalium.UI.Automation.Peers.AutomationPeer? OnCreateAutomationPeer()
    {
        return new TextEffectPresenterAutomationPeer(this);
    }



    /// <summary>Gets or sets the full text content. See <see cref="TextProperty"/>.</summary>
    public string Text
    {
        get => (string)(GetValue(TextProperty) ?? string.Empty);
        set => SetValue(TextProperty, value ?? string.Empty);
    }

    /// <summary>Gets or sets the font family.</summary>
    public FontFamily FontFamily
    {
        get => (FontFamily)GetValue(FontFamilyProperty)!;
        set => SetValue(FontFamilyProperty, value);
    }

    /// <summary>Gets or sets the font size in pixels.</summary>
    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty)!;
        set => SetValue(FontSizeProperty, value);
    }

    /// <summary>Gets or sets the font style.</summary>
    public FontStyle FontStyle
    {
        get => GetValue(FontStyleProperty) is FontStyle fs ? fs : FontStyles.Normal;
        set => SetValue(FontStyleProperty, value);
    }

    /// <summary>Gets or sets the font weight.</summary>
    public FontWeight FontWeight
    {
        get => GetValue(FontWeightProperty) is FontWeight fw ? fw : FontWeights.Normal;
        set => SetValue(FontWeightProperty, value);
    }

    /// <summary>Gets or sets the brush used to fill glyphs.</summary>
    public Brush? Foreground
    {
        get => (Brush?)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>Gets or sets the horizontal text alignment within the control.</summary>
    public TextAlignment TextAlignment
    {
        get => (TextAlignment)GetValue(TextAlignmentProperty)!;
        set => SetValue(TextAlignmentProperty, value);
    }

    /// <summary>Gets or sets the explicit line height; NaN = derive from font.</summary>
    public double LineHeight
    {
        get => (double)GetValue(LineHeightProperty)!;
        set => SetValue(LineHeightProperty, value);
    }

    /// <summary>
    /// Gets or sets whether text wraps at the available width. See
    /// <see cref="TextWrappingProperty"/>.
    /// </summary>
    public TextWrapping TextWrapping
    {
        get => (TextWrapping)GetValue(TextWrappingProperty)!;
        set => SetValue(TextWrappingProperty, value);
    }

    /// <summary>
    /// Gets or sets the per-cell animation driver. Defaults to a new
    /// <see cref="RiseSettleEffect"/>. Setting to <c>null</c> disables animation
    /// and all mutations apply instantly. See <see cref="TextEffectProperty"/>
    /// for how this relates to <see cref="UIElement.Effect"/>.
    /// </summary>
    public ITextEffect? TextEffect
    {
        get => (ITextEffect?)GetValue(TextEffectProperty);
        set => SetValue(TextEffectProperty, value);
    }

    /// <summary>Gets or sets the upper bound on live cells.</summary>
    public int MaxCells
    {
        get => (int)GetValue(MaxCellsProperty)!;
        set => SetValue(MaxCellsProperty, Math.Max(1, value));
    }

    /// <summary>Gets or sets the global animation speed ratio.</summary>
    public double AnimationSpeedRatio
    {
        get => (double)GetValue(AnimationSpeedRatioProperty)!;
        set => SetValue(AnimationSpeedRatioProperty, Math.Max(0.01, value));
    }

    /// <summary>Gets or sets whether mutations animate or apply instantly.</summary>
    public bool IsAnimationEnabled
    {
        get => (bool)GetValue(IsAnimationEnabledProperty)!;
        set => SetValue(IsAnimationEnabledProperty, value);
    }

    /// <summary>
    /// Gets a snapshot of the currently-live cells (ordered by layout position).
    /// Does not include cells that are still running their exit animation.
    /// </summary>
    public IReadOnlyList<TextEffectCell> Cells => _cells;



    /// <summary>
    /// Occurs after any mutation that changed the cell list. Bubbles.
    /// </summary>
    public event RoutedEventHandler TextChanged
    {
        add => AddHandler(TextChangedEvent, value);
        remove => RemoveHandler(TextChangedEvent, value);
    }

    /// <summary>
    /// Occurs when any cell transitions between lifecycle phases. Direct.
    /// </summary>
    public event RoutedEventHandler CellPhaseChanged
    {
        add => AddHandler(CellPhaseChangedEvent, value);
        remove => RemoveHandler(CellPhaseChangedEvent, value);
    }

    /// <summary>
    /// Raised once all cells settle into <see cref="TextEffectCellPhase.Visible"/>
    /// and no exiting cells remain. Not a routed event because it is a control-wide
    /// state signal, not a cell-level notification.
    /// </summary>
    public event EventHandler? AnimationIdle;



    /// <summary>
    /// Removes all content. Existing cells transition to
    /// <see cref="TextEffectCellPhase.Exiting"/>.
    /// </summary>
    public void ClearText()
    {
        if (_cells.Count == 0 && _exitingCells.Count == 0)
        {
            return;
        }

        var batchId = _nextBatchId++;
        var size = _cells.Count;
        for (int i = 0; i < _cells.Count; i++)
        {
            BeginExit(_cells[i], batchId, i, size);
        }

        _exitingCells.AddRange(_cells);
        _cells.Clear();

        InvalidateLayoutAndPaint();
        RaiseTextChanged();
        EnsureRenderingSubscription();
    }

    /// <summary>
    /// Appends <paramref name="text"/> at the end. Existing cells are untouched;
    /// each new grapheme enters as one cell, sharing a common stagger batch.
    /// </summary>
    public void AppendText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        InsertTextInternal(_cells.Count, text);
    }

    /// <summary>
    /// Appends <paramref name="text"/> and a single newline. Shorthand for
    /// <c>AppendText(text + "\n")</c>.
    /// </summary>
    public void AppendTextLine(string text = "")
    {
        AppendText((text ?? string.Empty) + "\n");
    }

    /// <summary>
    /// Inserts <paramref name="text"/> at <paramref name="cellIndex"/>. Cells at or
    /// after <paramref name="cellIndex"/> begin <see cref="TextEffectCellPhase.Shifting"/>
    /// to their new positions; inserted cells start <see cref="TextEffectCellPhase.Entering"/>.
    /// </summary>
    /// <param name="cellIndex">Insert position in cell (grapheme) space. Clamped to [0, Cells.Count].</param>
    /// <param name="text">Text to insert; may be empty (no-op).</param>
    public void InsertText(int cellIndex, string text)
    {
        InsertTextInternal(cellIndex, text);
    }

    /// <summary>
    /// Removes <paramref name="cellCount"/> cells starting at <paramref name="cellIndex"/>.
    /// Removed cells enter <see cref="TextEffectCellPhase.Exiting"/>; cells after
    /// the removed span shift to fill the gap.
    /// </summary>
    /// <param name="cellIndex">Start index in cell space. Clamped.</param>
    /// <param name="cellCount">Number of cells to remove. Clamped to what's available.</param>
    public void RemoveText(int cellIndex, int cellCount)
    {
        if (_cells.Count == 0 || cellCount <= 0)
        {
            return;
        }

        var start = Math.Clamp(cellIndex, 0, _cells.Count);
        var end = Math.Min(_cells.Count, start + cellCount);
        var actualCount = end - start;
        if (actualCount <= 0)
        {
            return;
        }

        var batchId = _nextBatchId++;
        var shiftBatchId = _nextBatchId++;

        // Capture pre-edit positions for the cells that will shift, so Shifting
        // animations interpolate from where the user last saw them rather than
        // from the new collapsed layout.
        var remainingAfter = _cells.Count - end;
        for (int i = 0; i < remainingAfter; i++)
        {
            var cell = _cells[end + i];
            cell.ShiftOriginX = cell.Bounds.X;
            cell.ShiftOriginY = cell.Bounds.Y;
        }

        // Exit the removed span.
        for (int i = 0; i < actualCount; i++)
        {
            var cell = _cells[start + i];
            BeginExit(cell, batchId, i, actualCount);
            _exitingCells.Add(cell);
        }

        _cells.RemoveRange(start, actualCount);

        // Start Shifting on the cells behind the removal.
        for (int i = 0; i < remainingAfter; i++)
        {
            BeginShift(_cells[start + i], shiftBatchId, i, remainingAfter);
        }

        InvalidateLayoutAndPaint();
        RaiseTextChanged();
        EnsureRenderingSubscription();
    }

    /// <summary>
    /// Replaces a span of cells with new text. Equivalent to
    /// <see cref="RemoveText"/> + <see cref="InsertText"/> at the same index, with
    /// the two edits merged into one render pass.
    /// </summary>
    public void ReplaceText(int cellIndex, int cellCount, string replacement)
    {
        using (BeginBatchEdit())
        {
            RemoveText(cellIndex, cellCount);
            if (!string.IsNullOrEmpty(replacement))
            {
                InsertText(Math.Min(cellIndex, _cells.Count), replacement);
            }
        }
    }

    /// <summary>
    /// Coalesces multiple mutations into a single layout + paint cycle. Dispose
    /// the returned token to commit.
    /// </summary>
    public IDisposable BeginBatchEdit()
    {
        _batchDepth++;
        return new BatchEditToken(this);
    }

    private sealed class BatchEditToken : IDisposable
    {
        private TextEffectPresenter? _owner;
        public BatchEditToken(TextEffectPresenter owner) { _owner = owner; }

        public void Dispose()
        {
            var owner = _owner;
            _owner = null;
            if (owner == null) return;
            owner._batchDepth--;
            if (owner._batchDepth <= 0)
            {
                owner._batchDepth = 0;
                owner.InvalidateLayoutAndPaint();
                // Every mutation that ran inside this batch hit
                // EnsureRenderingSubscription and bailed out (batchDepth > 0 by
                // design, so we can coalesce). We have to catch up now —
                // otherwise cells are scheduled Entering/Exiting with nobody
                // ever advancing the clock, and the animation simply never
                // plays. This is the only code path that drives the frame
                // loop on batched edits like Text setter / ReplaceText, which
                // is why "Reset text" and anything that goes through
                // ApplyFullTextReplace used to freeze silently.
                owner.EnsureRenderingSubscription();
            }
        }
    }



    private void InsertTextInternal(int cellIndex, string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var clampedIndex = Math.Clamp(cellIndex, 0, _cells.Count);
        var graphemes = SplitGraphemes(text);
        if (graphemes.Count == 0)
        {
            return;
        }

        var insertBatchId = _nextBatchId++;
        var newCells = new TextEffectCell[graphemes.Count];
        for (int i = 0; i < graphemes.Count; i++)
        {
            var cell = new TextEffectCell(_nextCellId++, graphemes[i], insertBatchId, i, graphemes.Count);
            newCells[i] = cell;
            BeginEnter(cell, insertBatchId, i, graphemes.Count);
        }

        // If inserting in the middle, the cells after the insertion point need
        // to shift to their new positions. Capture their current bounds first.
        var hasTail = clampedIndex < _cells.Count;
        int shiftBatchId = hasTail ? _nextBatchId++ : -1;
        if (hasTail)
        {
            var tailCount = _cells.Count - clampedIndex;
            for (int i = 0; i < tailCount; i++)
            {
                var cell = _cells[clampedIndex + i];
                cell.ShiftOriginX = cell.Bounds.X;
                cell.ShiftOriginY = cell.Bounds.Y;
            }
        }

        _cells.InsertRange(clampedIndex, newCells);

        if (hasTail)
        {
            var tailCount = _cells.Count - (clampedIndex + newCells.Length);
            for (int i = 0; i < tailCount; i++)
            {
                BeginShift(_cells[clampedIndex + newCells.Length + i], shiftBatchId, i, tailCount);
            }
        }

        EnforceMaxCells();
        InvalidateLayoutAndPaint();
        RaiseTextChanged();
        EnsureRenderingSubscription();
    }

    private void BeginEnter(TextEffectCell cell, int batchId, int indexInBatch, int batchSize)
    {
        var effect = TextEffect;
        if (!IsAnimationEnabled || effect == null)
        {
            cell.Phase = TextEffectCellPhase.Visible;
            cell.PhaseStartTimeMs = _elapsedMs;
            cell.PhaseDurationMs = 0;
            cell.PhaseDelayMs = 0;
            return;
        }

        cell.Phase = TextEffectCellPhase.Entering;
        cell.PhaseStartTimeMs = _elapsedMs;
        cell.PhaseDurationMs = Math.Max(1.0, effect.EnterDurationMs / AnimationSpeedRatio);
        cell.PhaseDelayMs = Math.Max(0.0, effect.GetStaggerDelayMs(indexInBatch, batchSize) / AnimationSpeedRatio);
    }

    private void BeginShift(TextEffectCell cell, int batchId, int indexInBatch, int batchSize)
    {
        var effect = TextEffect;
        if (!IsAnimationEnabled || effect == null)
        {
            cell.Phase = TextEffectCellPhase.Visible;
            cell.PhaseDurationMs = 0;
            cell.PhaseDelayMs = 0;
            return;
        }

        cell.Phase = TextEffectCellPhase.Shifting;
        cell.PhaseStartTimeMs = _elapsedMs;
        cell.PhaseDurationMs = Math.Max(1.0, effect.ShiftDurationMs / AnimationSpeedRatio);
        cell.PhaseDelayMs = 0;
    }

    private void BeginExit(TextEffectCell cell, int batchId, int indexInBatch, int batchSize)
    {
        var effect = TextEffect;
        if (!IsAnimationEnabled || effect == null)
        {
            cell.Phase = TextEffectCellPhase.Hidden;
            cell.PhaseDurationMs = 0;
            cell.PhaseDelayMs = 0;
            return;
        }

        cell.Phase = TextEffectCellPhase.Exiting;
        cell.PhaseStartTimeMs = _elapsedMs;
        cell.PhaseDurationMs = Math.Max(1.0, effect.ExitDurationMs / AnimationSpeedRatio);
        cell.PhaseDelayMs = Math.Max(0.0, effect.GetStaggerDelayMs(indexInBatch, batchSize) / AnimationSpeedRatio);
    }

    private void EnforceMaxCells()
    {
        var limit = MaxCells;
        var overflow = _cells.Count - limit;
        if (overflow <= 0)
        {
            return;
        }

        var batchId = _nextBatchId++;
        for (int i = 0; i < overflow; i++)
        {
            var cell = _cells[i];
            BeginExit(cell, batchId, i, overflow);
            _exitingCells.Add(cell);
        }
        _cells.RemoveRange(0, overflow);
    }

    private void AdvanceFrame(double deltaMs)
    {
        if (deltaMs <= 0)
        {
            return;
        }

        _elapsedMs += deltaMs;

        var anyActive = false;
        for (int i = 0; i < _cells.Count; i++)
        {
            if (UpdateCellPhase(_cells[i]))
            {
                anyActive = true;
            }
        }

        for (int i = _exitingCells.Count - 1; i >= 0; i--)
        {
            var cell = _exitingCells[i];
            var alive = UpdateCellPhase(cell);
            if (!alive || cell.Phase == TextEffectCellPhase.Hidden)
            {
                _exitingCells.RemoveAt(i);
            }
            else
            {
                anyActive = true;
            }
        }

        if (!anyActive)
        {
            UnsubscribeRendering();
            AnimationIdle?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Advances the cell's phase state. Returns true if the cell is still animating.
    /// </summary>
    private bool UpdateCellPhase(TextEffectCell cell)
    {
        if (cell.Phase == TextEffectCellPhase.Hidden || cell.Phase == TextEffectCellPhase.Visible)
        {
            return false;
        }

        var elapsedInPhase = _elapsedMs - cell.PhaseStartTimeMs - cell.PhaseDelayMs;
        if (elapsedInPhase < 0)
        {
            return true; // still in stagger delay
        }

        if (elapsedInPhase >= cell.PhaseDurationMs)
        {
            // Phase complete — transition.
            var previous = cell.Phase;
            cell.Phase = previous == TextEffectCellPhase.Exiting
                ? TextEffectCellPhase.Hidden
                : TextEffectCellPhase.Visible;
            RaiseCellPhaseChanged();
            return false;
        }

        return true;
    }

    internal double GetPhaseProgressLinear(TextEffectCell cell)
    {
        if (cell.PhaseDurationMs <= 0)
        {
            return 1.0;
        }

        var elapsed = _elapsedMs - cell.PhaseStartTimeMs - cell.PhaseDelayMs;
        if (elapsed < 0) return 0.0;
        if (elapsed >= cell.PhaseDurationMs) return 1.0;
        return elapsed / cell.PhaseDurationMs;
    }

    internal double GetTimeInPhase(TextEffectCell cell)
    {
        var elapsed = _elapsedMs - cell.PhaseStartTimeMs - cell.PhaseDelayMs;
        return Math.Max(0, elapsed);
    }

    internal double GetTotalTime(TextEffectCell cell) => _elapsedMs - cell.PhaseStartTimeMs;



    private void EnsureRenderingSubscription()
    {
        // Idempotent: if we're already subscribed this is a no-op and the
        // existing _lastTickUtc stays valid (no delta spike).
        if (_renderingSubscribed)
        {
            return;
        }

        // We need two things to drive a frame loop:
        //   1. A live visual tree connection — so InvalidateVisual produces
        //      pixels, and so we don't spin a timer for a presenter no-one
        //      will ever display.
        //   2. A mutation or initial state that actually wants animation —
        //      otherwise the caller is wasting cycles.
        //
        // The Loaded event is the canonical (1) signal, but it is not
        // reliably fired in every hosting scenario — e.g. a presenter assigned
        // as Border.Child via code-behind after the parent is already loaded
        // may miss its own Loaded. The Loaded-only latch produced the
        // "no animation at all, ever" bug: the Text setter runs before
        // Loaded and cells enter Entering with PhaseStartTimeMs=0; if
        // Loaded subsequently never fires, EnsureRenderingSubscription is
        // gated off forever and every mutation after that sits frozen.
        //
        // Falling back to `VisualParent != null` gives us a dependable "I am
        // attached to something" signal that doesn't require the event
        // round-trip. Tests construct presenters without a parent and skip
        // the subscription naturally (keeping AdvanceFrameForTesting
        // deterministic), while production presenters assigned into a tree
        // subscribe as soon as the first mutation asks.
        if (!_isAttached && VisualParent != null)
        {
            _isAttached = true;
        }
        if (!_isAttached)
        {
            return;
        }

        _lastTickUtc = Environment.TickCount64;
        CompositionTarget.Rendering += OnRenderingTick;
        CompositionTarget.Subscribe();
        _renderingSubscribed = true;
    }

    private void UnsubscribeRendering()
    {
        if (!_renderingSubscribed)
        {
            return;
        }

        CompositionTarget.Rendering -= OnRenderingTick;
        CompositionTarget.Unsubscribe();
        _renderingSubscribed = false;
    }

    private void OnRenderingTick(object? sender, EventArgs e)
    {
        var now = Environment.TickCount64;
        var delta = now - _lastTickUtc;
        _lastTickUtc = now;

        AdvanceFrame(delta);
        InvalidateVisual();
    }



    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        _isAttached = true;
        if (HasActiveAnimation())
        {
            EnsureRenderingSubscription();
        }
    }

    private void OnUnloaded(object? sender, RoutedEventArgs e)
    {
        _isAttached = false;
        UnsubscribeRendering();
    }

    /// <summary>
    /// Test hook: manually advances the animation clock by <paramref name="deltaMs"/>.
    /// Bypasses <see cref="CompositionTarget"/> entirely so unit tests can make
    /// deterministic assertions about phase transitions without a real frame loop.
    /// </summary>
    internal void AdvanceFrameForTesting(double deltaMs)
    {
        AdvanceFrame(deltaMs);
    }

    /// <summary>
    /// Test hook: current value of the internal animation clock, in ms.
    /// </summary>
    internal double ElapsedMsForTesting => _elapsedMs;

    /// <summary>
    /// Test hook: drives <see cref="OnRender"/> against a caller-supplied
    /// drawing context so unit tests can observe PushEffect/PopEffect ordering,
    /// opacity/transform pushes, and DrawText calls without a real GPU context.
    /// </summary>
    internal void RenderForTesting(Jalium.UI.Media.DrawingContext dc) => OnRender(dc);

    private bool HasActiveAnimation()
    {
        if (_exitingCells.Count > 0) return true;
        for (int i = 0; i < _cells.Count; i++)
        {
            var p = _cells[i].Phase;
            if (p == TextEffectCellPhase.Entering || p == TextEffectCellPhase.Shifting || p == TextEffectCellPhase.Exiting)
            {
                return true;
            }
        }
        return false;
    }



    private static void OnTextPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextEffectPresenter presenter) return;
        presenter.ApplyFullTextReplace((string?)e.NewValue ?? string.Empty);
    }

    private void ApplyFullTextReplace(string newText)
    {
        // Reconstruct graphemes and diff against the existing cell list to keep
        // identity stable for unchanged prefix — an extremely common case when
        // the caller uses Text setter just to append.
        var newGraphemes = SplitGraphemes(newText);
        var commonPrefix = 0;
        var minLen = Math.Min(newGraphemes.Count, _cells.Count);
        while (commonPrefix < minLen &&
               string.Equals(_cells[commonPrefix].Text, newGraphemes[commonPrefix], StringComparison.Ordinal))
        {
            commonPrefix++;
        }

        using (BeginBatchEdit())
        {
            if (_cells.Count > commonPrefix)
            {
                RemoveText(commonPrefix, _cells.Count - commonPrefix);
            }
            if (newGraphemes.Count > commonPrefix)
            {
                var appended = newText;
                // Take the tail of newText corresponding to the new graphemes.
                var head = string.Concat(newGraphemes.GetRange(0, commonPrefix));
                appended = newText.Substring(head.Length);
                InsertText(commonPrefix, appended);
            }
        }
    }

    private static void OnFontPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextEffectPresenter presenter)
        {
            presenter._widthCache.Clear();
            presenter.InvalidateLayoutAndPaint();
        }
    }

    private static void OnLayoutPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextEffectPresenter presenter)
        {
            presenter.InvalidateLayoutAndPaint();
        }
    }

    private static void OnVisualPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextEffectPresenter presenter)
        {
            presenter.InvalidateVisual();
        }
    }

    private static void OnTextEffectPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextEffectPresenter presenter)
        {
            presenter.InvalidateVisual();
        }
    }

    private static void OnMaxCellsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TextEffectPresenter presenter)
        {
            presenter.EnforceMaxCells();
        }
    }

    private static void OnAnimationEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextEffectPresenter presenter) return;

        if (e.NewValue is bool enabled && !enabled)
        {
            // Skip all in-flight animations to their terminal state.
            for (int i = 0; i < presenter._cells.Count; i++)
            {
                var cell = presenter._cells[i];
                if (cell.Phase != TextEffectCellPhase.Visible)
                {
                    cell.Phase = TextEffectCellPhase.Visible;
                    cell.PhaseDurationMs = 0;
                    cell.PhaseDelayMs = 0;
                }
            }
            presenter._exitingCells.Clear();
            presenter.UnsubscribeRendering();
            presenter.InvalidateVisual();
        }
    }

    private void InvalidateLayoutAndPaint()
    {
        _layoutDirty = true;
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void RaiseTextChanged()
    {
        RaiseEvent(new RoutedEventArgs(TextChangedEvent, this));
    }

    private void RaiseCellPhaseChanged()
    {
        RaiseEvent(new RoutedEventArgs(CellPhaseChangedEvent, this));
    }



    /// <summary>
    /// Returns whether a grapheme cluster is a line break — a lone CR or LF, or a
    /// CR+LF pair (which UAX #29 treats as a single grapheme cluster). Layout uses
    /// this to recognise the cells that force a new line.
    /// </summary>
    private static bool IsLineBreakText(string text)
        => text is "\n" or "\r" or "\r\n";

    /// <summary>
    /// Splits a string into grapheme clusters (user-perceived characters) via the
    /// shared <see cref="GraphemeClusters"/> helper. Emoji — including ZWJ
    /// sequences, skin-tone modifiers and flags — surrogate pairs and combining
    /// marks each map to a single grapheme; a CR+LF pair is one grapheme.
    /// </summary>
    internal static List<string> SplitGraphemes(string text)
        => GraphemeClusters.Split(text);




    /// <summary>
    /// BlurRadius above this threshold routes the cell through the Presenter-level
    /// blur pass; at or below it the cell is drawn directly to the main RT. The
    /// same threshold governs whether <see cref="OnRender"/> opens a blur scope
    /// at all — if the two disagree, cells fall into the gap between "too blurry
    /// for pass 1" and "not blurry enough for pass 2" and disappear, which
    /// manifests as blur terminating early on enter and a one-frame flash on
    /// exit / clear. Deliberately tiny so the hand-off sits at "blur = 0 ↔
    /// blur &gt; 0" — <see cref="GetOrCreatePresenterBlur"/> quantises small
    /// radii up to 1 px so the visual doesn't snap on crossover.
    /// </summary>
    private const double BlurActivationThreshold = 0.01;

    private double _cachedLineHeight;
    private Size _lastDesiredSize;
    private double _lastLaidOutWidth = double.NaN;
    private readonly Dictionary<string, FormattedText> _formattedCache = new(StringComparer.Ordinal);
    private string? _formattedCacheSignature;
    private readonly Dictionary<int, Jalium.UI.Media.Effects.BlurEffect> _presenterBlurCache = new();

    /// <summary>
    /// Recomputes cell bounds if layout is dirty or the constraint width changed
    /// under <see cref="TextWrapping.Wrap"/>. Cells are laid out left-to-right in
    /// reading order; <c>\n</c> cells force a line break; when wrapping is on,
    /// the layout also breaks at whitespace / CJK boundaries when the next cell
    /// would overflow the available width. Exiting cells keep whatever bounds
    /// they had at exit time — they render but don't participate in layout.
    /// </summary>
    private void EnsureLayout(double availableWidth)
    {
        var wrapping = TextWrapping;
        var widthChanged = wrapping == TextWrapping.Wrap
                           && !double.IsNaN(_lastLaidOutWidth)
                           && Math.Abs(_lastLaidOutWidth - availableWidth) > 0.5;

        if (!_layoutDirty && !widthChanged)
        {
            return;
        }

        var fontFamily = FontFamily.Source;
        var fontSize = FontSize > 0 ? FontSize : 14.0;
        var fontWeight = FontWeight.ToOpenTypeWeight();
        var fontStyle = FontStyle.ToOpenTypeStyle();

        _cachedLineHeight = ResolveLineHeight(fontFamily, fontSize, fontWeight, fontStyle);
        var lineHeight = _cachedLineHeight;

        if (wrapping == TextWrapping.Wrap && !double.IsInfinity(availableWidth) && availableWidth > 0)
        {
            LayoutWithWrap(availableWidth, lineHeight, fontFamily, fontSize, fontWeight, fontStyle);
        }
        else
        {
            LayoutNoWrap(lineHeight, fontFamily, fontSize, fontWeight, fontStyle);
        }

        _lastLaidOutWidth = availableWidth;
        _layoutDirty = false;
    }

    private void LayoutNoWrap(double lineHeight, string fontFamily, double fontSize, int fontWeight, int fontStyle)
    {
        double x = 0;
        double y = 0;
        double maxLineWidth = 0;

        for (int i = 0; i < _cells.Count; i++)
        {
            var cell = _cells[i];
            cell.LineHeight = lineHeight;

            if (IsLineBreakText(cell.Text))
            {
                cell.Bounds = new Rect(x, y, 0, lineHeight);
                maxLineWidth = Math.Max(maxLineWidth, x);
                x = 0;
                y += lineHeight;
                continue;
            }

            var width = MeasureCellWidth(cell.Text, fontFamily, fontSize, fontWeight, fontStyle);
            cell.Bounds = new Rect(x, y, width, lineHeight);
            x += width;
        }

        maxLineWidth = Math.Max(maxLineWidth, x);
        _lastDesiredSize = new Size(maxLineWidth, y + lineHeight);
    }

    /// <summary>
    /// Greedy line-breaking: walk cells left-to-right, track the last cell index
    /// at which breaking is legal, and when the next cell would overflow the
    /// available width, rewind to that break point and restart on a new line.
    /// This is the classic first-fit algorithm — not UAX #14 in full, but fast
    /// and sufficient for CJK + ASCII mixed animated text.
    /// </summary>
    private void LayoutWithWrap(double availableWidth, double lineHeight, string fontFamily, double fontSize, int fontWeight, int fontStyle)
    {
        double x = 0;
        double y = 0;
        double maxLineWidth = 0;
        int lineStartIdx = 0;
        int lastBreakIdx = 0; // index at which we can split the current line

        int i = 0;
        while (i < _cells.Count)
        {
            var cell = _cells[i];
            cell.LineHeight = lineHeight;

            // Explicit newline — always breaks and resets state.
            if (IsLineBreakText(cell.Text))
            {
                cell.Bounds = new Rect(x, y, 0, lineHeight);
                maxLineWidth = Math.Max(maxLineWidth, x);
                y += lineHeight;
                x = 0;
                i++;
                lineStartIdx = i;
                lastBreakIdx = i;
                continue;
            }

            var width = MeasureCellWidth(cell.Text, fontFamily, fontSize, fontWeight, fontStyle);
            var prevCellText = i > lineStartIdx ? _cells[i - 1].Text : null;
            var breakOpportunityHere = CanBreakBefore(prevCellText, cell.Text);

            // Record a break opportunity BEFORE checking overflow, so a CJK cell
            // that itself overflows can break at its own leading edge.
            if (breakOpportunityHere)
            {
                lastBreakIdx = i;
            }

            var wouldOverflow = x + width > availableWidth + 0.01 && x > 0;
            if (wouldOverflow && lastBreakIdx > lineStartIdx)
            {
                // Rewind to the last break point: wrap every cell from
                // lastBreakIdx onwards to a fresh line.
                maxLineWidth = Math.Max(maxLineWidth, ComputeLineWidth(lineStartIdx, lastBreakIdx - 1));
                y += lineHeight;
                x = 0;
                i = lastBreakIdx;
                lineStartIdx = lastBreakIdx;
                lastBreakIdx = lineStartIdx;
                continue;
            }

            cell.Bounds = new Rect(x, y, width, lineHeight);
            x += width;
            i++;
        }

        maxLineWidth = Math.Max(maxLineWidth, x);
        _lastDesiredSize = new Size(maxLineWidth, y + lineHeight);
    }

    /// <summary>
    /// Returns the rightmost laid-out X among cells [startIdx..endIdx]. Used when
    /// we rewind and commit the previous line — to stretch desired width to its
    /// true edge without re-measuring.
    /// </summary>
    private double ComputeLineWidth(int startIdx, int endIdx)
    {
        if (endIdx < startIdx) return 0;
        var last = _cells[endIdx];
        return last.Bounds.X + last.Bounds.Width;
    }

    /// <summary>
    /// Break-opportunity rule. Returns true if a line break is allowed between
    /// <paramref name="prev"/> and <paramref name="curr"/>. Simplified from
    /// UAX #14 — covers the common cases:
    ///   - whitespace cells are break points on both sides;
    ///   - CJK / kana / hangul can break anywhere between them and anything else;
    ///   - otherwise stick together (prevents breaking inside Latin words).
    /// Caller still permits a fallback break at the lineStart when there's no
    /// opportunity and a single cell already overflows.
    /// </summary>
    private static bool CanBreakBefore(string? prev, string curr)
    {
        if (prev is null) return false;
        if (IsWhitespaceGrapheme(prev)) return true;
        if (IsWhitespaceGrapheme(curr)) return true;
        if (IsCjkLike(curr)) return true;
        if (IsCjkLike(prev)) return true;
        return false;
    }

    private static bool IsWhitespaceGrapheme(string grapheme)
    {
        if (string.IsNullOrEmpty(grapheme)) return false;
        // Grapheme may be multi-code-point (ZWJ sequences), but only the first
        // code point matters for whitespace classification.
        return char.IsWhiteSpace(grapheme[0]);
    }

    private static bool IsCjkLike(string grapheme)
    {
        if (string.IsNullOrEmpty(grapheme)) return false;
        var c = grapheme[0];
        // Hiragana + Katakana
        if (c >= 0x3040 && c <= 0x30FF) return true;
        // CJK Unified Ideographs + extensions A (covers the overwhelming majority)
        if (c >= 0x3400 && c <= 0x9FFF) return true;
        // Hangul Syllables
        if (c >= 0xAC00 && c <= 0xD7AF) return true;
        // Fullwidth forms (CJK punctuation, fullwidth ASCII)
        if (c >= 0xFF00 && c <= 0xFFEF) return true;
        // CJK Symbols and Punctuation
        if (c >= 0x3000 && c <= 0x303F) return true;
        return false;
    }

    /// <inheritdoc />
    protected override Size MeasureOverride(Size availableSize)
    {
        EnsureLayout(availableSize.Width);
        return _lastDesiredSize;
    }

    /// <inheritdoc />
    protected override Size ArrangeOverride(Size finalSize)
    {
        // Under Wrap, the arrange-time width may differ from the measure-time
        // availableSize (e.g. a parent gave us more than we asked for). Re-layout
        // if it does, so visible wrapping matches the final render bounds.
        if (TextWrapping == TextWrapping.Wrap && !double.IsNaN(_lastLaidOutWidth)
            && Math.Abs(_lastLaidOutWidth - finalSize.Width) > 0.5)
        {
            _layoutDirty = true;
            EnsureLayout(finalSize.Width);
        }
        return base.ArrangeOverride(finalSize);
    }

    private double ResolveLineHeight(string fontFamily, double fontSize, int fontWeight, int fontStyle)
    {
        var explicitHeight = LineHeight;
        if (!double.IsNaN(explicitHeight) && explicitHeight > 0)
        {
            return explicitHeight;
        }

        var metrics = TextMeasurement.GetFontMetrics(fontFamily, fontSize, fontWeight, fontStyle);
        if (metrics.LineHeight > 0)
        {
            return metrics.LineHeight;
        }
        return fontSize * 1.3;
    }

    private double MeasureCellWidth(string text, string fontFamily, double fontSize, int fontWeight, int fontStyle)
    {
        var key = text;
        if (_widthCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var formatted = new FormattedText(text, fontFamily, fontSize)
        {
            FontWeight = fontWeight,
            FontStyle = fontStyle,
        };

        double width;
        if (TextMeasurement.MeasureText(formatted) && formatted.IsMeasured)
        {
            width = formatted.Width;
        }
        else
        {
            // Fallback rough estimate — DirectWrite unavailable (e.g. unit tests).
            width = text.Length * fontSize * 0.55;
        }

        if (_widthCache.Count >= 1024)
        {
            _widthCache.Clear();
        }
        _widthCache[key] = width;
        return width;
    }



    /// <inheritdoc />
    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        var dc = drawingContext;

        var foreground = Foreground;
        if (foreground == null)
        {
            return;
        }

        EnsureLayout(RenderSize.Width);

        var renderX = ResolveHorizontalOffset(_lastDesiredSize.Width);
        dc.PushClip(new RectangleGeometry(new Rect(RenderSize)));
        if (renderX != 0)
        {
            dc.PushTransform(new TranslateTransform { X = renderX, Y = 0 });
        }

        try
        {
            var fontSig = BuildFontSignature();
            if (!string.Equals(_formattedCacheSignature, fontSig, StringComparison.Ordinal))
            {
                _formattedCache.Clear();
                _formattedCacheSignature = fontSig;
            }

            // Two-pass rendering so we can get a real gaussian blur:
            //
            //   Pass 1 draws every cell whose payload has BlurRadius == 0 —
            //   Visible cells and (in mixed-state frames) Exiting cells with
            //   zero blur — directly to the main RT.
            //
            //   Pass 2 wraps every cell that requested blur inside a SINGLE
            //   PushEffect(Jalium.UI.Media.Effects.BlurEffect(maxRadius)) scope. The entire Presenter
            //   is captured once and pixel-shaded by the native compute blur;
            //   all blurry cells share that one capture. Per-cell capture
            //   would trigger one full-screen blur per character (16+ per
            //   frame in append scenarios) and overwhelm the D3D12 renderer,
            //   and per-cell CPU multi-sample looks like particle noise, not
            //   blur — this two-pass global capture is the only way to get a
            //   real defocus for text that looks like text, not a halo.
            //
            //   When all cells in the frame share the same blur radius (the
            //   common case under RiseSettleEffect with stagger=0), this is
            //   exact. Under stagger > 0 the max radius is an acceptable
            //   approximation — cells with a slightly smaller requested radius
            //   get slightly more blur for a frame or two, which is visually
            //   imperceptible.
            // If the TextEffect is a ShaderTextEffect, it replaces the normal
            // two-pass blur composition with a single global shader scope —
            // every cell renders inside one PushEffect wrapping the whole
            // presenter surface. Nested offscreen captures aren't supported
            // by the native D3D12 renderer, so we can't combine a shader
            // wrapper with the built-in blur pass; the shader wins.
            if (TextEffect is Effects.ShaderTextEffect shaderEffect)
            {
                shaderEffect.UpdateForFrame(RenderSize, _elapsedMs);
                var gpuEffect = shaderEffect.CurrentEffect;
                if (gpuEffect != null && gpuEffect.HasEffect)
                {
                    RenderAllCellsWithShader(dc, foreground, gpuEffect);
                    return;
                }
                // Shader is in its "off" phase — fall through to normal
                // rendering for this frame.
            }

            var (maxBlur, blurryBounds) = ScanBlurryCells();

            // Pass 1 — sharp cells, live first then exiting-with-zero-blur
            // on top (echoes the requirement that dissipating text renders
            // in front of the new line).
            for (int i = 0; i < _cells.Count; i++)
            {
                RenderCell(dc, _cells[i], foreground, CellRenderFilter.SharpOnly);
            }
            for (int i = 0; i < _exitingCells.Count; i++)
            {
                RenderCell(dc, _exitingCells[i], foreground, CellRenderFilter.SharpOnly);
            }

            // Pass 2 — blurry cells inside one global blur.
            if (maxBlur > BlurActivationThreshold && blurryBounds.Width > 0 && blurryBounds.Height > 0)
            {
                var blurEffect = GetOrCreatePresenterBlur(maxBlur);
                // Capture only the union of blurry-cells bounds (padded by the blur
                // radius so the halo isn't clipped), NOT the whole RenderSize. In
                // lyric/log scenarios the presenter grows unbounded as lines
                // accumulate; capturing RenderSize means allocating an offscreen
                // RT the size of the entire scroll-back, and blurring it every
                // frame — which blows past 520 ms (the enter duration) on long
                // sessions and hands a delta > 1.0 to UpdateCellPhase, skipping
                // the entire animation. Scoping to the blurry union keeps cost
                // O(live entering cells), not O(total cells ever added).
                dc.PushEffect(blurEffect, blurryBounds);
                try
                {
                    for (int i = 0; i < _cells.Count; i++)
                    {
                        RenderCell(dc, _cells[i], foreground, CellRenderFilter.BlurryOnly);
                    }
                    for (int i = 0; i < _exitingCells.Count; i++)
                    {
                        RenderCell(dc, _exitingCells[i], foreground, CellRenderFilter.BlurryOnly);
                    }
                }
                finally
                {
                    dc.PopEffect();
                }
            }
        }
        finally
        {
            if (renderX != 0)
            {
                dc.Pop();
            }
            dc.Pop();
        }
    }

    /// <summary>
    /// Shader pass: wrap all cells (sharp or blurry) in one PushEffect scope
    /// with the <see cref="Effects.ShaderTextEffect"/>'s <c>CurrentEffect</c>.
    /// Capture bounds cover every cell that will actually draw this frame,
    /// padded by the effect's own reported padding. Used only when a
    /// <see cref="Effects.ShaderTextEffect"/> is the active TextEffect.
    /// </summary>
    private void RenderAllCellsWithShader(DrawingContext dc, Brush foreground, IEffect shaderEffect)
    {
        // Compute the union of all cell bounds so the shader capture is no
        // larger than necessary — long presenters accumulate unbounded cell
        // counts, but only live cells need the shader pass.
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        bool any = false;
        AccumulateCellUnion(_cells, ref minX, ref minY, ref maxX, ref maxY, ref any);
        AccumulateCellUnion(_exitingCells, ref minX, ref minY, ref maxX, ref maxY, ref any);
        if (!any) return;

        var padding = shaderEffect.EffectPadding;
        var captureBounds = new Rect(
            minX - padding.Left,
            minY - padding.Top,
            (maxX - minX) + padding.Left + padding.Right,
            (maxY - minY) + padding.Top + padding.Bottom);

        dc.PushEffect(shaderEffect, captureBounds);
        try
        {
            for (int i = 0; i < _cells.Count; i++)
            {
                RenderCell(dc, _cells[i], foreground, CellRenderFilter.All);
            }
            for (int i = 0; i < _exitingCells.Count; i++)
            {
                RenderCell(dc, _exitingCells[i], foreground, CellRenderFilter.All);
            }
        }
        finally
        {
            dc.PopEffect();
        }
    }

    private static void AccumulateCellUnion(List<TextEffectCell> cells,
        ref double minX, ref double minY, ref double maxX, ref double maxY, ref bool any)
    {
        for (int i = 0; i < cells.Count; i++)
        {
            var c = cells[i];
            if (c.Phase == TextEffectCellPhase.Hidden) continue;
            if (IsLineBreakText(c.Text) || string.IsNullOrEmpty(c.Text) || c.Bounds.Width <= 0) continue;
            any = true;
            var x = c.Bounds.X;
            var y = c.Bounds.Y;
            var w = c.Bounds.Width;
            var h = c.Bounds.Height;
            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (x + w > maxX) maxX = x + w;
            if (y + h > maxY) maxY = y + h;
        }
    }

    /// <summary>
    /// Scans every live and exiting cell to find the largest blur radius
    /// requested this frame AND the union bounds of cells that will render
    /// in the blurry pass. Caller uses both to size the offscreen capture
    /// for <see cref="DrawingContext.PushEffect(Jalium.UI.IEffect, Rect)"/>.
    /// </summary>
    private (double maxBlur, Rect blurryBounds) ScanBlurryCells()
    {
        double max = 0;
        var effect = TextEffect;
        if (effect == null) return (0, default);

        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        bool any = false;

        for (int i = 0; i < _cells.Count; i++)
        {
            AccumulateBlurryCell(_cells[i], effect, ref max, ref minX, ref minY, ref maxX, ref maxY, ref any);
        }
        for (int i = 0; i < _exitingCells.Count; i++)
        {
            AccumulateBlurryCell(_exitingCells[i], effect, ref max, ref minX, ref minY, ref maxX, ref maxY, ref any);
        }

        if (!any || max <= BlurActivationThreshold)
        {
            return (max, default);
        }

        // Pad by the blur radius so the halo isn't clipped by the capture rect.
        var pad = max + 1.0;
        var bounds = new Rect(
            minX - pad,
            minY - pad,
            (maxX - minX) + pad * 2,
            (maxY - minY) + pad * 2);
        return (max, bounds);
    }

    private void AccumulateBlurryCell(TextEffectCell cell, ITextEffect effect,
        ref double max, ref double minX, ref double minY, ref double maxX, ref double maxY, ref bool any)
    {
        if (cell.Phase == TextEffectCellPhase.Hidden) return;
        if (IsLineBreakText(cell.Text) || string.IsNullOrEmpty(cell.Text)) return;

        var payload = TextCellRenderPayload.Identity;
        var ctx = new TextEffectFrameContext(
            cell, cell.Phase,
            GetPhaseProgressLinear(cell),
            GetTimeInPhase(cell), GetTotalTime(cell),
            RenderSize);
        effect.Apply(in ctx, ref payload);

        if (payload.Opacity <= 0.005) return;
        if (payload.BlurRadius <= BlurActivationThreshold) return;

        if (payload.BlurRadius > max) max = payload.BlurRadius;

        var x = cell.Bounds.X + payload.TranslateX;
        var y = cell.Bounds.Y + payload.TranslateY;
        var w = cell.Bounds.Width;
        var h = cell.Bounds.Height;

        if (x < minX) minX = x;
        if (y < minY) minY = y;
        if (x + w > maxX) maxX = x + w;
        if (y + h > maxY) maxY = y + h;
        any = true;
    }

    /// <summary>
    /// Cached <see cref="Jalium.UI.Media.Effects.BlurEffect"/> instances keyed by radius bucketed to
    /// 0.5 px — avoids per-frame allocation while still tracking the continuous
    /// animated radius closely enough to be imperceptible. Instance cache (not
    /// static) so each presenter clears on dispose.
    /// </summary>
    private Jalium.UI.Media.Effects.BlurEffect GetOrCreatePresenterBlur(double radius)
    {
        var bucket = (int)Math.Round(radius * 2.0);
        if (bucket < 2) bucket = 2;
        if (!_presenterBlurCache.TryGetValue(bucket, out var effect))
        {
            effect = new Jalium.UI.Media.Effects.BlurEffect(bucket / 2.0);
            _presenterBlurCache[bucket] = effect;
        }
        return effect;
    }

    private double ResolveHorizontalOffset(double contentWidth)
    {
        var renderWidth = RenderSize.Width;
        if (renderWidth <= 0 || contentWidth <= 0)
        {
            return 0;
        }

        return TextAlignment switch
        {
            TextAlignment.Center => (renderWidth - contentWidth) / 2.0,
            TextAlignment.Right => renderWidth - contentWidth,
            _ => 0,
        };
    }

    /// <summary>
    /// Which subset of cells RenderCell will actually draw this call. Used
    /// to split the cell list across the two-pass blur scope, or to render
    /// everything unfiltered under a <see cref="Effects.ShaderTextEffect"/>
    /// global shader pass.
    /// </summary>
    private enum CellRenderFilter
    {
        /// <summary>Only cells with BlurRadius ≤ activation threshold.</summary>
        SharpOnly,
        /// <summary>Only cells with BlurRadius &gt; activation threshold.</summary>
        BlurryOnly,
        /// <summary>Every cell, regardless of blur — used inside a shader pass.</summary>
        All,
    }

    /// <summary>
    /// Renders a single cell, filtered by <paramref name="filter"/> so OnRender
    /// can split the draw list across a global-blur PushEffect scope or a
    /// <see cref="Effects.ShaderTextEffect"/> wrapper.
    /// </summary>
    private void RenderCell(DrawingContext dc, TextEffectCell cell, Brush foreground, CellRenderFilter filter)
    {
        if (cell.Phase == TextEffectCellPhase.Hidden)
        {
            return;
        }

        if (IsLineBreakText(cell.Text) || string.IsNullOrEmpty(cell.Text) || cell.Bounds.Width <= 0)
        {
            return;
        }

        var payload = TextCellRenderPayload.Identity;
        var effect = TextEffect;
        if (effect != null)
        {
            var ctx = new TextEffectFrameContext(
                cell,
                cell.Phase,
                GetPhaseProgressLinear(cell),
                GetTimeInPhase(cell),
                GetTotalTime(cell),
                RenderSize);
            effect.Apply(in ctx, ref payload);
        }

        if (payload.Opacity <= 0.005)
        {
            return;
        }

        // Pass filter: split the cells across SharpOnly / BlurryOnly so the
        // two-pass global blur composition works correctly. Under a
        // ShaderTextEffect we render All cells inside one shader scope.
        var cellIsBlurry = payload.BlurRadius > BlurActivationThreshold;
        switch (filter)
        {
            case CellRenderFilter.SharpOnly when cellIsBlurry: return;
            case CellRenderFilter.BlurryOnly when !cellIsBlurry: return;
        }

        var formatted = GetOrCreateFormattedText(cell.Text, payload.ForegroundOverride ?? foreground);

        // Optional per-cell PerCellEffect (custom user effects — drop shadow,
        // shader, etc.). Separate from the Presenter-level blur pass: this
        // wraps an individual cell's draws, runs offscreen capture + the
        // effect's native pipeline on PopEffect.
        var cellEffect = payload.PerCellEffect;
        bool pushedCellEffect = false;
        if (cellEffect != null && cellEffect.HasEffect)
        {
            var captureBounds = new Rect(
                cell.Bounds.X + payload.TranslateX,
                cell.Bounds.Y + payload.TranslateY,
                cell.Bounds.Width,
                cell.Bounds.Height);
            dc.PushEffect(cellEffect, captureBounds);
            pushedCellEffect = true;
        }

        var pushCount = 0;
        try
        {
            dc.PushTransform(new TranslateTransform
            {
                X = cell.Bounds.X + payload.TranslateX,
                Y = cell.Bounds.Y + payload.TranslateY,
            });
            pushCount++;

            var hasScale = Math.Abs(payload.ScaleX - 1) > 0.0001 || Math.Abs(payload.ScaleY - 1) > 0.0001;
            var hasRotation = Math.Abs(payload.Rotation) > 0.0001;
            if (hasScale || hasRotation)
            {
                var origin = payload.TransformOrigin;
                var group = new TransformGroup();
                if (hasRotation)
                {
                    group.Children.Add(new RotateTransform
                    {
                        Angle = payload.Rotation,
                        CenterX = origin.X,
                        CenterY = origin.Y,
                    });
                }
                if (hasScale)
                {
                    group.Children.Add(new ScaleTransform
                    {
                        ScaleX = payload.ScaleX,
                        ScaleY = payload.ScaleY,
                        CenterX = origin.X,
                        CenterY = origin.Y,
                    });
                }
                dc.PushTransform(group);
                pushCount++;
            }

            dc.PushOpacity(payload.Opacity);
            pushCount++;
            dc.DrawText(formatted, new Point(0, 0));
        }
        finally
        {
            for (int i = 0; i < pushCount; i++)
            {
                dc.Pop();
            }

            if (pushedCellEffect)
            {
                dc.PopEffect();
            }
        }
    }

    private FormattedText GetOrCreateFormattedText(string text, Brush foreground)
    {
        // Cache is keyed by text only; foreground is assigned per use since the
        // effect may override it per frame. FontFamily/Size/Weight/Style changes
        // invalidate the cache via _formattedCacheSignature.
        if (!_formattedCache.TryGetValue(text, out var cached))
        {
            cached = new FormattedText(text, FontFamily.Source, FontSize > 0 ? FontSize : 14.0)
            {
                FontWeight = FontWeight.ToOpenTypeWeight(),
                FontStyle = FontStyle.ToOpenTypeStyle(),
            };
            TextMeasurement.MeasureText(cached);
            _formattedCache[text] = cached;
        }

        cached.Foreground = foreground;
        return cached;
    }

    private string BuildFontSignature()
    {
        return string.Concat(
            FontFamily.Source,
            "|",
            FontSize.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            "|",
            FontWeight.ToOpenTypeWeight().ToString(System.Globalization.CultureInfo.InvariantCulture),
            "|",
            FontStyle.ToOpenTypeStyle().ToString(System.Globalization.CultureInfo.InvariantCulture));
    }


}



// Avoid blanket using Jalium.UI.Media.Effects — Jalium.UI.Media also exposes
// a "Jalium.UI.Media.Effects.BlurEffect" type which would cause an ambiguous reference. Use the full
// name when we need the one from Effects.
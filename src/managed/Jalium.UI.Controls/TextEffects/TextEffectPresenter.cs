using System;
using System.Collections.Generic;
using System.Globalization;
using Jalium.UI;
using Jalium.UI.Controls.TextEffects.Effects;
using Jalium.UI.Input;
using Jalium.UI.Media;
using Jalium.UI.Threading;

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
    #region Dependency Properties

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
            new PropertyMetadata(FrameworkElement.DefaultFontFamilyName, OnFontPropertyChanged, null, inherits: true));

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

    #endregion

    #region Routed Events

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

    #endregion

    #region State

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

    #endregion

    #region Constructor

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
    protected override Jalium.UI.Automation.AutomationPeer? OnCreateAutomationPeer()
    {
        return new TextEffectPresenterAutomationPeer(this);
    }

    #endregion

    #region CLR Properties

    /// <summary>Gets or sets the full text content. See <see cref="TextProperty"/>.</summary>
    public string Text
    {
        get => (string)(GetValue(TextProperty) ?? string.Empty);
        set => SetValue(TextProperty, value ?? string.Empty);
    }

    /// <summary>Gets or sets the font family.</summary>
    public string FontFamily
    {
        get => (string)(GetValue(FontFamilyProperty) ?? FrameworkElement.DefaultFontFamilyName);
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

    #endregion

    #region Events

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

    #endregion

    #region Public Mutation API

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

    #endregion

    #region State Machine

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

    #endregion

    #region Rendering Loop

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

    #endregion

    #region Lifecycle

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

    #endregion

    #region Property Callbacks

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

    #endregion

    #region Grapheme Splitting

    /// <summary>
    /// Splits a string into grapheme clusters (user-perceived characters), using
    /// <see cref="StringInfo.ParseCombiningCharacters"/>. Emoji, ZWJ sequences,
    /// surrogate pairs, and combining marks each map to a single grapheme.
    /// Newline characters are kept as their own graphemes so layout can recognise them.
    /// </summary>
    internal static List<string> SplitGraphemes(string text)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            return result;
        }

        var starts = StringInfo.ParseCombiningCharacters(text);
        for (int i = 0; i < starts.Length; i++)
        {
            var start = starts[i];
            var end = i + 1 < starts.Length ? starts[i + 1] : text.Length;
            result.Add(text.Substring(start, end - start));
        }
        return result;
    }

    #endregion
}

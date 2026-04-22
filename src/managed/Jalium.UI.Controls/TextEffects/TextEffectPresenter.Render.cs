using System;
using System.Collections.Generic;
using Jalium.UI;
using Jalium.UI.Interop;
using Jalium.UI.Media;
// Avoid blanket using Jalium.UI.Media.Effects — Jalium.UI.Media also exposes
// a "Jalium.UI.Media.Effects.BlurEffect" type which would cause an ambiguous reference. Use the full
// name when we need the one from Effects.

namespace Jalium.UI.Controls.TextEffects;

partial class TextEffectPresenter
{
    #region Layout

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

        var fontFamily = FontFamily;
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

            if (cell.Text == "\n")
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
            if (cell.Text == "\n")
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

    #endregion

    #region Rendering

    /// <inheritdoc />
    protected override void OnRender(object drawingContext)
    {
        base.OnRender(drawingContext);

        if (drawingContext is not DrawingContext dc)
        {
            return;
        }

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
            if (c.Text == "\n" || string.IsNullOrEmpty(c.Text) || c.Bounds.Width <= 0) continue;
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
    /// for <see cref="DrawingContext.PushEffect"/>.
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
        if (cell.Text == "\n" || string.IsNullOrEmpty(cell.Text)) return;

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

        if (cell.Text == "\n" || string.IsNullOrEmpty(cell.Text) || cell.Bounds.Width <= 0)
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
            cached = new FormattedText(text, FontFamily, FontSize > 0 ? FontSize : 14.0)
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
            FontFamily,
            "|",
            FontSize.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            "|",
            FontWeight.ToOpenTypeWeight().ToString(System.Globalization.CultureInfo.InvariantCulture),
            "|",
            FontStyle.ToOpenTypeStyle().ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    #endregion
}

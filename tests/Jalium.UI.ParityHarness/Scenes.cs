using Jalium.UI.Interop;
using Jalium.UI.Media;

namespace Jalium.UI.ParityHarness;

/// <summary>
/// First-wave parity scene catalog. Every scene is a pure sequence of raw
/// draw calls against the 512x512 render target — deterministic, no
/// animation, no layout — so byte-identical command streams hit both
/// backends and the BMP diff isolates rasterization differences.
///
/// Path command encoding (jalium_triangulate.h):
///   tag 0 = LineTo  [0,x,y]        tag 1 = CubicTo [1,c1x,c1y,c2x,c2y,ex,ey]
///   tag 2 = MoveTo  [2,x,y]        tag 3 = QuadTo  [3,cx,cy,ex,ey]
///   tag 4 = ArcTo   [4,ex,ey,rx,ry,rot,large,sweep]   tag 5 = Close [5]
/// </summary>
internal static class Scenes
{
    public static readonly (string Name, Action<SceneContext> Draw)[] All =
    [
        ("solid-rect-grid", SolidRectGrid),
        ("rounded-rect", RoundedRect),
        ("ellipse", Ellipse),
        ("gradient-linear", GradientLinear),
        ("gradient-radial", GradientRadial),
        ("path-fill", PathFill),
        ("stroke-wide-dash", StrokeWideDash),
        ("line-fan", LineFan),
        ("text", Text),
        ("text-cleartype", TextClearType),
        ("bitmap", Bitmap),
        ("clip", Clip),
        ("clip-rotated-rounded", ClipRotatedRounded),
        ("opacity-layers", OpacityLayers),
        ("painter-order", PainterOrder),
        ("rounded-container-border", RoundedContainerBorder),
        ("superellipse", SuperEllipse),

        // ── Effect-system scenes (RenderTarget effect C ABI) ─────────────────
        // Each captures a KNOWN element into an offscreen bitmap
        // (BeginEffectCapture → draw → EndEffectCapture) then re-emits that
        // region through a DrawXxxEffect. These exercise the content-effect
        // path that, on Vulkan, samples the TRUE isolated element through the
        // GPU offscreen effect RT. C-gamma made that the DEFAULT, so both the
        // parity harness and the app get the real GPU-offscreen path with no env
        // set; JALIUM_VK_EFFECT_GPU_RT=0 is only a kill-switch back to the legacy
        // pass-through composite-back approximation.
        //
        // These are EXPECTED to diverge more than the vector scenes: D3D12 uses
        // a separable Gaussian blur while Vulkan uses a box/tent approximation,
        // and the shadow/glow halo derives from the alpha silhouette, so blur
        // radius + halo falloff differ by construction. The numbers below are
        // the parity BASELINE for the C-beta effect-alignment work, not a gate
        // we expect to pass today (see per-scene thresholds in parity_diff.py).
        ("effect-blur", EffectBlur),
        ("effect-dropshadow", EffectDropShadow),
        ("effect-outerglow", EffectOuterGlow),
        ("effect-colormatrix", EffectColorMatrix),
        ("effect-emboss", EffectEmboss),
        ("effect-innershadow", EffectInnerShadow),
        ("effect-liquidglass", EffectLiquidGlass),
        ("effect-transition", EffectTransition),

        // In-app backdrop filter (DrawBackdropFilterEx): samples the live
        // framebuffer, blurs it, and applies tint + saturation + noise +
        // luminosity + per-corner rounding. See BackdropAcrylic / BackdropPlain.
        ("backdrop-acrylic", BackdropAcrylic),
        ("backdrop-plain-blur", BackdropPlainBlur),
    ];

    // ── 8x8 grid of opaque solid rects with deterministic per-cell colors ──
    private static void SolidRectGrid(SceneContext s)
    {
        const int n = 8;
        const float cell = 512f / n;
        const float pad = 6f;
        for (int gy = 0; gy < n; gy++)
        {
            for (int gx = 0; gx < n; gx++)
            {
                // Deterministic pseudo-palette from the cell index.
                float r = ((gx * 37 + gy * 91) % 256) / 255f;
                float g = ((gx * 143 + gy * 29) % 256) / 255f;
                float b = ((gx * 71 + gy * 173) % 256) / 255f;
                var brush = s.Solid(r, g, b);
                s.Target.FillRectangle(gx * cell + pad, gy * cell + pad,
                    cell - 2 * pad, cell - 2 * pad, brush);
            }
        }
    }

    // ── Uniform + per-corner rounded rects, fill and stroke ────────────────
    private static void RoundedRect(SceneContext s)
    {
        var fillA = s.Solid(0.85f, 0.35f, 0.25f);
        var fillB = s.Solid(0.25f, 0.65f, 0.85f);
        var fillC = s.Solid(0.45f, 0.80f, 0.35f);
        var stroke = s.Solid(0.95f, 0.90f, 0.30f);

        // Uniform radii — small, medium, pill.
        s.Target.FillRoundedRectangle(32, 32, 200, 120, 12, 12, fillA);
        s.Target.FillRoundedRectangle(280, 32, 200, 120, 40, 40, fillB);
        s.Target.FillRoundedRectangle(32, 190, 200, 80, 40, 40, fillC); // ry == h/2 → pill

        // Uniform stroke.
        s.Target.DrawRoundedRectangle(280, 190, 200, 80, 24, 24, stroke, 6f);

        // Per-corner: distinct radius each corner, fill + stroke.
        s.Target.FillPerCornerRoundedRectangle(32, 310, 200, 150, 0, 24, 60, 8, fillB);
        s.Target.DrawPerCornerRoundedRectangle(280, 310, 200, 150, 48, 0, 16, 64, stroke, 4f);
    }

    // ── Filled / stroked ellipses incl. extreme aspect ratios ──────────────
    private static void Ellipse(SceneContext s)
    {
        var f1 = s.Solid(0.90f, 0.55f, 0.15f);
        var f2 = s.Solid(0.30f, 0.35f, 0.90f);
        var f3 = s.Solid(0.80f, 0.25f, 0.65f);
        var st = s.Solid(0.35f, 0.95f, 0.75f);

        s.Target.FillEllipse(128, 128, 90, 90, f1);       // circle
        s.Target.FillEllipse(370, 128, 120, 50, f2);      // wide
        s.Target.FillEllipse(128, 370, 40, 110, f3);      // tall
        s.Target.DrawEllipse(370, 370, 100, 80, st, 5f);  // stroked
        s.Target.DrawEllipse(370, 370, 60, 40, st, 1f);   // thin stroke inside
    }

    // ── Multi-stop linear gradients at varying angles ───────────────────────
    private static void GradientLinear(SceneContext s)
    {
        // Horizontal, 5 stops with hard-ish color changes.
        var g1 = s.Linear(32, 0, 480, 0,
        [
            0.00f, 0.90f, 0.10f, 0.10f, 1f,
            0.25f, 0.95f, 0.80f, 0.10f, 1f,
            0.50f, 0.15f, 0.80f, 0.25f, 1f,
            0.75f, 0.15f, 0.35f, 0.90f, 1f,
            1.00f, 0.70f, 0.15f, 0.85f, 1f,
        ]);
        s.Target.FillRectangle(32, 32, 448, 120, g1);

        // Diagonal, 3 stops, uneven positions.
        var g2 = s.Linear(32, 190, 480, 340,
        [
            0.00f, 0.05f, 0.05f, 0.05f, 1f,
            0.70f, 0.20f, 0.75f, 0.95f, 1f,
            1.00f, 1.00f, 1.00f, 1.00f, 1f,
        ]);
        s.Target.FillRectangle(32, 190, 448, 150, g2);

        // Vertical, 2 stops with alpha ramp over the cleared background.
        var g3 = s.Linear(0, 370, 0, 480,
        [
            0.00f, 1.00f, 0.60f, 0.10f, 1.00f,
            1.00f, 1.00f, 0.60f, 0.10f, 0.00f,
        ]);
        s.Target.FillRectangle(32, 370, 448, 110, g3);
    }

    // ── Multi-stop radial gradients, centered + offset origin ───────────────
    private static void GradientRadial(SceneContext s)
    {
        var g1 = s.Radial(150, 150, 118, 118, 150, 150,
        [
            0.00f, 1.00f, 0.95f, 0.70f, 1f,
            0.40f, 0.95f, 0.55f, 0.15f, 1f,
            0.80f, 0.70f, 0.15f, 0.10f, 1f,
            1.00f, 0.20f, 0.05f, 0.15f, 1f,
        ]);
        s.Target.FillRectangle(32, 32, 236, 236, g1);

        // Offset gradient origin (fake specular highlight).
        var g2 = s.Radial(370, 150, 100, 100, 340, 115,
        [
            0.00f, 0.95f, 0.95f, 1.00f, 1f,
            0.50f, 0.30f, 0.55f, 0.95f, 1f,
            1.00f, 0.05f, 0.10f, 0.35f, 1f,
        ]);
        s.Target.FillEllipse(370, 150, 100, 100, g2);

        // Elliptical radius, wide.
        var g3 = s.Radial(256, 400, 200, 64, 256, 400,
        [
            0.00f, 0.10f, 0.90f, 0.60f, 1f,
            1.00f, 0.10f, 0.20f, 0.25f, 1f,
        ]);
        s.Target.FillRectangle(56, 336, 400, 128, g3);
    }

    // ── Concave polygon + compound EvenOdd ring (hole must stay open) ───────
    private static void PathFill(SceneContext s)
    {
        var star = s.Solid(0.95f, 0.75f, 0.20f);
        var ring = s.Solid(0.30f, 0.75f, 0.95f);

        // 5-point star (concave, self-intersection-free spoke outline).
        const float cx = 150f, cy = 150f, rOut = 110f, rIn = 44f;
        var cmds = new List<float>();
        float sx = 0, sy = 0;
        for (int i = 0; i < 10; i++)
        {
            double a = -Math.PI / 2 + i * Math.PI / 5;
            float r = (i % 2 == 0) ? rOut : rIn;
            float x = cx + (float)(Math.Cos(a) * r);
            float y = cy + (float)(Math.Sin(a) * r);
            if (i == 0) { sx = x; sy = y; }
            else { cmds.AddRange([0f, x, y]); }
        }
        cmds.Add(5f); // Close
        s.Target.FillPath(sx, sy, cmds.ToArray(), star, fillRule: 1 /* NonZero */);

        // Compound EvenOdd: outer square + SAME-winding inner square via
        // MoveTo (tag 2) — EvenOdd must punch the hole regardless of winding.
        // Regression guard for the "compound holes filled solid" class of bug.
        float ox = 300, oy = 60, osz = 200;   // outer
        float ix = 350, iy = 110, isz = 100;  // inner (same CW order)
        float[] compound =
        [
            0f, ox + osz, oy,
            0f, ox + osz, oy + osz,
            0f, ox, oy + osz,
            5f,
            2f, ix, iy,
            0f, ix + isz, iy,
            0f, ix + isz, iy + isz,
            0f, ix, iy + isz,
            5f,
        ];
        s.Target.FillPath(ox, oy, compound, ring, fillRule: 0 /* EvenOdd */);

        // Bezier blob (cubics) — smooth concavities.
        var blob = s.Solid(0.75f, 0.30f, 0.80f);
        float[] blobCmds =
        [
            1f, 210f, 320f, 250f, 470f, 150f, 460f,
            1f, 90f, 455f, 40f, 420f, 60f, 390f,
            1f, 80f, 355f, 40f, 330f, 90f, 330f,
            5f,
        ];
        s.Target.FillPath(90f, 330f, blobCmds, blob, fillRule: 1);
    }

    // ── Wide strokes: joins, caps, dash patterns ────────────────────────────
    private static void StrokeWideDash(SceneContext s)
    {
        var wide = s.Solid(0.95f, 0.45f, 0.20f);
        var dash = s.Solid(0.35f, 0.85f, 0.95f);
        var thin = s.Solid(0.85f, 0.85f, 0.30f);

        // Wide zig-zag, miter joins, butt caps.
        float[] zig =
        [
            0f, 140f, 160f,
            0f, 220f, 60f,
            0f, 300f, 160f,
            0f, 380f, 60f,
            0f, 460f, 160f,
        ];
        s.Target.StrokePath(60f, 60f, zig, wide, strokeWidth: 18f, closed: false,
            lineJoin: 0, miterLimit: 10f, lineCap: 0);

        // Dashed rounded zig (round join, round cap, dash 24-12, offset 6).
        float[] zag =
        [
            0f, 160f, 320f,
            0f, 260f, 220f,
            0f, 360f, 320f,
            0f, 460f, 220f,
        ];
        s.Target.StrokePath(60f, 220f, zag, dash, strokeWidth: 10f, closed: false,
            lineJoin: 2, miterLimit: 10f, lineCap: 2,
            dashPattern: [24f, 12f], dashOffset: 6f);

        // Closed dashed rectangle path (square caps, dash 10-6).
        float[] rect =
        [
            0f, 440f, 380f,
            0f, 440f, 460f,
            0f, 72f, 460f,
            5f,
        ];
        s.Target.StrokePath(72f, 380f, rect, thin, strokeWidth: 4f, closed: true,
            lineJoin: 0, miterLimit: 10f, lineCap: 1,
            dashPattern: [10f, 6f], dashOffset: 0f);
    }

    // ── DrawLine fan — AA quality across the full angle sweep ───────────────
    private static void LineFan(SceneContext s)
    {
        var pen1 = s.Solid(0.95f, 0.95f, 0.95f);
        var pen2 = s.Solid(0.95f, 0.55f, 0.25f);
        const float cx = 256f, cy = 256f, radius = 220f;

        // 48 spokes → 7.5° steps: exercises near-horizontal, near-vertical
        // and every awkward slope in between at 1px and 3px widths.
        for (int i = 0; i < 48; i++)
        {
            double a = i * Math.PI * 2 / 48;
            float x = cx + (float)(Math.Cos(a) * radius);
            float y = cy + (float)(Math.Sin(a) * radius);
            var pen = (i % 2 == 0) ? pen1 : pen2;
            float w = (i % 2 == 0) ? 1f : 3f;
            s.Target.DrawLine(cx, cy, x, y, pen, w);
        }
    }

    // ── Text: grayscale AA, multiple sizes + bold ───────────────────────────
    private static void Text(SceneContext s)
    {
        var white = s.Solid(0.95f, 0.95f, 0.95f);
        var accent = s.Solid(0.55f, 0.80f, 1.00f);

        var f12 = s.Text("Segoe UI", 12f);
        var f16 = s.Text("Segoe UI", 16f);
        var f24 = s.Text("Segoe UI", 24f);
        var f36 = s.Text("Segoe UI", 36f);
        var f24b = s.Text("Segoe UI", 24f, weight: 700);
        var f48b = s.Text("Segoe UI", 48f, weight: 700);

        const string pangram = "Sphinx of black quartz, judge my vow 0123456789";
        s.Target.DrawText(pangram, f12, 24, 30, 464, 24, white);
        s.Target.DrawText(pangram, f16, 24, 64, 464, 30, white);
        s.Target.DrawText("The quick brown fox jumps over 42 lazy dogs.", f24, 24, 110, 464, 40, white);
        s.Target.DrawText("Jalium parity 512", f36, 24, 170, 464, 60, accent);
        s.Target.DrawText("Bold weight seven hundred", f24b, 24, 250, 464, 40, white);
        s.Target.DrawText("AaBbGg 08", f48b, 24, 310, 464, 80, accent);
        // Tiny text tests hinting/subpixel positioning differences hardest.
        s.Target.DrawText("tiny 9px glyphs iiillll 1Il| oO0", s.Text("Segoe UI", 9f), 24, 420, 464, 20, white);
    }

    // ── Text with per-format ClearType (dual-source sub-pixel AA) ──────────
    // Exercises the SRC1 dual-source blend contract (D3D12 bitmap_text.ps.hlsl
    // is the reference; Vulkan reads SRC1 from fragment output Location 0,
    // Index 1). Backgrounds span dark / light / saturated colour because
    // per-channel SRC1 blend errors show up differently against each; if a
    // backend silently falls back to grayscale the sub-pixel colour fringes
    // vanish, which the cross-backend diff catches.
    private static void TextClearType(SceneContext s)
    {
        NativeTextFormat Ct(string family, float size, int weight = 400)
        {
            var format = s.Text(family, size, weight);
            format.SetTextRenderingMode(3 /* TextRenderingMode.ClearType */);
            return format;
        }

        s.Target.FillRectangle(0, 0, 512, 170, s.Solid(0.10f, 0.10f, 0.12f));
        s.Target.FillRectangle(0, 170, 512, 172, s.Solid(0.92f, 0.92f, 0.90f));
        s.Target.FillRectangle(0, 342, 512, 170, s.Solid(0.15f, 0.45f, 0.75f));

        var white = s.Solid(0.95f, 0.95f, 0.95f);
        var black = s.Solid(0.10f, 0.10f, 0.10f);
        var amber = s.Solid(1.00f, 0.75f, 0.20f);

        var f14 = Ct("Segoe UI", 14f);
        var f22 = Ct("Segoe UI", 22f);
        var f36b = Ct("Segoe UI", 36f, weight: 700);

        const string pangram = "Sphinx of black quartz, judge my vow 0123456789";
        // Dark panel.
        s.Target.DrawText(pangram, f14, 24, 30, 464, 24, white);
        s.Target.DrawText("ClearType dual-source SRC1", f22, 24, 70, 464, 36, amber);
        s.Target.DrawText("AaBbGg 08", f36b, 24, 108, 464, 60, white);
        // Light panel: dark-on-light is where sub-pixel fringing is strongest.
        s.Target.DrawText(pangram, f14, 24, 200, 464, 24, black);
        s.Target.DrawText("subpixel on light ground", f22, 24, 240, 464, 36, black);
        // Saturated panel.
        s.Target.DrawText(pangram, f14, 24, 372, 464, 24, white);
        s.Target.DrawText("tiny 9px iiillll 1Il| oO0", Ct("Segoe UI", 9f), 24, 410, 464, 20, white);
        s.Target.DrawText("colored ground 42", f36b, 24, 440, 464, 60, amber);
    }

    // ── Bitmaps: opaque + semi-transparent, NearestNeighbor + Linear ────────
    private static void Bitmap(SceneContext s)
    {
        // 8x8 OPAQUE PNG: quadrant primaries + white diagonal (see tools note
        // in the parity docs; generated by a 20-line python zlib script).
        const string opaquePngB64 =
            "iVBORw0KGgoAAAANSUhEUgAAAAgAAAAICAYAAADED76LAAAAP0lEQVR42mP4DwR3NDTgWOOEDQpmAAki" +
            "K8KqAFkRTgUwRRgKjKKe/UfGIPDshAYcYygACSIrwqoAWRFOBTBFAPj/paEKk05EAAAAAElFTkSuQmCC";
        // 8x8 SEMI-TRANSPARENT PNG: magenta/cyan checkerboard, alpha 255/96.
        const string semiPngB64 =
            "iVBORw0KGgoAAAANSUhEUgAAAAgAAAAICAYAAADED76LAAAAHElEQVR42mN4pnHiv8adOwm4aAZ8kiCa" +
            "YViYAADHNaKhFcrVUgAAAABJRU5ErkJggg==";

        var opaquePng = s.BitmapFromEncoded(Convert.FromBase64String(opaquePngB64));
        var semiPng = s.BitmapFromEncoded(Convert.FromBase64String(semiPngB64));

        // Programmatic raw-BGRA bitmap (no decoder in the loop): 8x8 hue wheel.
        byte[] raw = new byte[8 * 8 * 4];
        for (int y = 0; y < 8; y++)
        {
            for (int x = 0; x < 8; x++)
            {
                int i = (y * 8 + x) * 4;
                raw[i + 0] = (byte)(x * 36);          // B
                raw[i + 1] = (byte)(y * 36);          // G
                raw[i + 2] = (byte)(255 - x * 30);    // R
                raw[i + 3] = 255;                     // A
            }
        }
        var rawBitmap = s.BitmapFromPixels(raw, 8, 8);

        // Backdrop stripes so semi-transparent draws have structure to blend over.
        var stripe = s.Solid(0.30f, 0.30f, 0.38f);
        for (int i = 0; i < 8; i++)
        {
            s.Target.FillRectangle(0, i * 64f, 512f, 32f, stripe);
        }

        // Row 1: opaque PNG upscaled 8→176 — Nearest (blocky) vs Linear (soft).
        s.Target.DrawBitmap(opaquePng, 40, 40, 176, 176, 1f, BitmapScalingMode.NearestNeighbor);
        s.Target.DrawBitmap(opaquePng, 296, 40, 176, 176, 1f, BitmapScalingMode.Linear);

        // Row 2: semi-transparent PNG — alpha blend correctness × both samplers.
        s.Target.DrawBitmap(semiPng, 40, 260, 176, 176, 1f, BitmapScalingMode.NearestNeighbor);
        s.Target.DrawBitmap(semiPng, 296, 260, 176, 176, 1f, BitmapScalingMode.Linear);

        // Raw-pixel bitmap, opacity 0.6, default sampler.
        s.Target.DrawBitmap(rawBitmap, 208, 208, 96, 96, 0.6f);
    }

    // ── Clips: rect / rounded / nested rounded / rotated-ancestor AABB ──────
    private static void Clip(SceneContext s)
    {
        var red = s.Solid(0.90f, 0.25f, 0.20f);
        var cyan = s.Solid(0.20f, 0.80f, 0.85f);
        var lime = s.Solid(0.55f, 0.90f, 0.25f);
        var vio = s.Solid(0.70f, 0.40f, 0.95f);

        // 1. Axis-aligned rect clip: ellipse partially inside — the visible
        //    left arc AND the hard right/bottom cut edges must both show.
        s.Target.PushClip(32, 32, 200, 200);
        s.Target.FillEllipse(210, 170, 90, 90, red);
        s.Target.PopClip();

        // 2. Rounded clip: full-bleed fill must show rounded corners.
        s.Target.PushRoundedRectClip(280, 32, 200, 200, 36, 36);
        s.Target.FillRectangle(280, 32, 200, 200, cyan);
        s.Target.PopClip();

        // 3. Nested two-level rounded clips (the Vulkan nested rounded-clip
        //    bitmap-drop regression class): inner content obeys BOTH radii.
        s.Target.PushRoundedRectClip(32, 280, 200, 200, 48, 48);
        s.Target.FillRectangle(32, 280, 200, 200, vio);
        s.Target.PushRoundedRectClip(72, 320, 120, 120, 30, 30);
        s.Target.FillRectangle(32, 280, 200, 200, lime);
        s.Target.PopClip();
        s.Target.PopClip();

        // 4. Rotated ancestor + axis-aligned clip: the clip rect is pushed
        //    INSIDE a rotated transform, so backends must intersect in the
        //    right space (AABB-vs-true-shape divergence shows here).
        const float angle = 25f * MathF.PI / 180f;
        float c = MathF.Cos(angle), n = MathF.Sin(angle);
        // Column-major 3x2 rotate about (380, 380): T(cx,cy)·R·T(-cx,-cy).
        float cx = 380f, cy = 380f;
        float dx = cx - c * cx + n * cy;
        float dy = cy - n * cx - c * cy;
        s.Target.PushTransform([c, n, -n, c, dx, dy]);
        s.Target.PushClip(310, 310, 140, 140);
        s.Target.FillEllipse(380, 380, 110, 110, red);
        s.Target.FillRectangle(310, 310, 140, 30, cyan);
        s.Target.PopClip();
        s.Target.PopTransform();
    }

    // ── Rotated ROUNDED clips: a rounded clip pushed inside a rotated
    //    transform keeps its SDF mask — axis-aligned over the rotated rect's
    //    AABB with column-norm-scaled radii, the D3D12 ResolveLiveRoundedClip
    //    approximation both backends must share (geometrically loose for the
    //    rotated rect, but pixel-identical across backends).
    private static void ClipRotatedRounded(SceneContext s)
    {
        var red = s.Solid(0.95f, 0.20f, 0.15f);
        var cyan = s.Solid(0.15f, 0.80f, 0.85f);
        var lime = s.Solid(0.55f, 0.90f, 0.20f);

        const float angle = 25f * MathF.PI / 180f;
        float c = MathF.Cos(angle), n = MathF.Sin(angle);

        // 1. Rotated rounded include over a full-bleed rect (SolidRect quad
        //    path, no slot contention): the visible shape is the clip's
        //    rotated AABB with rounded corners.
        float cx = 150f, cy = 150f;
        float dx = cx - c * cx + n * cy;
        float dy = cy - n * cx - c * cy;
        s.Target.PushTransform([c, n, -n, c, dx, dy]);
        s.Target.PushRoundedRectClip(70, 70, 160, 160, 40, 40);
        s.Target.FillRectangle(30, 30, 240, 240, red);
        s.Target.PopClip();
        s.Target.PopTransform();

        // 2. Same clip class over a circle (rotated-local geometry path):
        //    exercises the ancestor-mask vs local-coverage slot interaction.
        float cx2 = 380f, cy2 = 150f;
        float dx2 = cx2 - c * cx2 + n * cy2;
        float dy2 = cy2 - n * cx2 - c * cy2;
        s.Target.PushTransform([c, n, -n, c, dx2, dy2]);
        s.Target.PushRoundedRectClip(310, 80, 140, 140, 30, 30);
        s.Target.FillEllipse(380, 150, 110, 110, cyan);
        s.Target.PopClip();
        s.Target.PopTransform();

        // 3. Axis-aligned rounded clip control: column norms collapse to
        //    |m11| / |m22| here, so this must stay byte-identical to the
        //    pre-change output.
        s.Target.PushRoundedRectClip(70, 320, 160, 160, 36, 36);
        s.Target.FillRectangle(70, 320, 160, 160, lime);
        s.Target.PopClip();
    }

    // ── Opacity groups: nested layers must multiply, not stack-overwrite ────
    private static void OpacityLayers(SceneContext s)
    {
        var red = s.Solid(0.95f, 0.20f, 0.15f);
        var green = s.Solid(0.15f, 0.85f, 0.30f);
        var blue = s.Solid(0.20f, 0.40f, 0.95f);
        var white = s.Solid(1f, 1f, 1f);

        // Reference row: direct alpha in the brush.
        s.Target.FillRectangle(32, 32, 448, 60, s.Solid(0.95f, 0.20f, 0.15f, 0.5f));

        // PushOpacity 0.5 over overlapping shapes.
        s.Target.PushOpacity(0.5f);
        s.Target.FillRectangle(32, 130, 260, 120, red);
        s.Target.FillEllipse(300, 190, 110, 60, green);
        s.Target.PopOpacity();

        // Nested opacity: 0.6 × 0.5 = 0.3 effective on the inner rect.
        s.Target.PushOpacity(0.6f);
        s.Target.FillRectangle(32, 290, 448, 80, blue);
        s.Target.PushOpacity(0.5f);
        s.Target.FillRectangle(96, 310, 320, 40, white);
        s.Target.PopOpacity();
        s.Target.PopOpacity();

        // Full-opacity control strip (must be identical across backends).
        s.Target.FillRectangle(32, 410, 448, 50, green);
    }

    // ── Painter's algorithm: opaque rect must cover the path below it ───────
    // Regression class: engine flush ordering (Impeller batches vs SDF rects)
    // letting a later-issued opaque rect slip UNDER an earlier path.
    private static void PainterOrder(SceneContext s)
    {
        var wave = s.Solid(0.95f, 0.60f, 0.10f);
        var cover = s.Solid(0.15f, 0.25f, 0.55f);
        var top = s.Solid(0.90f, 0.90f, 0.95f);

        // Full-width cubic wave band.
        float[] waveCmds =
        [
            1f, 160f, 100f, 350f, 300f, 512f, 200f,
            0f, 512f, 512f,
            0f, 0f, 512f,
            5f,
        ];
        s.Target.FillPath(0f, 200f, waveCmds, wave, fillRule: 1);

        // Opaque rect ISSUED AFTER the path — must paint on top of it.
        s.Target.FillRectangle(96, 180, 320, 200, cover);

        // And a path ISSUED AFTER the rect — must paint on top of the rect.
        float[] triangle =
        [
            0f, 320f, 340f,
            0f, 192f, 340f,
            5f,
        ];
        s.Target.FillPath(256f, 220f, triangle, top, fillRule: 1);
    }

    // ── A1 regression: solid Border inside a rounded-clip container ─────────
    // The titlebar-class bug: a thin/solid fill inside a rounded clip losing
    // pixels (per-batch scissor / stencil collapse). Draw a WPF-ish "Border":
    // rounded container clip + solid background + 1px inner border lines.
    private static void RoundedContainerBorder(SceneContext s)
    {
        var containerBg = s.Solid(0.18f, 0.20f, 0.26f);
        var headerBg = s.Solid(0.30f, 0.55f, 0.90f);
        var hairline = s.Solid(0.95f, 0.95f, 0.98f);
        var chip = s.Solid(0.95f, 0.35f, 0.35f);

        // Container: rounded clip + full-bleed background.
        s.Target.PushRoundedRectClip(64, 64, 384, 384, 28, 28);
        s.Target.FillRectangle(64, 64, 384, 384, containerBg);

        // Header strip: SOLID fill that crosses the rounded corners — the
        // A1 class regression drops exactly these thin solid fills.
        s.Target.FillRectangle(64, 64, 384, 56, headerBg);

        // 1px hairlines (top of body, bottom of header) — solid, full width.
        s.Target.FillRectangle(64, 120, 384, 1, hairline);
        s.Target.FillRectangle(64, 447, 384, 1, hairline);

        // Small solid chips inside (various sizes down to 2px).
        s.Target.FillRectangle(96, 160, 120, 32, chip);
        s.Target.FillRectangle(96, 210, 60, 8, chip);
        s.Target.FillRectangle(96, 230, 30, 2, chip);
        s.Target.PopClip();

        // Border stroke on top of the clipped content (outside the clip).
        s.Target.DrawRoundedRectangle(64, 64, 384, 384, 28, 28, hairline, 2f);
    }

    // ── SuperEllipse (Border.Shape=SuperEllipse): SetShapeType + rounded-rect ──
    // Shape contract: shapeType==1 renders the FULL-RECT Lamé superellipse
    // |X/(w/2)|^n + |Y/(h/2)|^n = 1 — curvature comes from the exponent alone,
    // the corner-radius arguments are ignored (D3D12 sdSuperEllipseRect, the
    // managed Border layout clip, and the Vulkan tessellator all agree).
    // Every call pair mirrors the managed Border: set(1,n) → draw → set(0,4).
    // The radius=0 tiles are the regression case: the managed Border passes the
    // element's real CornerRadius, which DEFAULTS TO 0 — an earlier Vulkan
    // per-corner-arc implementation degenerated exactly these into
    // sharp-cornered rectangles while D3D12 showed the full squircle.
    private static void SuperEllipse(SceneContext s)
    {
        var fillA = s.Solid(0.85f, 0.35f, 0.25f);
        var fillB = s.Solid(0.25f, 0.65f, 0.85f);
        var fillC = s.Solid(0.45f, 0.80f, 0.35f);
        var stroke = s.Solid(0.95f, 0.90f, 0.30f);

        // Per-corner fill, radius 0 (the Border default — the regression case).
        s.Target.SetShapeType(1, 4.0f);
        s.Target.FillPerCornerRoundedRectangle(32, 32, 200, 120, 0, 0, 0, 0, fillA);
        s.Target.SetShapeType(0, 4.0f);

        // Per-corner fill, NON-zero radii — must render identically to radius 0
        // (the radii are ignored under shapeType==1 by contract).
        s.Target.SetShapeType(1, 4.0f);
        s.Target.FillPerCornerRoundedRectangle(280, 32, 200, 120, 24, 24, 24, 24, fillB);
        s.Target.SetShapeType(0, 4.0f);

        // Gradient fill (exercises the Vulkan in-order gradient fan path).
        var grad = s.Linear(32, 190, 232, 270,
        [
            0.00f, 0.90f, 0.10f, 0.10f, 1f,
            0.50f, 0.95f, 0.80f, 0.10f, 1f,
            1.00f, 0.15f, 0.35f, 0.90f, 1f,
        ]);
        s.Target.SetShapeType(1, 4.0f);
        s.Target.FillPerCornerRoundedRectangle(32, 190, 200, 80, 0, 0, 0, 0, grad);
        s.Target.SetShapeType(0, 4.0f);

        // Per-corner stroke, radius 0 (the Border BorderBrush path).
        s.Target.SetShapeType(1, 4.0f);
        s.Target.DrawPerCornerRoundedRectangle(280, 190, 200, 80, 0, 0, 0, 0, stroke, 5f);
        s.Target.SetShapeType(0, 4.0f);

        // Lower exponent (n=2.5, closer to an ellipse) — exponent plumbing.
        s.Target.SetShapeType(1, 2.5f);
        s.Target.FillPerCornerRoundedRectangle(32, 310, 200, 150, 0, 0, 0, 0, fillC);
        s.Target.SetShapeType(0, 4.0f);

        // Uniform overload + high exponent (n=6, nearly rectangular).
        s.Target.SetShapeType(1, 6.0f);
        s.Target.FillRoundedRectangle(280, 310, 200, 150, 0, 0, fillB);
        s.Target.SetShapeType(0, 4.0f);
    }

    // ========================================================================
    // Effect-system scenes
    //
    // Contract shared by every effect scene:
    //   1. BeginEffectCapture(rx,ry,rw,rh)  — arm the offscreen region
    //   2. draw a KNOWN element strictly INSIDE that region
    //   3. EndEffectCapture()               — close the region
    //   4. DrawXxxEffect(rx,ry,rw,rh, ...)  — re-emit the region through the
    //      effect, using the SAME rect as the capture
    //
    // The capture rect must leave headroom around the element for effects whose
    // output grows past the source silhouette (blur, drop-shadow offset+halo,
    // outer glow), or the halo is scissored to the region and the two backends
    // clip it identically — masking the very divergence we want to measure. The
    // element is drawn a fixed inset inside each region so that headroom exists.
    //
    // Mirrors the real framework flow (RenderTargetDrawingContext.PushEffect →
    // BeginEffectCapture, PopEffect → EndEffectCapture + ApplyElementEffect →
    // DrawXxxEffect) and the Vulkan Stage-4 smoke tests
    // (tests/Jalium.UI.Tests/VulkanEffectGpuRtSmokeTests.cs).
    // ========================================================================

    // Paints the standard "known element": an opaque rounded rect body plus a
    // partly-transparent rounded rect inset, giving each effect both a hard
    // alpha silhouette (for shadow/glow) and an interior gradient of coverage
    // (for blur/emboss/color-matrix). Drawn INSIDE [rx,ry,rw,rh] with `inset`
    // headroom on every side.
    private static void CaptureKnownElement(SceneContext s, float rx, float ry, float rw, float rh, float inset)
    {
        float ex = rx + inset, ey = ry + inset;
        float ew = rw - 2 * inset, eh = rh - 2 * inset;

        var body = s.Solid(0.30f, 0.62f, 0.92f);          // opaque blue body
        var accent = s.Solid(0.98f, 0.78f, 0.22f, 0.75f); // semi-transparent amber inset
        var dot = s.Solid(0.95f, 0.30f, 0.35f);           // opaque detail (edges for emboss)

        s.Target.FillRoundedRectangle(ex, ey, ew, eh, 18, 18, body);
        // Interior amber panel with its own alpha — interacts with color-matrix
        // and blur, and gives emboss a strong internal edge.
        s.Target.FillRoundedRectangle(ex + ew * 0.22f, ey + eh * 0.24f,
            ew * 0.56f, eh * 0.52f, 12, 12, accent);
        // Two small opaque dots: high-frequency edges so emboss / blur radius
        // differences are legible.
        float r = MathF.Min(ew, eh) * 0.09f;
        s.Target.FillEllipse(ex + ew * 0.30f, ey + eh * 0.32f, r, r, dot);
        s.Target.FillEllipse(ex + ew * 0.70f, ey + eh * 0.68f, r, r, dot);
    }

    // ── Gaussian (D3D12) vs box/tent (Vulkan) blur across three radii ───────
    // 2x2 layout: top-left source (NO effect, reference), then radius 8 / 16 /
    // 30. The radius sweep is the whole point — the two blur kernels diverge
    // more the wider the radius, so the maxRegion should climb across cells.
    private static void EffectBlur(SceneContext s)
    {
        // Region grid — 4 quadrants, generous inset so the blur skirt stays
        // inside its region (else it clips at the region edge on both backends
        // and the interesting falloff is hidden).
        (float x, float y) tl = (24, 24), tr = (280, 24), bl = (24, 280), br = (280, 280);
        const float rw = 208, rh = 208, inset = 44;

        // Reference (source only) — top-left, no capture/effect.
        CaptureKnownElement(s, tl.x, tl.y, rw, rh, inset);

        foreach (var (pos, radius) in new[] { (tr, 8f), (bl, 16f), (br, 30f) })
        {
            s.Target.BeginEffectCapture(pos.x, pos.y, rw, rh);
            CaptureKnownElement(s, pos.x, pos.y, rw, rh, inset);
            s.Target.EndEffectCapture();
            s.Target.DrawBlurEffect(pos.x, pos.y, rw, rh, radius);
        }
    }

    // ── Drop shadow: offset + blur + colored halo behind the element ────────
    // The halo is derived from the element's alpha silhouette; D3D12 blurs it
    // with a Gaussian, Vulkan with its box approximation, and the offset places
    // it down-right so the divergence sits on a clean background band.
    private static void EffectDropShadow(SceneContext s)
    {
        const float rw = 240, rh = 200, inset = 52;

        // Two shadows: a soft neutral one and a tighter colored one, so the
        // report captures both a broad low-alpha skirt and a crisp edge.
        (float x, float y) a = (36, 40), b = (300, 40), c = (168, 280);

        s.Target.BeginEffectCapture(a.x, a.y, rw, rh);
        CaptureKnownElement(s, a.x, a.y, rw, rh, inset);
        s.Target.EndEffectCapture();
        // blur 12, offset (10,10), soft black 60%.
        s.Target.DrawDropShadowEffect(a.x, a.y, rw, rh, 12f, 10f, 10f, 0f, 0f, 0f, 0.6f);

        s.Target.BeginEffectCapture(b.x, b.y, rw, rh);
        CaptureKnownElement(s, b.x, b.y, rw, rh, inset);
        s.Target.EndEffectCapture();
        // blur 6, offset (6,8), saturated indigo.
        s.Target.DrawDropShadowEffect(b.x, b.y, rw, rh, 6f, 6f, 8f, 0.15f, 0.10f, 0.55f, 0.85f);

        s.Target.BeginEffectCapture(c.x, c.y, rw, rh);
        CaptureKnownElement(s, c.x, c.y, rw, rh, inset);
        s.Target.EndEffectCapture();
        // Rounded-corner-aware shadow (per-corner radii feed the SDF silhouette).
        s.Target.DrawDropShadowEffect(c.x, c.y, rw, rh, 16f, 0f, 0f, 0f, 0f, 0f, 0.7f,
            0f, 0f, 24f, 24f, 24f, 24f);
    }

    // ── Outer glow: symmetric colored bloom around the alpha silhouette ─────
    // intensity + glowSize + color. D3D12 and Vulkan differ in both the glow
    // kernel and how intensity scales the accumulated alpha, so the bloom
    // radius and brightness fall off differently — a silhouette-vs-halo class
    // baseline for C-beta.
    private static void EffectOuterGlow(SceneContext s)
    {
        const float rw = 232, rh = 220, inset = 56;
        (float x, float y) a = (30, 40), b = (280, 40), c = (150, 276);

        s.Target.BeginEffectCapture(a.x, a.y, rw, rh);
        CaptureKnownElement(s, a.x, a.y, rw, rh, inset);
        s.Target.EndEffectCapture();
        // glowSize 20, warm amber, intensity 1.0.
        s.Target.DrawOuterGlowEffect(a.x, a.y, rw, rh, 20f, 1.0f, 0.75f, 0.20f, 1f, 1.0f);

        s.Target.BeginEffectCapture(b.x, b.y, rw, rh);
        CaptureKnownElement(s, b.x, b.y, rw, rh, inset);
        s.Target.EndEffectCapture();
        // glowSize 12, cyan, higher intensity 1.8 (over-driven bloom).
        s.Target.DrawOuterGlowEffect(b.x, b.y, rw, rh, 12f, 0.20f, 0.90f, 1.0f, 1f, 1.8f);

        s.Target.BeginEffectCapture(c.x, c.y, rw, rh);
        CaptureKnownElement(s, c.x, c.y, rw, rh, inset);
        s.Target.EndEffectCapture();
        // Wide, soft magenta glow, intensity 0.7.
        s.Target.DrawOuterGlowEffect(c.x, c.y, rw, rh, 30f, 0.95f, 0.25f, 0.85f, 1f, 0.7f);
    }

    // ── Color matrix: 4x5 row-major (R,G,B,A rows; 5th col = bias) ──────────
    // Two panels: a luma-weighted grayscale and a saturation-boost matrix.
    // This is the effect most likely to be byte-close between backends when
    // both run the color-matrix shader (a per-pixel affine transform, no
    // spatial kernel) — but it degrades to composite-back (element unmodified)
    // if dxcompiler.dll can't load, so a big diff here can also mean "one
    // backend compiled the shader and the other fell back."
    private static void EffectColorMatrix(SceneContext s)
    {
        const float rw = 232, rh = 420, inset = 40;
        (float x, float y) left = (24, 46), right = (280, 46);

        // Grayscale: each output channel = luma of the input (Rec.601-ish).
        s.Target.BeginEffectCapture(left.x, left.y, rw, rh);
        CaptureKnownElement(s, left.x, left.y, rw, rh, inset);
        s.Target.EndEffectCapture();
        ReadOnlySpan<float> grayscale =
        [
            0.299f, 0.587f, 0.114f, 0f, 0f,
            0.299f, 0.587f, 0.114f, 0f, 0f,
            0.299f, 0.587f, 0.114f, 0f, 0f,
            0f,     0f,     0f,     1f, 0f,
        ];
        s.Target.DrawColorMatrixEffect(left.x, left.y, rw, rh, grayscale);

        // Saturation boost (factor 1.8 about the luma point) — standard
        // s + (1-s)*luma per channel construction.
        const float sat = 1.8f;
        const float lr = 0.3086f, lg = 0.6094f, lb = 0.0820f; // Rec.709 luma weights
        float ar = (1 - sat) * lr, ag = (1 - sat) * lg, ab = (1 - sat) * lb;
        s.Target.BeginEffectCapture(right.x, right.y, rw, rh);
        CaptureKnownElement(s, right.x, right.y, rw, rh, inset);
        s.Target.EndEffectCapture();
        ReadOnlySpan<float> saturate =
        [
            ar + sat, ag,       ab,       0f, 0f,
            ar,       ag + sat, ab,       0f, 0f,
            ar,       ag,       ab + sat, 0f, 0f,
            0f,       0f,       0f,       1f, 0f,
        ];
        s.Target.DrawColorMatrixEffect(right.x, right.y, rw, rh, saturate);
    }

    // ── Emboss: directional relief from the luminance gradient ──────────────
    // amount + light direction (x,y) + relief depth. The result is a gray
    // bas-relief; the two backends sample the gradient with different taps, so
    // edges shift by a pixel and the flat interior gray can differ by a level.
    private static void EffectEmboss(SceneContext s)
    {
        const float rw = 232, rh = 220, inset = 30;
        (float x, float y) a = (30, 40), b = (280, 40), c = (150, 276);

        // Light from top-left, medium amount, relief 2.
        s.Target.BeginEffectCapture(a.x, a.y, rw, rh);
        CaptureKnownElement(s, a.x, a.y, rw, rh, inset);
        s.Target.EndEffectCapture();
        s.Target.DrawEmbossEffect(a.x, a.y, rw, rh, 1.0f, -1f, -1f, 2f);

        // Light from the right, stronger amount, shallow relief.
        s.Target.BeginEffectCapture(b.x, b.y, rw, rh);
        CaptureKnownElement(s, b.x, b.y, rw, rh, inset);
        s.Target.EndEffectCapture();
        s.Target.DrawEmbossEffect(b.x, b.y, rw, rh, 1.6f, 1f, 0f, 1f);

        // Light from the bottom, deep relief.
        s.Target.BeginEffectCapture(c.x, c.y, rw, rh);
        CaptureKnownElement(s, c.x, c.y, rw, rh, inset);
        s.Target.EndEffectCapture();
        s.Target.DrawEmbossEffect(c.x, c.y, rw, rh, 0.8f, 0f, 1f, 3f);
    }

    // ── Inner shadow: shadow cast INWARD from the silhouette edges ──────────
    // blur + offset + color, occluded to the element interior. The element
    // stays put; a dark gradient hugs the inside of the top/left edges (for a
    // down-right offset). Backends differ in the inner-edge falloff.
    private static void EffectInnerShadow(SceneContext s)
    {
        const float rw = 240, rh = 200, inset = 30;
        (float x, float y) a = (36, 40), b = (300, 40), c = (168, 280);

        s.Target.BeginEffectCapture(a.x, a.y, rw, rh);
        CaptureKnownElement(s, a.x, a.y, rw, rh, inset);
        s.Target.EndEffectCapture();
        // blur 10, offset (6,6), black 70%.
        s.Target.DrawInnerShadowEffect(a.x, a.y, rw, rh, 10f, 6f, 6f, 0f, 0f, 0f, 0.7f);

        s.Target.BeginEffectCapture(b.x, b.y, rw, rh);
        CaptureKnownElement(s, b.x, b.y, rw, rh, inset);
        s.Target.EndEffectCapture();
        // Tight, offset up-left, indigo tint.
        s.Target.DrawInnerShadowEffect(b.x, b.y, rw, rh, 5f, -5f, -5f, 0.12f, 0.10f, 0.45f, 0.8f);

        s.Target.BeginEffectCapture(c.x, c.y, rw, rh);
        CaptureKnownElement(s, c.x, c.y, rw, rh, inset);
        s.Target.EndEffectCapture();
        // Rounded-corner-aware inner shadow, straight down.
        s.Target.DrawInnerShadowEffect(c.x, c.y, rw, rh, 14f, 0f, 8f, 0f, 0f, 0f, 0.65f,
            0f, 0f, 24f, 24f, 24f, 24f);
    }

    // ── Liquid glass: SDF refraction + highlight + inner shadow over content ─
    // Unlike the capture-based effects, liquid glass samples the LIVE scene
    // behind its rect (a backdrop-style effect), so we first lay down a busy
    // gradient/shape background and then draw the glass panel over it. Two
    // panels exercise the default rounded-rect SDF (shapeType 0) and the
    // super-ellipse (shapeType 1) with a chromatic-aberration variant.
    private static void EffectLiquidGlass(SceneContext s)
    {
        // Busy backdrop so refraction/blur have structure to distort.
        var g = s.Linear(0, 0, 512, 512,
        [
            0.00f, 0.90f, 0.30f, 0.20f, 1f,
            0.35f, 0.95f, 0.80f, 0.20f, 1f,
            0.65f, 0.15f, 0.75f, 0.55f, 1f,
            1.00f, 0.20f, 0.35f, 0.90f, 1f,
        ]);
        s.Target.FillRectangle(0, 0, 512, 512, g);
        // High-contrast stripes under the glass so the refraction offset shows.
        var stripe = s.Solid(0.02f, 0.02f, 0.05f);
        for (int i = 0; i < 16; i++)
        {
            s.Target.FillRectangle(0, i * 32f, 512f, 14f, stripe);
        }
        var accentDot = s.Solid(1f, 1f, 1f);
        s.Target.FillEllipse(256, 130, 34, 34, accentDot);
        s.Target.FillEllipse(150, 380, 26, 26, accentDot);

        // Panel 1: default rounded-rect SDF glass (shapeType 0), moderate
        // refraction, subtle tint.
        s.Target.DrawLiquidGlass(
            48, 48, 200, 180,
            cornerRadius: 32f, blurRadius: 8f, refractionAmount: 60f,
            chromaticAberration: 0f,
            tintR: 0.10f, tintG: 0.12f, tintB: 0.16f, tintOpacity: 0.30f,
            lightX: -1f, lightY: -1f, highlightBoost: 0.4f,
            shapeType: 0, shapeExponent: 4f);

        // Panel 2: super-ellipse SDF (shapeType 1) with chromatic aberration —
        // the RGB channels refract by different amounts, a strong per-backend
        // divergence source.
        s.Target.DrawLiquidGlass(
            270, 260, 200, 200,
            cornerRadius: 40f, blurRadius: 10f, refractionAmount: 80f,
            chromaticAberration: 6f,
            tintR: 0.08f, tintG: 0.08f, tintB: 0.10f, tintOpacity: 0.25f,
            lightX: 1f, lightY: -1f, highlightBoost: 0.6f,
            shapeType: 1, shapeExponent: 5f);
    }

    // ── Transition shader: blend two captured content slots by progress+mode ─
    // Slot 0 = "old" content, slot 1 = "new" content. Each is captured over
    // the SAME rect, then DrawTransitionShader blends them at a fixed progress
    // with a chosen mode index (0-9). Held at progress 0.5 so both slots are
    // visible and the blend geometry (wipe edge / dissolve pattern) is what the
    // diff measures. If the harness cannot drive the two-slot capture the
    // scene simply renders whatever the backend composites — see report TODO.
    private static void EffectTransition(SceneContext s)
    {
        const float rx = 56, ry = 56, rw = 400, rh = 400;

        // Slot 0: "old" content — warm circle on a solid ground.
        s.Target.BeginTransitionCapture(0, rx, ry, rw, rh);
        s.Target.FillRectangle(rx, ry, rw, rh, s.Solid(0.85f, 0.35f, 0.20f));
        s.Target.FillEllipse(rx + rw * 0.5f, ry + rh * 0.5f, 120, 120, s.Solid(0.98f, 0.85f, 0.35f));
        s.Target.EndTransitionCapture(0);

        // Slot 1: "new" content — cool grid on a solid ground.
        s.Target.BeginTransitionCapture(1, rx, ry, rw, rh);
        s.Target.FillRectangle(rx, ry, rw, rh, s.Solid(0.15f, 0.35f, 0.75f));
        var tile = s.Solid(0.60f, 0.85f, 0.98f);
        for (int gy = 0; gy < 5; gy++)
        {
            for (int gx = 0; gx < 5; gx++)
            {
                s.Target.FillRectangle(rx + 20 + gx * 74f, ry + 20 + gy * 74f, 54, 54, tile);
            }
        }
        s.Target.EndTransitionCapture(1);

        // Blend held mid-transition (progress 0.5), mode 0 (typically a
        // linear/fade wipe). A fixed non-animating progress keeps the frame
        // deterministic for the diff.
        s.Target.DrawTransitionShader(rx, ry, rw, rh, 0.5f, 0, cornerRadius: 0f);
    }

    // ── In-app Acrylic backdrop: blur + tint + saturation + noise + corners ──
    // Lays down a busy multi-colour background, then draws an Acrylic panel over
    // the CENTRE through DrawBackdropFilterEx. The panel must show the blurred
    // background (not the sharp shapes), a blue-grey tint, a measurable amount
    // of high-frequency NOISE (adjacent-pixel variance the un-noised path lacks),
    // and rounded corners. Noise is exaggerated (0.5) so it is legible in 8-bit.
    private static void BackdropAcrylic(SceneContext s)
    {
        // Busy background: diagonal colour bands + hard shapes give the blur
        // something to average and the noise a flat-ish region to perturb.
        var b1 = s.Solid(0.90f, 0.25f, 0.20f);
        var b2 = s.Solid(0.15f, 0.55f, 0.90f);
        var b3 = s.Solid(0.95f, 0.80f, 0.15f);
        s.Target.FillRectangle(0, 0, 512, 512, s.Solid(0.20f, 0.60f, 0.35f));
        for (int i = 0; i < 12; i++)
        {
            var c = (i % 3 == 0) ? b1 : (i % 3 == 1) ? b2 : b3;
            s.Target.FillRectangle(0, i * 44f, 512f, 22f, c);
        }
        s.Target.FillEllipse(150, 150, 70, 70, b1);
        s.Target.FillEllipse(360, 360, 80, 80, b2);

        // Acrylic panel over the centre. Blue-grey tint, 60% opacity, blur 6
        // (source texels), saturation 1.4 (vibrant), EXAGGERATED noise 0.5,
        // luminosity 1.05, 32px uniform corners.
        s.Target.DrawBackdropFilterEx(
            128, 128, 256, 256,
            "blur(6px)", "acrylic", "#2C3550",
            materialTintOpacity: 0.60f,
            materialBlurRadius: 6f,
            noiseIntensity: 0.5f,
            saturation: 1.4f,
            luminosity: 1.05f,
            cornerRadiusTL: 32f, cornerRadiusTR: 32f,
            cornerRadiusBR: 32f, cornerRadiusBL: 32f);
    }

    // ── In-app plain blur backdrop (BlurEffect): blur only, no tint/noise/sat ─
    // A sharp 32px checkerboard background; the blur panel over the centre must
    // read as SMOOTHED (neighbouring cells averaged) rather than the crisp
    // checker, proving the blur really samples the framebuffer behind it. No
    // tint, so the panel stays close to the (blurred) background colour.
    private static void BackdropPlainBlur(SceneContext s)
    {
        var dark = s.Solid(0.06f, 0.06f, 0.10f);
        var light = s.Solid(0.92f, 0.92f, 0.96f);
        const int cell = 32;
        for (int gy = 0; gy < 512 / cell; gy++)
        {
            for (int gx = 0; gx < 512 / cell; gx++)
            {
                var c = ((gx + gy) % 2 == 0) ? dark : light;
                s.Target.FillRectangle(gx * cell, gy * cell, cell, cell, c);
            }
        }

        // Blur-only panel (BlurEffect semantics): no tint, no noise, no
        // saturation shift, no luminosity shift; sharp corners.
        s.Target.DrawBackdropFilterEx(
            120, 120, 272, 272,
            "blur(8px)", string.Empty, string.Empty,
            materialTintOpacity: 0f,
            materialBlurRadius: 8f,
            noiseIntensity: 0f,
            saturation: 1f,
            luminosity: 1f,
            cornerRadiusTL: 0f, cornerRadiusTR: 0f,
            cornerRadiusBR: 0f, cornerRadiusBL: 0f);
    }
}

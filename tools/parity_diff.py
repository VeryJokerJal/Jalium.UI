#!/usr/bin/env python3
"""Cross-backend parity BMP diff driver.

Reads two output directories produced by Jalium.UI.ParityHarness (one per
backend), compares each same-named 32bpp BMP pixel-by-pixel, and emits:
  * per-scene: diffPixelCount, diffPct, maxRegion (largest connected diff
    blob, approximated by row-run merging), verdict line
  * a summary markdown report (parity_report.md in --out, default dirB)

Verdict thresholds (per scene, override table below):
  PASS  diffPct < threshold AND maxRegion < maxRegionPx2
  WARN  diffPct < 4x threshold (visible but bounded divergence)
  FAIL  otherwise

Pixels differ when any BGR channel differs by more than --tolerance
(default 2/255 — absorbs pure rounding noise; alpha is ignored because the
readback ABI reports raw swap-chain alpha and the parity scenes render into
opaque windows where alpha ≈ 1 on both backends by construction).

No external imaging dependency — the harness writes plain 54-byte-header
32bpp BMPs, parsed here with struct alone.

Usage:
  python tools/parity_diff.py out/parity/d3d12 out/parity/vulkan [--out DIR]
Exit codes: 0 all PASS/WARN, 1 any FAIL or missing pair, 2 nothing to compare
(e.g. one side SKIPped because its backend lacks readback support).
"""

from __future__ import annotations

import argparse
import os
import struct
import sys

# ── Per-scene thresholds ────────────────────────────────────────────────────
# Defaults: AA-class scenes tolerate < 0.5% differing pixels with the largest
# connected diff region under 64 px² (an AA fringe, not a missing shape).
# Scenes dominated by known-divergent rasterization (text hinting, sampler
# rounding) get explicit overrides; tighten as backend parity improves.
DEFAULT_DIFF_PCT = 0.5      # % of all pixels
DEFAULT_MAX_REGION = 64     # px² of the largest connected diff region

SCENE_OVERRIDES: dict[str, tuple[float, int]] = {
    # (diffPct%, maxRegionPx²)
    "solid-rect-grid": (0.05, 16),          # axis-aligned opaque fills: near-exact
    "rounded-container-border": (0.5, 128), # SDF corner AA arcs differ slightly
    "superellipse": (1.6, 1024),            # D3D12 renders the Lamé boundary as a
                                            # per-pixel SDF, Vulkan as a tessellated
                                            # polygon: the whole curved perimeter is
                                            # an AA-model seam (~1.5%). The stroke
                                            # tile also carries a KNOWN D3D12 defect
                                            # (sdSuperEllipseRect pseudo-distance is
                                            # anisotropic, so its stroke band is up
                                            # to ~2.4x too wide on the long sides).
                                            # Sized so the shape-contract regression
                                            # (square corners at radius 0: 5.7%)
                                            # lands in FAIL; tighten once the D3D12
                                            # stroke distance is normalised.
    "staging-mixed-superellipse-liquidglass": (0.5, 256),
                                            # One large SDF-vs-tessellated boundary
                                            # plus a tiny LiquidGlass panel. The
                                            # expected seam is ~0.3%; a stale staging
                                            # base drops the entire squircle (~20%).
    "gradient-linear": (1.0, 256),          # interpolation precision differs
    "gradient-radial": (1.5, 512),
    "text": (3.0, 4096),                    # glyph raster/hinting: whole glyphs may shift
    "text-cleartype": (3.0, 4096),          # same raster variance + sub-pixel fringe deltas
    "bitmap": (2.0, 2048),                  # sampler rounding on 22x upscale
    "line-fan": (1.5, 256),                 # 1px line AA models differ per backend
    "stroke-wide-dash": (1.5, 512),         # dash phase + join tessellation

    # ── Effect-system scenes ────────────────────────────────────────────────
    # These are a KNOWN-DIVERGENT baseline for the C-beta effect-alignment work,
    # NOT a parity target we expect to pass today: D3D12 blurs with a separable
    # Gaussian while Vulkan uses a box/tent approximation, and the shadow/glow
    # halo derives from the alpha silhouette, so blur radius + halo falloff
    # differ by construction. The thresholds below are widened for the larger
    # AA / soft-edge surface an effect covers, but DELIBERATELY NOT wide enough
    # to swallow a structural miss: the FAIL trigger (>= 4x diffPct OR >= 8x
    # maxRegion) still fires on "the shape rendered differently or the effect
    # was skipped entirely", so these scenes are expected to land in WARN/FAIL
    # and the numbers are the signal, not a pass/fail gate. Do NOT raise these
    # to force PASS — that would erase the very divergence being tracked. Tighten
    # them toward the vector-scene budgets as C-beta lands blur-kernel parity.
    "effect-blur": (2.5, 4096),         # Gaussian vs box: whole soft skirt differs
    "effect-dropshadow": (3.0, 8192),   # halo shape + offset falloff diverge
    "effect-outerglow": (3.0, 8192),    # bloom kernel + intensity scaling differ
    "effect-colormatrix": (1.0, 512),   # per-pixel affine: should be closest —
                                        # a big diff here flags a shader/degrade split
    "effect-emboss": (2.5, 2048),       # gradient tap offsets shift relief edges
    "effect-innershadow": (2.5, 4096),  # inner-edge falloff differs
    "effect-liquidglass": (4.0, 16384), # SDF refraction + chromatic aberration:
                                        # the most divergent path by design
    "effect-transition": (3.0, 8192),   # blend/wipe geometry + slot compositing
}

BG = None  # filled from CLI if provided


def read_bmp(path: str):
    """Return (width, height, bytearray BGRA top-down)."""
    with open(path, "rb") as f:
        data = f.read()
    if data[:2] != b"BM":
        raise ValueError(f"{path}: not a BMP")
    off = struct.unpack("<I", data[10:14])[0]
    w = struct.unpack("<i", data[18:22])[0]
    h = struct.unpack("<i", data[22:26])[0]
    bpp = struct.unpack("<H", data[28:30])[0]
    if bpp != 32:
        raise ValueError(f"{path}: expected 32bpp, got {bpp}")
    top_down = h < 0
    h = abs(h)
    px = bytearray(data[off:off + w * h * 4])
    if not top_down:  # bottom-up → flip to top-down for uniform addressing
        flipped = bytearray(w * h * 4)
        row = w * 4
        for y in range(h):
            flipped[y * row:(y + 1) * row] = px[(h - 1 - y) * row:(h - y) * row]
        px = flipped
    return w, h, px


def diff_mask(a: bytes, b: bytes, w: int, h: int, tol: int):
    """Per-pixel BGR compare → (diffCount, set of differing pixel indices)."""
    diffs = set()
    for i in range(w * h):
        o = i * 4
        if (abs(a[o] - b[o]) > tol or
                abs(a[o + 1] - b[o + 1]) > tol or
                abs(a[o + 2] - b[o + 2]) > tol):
            diffs.add(i)
    return diffs


def max_region(diffs: set[int], w: int) -> int:
    """Largest connected diff blob (px²), approximated by merging horizontal
    runs that overlap vertically (union-find over row runs — coarse but
    monotone: never under-reports a big missing-shape region)."""
    if not diffs:
        return 0
    # Build per-row runs.
    runs = []  # (y, x0, x1, id)
    by_row: dict[int, list[tuple[int, int]]] = {}
    for idx in diffs:
        y, x = divmod(idx, w)
        by_row.setdefault(y, []).append((x, x))
    for y in sorted(by_row):
        xs = sorted(x0 for x0, _ in by_row[y])
        start = prev = xs[0]
        for x in xs[1:]:
            if x == prev + 1:
                prev = x
                continue
            runs.append([y, start, prev])
            start = prev = x
        runs.append([y, start, prev])

    parent = list(range(len(runs)))

    def find(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    def union(i, j):
        ri, rj = find(i), find(j)
        if ri != rj:
            parent[rj] = ri

    # Merge runs on adjacent rows with x-overlap (8-connectivity: ±1 slack).
    prev_row: list[int] = []
    prev_y = None
    cur_row: list[int] = []
    for i, (y, x0, x1) in enumerate(runs):
        if y != prev_y:
            prev_row = cur_row if prev_y is not None and y - prev_y == 1 else []
            cur_row = []
            prev_y = y
        for j in prev_row:
            jy, jx0, jx1 = runs[j]
            if jx0 <= x1 + 1 and x0 <= jx1 + 1:
                union(i, j)
        cur_row.append(i)

    sizes: dict[int, int] = {}
    for i, (y, x0, x1) in enumerate(runs):
        r = find(i)
        sizes[r] = sizes.get(r, 0) + (x1 - x0 + 1)
    return max(sizes.values())


def main() -> int:
    ap = argparse.ArgumentParser(description="Jalium parity BMP diff")
    ap.add_argument("dirA", help="first harness output dir (e.g. d3d12)")
    ap.add_argument("dirB", help="second harness output dir (e.g. vulkan)")
    ap.add_argument("--out", default=None, help="report dir (default: dirB)")
    ap.add_argument("--tolerance", type=int, default=2,
                    help="per-channel byte tolerance before a pixel counts as diff (default 2)")
    args = ap.parse_args()

    out_dir = args.out or args.dirB
    os.makedirs(out_dir, exist_ok=True)
    report_path = os.path.join(out_dir, "parity_report.md")

    # SKIP markers: a side whose backend lacks readback produced no BMPs.
    for d, tag in ((args.dirA, "A"), (args.dirB, "B")):
        skips = [f for f in os.listdir(d)] if os.path.isdir(d) else []
        if any(f.startswith("SKIP.") for f in skips):
            msg = (f"side {tag} ({d}) is a SKIP run -- its backend reported "
                   "readback NOT_SUPPORTED. Nothing to compare.")
            print(msg)
            with open(report_path, "w", encoding="utf-8") as f:
                f.write(f"# Jalium parity report\n\n**{msg}**\n")
            return 2

    if not os.path.isdir(args.dirA) or not os.path.isdir(args.dirB):
        print("one of the input directories does not exist", file=sys.stderr)
        return 2

    scenes_a = {f[:-4] for f in os.listdir(args.dirA) if f.endswith(".bmp")}
    scenes_b = {f[:-4] for f in os.listdir(args.dirB) if f.endswith(".bmp")}

    # Per-scene fault markers: the harness writes `<scene>.FAILED.txt` when a
    # scene rendered on one backend but faulted on the other (e.g. a Vulkan
    # GPU-offscreen-effect scene hitting EndDraw InvalidState). These scenes are
    # absent from that side's BMP set; we surface them SEPARATELY from genuinely
    # unregistered scenes so the report attributes the gap to a real backend
    # fault (the actionable C-beta signal) rather than a mismatched catalog.
    def read_faults(d: str) -> dict[str, str]:
        out: dict[str, str] = {}
        if not os.path.isdir(d):
            return out
        for f in os.listdir(d):
            if f.endswith(".FAILED.txt"):
                scene = f[: -len(".FAILED.txt")]
                try:
                    with open(os.path.join(d, f), encoding="utf-8") as fh:
                        txt = fh.read()
                    err = next((ln.split("=", 1)[1].strip()
                                for ln in txt.splitlines()
                                if ln.startswith("error=")), "(unspecified)")
                except OSError:
                    err = "(marker unreadable)"
                out[scene] = err
        return out

    faults_a = read_faults(args.dirA)
    faults_b = read_faults(args.dirB)

    common = sorted(scenes_a & scenes_b)
    if not common:
        print("no common scene BMPs between the two directories", file=sys.stderr)
        return 2

    rows = []
    any_fail = False
    for scene in common:
        wa, ha, pa = read_bmp(os.path.join(args.dirA, scene + ".bmp"))
        wb, hb, pb = read_bmp(os.path.join(args.dirB, scene + ".bmp"))
        if (wa, ha) != (wb, hb):
            rows.append((scene, "-", "-", "-", "FAIL (size mismatch)"))
            any_fail = True
            continue
        diffs = diff_mask(pa, pb, wa, ha, args.tolerance)
        total = wa * ha
        pct = len(diffs) * 100.0 / total
        region = max_region(diffs, wa)
        thr_pct, thr_region = SCENE_OVERRIDES.get(scene, (DEFAULT_DIFF_PCT, DEFAULT_MAX_REGION))
        # PASS: within both budgets. FAIL: either budget blown wide open —
        # >= 4x the pixel budget (broad divergence) or >= 8x the region budget
        # (a shape-sized connected blob: something rendered differently or not
        # at all, regardless of how small its area share is). WARN: the
        # bounded band in between — visible, worth a look, not a gate-breaker.
        if pct < thr_pct and region < thr_region:
            verdict = "PASS"
        elif pct >= thr_pct * 4 or region >= thr_region * 8:
            verdict = "FAIL"
            any_fail = True
        else:
            verdict = "WARN"
        rows.append((scene, len(diffs), f"{pct:.3f}%", region,
                     f"{verdict} (thr {thr_pct}% / {thr_region}px^2)"))
        # ASCII-only console output: a GBK/legacy-codepage Windows console
        # cannot encode superscript characters and would crash the run.
        print(f"{verdict:4s} {scene:30s} diffPixels={len(diffs):7d} "
              f"diffPct={pct:7.3f}% maxRegion={region:6d}px^2")

    # Split the one-sided scenes into "faulted here" (a FAILED marker explains
    # the gap) vs "genuinely absent" (no marker — a catalog mismatch).
    only_b = scenes_b - scenes_a   # absent on side A
    only_a = scenes_a - scenes_b   # absent on side B
    faulted_a = sorted(s for s in only_b if s in faults_a)   # A faulted → A has no bmp
    faulted_b = sorted(s for s in only_a if s in faults_b)
    missing_a = sorted(s for s in only_b if s not in faults_a)
    missing_b = sorted(s for s in only_a if s not in faults_b)

    # Console summary of faults so a redirected log shows them without opening md.
    for s in faulted_a:
        print(f"FAULT side A {s:30s} {faults_a[s]}")
    for s in faulted_b:
        print(f"FAULT side B {s:30s} {faults_b[s]}")

    with open(report_path, "w", encoding="utf-8") as f:
        f.write("# Jalium backend parity report\n\n")
        f.write(f"- side A: `{os.path.abspath(args.dirA)}`\n")
        f.write(f"- side B: `{os.path.abspath(args.dirB)}`\n")
        f.write(f"- channel tolerance: ±{args.tolerance}/255 on B,G,R (alpha ignored — "
                "raw swap-chain alpha, opaque-window scenes)\n\n")
        f.write("| scene | diffPixels | diffPct | maxRegion (px^2) | verdict |\n")
        f.write("|---|---:|---:|---:|---|\n")
        for scene, cnt, pct, region, verdict in rows:
            f.write(f"| {scene} | {cnt} | {pct} | {region} | {verdict} |\n")
        if faulted_a or faulted_b:
            # A scene that faulted on one backend is a real parity gap, but it is
            # a KNOWN, attributed one (the backend could not render it), so it is
            # reported as FAULT rather than folded into the anonymous Missing
            # list. It still counts toward the failing verdict.
            f.write("\n## Faulted scenes (rendered on one backend, faulted on the other)\n\n")
            for s in faulted_a:
                f.write(f"- `{s}` FAULTED on side A (absent there): {faults_a[s]}\n")
            for s in faulted_b:
                f.write(f"- `{s}` FAULTED on side B (absent there): {faults_b[s]}\n")
            any_fail = True
        if missing_a or missing_b:
            f.write("\n## Missing scenes\n\n")
            for m in missing_a:
                f.write(f"- `{m}` present only in side B\n")
            for m in missing_b:
                f.write(f"- `{m}` present only in side A\n")
            any_fail = True
        f.write("\n_maxRegion is a row-run union-find approximation of the largest "
                "connected diff blob (in pixels): small = scattered AA fringe, "
                "large = a shape rendered differently or missing._\n")

    print(f"\nreport: {report_path}")
    return 1 if any_fail else 0


if __name__ == "__main__":
    sys.exit(main())

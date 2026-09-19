// Vello GPU Pipeline V3 — flatten
// Port of vello 0.10.0 shader/flatten.wgsl.
// Flattens curves (and GPU-expands strokes via Euler spiral parallel curves)
// into the LineSoup buffer, accumulating per-path device-space bounding boxes.
//
// Includes the 0.6.0 correct-tolerance-from-affine fix (#1187) and the 0.7.0
// near-parallel miter join fix (#1323).
//
// path_bboxes is bound as a raw buffer because x0..y1 need signed atomics:
//   PathBbox stride 24: x0@0 y0@4 x1@8 y1@12 draw_flags@16 trans_ix@20
//
// Bindings: b0 config | t0 scene | t1 tag_monoids | u0 path_bboxes | u1 bump | u2 lines
// Dispatch: (ceil(n_pathtags / 256), 1, 1)

#include "vello_shared.hlsli"

StructuredBuffer<uint> scene : register(t0);
StructuredBuffer<TagMonoid> tag_monoids : register(t1);

RWByteAddressBuffer path_bboxes : register(u0);
RWByteAddressBuffer bump : register(u1);
RWStructuredBuffer<LineSoup> lines : register(u2);

// This shader deliberately uses its own Transform with fma (mad) evaluation —
// watertightness-relevant, matching upstream flatten.wgsl's private Transform.
struct FlatTransform
{
    float4 mat;
    float2 translate;
};

FlatTransform flat_transform_identity()
{
    FlatTransform t;
    t.mat = float4(1.0, 0.0, 0.0, 1.0);
    t.translate = float2(0.0, 0.0);
    return t;
}

FlatTransform read_flat_transform(uint base_offset, uint ix)
{
    uint base = base_offset + ix * 6u;
    float c0 = asfloat(scene[base]);
    float c1 = asfloat(scene[base + 1u]);
    float c2 = asfloat(scene[base + 2u]);
    float c3 = asfloat(scene[base + 3u]);
    float c4 = asfloat(scene[base + 4u]);
    float c5 = asfloat(scene[base + 5u]);
    FlatTransform t;
    t.mat = float4(c0, c1, c2, c3);
    t.translate = float2(c4, c5);
    return t;
}

float2 flat_transform_apply(FlatTransform transform, float2 p)
{
    float px = mad(transform.mat.x, p.x, mad(transform.mat.z, p.y, transform.translate.x));
    float py = mad(transform.mat.y, p.x, mad(transform.mat.w, p.y, transform.translate.y));
    return float2(px, py);
}

struct CubicParams
{
    float th0;
    float th1;
    float chord_len;
    float err;
};

struct EulerParams
{
    float th0;
    // th1 need not be explicitly stored, as it can be derived from k0 - th0
    float k0;
    float k1;
    float ch;
};

struct EulerSeg
{
    float2 p0;
    float2 p1;
    EulerParams params;
};

// Threshold below which a derivative is considered too small.
#define DERIV_THRESH 1e-6
#define DERIV_THRESH_SQUARED (DERIV_THRESH * DERIV_THRESH)
// Amount to nudge t when derivative is near-zero.
#define DERIV_EPS 1e-6
// Limit for subdivision of cubic Béziers.
#define SUBDIV_LIMIT (1.0 / 65536.0)
// Robust ESPC computation: below this value, treat curve as circular arc
#define K1_THRESH 1e-3
// Robust ESPC: below this value, evaluate ES rather than parallel curve
#define DIST_THRESH 1e-3
// Threshold for tangents to be considered near zero length
#define TANGENT_THRESH 1e-6

// Compute cubic parameters from endpoints and derivatives.
CubicParams cubic_from_points_derivs(float2 p0, float2 p1, float2 q0, float2 q1, float dt)
{
    float2 chord = p1 - p0;
    float chord_squared = dot(chord, chord);
    float chord_len = sqrt(chord_squared);
    if (chord_squared < DERIV_THRESH_SQUARED) {
        float chord_err = sqrt((9.0 / 32.0) * (dot(q0, q0) + dot(q1, q1))) * dt;
        CubicParams degen;
        degen.th0 = 0.0;
        degen.th1 = 0.0;
        degen.chord_len = DERIV_THRESH;
        degen.err = chord_err;
        return degen;
    }
    float scale = dt / chord_squared;
    float2 h0 = float2(q0.x * chord.x + q0.y * chord.y, q0.y * chord.x - q0.x * chord.y);
    float th0 = atan2(h0.y, h0.x);
    float d0 = length(h0) * scale;
    float2 h1 = float2(q1.x * chord.x + q1.y * chord.y, q1.x * chord.y - q1.y * chord.x);
    float th1 = atan2(h1.y, h1.x);
    float d1 = length(h1) * scale;

    // Estimate error of geometric Hermite interpolation to Euler spiral.
    float cth0 = cos(th0);
    float cth1 = cos(th1);
    float err = 2.0;
    if (cth0 * cth1 >= 0.0) {
        float e0 = (2.0 / 3.0) / max(1.0 + cth0, 1e-9);
        float e1 = (2.0 / 3.0) / max(1.0 + cth1, 1e-9);
        float s0 = sin(th0);
        float s1 = sin(th1);
        float s01 = cth0 * s1 + cth1 * s0;
        float amin = 0.15 * (2.0 * e0 * s0 + 2.0 * e1 * s1 - e0 * e1 * s01);
        float a = 0.15 * (2.0 * d0 * s0 + 2.0 * d1 * s1 - d0 * d1 * s01);
        float aerr = abs(a - amin);
        float symm = abs(th0 + th1);
        float asymm = abs(th0 - th1);
        float dist = length(float2(d0 - e0, d1 - e1));
        float symm2 = symm * symm;
        float ctr = (4.625e-6 * symm * symm2 + 7.5e-3 * asymm) * symm2;
        float halo = (5e-3 * symm + 7e-2 * asymm) * dist;
        err = ctr + 1.55 * aerr + halo;
    }
    err *= chord_len;
    CubicParams cp;
    cp.th0 = th0;
    cp.th1 = th1;
    cp.chord_len = chord_len;
    cp.err = err;
    return cp;
}

EulerParams es_params_from_angles(float th0, float th1)
{
    float k0 = th0 + th1;
    float dth = th1 - th0;
    float d2 = dth * dth;
    float k2 = k0 * k0;
    float a = 6.0;
    a -= d2 * (1.0 / 70.0);
    a -= (d2 * d2) * (1.0 / 10780.0);
    a += (d2 * d2 * d2) * 2.769178184818219e-07;
    float b = -0.1 + d2 * (1.0 / 4200.0) + d2 * d2 * 1.6959677820260655e-05;
    float c = -1.0 / 1400.0 + d2 * 6.84915970574303e-05 - k2 * 7.936475029053326e-06;
    a += (b + c * k2) * k2;
    float k1 = dth * a;

    // calculation of chord
    float ch = 1.0;
    ch -= d2 * (1.0 / 40.0);
    ch += (d2 * d2) * 0.00034226190482569864;
    ch -= (d2 * d2 * d2) * 1.9349474568904524e-06;
    float b_ = -1.0 / 24.0 + d2 * 0.0024702380951963226 - d2 * d2 * 3.7297408997537985e-05;
    float c_ = 1.0 / 1920.0 - d2 * 4.87350869747975e-05 - k2 * 3.1001936068463107e-06;
    ch += (b_ + c_ * k2) * k2;
    EulerParams ep;
    ep.th0 = th0;
    ep.k0 = k0;
    ep.k1 = k1;
    ep.ch = ch;
    return ep;
}

float es_params_eval_th(EulerParams params, float t)
{
    return (params.k0 + 0.5 * params.k1 * (t - 1.0)) * t - params.th0;
}

// Integrate Euler spiral.
float2 integ_euler_10(float k0, float k1)
{
    float t1_1 = k0;
    float t1_2 = 0.5 * k1;
    float t2_2 = t1_1 * t1_1;
    float t2_3 = 2.0 * (t1_1 * t1_2);
    float t2_4 = t1_2 * t1_2;
    float t3_4 = t2_2 * t1_2 + t2_3 * t1_1;
    float t3_6 = t2_4 * t1_2;
    float t4_4 = t2_2 * t2_2;
    float t4_5 = 2.0 * (t2_2 * t2_3);
    float t4_6 = 2.0 * (t2_2 * t2_4) + t2_3 * t2_3;
    float t4_7 = 2.0 * (t2_3 * t2_4);
    float t4_8 = t2_4 * t2_4;
    float t5_6 = t4_4 * t1_2 + t4_5 * t1_1;
    float t5_8 = t4_6 * t1_2 + t4_7 * t1_1;
    float t6_6 = t4_4 * t2_2;
    float t6_7 = t4_4 * t2_3 + t4_5 * t2_2;
    float t6_8 = t4_4 * t2_4 + t4_5 * t2_3 + t4_6 * t2_2;
    float t7_8 = t6_6 * t1_2 + t6_7 * t1_1;
    float t8_8 = t6_6 * t2_2;
    float u = 1.0;
    u -= (1.0 / 24.0) * t2_2 + (1.0 / 160.0) * t2_4;
    u += (1.0 / 1920.0) * t4_4 + (1.0 / 10752.0) * t4_6 + (1.0 / 55296.0) * t4_8;
    u -= (1.0 / 322560.0) * t6_6 + (1.0 / 1658880.0) * t6_8;
    u += (1.0 / 92897280.0) * t8_8;
    float v = (1.0 / 12.0) * t1_2;
    v -= (1.0 / 480.0) * t3_4 + (1.0 / 2688.0) * t3_6;
    v += (1.0 / 53760.0) * t5_6 + (1.0 / 276480.0) * t5_8;
    v -= (1.0 / 11612160.0) * t7_8;
    return float2(u, v);
}

float2 es_params_eval(EulerParams params, float t)
{
    float thm = es_params_eval_th(params, t * 0.5);
    float k0 = params.k0;
    float k1 = params.k1;
    float2 uv = integ_euler_10((k0 + k1 * (0.5 * t - 0.5)) * t, k1 * t * t);
    float scale = t / params.ch;
    float s = scale * sin(thm);
    float c = scale * cos(thm);
    float x = uv.x * c - uv.y * s;
    float y = -uv.y * c - uv.x * s;
    return float2(x, y);
}

float2 es_params_eval_with_offset(EulerParams params, float t, float offset)
{
    float th = es_params_eval_th(params, t);
    float2 v = offset * float2(sin(th), cos(th));
    return es_params_eval(params, t) + v;
}

// Note: offset provided is scaled so that 1 = chord length
float2 es_seg_eval_with_offset(EulerSeg es, float t, float normalized_offset)
{
    float2 chord = es.p1 - es.p0;
    float2 xy = es_params_eval_with_offset(es.params, t, normalized_offset);
    return es.p0 + float2(chord.x * xy.x - chord.y * xy.y, chord.x * xy.y + chord.y * xy.x);
}

float pow_1_5_signed(float x)
{
    return x * sqrt(abs(x));
}

#define BREAK1 0.8
#define BREAK2 1.25
#define BREAK3 2.1
#define SIN_SCALE 1.0976991822760038
#define QUAD_A1 0.6406
#define QUAD_B1 (-0.81)
#define QUAD_C1 0.9148117935952064
#define QUAD_A2 0.5
#define QUAD_B2 (-0.156)
#define QUAD_C2 0.16145779359520596
#define QUAD_W1 (0.5 * QUAD_B1 / QUAD_A1)
#define QUAD_V1 (1.0 / QUAD_A1)
#define QUAD_U1 (QUAD_W1 * QUAD_W1 - QUAD_C1 / QUAD_A1)
#define QUAD_W2 (0.5 * QUAD_B2 / QUAD_A2)
#define QUAD_V2 (1.0 / QUAD_A2)
#define QUAD_U2 (QUAD_W2 * QUAD_W2 - QUAD_C2 / QUAD_A2)
#define FRAC_PI_4 0.7853981633974483
#define CBRT_9_8 1.040041911525952

float espc_int_approx(float x)
{
    float y = abs(x);
    float a;
    if (y < BREAK1) {
        a = sin(SIN_SCALE * y) * (1.0 / SIN_SCALE);
    } else if (y < BREAK2) {
        a = (sqrt(8.0) / 3.0) * pow_1_5_signed(y - 1.0) + FRAC_PI_4;
    } else {
        float3 abc = (y < BREAK3) ? float3(QUAD_A1, QUAD_B1, QUAD_C1)
                                  : float3(QUAD_A2, QUAD_B2, QUAD_C2);
        a = (abc.x * y + abc.y) * y + abc.z;
    }
    return a * sign(x);
}

float espc_int_inv_approx(float x)
{
    float y = abs(x);
    float a;
    if (y < 0.7010707591262915) {
        a = asin(y * SIN_SCALE) * (1.0 / SIN_SCALE);
    } else if (y < 0.903249293595206) {
        float b = y - FRAC_PI_4;
        float u = pow(abs(b), 2.0 / 3.0) * sign(b);
        a = u * CBRT_9_8 + 1.0;
    } else {
        float3 uvw = (y < 2.038857793595206) ? float3(QUAD_U1, QUAD_V1, QUAD_W1)
                                             : float3(QUAD_U2, QUAD_V2, QUAD_W2);
        a = sqrt(uvw.x + uvw.y * y) - uvw.z;
    }
    return a * sign(x);
}

struct PointDeriv
{
    float2 point_;
    float2 deriv;
};

PointDeriv eval_cubic_and_deriv(float2 p0, float2 p1, float2 p2, float2 p3, float t)
{
    float m = 1.0 - t;
    float mm = m * m;
    float mt = m * t;
    float tt = t * t;
    float2 p = p0 * (mm * m) + (p1 * (3.0 * mm) + p2 * (3.0 * mt) + p3 * tt) * t;
    float2 q = (p1 - p0) * mm + (p2 - p1) * (2.0 * mt) + (p3 - p2) * tt;
    PointDeriv r;
    r.point_ = p;
    r.deriv = q;
    return r;
}

float2 cubic_start_tangent(float2 p0, float2 p1, float2 p2, float2 p3)
{
    const float EPS = 1e-12;
    float2 d01 = p1 - p0;
    float2 d02 = p2 - p0;
    float2 d03 = p3 - p0;
    return (dot(d01, d01) > EPS) ? d01 : ((dot(d02, d02) > EPS) ? d02 : d03);
}

float2 cubic_end_tangent(float2 p0, float2 p1, float2 p2, float2 p3)
{
    const float EPS = 1e-12;
    float2 d23 = p3 - p2;
    float2 d13 = p3 - p1;
    float2 d03 = p3 - p0;
    return (dot(d23, d23) > EPS) ? d23 : ((dot(d13, d13) > EPS) ? d13 : d03);
}

#define ESPC_ROBUST_NORMAL 0
#define ESPC_ROBUST_LOW_K1 1
#define ESPC_ROBUST_LOW_DIST 2

// `pathdata_base` is decoded once and reused by helpers below.
static uint pathdata_base_g;

// The bounding box of the shape flattened by a single shader invocation.
static float4 flat_bbox;

// Writes a line into the `lines` buffer at a pre-allocated location.
void write_line(uint line_ix, uint path_ix, float2 p0, float2 p1)
{
    flat_bbox = float4(min(flat_bbox.xy, min(p0, p1)), max(flat_bbox.zw, max(p0, p1)));
    if (line_ix < lines_size) {
        LineSoup ls;
        ls.path_ix = path_ix;
        ls.p0 = p0;
        ls.p1 = p1;
        ls.pad = 0u;
        lines[line_ix] = ls;
    }
}

void write_line_with_transform(uint line_ix, uint path_ix, float2 p0, float2 p1, FlatTransform t)
{
    float2 tp0 = flat_transform_apply(t, p0);
    float2 tp1 = flat_transform_apply(t, p1);
    write_line(line_ix, path_ix, tp0, tp1);
}

void output_line(uint path_ix, float2 p0, float2 p1)
{
    uint line_ix;
    bump.InterlockedAdd(BUMP_LINES, 1u, line_ix);
    write_line(line_ix, path_ix, p0, p1);
}

void output_line_with_transform(uint path_ix, float2 p0, float2 p1, FlatTransform transform)
{
    uint line_ix;
    bump.InterlockedAdd(BUMP_LINES, 1u, line_ix);
    write_line_with_transform(line_ix, path_ix, p0, p1, transform);
}

void output_two_lines_with_transform(uint path_ix, float2 p00, float2 p01, float2 p10, float2 p11,
                                     FlatTransform transform)
{
    uint line_ix;
    bump.InterlockedAdd(BUMP_LINES, 2u, line_ix);
    write_line_with_transform(line_ix, path_ix, p00, p01, transform);
    write_line_with_transform(line_ix + 1u, path_ix, p10, p11, transform);
}

struct CubicPoints
{
    float2 p0;
    float2 p1;
    float2 p2;
    float2 p3;
};

// This function flattens a cubic Bézier by first converting it into Euler
// spiral segments, and then computes a near-optimal flattening of the parallel
// curves of the Euler spiral segments.
void flatten_euler(CubicPoints cubic, uint path_ix, FlatTransform local_to_device, float offset,
                   float2 start_p, float2 end_p)
{
    float2 p0;
    float2 p1;
    float2 p2;
    float2 p3;
    float scale;
    FlatTransform transform;
    float2 t_start = start_p;
    float2 t_end = end_p;
    if (offset == 0.0) {
        FlatTransform t = local_to_device;
        p0 = flat_transform_apply(t, cubic.p0);
        p1 = flat_transform_apply(t, cubic.p1);
        p2 = flat_transform_apply(t, cubic.p2);
        p3 = flat_transform_apply(t, cubic.p3);
        scale = 1.0;
        transform = flat_transform_identity();
        t_start = p0;
        t_end = p3;
    } else {
        p0 = cubic.p0;
        p1 = cubic.p1;
        p2 = cubic.p2;
        p3 = cubic.p3;

        transform = local_to_device;
        float4 mat = transform.mat;
        // The scale is the semi-major axis of the ellipse given by the unit
        // circle transformed by `transform`. This is the greater of the two
        // singular values of the 2x2 `transform` matrix (ignoring translation).
        scale = 0.5 * (length(float2(mat.x + mat.w, mat.y - mat.z)) +
                       length(float2(mat.x - mat.w, mat.y + mat.z)));
    }

    // Drop zero length lines. This is an exact equality test because dropping
    // very short line segments may result in loss of watertightness.
    if (all(p0 == p1) && all(p0 == p2) && all(p0 == p3)) {
        return;
    }

    const float tol = 0.25;
    uint t0_u = 0u;
    float dt = 1.0;
    float2 last_p = p0;
    float2 last_q = p1 - p0;
    if (dot(last_q, last_q) < DERIV_THRESH_SQUARED) {
        last_q = eval_cubic_and_deriv(p0, p1, p2, p3, DERIV_EPS).deriv;
    }
    float last_t = 0.0;
    float2 lp0 = t_start;
    [loop]
    for (;;) {
        float t0 = (float)t0_u * dt;
        if (t0 == 1.0) {
            break;
        }
        float t1 = t0 + dt;
        float2 this_p0 = last_p;
        float2 this_q0 = last_q;
        PointDeriv this_pq1 = eval_cubic_and_deriv(p0, p1, p2, p3, t1);
        if (dot(this_pq1.deriv, this_pq1.deriv) < DERIV_THRESH_SQUARED) {
            PointDeriv new_pq1 = eval_cubic_and_deriv(p0, p1, p2, p3, t1 - DERIV_EPS);
            this_pq1.deriv = new_pq1.deriv;
            if (t1 < 1.0) {
                this_pq1.point_ = new_pq1.point_;
                t1 = t1 - DERIV_EPS;
            }
        }
        float actual_dt = t1 - last_t;
        CubicParams cubic_params =
            cubic_from_points_derivs(this_p0, this_pq1.point_, this_q0, this_pq1.deriv, actual_dt);
        if (cubic_params.err * scale <= tol || dt <= SUBDIV_LIMIT) {
            EulerParams euler_params = es_params_from_angles(cubic_params.th0, cubic_params.th1);
            EulerSeg es;
            es.p0 = this_p0;
            es.p1 = this_pq1.point_;
            es.params = euler_params;
            float k0 = es.params.k0 - 0.5 * es.params.k1;
            float k1 = es.params.k1;
            float normalized_offset = offset / cubic_params.chord_len;
            float dist_scaled = normalized_offset * es.params.ch;
            float scale_multiplier =
                sqrt(0.125 * scale * cubic_params.chord_len / (es.params.ch * tol));
            float a = 0.0;
            float b = 0.0;
            float integral = 0.0;
            float int0 = 0.0;
            float n_frac = 0.0;
            int robust = ESPC_ROBUST_NORMAL;
            if (abs(k1) < K1_THRESH) {
                float k = es.params.k0;
                n_frac = sqrt(abs(k * (k * dist_scaled + 1.0)));
                robust = ESPC_ROBUST_LOW_K1;
            } else if (abs(dist_scaled) < DIST_THRESH) {
                a = k1;
                b = k0;
                int0 = pow_1_5_signed(b);
                float int1 = pow_1_5_signed(a + b);
                integral = int1 - int0;
                n_frac = (2.0 / 3.0) * integral / a;
                robust = ESPC_ROBUST_LOW_DIST;
            } else {
                a = -2.0 * dist_scaled * k1;
                b = -1.0 - 2.0 * dist_scaled * k0;
                int0 = espc_int_approx(b);
                float int1 = espc_int_approx(a + b);
                integral = int1 - int0;
                float k_peak = k0 - k1 * b / a;
                float integrand_peak = sqrt(abs(k_peak * (k_peak * dist_scaled + 1.0)));
                n_frac = integral * integrand_peak / a;
            }
            // Bound number of subdivisions to a reasonable number when the
            // scale is huge. This may give slightly incorrect rendering but
            // avoids hangs.
            float n = clamp(ceil(n_frac * scale_multiplier), 1.0, 100.0);
            [loop]
            for (uint i = 0u; i < (uint)n; i++) {
                float2 lp1;
                if (i + 1u == (uint)n && t1 == 1.0) {
                    lp1 = t_end;
                } else {
                    float t = (float)(i + 1u) / n;
                    float s = t;
                    if (robust != ESPC_ROBUST_LOW_K1) {
                        float u = integral * t + int0;
                        float inv;
                        if (robust == ESPC_ROBUST_LOW_DIST) {
                            inv = pow(abs(u), 2.0 / 3.0) * sign(u);
                        } else {
                            inv = espc_int_inv_approx(u);
                        }
                        s = (inv - b) / a;
                    }
                    lp1 = es_seg_eval_with_offset(es, s, normalized_offset);
                }
                float2 l0 = (offset >= 0.0) ? lp0 : lp1;
                float2 l1 = (offset >= 0.0) ? lp1 : lp0;
                output_line_with_transform(path_ix, l0, l1, transform);
                lp0 = lp1;
            }
            last_p = this_pq1.point_;
            last_q = this_pq1.deriv;
            last_t = t1;
            t0_u += 1u;
            uint shift = firstbitlow(t0_u);
            t0_u >>= shift;
            dt *= (float)(1u << shift);
        } else {
            t0_u = t0_u * 2u;
            dt *= 0.5;
        }
    }
}

// Flattens the circular arc that subtends the angle begin-center-end. It is
// assumed that ||begin - center|| == ||end - center||. The direction is always
// a counter-clockwise (Y-down) rotation starting from `begin` towards `end`,
// centered at `center`, subtended by `angle` (assumed positive). A line
// segment is always drawn from the arc's terminus to `end`.
void flatten_arc(uint path_ix, float2 begin_p, float2 end_p, float2 center, float angle,
                 FlatTransform transform)
{
    float2 p0 = flat_transform_apply(transform, begin_p);
    float2 r = begin_p - center;

    const float MIN_THETA = 0.0001;
    const float tol = 0.25;
    float radius = max(tol, length(p0 - flat_transform_apply(transform, center)));
    float theta = max(MIN_THETA, 2.0 * acos(1.0 - tol / radius));

    // Always output at least one line so that we always draw the chord.
    uint n_lines = max(1u, (uint)ceil(angle / theta));

    float c = cos(theta);
    float s = sin(theta);

    uint line_ix;
    bump.InterlockedAdd(BUMP_LINES, n_lines, line_ix);
    [loop]
    for (uint i = 0u; i < n_lines - 1u; i += 1u) {
        // rot * r with rot = mat2x2(c, -s, s, c) (column-major)
        r = float2(c * r.x + s * r.y, -s * r.x + c * r.y);
        float2 p1 = flat_transform_apply(transform, center + r);
        write_line(line_ix + i, path_ix, p0, p1);
        p0 = p1;
    }
    float2 p1 = flat_transform_apply(transform, end_p);
    write_line(line_ix + n_lines - 1u, path_ix, p0, p1);
}

void draw_cap(uint path_ix, uint cap_style, float2 point_p, float2 cap0, float2 cap1,
              float2 offset_tangent, FlatTransform transform)
{
    if (cap_style == STYLE_FLAGS_CAP_ROUND) {
        flatten_arc(path_ix, cap0, cap1, point_p, 3.1415927, transform);
        return;
    }

    float2 start = cap0;
    float2 end_p = cap1;
    bool is_square = (cap_style == STYLE_FLAGS_CAP_SQUARE);
    uint line_ix;
    bump.InterlockedAdd(BUMP_LINES, is_square ? 3u : 1u, line_ix);
    if (is_square) {
        float2 v = offset_tangent;
        float2 p0 = start + v;
        float2 p1 = end_p + v;
        write_line_with_transform(line_ix + 1u, path_ix, start, p0, transform);
        write_line_with_transform(line_ix + 2u, path_ix, p1, end_p, transform);
        start = p0;
        end_p = p1;
    }
    write_line_with_transform(line_ix, path_ix, start, end_p, transform);
}

void draw_join(uint path_ix, uint style_flags, float2 p0, float2 tan_prev, float2 tan_next,
               float2 n_prev, float2 n_next, FlatTransform transform)
{
    float2 front0 = p0 + n_prev;
    float2 front1 = p0 + n_next;
    float2 back0 = p0 - n_next;
    float2 back1 = p0 - n_prev;

    float cr = tan_prev.x * tan_next.y - tan_prev.y * tan_next.x;
    float d = dot(tan_prev, tan_next);

    switch (style_flags & STYLE_FLAGS_JOIN_MASK) {
        case STYLE_FLAGS_JOIN_BEVEL: {
            output_two_lines_with_transform(path_ix, front0, front1, back0, back1, transform);
            break;
        }
        case STYLE_FLAGS_JOIN_MITER: {
            float hypot_v = length(float2(cr, d));
            float miter_limit = f16tof32(style_flags & STYLE_MITER_LIMIT_MASK);

            uint line_ix;
            // Given the two tangents `tan_prev` and `tan_next` arranged
            // tail-to-tail, the miter length ratio is `1 / |cos(theta/2)|`,
            // where `theta` is the angle between the tangents. `hypot` is
            // `|tan_prev| * |tan_next|` and `hypot + d` is
            // `2 * |tan_prev| * |tan_next| * cos^2(theta/2)`, so the following
            // tests whether `1/|cos(theta/2)| < miter_limit`. Also avoid the
            // miter computation when `cr` is very small; the intersection math
            // divides by `cr` and becomes numerically unstable for
            // near-collinear tangents.
            if (2.0 * hypot_v < (hypot_v + d) * miter_limit * miter_limit
                && abs(cr) > TANGENT_THRESH * TANGENT_THRESH)
            {
                bool is_backside = cr > 0.0;
                float2 fp_last = is_backside ? back1 : front0;
                float2 fp_this = is_backside ? back0 : front1;
                float2 p = is_backside ? back0 : front0;

                float2 v = fp_this - fp_last;
                float h = (tan_prev.x * v.y - tan_prev.y * v.x) / cr;
                float2 miter_pt = fp_this - tan_next * h;

                bump.InterlockedAdd(BUMP_LINES, 3u, line_ix);
                write_line_with_transform(line_ix, path_ix, p, miter_pt, transform);
                line_ix += 1u;

                if (is_backside) {
                    back0 = miter_pt;
                } else {
                    front0 = miter_pt;
                }
            } else {
                bump.InterlockedAdd(BUMP_LINES, 2u, line_ix);
            }
            write_line_with_transform(line_ix, path_ix, front0, front1, transform);
            write_line_with_transform(line_ix + 1u, path_ix, back0, back1, transform);
            break;
        }
        case STYLE_FLAGS_JOIN_ROUND: {
            float2 arc0;
            float2 arc1;
            float2 other0;
            float2 other1;
            if (cr > 0.0) {
                arc0 = back0;
                arc1 = back1;
                other0 = front0;
                other1 = front1;
            } else {
                arc0 = front0;
                arc1 = front1;
                other0 = back0;
                other1 = back1;
            }
            flatten_arc(path_ix, arc0, arc1, p0, abs(atan2(cr, d)), transform);
            output_line_with_transform(path_ix, other0, other1, transform);
            break;
        }
        default: {
            break;
        }
    }
}

float2 read_f32_point(uint ix)
{
    float x = asfloat(scene[pathdata_base_g + ix]);
    float y = asfloat(scene[pathdata_base_g + ix + 1u]);
    return float2(x, y);
}

float2 read_i16_point(uint ix)
{
    uint raw = scene[pathdata_base_g + ix];
    float x = (float)(int(raw << 16u) >> 16);
    float y = (float)(int(raw) >> 16);
    return float2(x, y);
}

int round_down(float x)
{
    return (int)floor(x);
}

int round_up(float x)
{
    return (int)ceil(x);
}

struct PathTagData
{
    uint tag_byte;
    TagMonoid monoid;
};

PathTagData compute_tag_monoid(uint ix)
{
    uint tag_word = scene[pathtag_base + (ix >> 2u)];
    uint shift = (ix & 3u) * 8u;
    TagMonoid tm = reduce_tag(tag_word & ((1u << shift) - 1u));
    tm = combine_tag_monoid(tag_monoids[ix >> 2u], tm);
    uint tag_byte = (tag_word >> shift) & 0xffu;
    // We no longer encode an initial transform and style so these are off by
    // one. Note: an alternative would be to adjust config.transform_base and
    // config.style_base.
    tm.trans_ix -= 1u;
    tm.style_ix -= STYLE_SIZE_IN_WORDS;
    PathTagData r;
    r.tag_byte = tag_byte;
    r.monoid = tm;
    return r;
}

CubicPoints read_path_segment(PathTagData tag, bool is_stroke)
{
    float2 p0 = 0.0.xx;
    float2 p1 = 0.0.xx;
    float2 p2 = 0.0.xx;
    float2 p3 = 0.0.xx;

    uint seg_type = tag.tag_byte & PATH_TAG_SEG_TYPE;
    uint pathseg_offset = tag.monoid.pathseg_offset;
    bool is_stroke_cap_marker = is_stroke && (tag.tag_byte & PATH_TAG_SUBPATH_END) != 0u;

    if ((tag.tag_byte & PATH_TAG_F32) != 0u) {
        p0 = read_f32_point(pathseg_offset);
        p1 = read_f32_point(pathseg_offset + 2u);
        if (seg_type >= PATH_TAG_QUADTO) {
            p2 = read_f32_point(pathseg_offset + 4u);
            if (seg_type == PATH_TAG_CUBICTO) {
                p3 = read_f32_point(pathseg_offset + 6u);
            }
        }
    } else {
        p0 = read_i16_point(pathseg_offset);
        p1 = read_i16_point(pathseg_offset + 1u);
        if (seg_type >= PATH_TAG_QUADTO) {
            p2 = read_i16_point(pathseg_offset + 2u);
            if (seg_type == PATH_TAG_CUBICTO) {
                p3 = read_i16_point(pathseg_offset + 3u);
            }
        }
    }

    if (is_stroke_cap_marker && seg_type == PATH_TAG_QUADTO) {
        // The stroke cap marker for an open path is encoded as a quadto where
        // p1 and p2 store the start control point of the subpath, forming the
        // start tangent. p0 is ignored. (A lineto would require adding a
        // moveto, terminating the subpath too early.)
        p0 = p1;
        p1 = p2;
        seg_type = PATH_TAG_LINETO;
    }

    // Degree-raise
    if (seg_type == PATH_TAG_LINETO) {
        p3 = p1;
        p2 = p3 + (1.0 / 3.0) * (p0 - p3);
        p1 = p0 + (1.0 / 3.0) * (p3 - p0);
    } else if (seg_type == PATH_TAG_QUADTO) {
        p3 = p2;
        p2 = p1 + (1.0 / 3.0) * (p2 - p1);
        p1 = p1 + (1.0 / 3.0) * (p0 - p1);
    }

    CubicPoints cp;
    cp.p0 = p0;
    cp.p1 = p1;
    cp.p2 = p2;
    cp.p3 = p3;
    return cp;
}

struct NeighboringSegment
{
    bool do_join;
    // Device-space start tangent vector
    float2 tangent;
};

NeighboringSegment read_neighboring_segment(uint ix)
{
    PathTagData tag = compute_tag_monoid(ix);
    CubicPoints pts = read_path_segment(tag, true);

    bool is_closed = (tag.tag_byte & PATH_TAG_SEG_TYPE) == PATH_TAG_LINETO;
    bool is_stroke_cap_marker = (tag.tag_byte & PATH_TAG_SUBPATH_END) != 0u;
    bool do_join = !is_stroke_cap_marker || is_closed;
    float2 tangent = pts.p3 - pts.p0;
    if (!is_stroke_cap_marker) {
        tangent = cubic_start_tangent(pts.p0, pts.p1, pts.p2, pts.p3);
    }
    NeighboringSegment r;
    r.do_join = do_join;
    r.tangent = tangent;
    return r;
}

[numthreads(256, 1, 1)]
void main(uint3 global_id : SV_DispatchThreadID)
{
    uint ix = global_id.x;
    pathdata_base_g = pathdata_base;
    flat_bbox = float4(1e31, 1e31, -1e31, -1e31);

    PathTagData tag = compute_tag_monoid(ix);
    uint path_ix = tag.monoid.path_ix;
    uint style_ix = tag.monoid.style_ix;
    uint trans_ix = tag.monoid.trans_ix;

    uint bbox_base = path_ix * 24u;
    uint style_flags = scene[style_base + style_ix];
    // The fill bit is always set to 0 for strokes which represents a non-zero fill.
    uint draw_flags = ((style_flags & STYLE_FLAGS_FILL) == 0u) ? 0u : DRAW_INFO_FLAGS_FILL_RULE_BIT;
    if ((tag.tag_byte & PATH_TAG_PATH) != 0u) {
        path_bboxes.Store(bbox_base + 16u, draw_flags);
        path_bboxes.Store(bbox_base + 20u, trans_ix);
    }
    // Decode path data
    uint seg_type = tag.tag_byte & PATH_TAG_SEG_TYPE;
    if (seg_type != 0u) {
        bool is_stroke = (style_flags & STYLE_FLAGS_STYLE) != 0u;
        FlatTransform transform = read_flat_transform(transform_base, trans_ix);
        CubicPoints pts = read_path_segment(tag, is_stroke);

        if (is_stroke) {
            float linewidth = asfloat(scene[style_base + style_ix + 1u]);
            float offset = 0.5 * linewidth;

            bool is_open = (tag.tag_byte & PATH_TAG_SEG_TYPE) != PATH_TAG_LINETO;
            bool is_stroke_cap_marker = (tag.tag_byte & PATH_TAG_SUBPATH_END) != 0u;
            if (is_stroke_cap_marker) {
                if (is_open) {
                    // Draw start cap
                    float2 tangent = pts.p3 - pts.p0;
                    float2 offset_tangent = offset * normalize(tangent);
                    float2 n = offset_tangent.yx * float2(-1.0, 1.0);
                    draw_cap(path_ix, (style_flags & STYLE_FLAGS_START_CAP_MASK) >> 2u,
                             pts.p0, pts.p0 - n, pts.p0 + n, -offset_tangent, transform);
                }
                // Don't draw anything if the path is closed.
            } else {
                // Read the neighboring segment.
                NeighboringSegment neighbor = read_neighboring_segment(ix + 1u);
                float2 tan_start = cubic_start_tangent(pts.p0, pts.p1, pts.p2, pts.p3);
                if (dot(tan_start, tan_start) < TANGENT_THRESH * TANGENT_THRESH) {
                    tan_start = float2(TANGENT_THRESH, 0.0);
                }
                float2 tan_prev = cubic_end_tangent(pts.p0, pts.p1, pts.p2, pts.p3);
                if (dot(tan_prev, tan_prev) < TANGENT_THRESH * TANGENT_THRESH) {
                    tan_prev = float2(TANGENT_THRESH, 0.0);
                }
                float2 tan_next = neighbor.tangent;
                if (dot(tan_next, tan_next) < TANGENT_THRESH * TANGENT_THRESH) {
                    tan_next = float2(TANGENT_THRESH, 0.0);
                }
                float2 n_start = offset * normalize(float2(-tan_start.y, tan_start.x));
                float2 offset_tangent = offset * normalize(tan_prev);
                float2 n_prev = offset_tangent.yx * float2(-1.0, 1.0);
                float2 n_next = offset * normalize(tan_next).yx * float2(-1.0, 1.0);

                // Render offset curves
                flatten_euler(pts, path_ix, transform, offset, pts.p0 + n_start, pts.p3 + n_prev);
                flatten_euler(pts, path_ix, transform, -offset, pts.p0 - n_start, pts.p3 - n_prev);

                if (neighbor.do_join) {
                    draw_join(path_ix, style_flags, pts.p3, tan_prev, tan_next,
                              n_prev, n_next, transform);
                } else {
                    // Draw end cap.
                    draw_cap(path_ix, (style_flags & STYLE_FLAGS_END_CAP_MASK),
                             pts.p3, pts.p3 + n_prev, pts.p3 - n_prev, offset_tangent, transform);
                }
            }
        } else {
            flatten_euler(pts, path_ix, transform, 0.0, pts.p0, pts.p3);
        }
        // Update bounding box using atomics only. Computing a monoid is a
        // potential future optimization.
        if (flat_bbox.z > flat_bbox.x || flat_bbox.w > flat_bbox.y) {
            int dummy;
            path_bboxes.InterlockedMin(bbox_base + 0u, round_down(flat_bbox.x), dummy);
            path_bboxes.InterlockedMin(bbox_base + 4u, round_down(flat_bbox.y), dummy);
            path_bboxes.InterlockedMax(bbox_base + 8u, round_up(flat_bbox.z), dummy);
            path_bboxes.InterlockedMax(bbox_base + 12u, round_up(flat_bbox.w), dummy);
        }
    }
}

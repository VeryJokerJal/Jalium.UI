// Vello GPU Pipeline V2 — flatten (Euler Spiral)
// Converts PathSegment (lines, quads, cubics) into LineSoup using Vello's
// Euler spiral-based adaptive flattening algorithm for near-optimal subdivision.
// Writes path bounding boxes atomically.
//
// Ported from: vello_shaders/shader/flatten.wgsl
//
// Dispatch: ceil(num_segments / 256), 1, 1

#include "vello_shared.hlsli"

cbuffer VelloConfig : register(b0)
{
    uint width_in_tiles;
    uint height_in_tiles;
    uint target_width;
    uint target_height;
    uint base_color;
    uint n_drawobj;
    uint n_path;
    uint n_clip;
    uint bin_data_start;
    uint lines_size;
    uint binning_size;
    uint tiles_size;
    uint seg_counts_size;
    uint segments_size;
    uint blend_size;
    uint ptcl_size;
    uint num_segments;
    uint pad0, pad1, pad2, pad3, pad4, pad5, pad6;
};

#define SEG_LINE  0u
#define SEG_QUAD  1u
#define SEG_CUBIC 2u

// Euler spiral constants
#define DERIV_THRESH          1e-6
#define DERIV_THRESH_SQUARED  1e-12
#define DERIV_EPS             1e-6
#define SUBDIV_LIMIT          (1.0 / 65536.0)
#define K1_THRESH             1e-3
#define DIST_THRESH           1e-3

// ESPC approximation constants
#define BREAK1       0.8
#define BREAK2       1.25
#define BREAK3       2.1
#define SIN_SCALE    1.0976991822760038
#define FRAC_PI_4    0.7853981633974483
#define CBRT_9_8     1.040041911525952

static const float QUAD_A1 = 0.6406;
static const float QUAD_B1 = -0.81;
static const float QUAD_C1 = 0.9148117935952064;
static const float QUAD_A2 = 0.5;
static const float QUAD_B2 = -0.156;
static const float QUAD_C2 = 0.16145779359520596;
static const float QUAD_W1 = 0.5 * QUAD_B1 / QUAD_A1;
static const float QUAD_V1 = 1.0 / QUAD_A1;
static const float QUAD_U1 = QUAD_W1 * QUAD_W1 - QUAD_C1 / QUAD_A1;
static const float QUAD_W2 = 0.5 * QUAD_B2 / QUAD_A2;
static const float QUAD_V2 = 1.0 / QUAD_A2;
static const float QUAD_U2 = QUAD_W2 * QUAD_W2 - QUAD_C2 / QUAD_A2;

#define ESPC_ROBUST_NORMAL   0
#define ESPC_ROBUST_LOW_K1   1
#define ESPC_ROBUST_LOW_DIST 2

StructuredBuffer<PathSegment> segments : register(t0);
RWStructuredBuffer<LineSoup> lines : register(u0);
RWByteAddressBuffer bump : register(u1);
RWByteAddressBuffer path_bboxes : register(u2);

struct PointDeriv { float2 pt; float2 deriv; };
struct CubicParams { float th0; float th1; float chord_len; float err; };
struct EulerParams { float th0; float k0; float k1; float ch; };

// ── Euler Spiral Integration (10-term Taylor) ──
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
    u -= (1.0/24.0)*t2_2 + (1.0/160.0)*t2_4;
    u += (1.0/1920.0)*t4_4 + (1.0/10752.0)*t4_6 + (1.0/55296.0)*t4_8;
    u -= (1.0/322560.0)*t6_6 + (1.0/1658880.0)*t6_8;
    u += (1.0/92897280.0)*t8_8;
    float v = (1.0/12.0)*t1_2;
    v -= (1.0/480.0)*t3_4 + (1.0/2688.0)*t3_6;
    v += (1.0/53760.0)*t5_6 + (1.0/276480.0)*t5_8;
    v -= (1.0/11612160.0)*t7_8;
    return float2(u, v);
}

// ── Euler Spiral Parameters from Angles ──
EulerParams es_params_from_angles(float th0, float th1)
{
    float k0 = th0 + th1;
    float dth = th1 - th0;
    float d2 = dth * dth;
    float k2 = k0 * k0;
    float a = 6.0;
    a -= d2 * (1.0/70.0);
    a -= (d2*d2) * (1.0/10780.0);
    a += (d2*d2*d2) * 2.769178184818219e-07;
    float b = -0.1 + d2*(1.0/4200.0) + d2*d2*1.6959677820260655e-05;
    float c = -1.0/1400.0 + d2*6.84915970574303e-05 - k2*7.936475029053326e-06;
    a += (b + c*k2) * k2;
    float k1 = dth * a;
    float ch = 1.0;
    ch -= d2*(1.0/40.0);
    ch += (d2*d2)*0.00034226190482569864;
    ch -= (d2*d2*d2)*1.9349474568904524e-06;
    float b2 = -1.0/24.0 + d2*0.0024702380951963226 - d2*d2*3.7297408997537985e-05;
    float c2 = 1.0/1920.0 - d2*4.87350869747975e-05 - k2*3.1001936068463107e-06;
    ch += (b2 + c2*k2) * k2;
    EulerParams ep;
    ep.th0 = th0; ep.k0 = k0; ep.k1 = k1; ep.ch = ch;
    return ep;
}

// ── Cubic parameters from endpoints and derivatives ──
CubicParams cubic_from_points_derivs(float2 p0, float2 p1, float2 q0, float2 q1, float dt)
{
    float2 chord = p1 - p0;
    float chord_sq = dot(chord, chord);
    float chord_len = sqrt(chord_sq);
    CubicParams cp;
    if (chord_sq < DERIV_THRESH_SQUARED) {
        float chord_err = sqrt((9.0/32.0) * (dot(q0,q0) + dot(q1,q1))) * dt;
        cp.th0 = 0; cp.th1 = 0; cp.chord_len = DERIV_THRESH; cp.err = chord_err;
        return cp;
    }
    float scale = dt / chord_sq;
    float2 h0 = float2(q0.x*chord.x + q0.y*chord.y, q0.y*chord.x - q0.x*chord.y);
    float t0 = atan2(h0.y, h0.x);
    float d0 = length(h0) * scale;
    float2 h1 = float2(q1.x*chord.x + q1.y*chord.y, q1.x*chord.y - q1.y*chord.x);
    float t1 = atan2(h1.y, h1.x);
    float d1 = length(h1) * scale;
    float ct0 = cos(t0), ct1 = cos(t1);
    float err = 2.0;
    if (ct0*ct1 >= 0.0) {
        float e0 = (2.0/3.0) / max(1.0+ct0, 1e-9);
        float e1 = (2.0/3.0) / max(1.0+ct1, 1e-9);
        float s0 = sin(t0), s1 = sin(t1);
        float s01 = ct0*s1 + ct1*s0;
        float amin = 0.15*(2.0*e0*s0 + 2.0*e1*s1 - e0*e1*s01);
        float a = 0.15*(2.0*d0*s0 + 2.0*d1*s1 - d0*d1*s01);
        float aerr = abs(a - amin);
        float symm = abs(t0 + t1);
        float asymm = abs(t0 - t1);
        float dist = length(float2(d0-e0, d1-e1));
        float symm2 = symm*symm;
        float ctr = (4.625e-6*symm*symm2 + 7.5e-3*asymm)*symm2;
        float halo = (5e-3*symm + 7e-2*asymm)*dist;
        err = ctr + 1.55*aerr + halo;
    }
    err *= chord_len;
    cp.th0 = t0; cp.th1 = t1; cp.chord_len = chord_len; cp.err = err;
    return cp;
}

// ── Helper functions ──
PointDeriv eval_cubic_and_deriv(float2 p0, float2 p1, float2 p2, float2 p3, float t)
{
    float m = 1.0 - t;
    float mm = m*m, mt = m*t, tt = t*t;
    PointDeriv pd;
    pd.pt = p0*(mm*m) + (p1*(3.0*mm) + p2*(3.0*mt) + p3*tt)*t;
    pd.deriv = (p1-p0)*mm + (p2-p1)*(2.0*mt) + (p3-p2)*tt;
    return pd;
}

float es_params_eval_th(EulerParams ep, float t)
{
    return (ep.k0 + 0.5*ep.k1*(t-1.0))*t - ep.th0;
}

float2 es_params_eval(EulerParams ep, float t)
{
    float thm = es_params_eval_th(ep, t*0.5);
    float2 uv = integ_euler_10((ep.k0 + ep.k1*(0.5*t - 0.5))*t, ep.k1*t*t);
    float s = (t / ep.ch) * sin(thm);
    float c = (t / ep.ch) * cos(thm);
    return float2(uv.x*c - uv.y*s, -uv.y*c - uv.x*s);
}

float2 es_seg_eval(float2 seg_p0, float2 seg_p1, EulerParams ep, float t)
{
    float2 chord = seg_p1 - seg_p0;
    float2 xy = es_params_eval(ep, t);
    return seg_p0 + float2(chord.x*xy.x - chord.y*xy.y, chord.x*xy.y + chord.y*xy.x);
}

float pow_1_5_signed(float x) { return x * sqrt(abs(x)); }

float espc_int_approx(float x)
{
    float y = abs(x);
    float a;
    if (y < BREAK1) {
        a = sin(SIN_SCALE * y) * (1.0 / SIN_SCALE);
    } else if (y < BREAK2) {
        a = (sqrt(8.0)/3.0) * pow_1_5_signed(y - 1.0) + FRAC_PI_4;
    } else {
        float qa = (y < BREAK3) ? QUAD_A1 : QUAD_A2;
        float qb = (y < BREAK3) ? QUAD_B1 : QUAD_B2;
        float qc = (y < BREAK3) ? QUAD_C1 : QUAD_C2;
        a = (qa*y + qb)*y + qc;
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
        float u = pow(abs(b), 2.0/3.0) * sign(b);
        a = u * CBRT_9_8 + 1.0;
    } else {
        float qu = (y < 2.038857793595206) ? QUAD_U1 : QUAD_U2;
        float qv = (y < 2.038857793595206) ? QUAD_V1 : QUAD_V2;
        float qw = (y < 2.038857793595206) ? QUAD_W1 : QUAD_W2;
        a = sqrt(qu + qv*y) - qw;
    }
    return a * sign(x);
}

// ── Atomic bbox update ──
void UpdatePathBbox(uint path_ix, float2 p)
{
    uint addr = path_ix * 24;
    int px = (int)floor(p.x), py = (int)floor(p.y);
    int px1 = (int)ceil(p.x), py1 = (int)ceil(p.y);
    int dummy;
    path_bboxes.InterlockedMin(addr+0, px, dummy);
    path_bboxes.InterlockedMin(addr+4, py, dummy);
    path_bboxes.InterlockedMax(addr+8, px1, dummy);
    path_bboxes.InterlockedMax(addr+12, py1, dummy);
}

// ── Output a line segment ──
void OutputLine(uint path_ix, float2 p0, float2 p1, uint line_ix)
{
    LineSoup ls;
    ls.path_ix = path_ix;
    ls.p0 = p0;
    ls.p1 = p1;
    ls.pad = 0;
    lines[line_ix] = ls;
    UpdatePathBbox(path_ix, p0);
    UpdatePathBbox(path_ix, p1);
}

// ── Main Euler Spiral Flatten ──
void flatten_euler_cubic(float2 p0, float2 p1, float2 p2, float2 p3, uint path_ix)
{
    // Drop zero-length segments
    if (all(p0 == p1) && all(p0 == p2) && all(p0 == p3)) return;

    float tol = 0.25;
    uint t0_u = 0u;
    float dt = 1.0;
    float2 last_p = p0;
    float2 last_q = p1 - p0;
    if (dot(last_q, last_q) < DERIV_THRESH_SQUARED) {
        last_q = eval_cubic_and_deriv(p0, p1, p2, p3, DERIV_EPS).deriv;
    }
    float last_t = 0.0;
    float2 lp0 = p0;

    [loop] for (uint safety = 0; safety < 2048; safety++) {
        float t0_f = (float)t0_u * dt;
        if (t0_f >= 1.0) break;

        float t1 = t0_f + dt;
        float2 this_p0 = last_p;
        float2 this_q0 = last_q;
        PointDeriv this_pq1 = eval_cubic_and_deriv(p0, p1, p2, p3, t1);

        if (dot(this_pq1.deriv, this_pq1.deriv) < DERIV_THRESH_SQUARED) {
            PointDeriv new_pq1 = eval_cubic_and_deriv(p0, p1, p2, p3, t1 - DERIV_EPS);
            this_pq1.deriv = new_pq1.deriv;
            if (t1 < 1.0) {
                this_pq1.pt = new_pq1.pt;
                t1 -= DERIV_EPS;
            }
        }

        float actual_dt = t1 - last_t;
        CubicParams cp = cubic_from_points_derivs(this_p0, this_pq1.pt, this_q0, this_pq1.deriv, actual_dt);

        if (cp.err <= tol || dt <= SUBDIV_LIMIT) {
            // Accept this segment — flatten using Euler spiral
            EulerParams ep = es_params_from_angles(cp.th0, cp.th1);
            float k0 = ep.k0 - 0.5*ep.k1;
            float k1 = ep.k1;
            float scale_mult = sqrt(0.125 * cp.chord_len / (ep.ch * tol));

            float n_frac;
            int robust = ESPC_ROBUST_NORMAL;
            float a_val = 0, b_val = 0, integral = 0, int0 = 0;

            if (abs(k1) < K1_THRESH) {
                float k = ep.k0;
                n_frac = sqrt(abs(k));
                robust = ESPC_ROBUST_LOW_K1;
            } else {
                a_val = k1;
                b_val = k0;
                int0 = pow_1_5_signed(b_val);
                float int1 = pow_1_5_signed(a_val + b_val);
                integral = int1 - int0;
                n_frac = (2.0/3.0) * integral / a_val;
                robust = ESPC_ROBUST_LOW_DIST;
            }

            float n = clamp(ceil(n_frac * scale_mult), 1.0, 100.0);
            uint n_lines = (uint)n;

            // Allocate output lines
            uint base_ix;
            bump.InterlockedAdd(BUMP_LINES, n_lines, base_ix);
            if (base_ix + n_lines > lines_size) {
                uint dummy;
                bump.InterlockedOr(BUMP_FAILED, STAGE_FLATTEN, dummy);
                return;
            }

            for (uint i = 0; i < n_lines; i++) {
                float2 lp1;
                if (i + 1u == n_lines && t1 >= 1.0 - 1e-6) {
                    lp1 = p3;
                } else {
                    float t = (float)(i + 1u) / n;
                    float s = t;
                    if (robust != ESPC_ROBUST_LOW_K1 && abs(a_val) > 1e-9) {
                        float u = integral * t + int0;
                        float inv;
                        if (robust == ESPC_ROBUST_LOW_DIST) {
                            inv = pow(abs(u), 2.0/3.0) * sign(u);
                        } else {
                            inv = espc_int_inv_approx(u);
                        }
                        s = (inv - b_val) / a_val;
                        s = clamp(s, 0.0, 1.0);
                    }
                    lp1 = es_seg_eval(this_p0, this_pq1.pt, ep, s);
                }
                OutputLine(path_ix, lp0, lp1, base_ix + i);
                lp0 = lp1;
            }

            last_p = this_pq1.pt;
            last_q = this_pq1.deriv;
            last_t = t1;
            t0_u += 1u;
            // Compact: merge adjacent equal-sized intervals
            uint shift = firstbitlow(t0_u);
            t0_u >>= shift;
            dt *= (float)(1u << shift);
        } else {
            // Subdivide
            t0_u *= 2u;
            dt *= 0.5;
        }
    }
}

[numthreads(256, 1, 1)]
void main(uint3 gid : SV_DispatchThreadID)
{
    uint segIdx = gid.x;
    if (segIdx >= num_segments) return;

    PathSegment seg = segments[segIdx];
    uint path_ix = seg.pathIndex;
    uint tag = seg.tag;

    if (tag == SEG_LINE) {
        // Simple line: output directly
        uint base_ix;
        bump.InterlockedAdd(BUMP_LINES, 1u, base_ix);
        if (base_ix < lines_size) {
            OutputLine(path_ix, seg.p0, seg.p1, base_ix);
        }
    } else if (tag == SEG_QUAD) {
        // Degree-raise quadratic to cubic
        float2 cp0 = seg.p0;
        float2 cp1 = seg.p0 + (2.0/3.0) * (seg.p1 - seg.p0);
        float2 cp2 = seg.p2 + (2.0/3.0) * (seg.p1 - seg.p2);
        float2 cp3 = seg.p2;
        flatten_euler_cubic(cp0, cp1, cp2, cp3, path_ix);
    } else if (tag == SEG_CUBIC) {
        flatten_euler_cubic(seg.p0, seg.p1, seg.p2, seg.p3, path_ix);
    }
}

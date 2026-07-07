#pragma once

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

// ============================================================================
// Forward Declarations (Opaque Types)
// ============================================================================

typedef struct JaliumContext JaliumContext;
typedef struct JaliumRenderTarget JaliumRenderTarget;
typedef struct JaliumBrush JaliumBrush;
typedef struct JaliumTextFormat JaliumTextFormat;
typedef struct JaliumGeometry JaliumGeometry;
typedef struct JaliumImage JaliumImage;
typedef struct JaliumInkLayerBitmap JaliumInkLayerBitmap;
typedef struct JaliumBrushShader   JaliumBrushShader;

// ============================================================================
// Enumerations
// ============================================================================

/// Result codes for Jalium API calls.
typedef enum JaliumResult {
    JALIUM_OK = 0,
    JALIUM_ERROR_INVALID_ARGUMENT = 1,
    JALIUM_ERROR_OUT_OF_MEMORY = 2,
    JALIUM_ERROR_NOT_SUPPORTED = 3,
    JALIUM_ERROR_DEVICE_LOST = 4,
    JALIUM_ERROR_BACKEND_NOT_AVAILABLE = 5,
    JALIUM_ERROR_INITIALIZATION_FAILED = 6,
    JALIUM_ERROR_RESOURCE_CREATION_FAILED = 7,
    JALIUM_ERROR_INVALID_STATE = 8,
    /// Present submission failed transiently (e.g. DXGI_ERROR_INVALID_CALL
    /// during a mode change) on a HEALTHY device: the frame's GPU work was
    /// submitted but never reached the screen. Distinct from
    /// JALIUM_ERROR_INVALID_STATE so the managed side can return the consumed
    /// present credit and simply repaint — recreating the render target for a
    /// dropped present would turn a one-frame hiccup into a visible stall.
    JALIUM_ERROR_PRESENT_FAILED = 9,
    /// A resize / swap-chain operation was refused because a command list is
    /// still open and references the resources it would free (cross-thread
    /// render in flight, or a frame left open). NOT a failure — the managed
    /// caller re-stashes and retries at the next safe point. Introduced to
    /// avoid the #921 OBJECT_DELETED_WHILE_STILL_IN_USE use-after-free on resize.
    JALIUM_ERROR_BUSY = 10,
    JALIUM_ERROR_UNKNOWN = 99
} JaliumResult;

/// Result codes for ink-layer brush dispatch
/// (jalium_ink_layer_bitmap_dispatch_brush and the
/// IRenderBackend::DispatchBrush virtual behind it).
///
/// **Backend-agnostic contract**: the managed caller picks its recovery
/// strategy from the code alone — it must never need to know which backend
/// produced it. Every backend classifies its internal failure reasons into
/// one of these categories before returning:
///
///   - `>= 0`  success (the stroke was baked into the layer, or there was
///             provably nothing to draw).
///   - INVALID_ARG   the call itself is malformed (null handle/pointer,
///                   too few points, bad constants size). Retrying the same
///                   call can never succeed; the caller skips the stroke.
///   - INVALID_STATE the layer's backing resources are absent (construction
///                   or resize failed earlier, or the handle was torn down).
///                   Not retryable as-is; the caller's normal layer
///                   (re)construction path is the recovery.
///   - TRANSIENT     a momentary resource failure inside the dispatch
///                   (upload-buffer allocation, command-buffer begin/submit
///                   hiccup). The layer and shader handles are still healthy:
///                   retrying the SAME handles next frame is expected to
///                   succeed. Callers must NOT tear down / rebuild the ink
///                   resource chain — that would amplify a one-frame hiccup
///                   into a rebuild storm.
///   - STALE_CONTEXT the device generation behind the handles is gone or
///                   inconsistent (device-lost latch, or bitmap and shader
///                   baked on different generations). Retrying the same
///                   handles can NEVER succeed; the caller must rebuild the
///                   whole ink resource chain (layer bitmaps + every shader
///                   handle) so everything re-pairs on the current generation.
///
/// The TRANSIENT / STALE_CONTEXT values are deliberately placed far away
/// from the legacy per-backend codes (-1..-7) so a stale comparison against
/// a historical raw code can never misclassify one of these categories.
typedef enum JaliumInkDispatchResult {
    JALIUM_INK_DISPATCH_OK                  = 0,
    JALIUM_INK_DISPATCH_ERROR_INVALID_ARG   = -1,
    JALIUM_INK_DISPATCH_ERROR_INVALID_STATE = -2,
    JALIUM_INK_DISPATCH_ERROR_TRANSIENT     = -100,
    JALIUM_INK_DISPATCH_ERROR_STALE_CONTEXT = -101
} JaliumInkDispatchResult;

/// Rendering backend types.
typedef enum JaliumBackend {
    JALIUM_BACKEND_AUTO = 0,        ///< Automatically select the best available backend
    JALIUM_BACKEND_D3D12 = 1,       ///< Direct3D 12
    JALIUM_BACKEND_VULKAN = 3,      ///< Vulkan
    JALIUM_BACKEND_METAL = 5,       ///< Metal (macOS/iOS)
    JALIUM_BACKEND_SOFTWARE = 7     ///< Software rasterizer
} JaliumBackend;

/// Rendering engine types.
/// The rendering engine determines how 2D vector graphics are rasterized
/// on the GPU.  This is orthogonal to the GPU backend (D3D12/Vulkan/Metal).
typedef enum JaliumRenderingEngine {
    JALIUM_ENGINE_AUTO    = 0,   ///< Automatic: defaults to Impeller on all platforms
    JALIUM_ENGINE_VELLO   = 1,   ///< Vello: GPU compute pipeline (prefix-sum tiling)
    JALIUM_ENGINE_IMPELLER = 2   ///< Impeller: tessellation-based pipeline (Flutter)
} JaliumRenderingEngine;

/// GPU adapter preference for multi-GPU systems.
typedef enum JaliumGpuPreference {
    JALIUM_GPU_PREFERENCE_AUTO = 0,             ///< Let the OS/driver decide (default)
    JALIUM_GPU_PREFERENCE_HIGH_PERFORMANCE = 1, ///< Prefer discrete/high-performance GPU
    JALIUM_GPU_PREFERENCE_MINIMUM_POWER = 2,    ///< Prefer integrated/low-power GPU
} JaliumGpuPreference;

/// GPU adapter type classification.
typedef enum JaliumGpuAdapterType {
    JALIUM_GPU_ADAPTER_TYPE_UNKNOWN = 0,     ///< Unknown or unclassified adapter
    JALIUM_GPU_ADAPTER_TYPE_DISCRETE = 1,    ///< Discrete GPU (dedicated graphics card)
    JALIUM_GPU_ADAPTER_TYPE_INTEGRATED = 2,  ///< Integrated GPU (on-CPU graphics)
    JALIUM_GPU_ADAPTER_TYPE_SOFTWARE = 3,    ///< Software/WARP adapter
} JaliumGpuAdapterType;

/// Host platform identifier for native window/surface handles.
typedef enum JaliumPlatform {
    JALIUM_PLATFORM_UNKNOWN = 0,
    JALIUM_PLATFORM_WINDOWS = 1,
    JALIUM_PLATFORM_LINUX_X11 = 2,
    JALIUM_PLATFORM_ANDROID = 3,
    JALIUM_PLATFORM_MACOS = 4
} JaliumPlatform;

/// Surface descriptor kind used when creating render targets in a platform-neutral way.
typedef enum JaliumSurfaceKind {
    JALIUM_SURFACE_KIND_NATIVE_WINDOW = 1,
    JALIUM_SURFACE_KIND_COMPOSITION_TARGET = 2
} JaliumSurfaceKind;

/// Brush types.
typedef enum JaliumBrushType {
    JALIUM_BRUSH_SOLID = 0,
    JALIUM_BRUSH_LINEAR_GRADIENT = 1,
    JALIUM_BRUSH_RADIAL_GRADIENT = 2,
    JALIUM_BRUSH_IMAGE = 3
} JaliumBrushType;

/// Text alignment options.
typedef enum JaliumTextAlignment {
    JALIUM_TEXT_ALIGN_LEADING = 0,    ///< Left for LTR, Right for RTL
    JALIUM_TEXT_ALIGN_TRAILING = 1,   ///< Right for LTR, Left for RTL
    JALIUM_TEXT_ALIGN_CENTER = 2,
    JALIUM_TEXT_ALIGN_JUSTIFIED = 3
} JaliumTextAlignment;

/// Paragraph alignment options.
typedef enum JaliumParagraphAlignment {
    JALIUM_PARAGRAPH_ALIGN_NEAR = 0,    ///< Top
    JALIUM_PARAGRAPH_ALIGN_FAR = 1,     ///< Bottom
    JALIUM_PARAGRAPH_ALIGN_CENTER = 2
} JaliumParagraphAlignment;

/// Font weight values.
typedef enum JaliumFontWeight {
    JALIUM_FONT_WEIGHT_THIN = 100,
    JALIUM_FONT_WEIGHT_EXTRA_LIGHT = 200,
    JALIUM_FONT_WEIGHT_LIGHT = 300,
    JALIUM_FONT_WEIGHT_NORMAL = 400,
    JALIUM_FONT_WEIGHT_MEDIUM = 500,
    JALIUM_FONT_WEIGHT_SEMI_BOLD = 600,
    JALIUM_FONT_WEIGHT_BOLD = 700,
    JALIUM_FONT_WEIGHT_EXTRA_BOLD = 800,
    JALIUM_FONT_WEIGHT_BLACK = 900
} JaliumFontWeight;

/// Font style values.
typedef enum JaliumFontStyle {
    JALIUM_FONT_STYLE_NORMAL = 0,
    JALIUM_FONT_STYLE_ITALIC = 1,
    JALIUM_FONT_STYLE_OBLIQUE = 2
} JaliumFontStyle;

/// Text trimming options.
typedef enum JaliumTextTrimming {
    JALIUM_TEXT_TRIMMING_NONE = 0,             ///< No trimming
    JALIUM_TEXT_TRIMMING_CHARACTER_ELLIPSIS = 1, ///< Trim at character boundary with ellipsis
    JALIUM_TEXT_TRIMMING_WORD_ELLIPSIS = 2     ///< Trim at word boundary with ellipsis
} JaliumTextTrimming;

/// Stroke line join styles.
typedef enum JaliumLineJoin {
    JALIUM_LINE_JOIN_MITER = 0,     ///< Sharp corner (default)
    JALIUM_LINE_JOIN_BEVEL = 1,     ///< Flat corner
    JALIUM_LINE_JOIN_ROUND = 2      ///< Rounded corner
} JaliumLineJoin;

/// Word wrapping options.
typedef enum JaliumWordWrapping {
    JALIUM_WORD_WRAP = 0,            ///< Wrap at word boundaries
    JALIUM_WORD_WRAP_NONE = 1,       ///< No wrapping (single line)
    JALIUM_WORD_WRAP_CHARACTER = 2,  ///< Wrap at character boundaries
    JALIUM_WORD_WRAP_EMERGENCY = 3   ///< Wrap at word boundaries, break words if needed
} JaliumWordWrapping;

/// Text hit-test result.
typedef struct JaliumTextHitTestResult {
    uint32_t textPosition;   ///< Character index at the hit point
    int32_t  isTrailingHit;  ///< Non-zero if hit is on the trailing edge of the character
    int32_t  isInside;       ///< Non-zero if the point is inside the text layout
    float    caretX;         ///< X position of the caret at this text position
    float    caretY;         ///< Y position of the caret
    float    caretHeight;    ///< Height of the caret
} JaliumTextHitTestResult;

// ============================================================================
// Structures
// ============================================================================

/// Represents a color with RGBA components in sRGB gamma space.
/// All backends expect colors in sRGB gamma (non-linear).  GPU backends
/// (D3D12/Vulkan) use sRGB render-target views to convert to linear for
/// blending and back to sRGB on write.  The software backend performs the
/// sRGB↔linear conversion explicitly when blending or interpolating gradients.
typedef struct JaliumColor {
    float r;    ///< Red component (0.0 - 1.0, sRGB gamma)
    float g;    ///< Green component (0.0 - 1.0, sRGB gamma)
    float b;    ///< Blue component (0.0 - 1.0, sRGB gamma)
    float a;    ///< Alpha component (0.0 - 1.0, linear)
} JaliumColor;

/// Represents a point in 2D space.
typedef struct JaliumPoint {
    float x;
    float y;
} JaliumPoint;

/// Represents a size in 2D space.
typedef struct JaliumSize {
    float width;
    float height;
} JaliumSize;

/// Represents a rectangle in 2D space.
typedef struct JaliumRect {
    float x;
    float y;
    float width;
    float height;
} JaliumRect;

/// Represents a 3x2 transformation matrix (column-major).
typedef struct JaliumMatrix {
    float m11, m12;    ///< First column
    float m21, m22;    ///< Second column
    float m31, m32;    ///< Third column (translation)
} JaliumMatrix;

/// Represents a gradient stop.  Color components are in sRGB gamma space.
typedef struct JaliumGradientStop {
    float position;    ///< Position along the gradient (0.0 - 1.0)
    float r;           ///< Red component (sRGB gamma)
    float g;           ///< Green component (sRGB gamma)
    float b;           ///< Blue component (sRGB gamma)
    float a;           ///< Alpha component (linear)
} JaliumGradientStop;

/// Represents text metrics for layout measurement.
typedef struct JaliumTextMetrics {
    float width;           ///< The width of the text layout area
    float height;          ///< The height of the text layout area
    float lineHeight;      ///< The natural line height (ascent + descent + lineGap)
    float baseline;        ///< The baseline offset from the top
    float ascent;          ///< The ascent of the font (above baseline)
    float descent;         ///< The descent of the font (below baseline)
    float lineGap;         ///< The recommended line gap
    uint32_t lineCount;    ///< The number of lines in the layout
} JaliumTextMetrics;

/// Information about the selected GPU adapter.
typedef struct JaliumAdapterInfo {
    wchar_t name[128];              ///< Adapter description string
    int32_t adapterType;            ///< JaliumGpuAdapterType value
    uint64_t dedicatedVideoMemory;  ///< Dedicated video memory in bytes
    uint64_t sharedSystemMemory;    ///< Shared system memory in bytes
    uint32_t vendorId;              ///< PCI vendor ID
    uint32_t deviceId;              ///< PCI device ID
} JaliumAdapterInfo;

/// Per-render-target swap chain / present configuration.<br/>
/// Surfaces 后端实际采用的 present 路径（FLIP vs BLT、是否走 tearing / frame-latency
/// waitable / composition）给宿主，让"为什么这帧慢"在 UI 上可见。<br/>
/// 通过 jalium_render_target_get_present_info 查询。
///
/// **Cross-backend semantics**:
///   - `swapEffect` uses the native enum of whichever backend filled the
///     struct (D3D12 → DXGI_SWAP_EFFECT, Vulkan → VkPresentModeKHR).
///     Integer values do NOT share a namespace — callers must branch on
///     the active backend type before mapping to a display string.
///   - `waitableEnabled` / `maxFrameLatency` are D3D12-only concepts (the
///     swap-chain frame-latency waitable HANDLE + SetMaximumFrameLatency);
///     Vulkan WSI has no equivalent and always reports 0 for both.
///   - `tearingEnabled` maps to D3D12 ALLOW_TEARING flag OR Vulkan
///     VK_PRESENT_MODE_IMMEDIATE_KHR — both describe "vsync may be skipped".
typedef struct JaliumPresentInfo {
    /// Raw enum value with backend-specific meaning:
    ///   - D3D12 → DXGI_SWAP_EFFECT: 0=Discard, 1=Sequential, 3=FlipSequential, 4=FlipDiscard
    ///   - Vulkan → VkPresentModeKHR: 0=IMMEDIATE, 1=MAILBOX, 2=FIFO, 3=FIFO_RELAXED
    int32_t swapEffect;
    /// 后台缓冲数量（通常 2 或 3）。Cross-backend consistent.
    int32_t bufferCount;
    /// 1 = "tearing path" 启用. D3D12: DXGI_SWAP_CHAIN_FLAG_ALLOW_TEARING.
    /// Vulkan: present mode == VK_PRESENT_MODE_IMMEDIATE_KHR.
    int32_t tearingEnabled;
    /// 1 = DXGI_SWAP_CHAIN_FLAG_FRAME_LATENCY_WAITABLE_OBJECT 启用 (D3D12 only),
    /// 配合 SetMaximumFrameLatency(1) 是当前框架的低延迟主路径。
    /// Always 0 on Vulkan — WSI has no equivalent OS HANDLE.
    int32_t waitableEnabled;
    /// 当前 SetMaximumFrameLatency 值；waitableEnabled == 0 时无意义。
    /// Always 0 on Vulkan.
    int32_t maxFrameLatency;
    /// 1 = composition path (D3D12 DirectComposition / Vulkan premultiplied
    /// composite alpha), 0 = 普通 HWND / opaque surface.
    int32_t composition;
} JaliumPresentInfo;

/// Per-frame GPU resource snapshot used by DevTools Perf tab.
/// All fields are point-in-time values at call site — not accumulators.
/// Missing / not-applicable categories should be zero-filled by the backend.
///
/// Frame-pacing fields (frameGpuWaitNs / swapBufferCount / lastFrameGpuWorkNs)
/// answer "why is BeginDraw slow?" by separating UI-thread fence-wait time
/// from actual GPU work time. On a slow GPU (e.g. iGPU under heavy effect
/// load) BeginDraw can appear to take 45+ ms; the diagnostics tell whether
/// that is a 50 ms fence-wait timeout retry loop versus a single long
/// recording pass. Required reading: Frame pacing block in DevTools PerfTab.
typedef struct JaliumGpuStats {
    int32_t glyphSlotsUsed;    ///< Glyph cache entries currently resident
    int32_t glyphSlotsTotal;   ///< Estimated slot capacity at current avg glyph size
    int64_t glyphBytes;        ///< Bytes of the glyph atlas that are packed
    int32_t pathEntries;       ///< Path / tessellation cache entry count
    int64_t pathBytes;         ///< Path / tessellation cache bytes in flight
    int32_t textureCount;      ///< Backend-owned GPU textures (atlas + swap + effects)
    int64_t textureBytes;      ///< Combined texture bytes

    // ── Frame-pacing diagnostics ─────────────────────────────────────────
    // Backends that do not yet wire these up leave them as zero (the struct
    // is zero-initialised by D3D12RenderTarget::QueryGpuStats before fill).
    int64_t frameGpuWaitNs;          ///< UI-thread wall time spent inside fence waits across BeginFrame attempts for the most recently completed frame (sum across retried attempts). On D3D12 this is close to 0 when the swap-chain waitable handle is doing the actual blocking instead.
    int32_t swapBufferCount;         ///< Back-buffer count of the swap chain (2 / 3).
    int32_t reserved0;               ///< Padding for 8-byte alignment of the next int64_t.
    int64_t lastFramePresentToReadyNs; ///< Wall time between previous EndFrame's queue Signal and this frame's first observed fence completion. **Not** pure GPU work — includes DWM composition, DXGI runtime queue latency, swap-chain back-pressure. The hardware-timestamp GPU breakdown (see JaliumGpuTimingStats.totalGpuNs) is the canonical "what did the GPU actually do" number.
    int64_t frameWaitableWaitNs;     ///< UI-thread wall time spent waiting on the swap-chain frame-latency waitable across BeginFrame attempts. Captures the OS / DWM portion of present-to-ready latency that fence waits miss.
    int64_t presentBlockNs;          ///< Wall time the most recent EndDraw spent blocked inside the swap-chain Present call itself. Under a slow compositor (occlusion throttling, remote/virtual displays) Present(1) stalls until the DWM retires the previous frame — this field separates that stall from genuine CPU encode work in the EndDraw timing row.
} JaliumGpuStats;

/// Per-frame GPU work breakdown by draw-call category, sourced from
/// hardware timestamp queries on the graphics queue. Reports the previous
/// frame's numbers (read back after fence sync). All values in nanoseconds.
///
/// timingValid: 1 when readback succeeded for the previous frame, 0 when
/// the backend hasn't yet collected a frame's worth of data (first frame
/// after startup or after a reset). Categories sum to roughly totalGpuNs
/// minus driver overhead; "otherNs" captures everything outside the
/// classified categories (barriers, MSAA resolves, idle gaps).
typedef struct JaliumGpuTimingStats {
    int64_t totalGpuNs;          ///< Wall time between first and last frame-scoped timestamp.
    int64_t sdfRectNs;           ///< SDF rect / ellipse / line / punch-rect batches.
    int64_t textNs;              ///< Text batches.
    int64_t bitmapNs;            ///< Bitmap + snapshot-blit batches.
    int64_t pathNs;              ///< Stencil-then-cover paths + triangle fill batches.
    int64_t backdropNs;          ///< DrawBackdropFilter (CaptureSnapshot + blur passes + composite).
    int64_t liquidGlassNs;       ///< DrawLiquidGlass effect (SDF refraction + highlight + shadow).
    int64_t otherNs;             ///< Frame boundary / barriers / unclassified GPU work.
    int32_t batchCount;          ///< Total draw batches recorded in the previous frame.
    int32_t timingValid;         ///< 1 = data is valid, 0 = backend has not produced any frame yet.
} JaliumGpuTimingStats;

/// Platform-neutral native surface descriptor.
/// handle0/1/2 are backend/platform-specific payload slots (for example HWND,
/// X11 Display + Window, or ANativeWindow pointer).
typedef struct JaliumSurfaceDescriptor {
    int32_t platform;
    int32_t kind;
    intptr_t handle0;
    intptr_t handle1;
    intptr_t handle2;
} JaliumSurfaceDescriptor;

// ============================================================================
// Function Pointers
// ============================================================================

/// Factory function type for creating rendering backends.
struct IRenderBackend;
typedef struct IRenderBackend* (*JaliumBackendFactory)(void);

/// Optional callback used to determine whether a registered backend is
/// currently runnable on the host platform/runtime.
typedef int32_t (*JaliumBackendAvailabilityCallback)(void);

#ifdef __cplusplus
}
#endif

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
    JALIUM_ERROR_UNKNOWN = 99
} JaliumResult;

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

/// Per-frame GPU resource snapshot used by DevTools Perf tab.
/// All fields are point-in-time values at call site — not accumulators.
/// Missing / not-applicable categories should be zero-filled by the backend.
typedef struct JaliumGpuStats {
    int32_t glyphSlotsUsed;    ///< Glyph cache entries currently resident
    int32_t glyphSlotsTotal;   ///< Estimated slot capacity at current avg glyph size
    int64_t glyphBytes;        ///< Bytes of the glyph atlas that are packed
    int32_t pathEntries;       ///< Path / tessellation cache entry count
    int64_t pathBytes;         ///< Path / tessellation cache bytes in flight
    int32_t textureCount;      ///< Backend-owned GPU textures (atlas + swap + effects)
    int64_t textureBytes;      ///< Combined texture bytes
} JaliumGpuStats;

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

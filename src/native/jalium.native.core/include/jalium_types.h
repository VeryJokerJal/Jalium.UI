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
    JALIUM_BACKEND_SOFTWARE = 99    ///< Software rasterizer
} JaliumBackend;

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

// ============================================================================
// Structures
// ============================================================================

/// Represents a color with RGBA components.
typedef struct JaliumColor {
    float r;    ///< Red component (0.0 - 1.0)
    float g;    ///< Green component (0.0 - 1.0)
    float b;    ///< Blue component (0.0 - 1.0)
    float a;    ///< Alpha component (0.0 - 1.0)
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

/// Represents a gradient stop.
typedef struct JaliumGradientStop {
    float position;    ///< Position along the gradient (0.0 - 1.0)
    float r;           ///< Red component
    float g;           ///< Green component
    float b;           ///< Blue component
    float a;           ///< Alpha component
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

#pragma once

#ifdef _WIN32

#include <cstdint>
#include <string>

// Forward-declare GDI handle types so callers don't have to drag <windows.h>
// into every header that touches the pool. Definitions match the typedefs
// in <windef.h>.
struct HDC__;
struct HBITMAP__;
struct HFONT__;
typedef struct HDC__*    HDC;
typedef struct HBITMAP__* HBITMAP;
typedef struct HFONT__*  HFONT;

namespace jalium {

// Thread-local pool of GDI resources used by the Vulkan backend's Windows
// text rasterization path.
//
// Why pool: cache miss in RenderText used to do
//     CreateDIBSection + CreateCompatibleDC + CreateFontW + ... + DeleteObject + DeleteDC
// every single time, ~2-5 ms per call. Gallery's first frame triggers
// hundreds of misses (cache empty), so the GDI handle dance alone added
// up to a full second of wall time.
//
// Lifetime model: resources are thread_local and "perma-leased" — once
// AcquireFont/AcquireMemoryDc/AcquireDib hands a handle out, the pool keeps
// owning it. Callers do NOT call DeleteObject / DeleteDC. The DIB grows
// monotonically; HFONTs accumulate (typical UI uses ≤ 30 distinct
// (family, size, weight, italic) combinations, so unbounded is fine). The
// HDC is a single CompatibleDC reused forever. All resources are released
// at thread exit by the C runtime (not explicitly tracked — by design,
// because the render thread has the same lifetime as the process).
//
// Concurrency: there is no synchronization. Vulkan rendering is
// single-threaded per surface, and storage is thread_local, so each render
// thread has its own independent pool.
class Win32GdiPool {
public:
    // Acquire (or create) a cached HFONT keyed by (fontFamilyId, height,
    // weight, italic, quality). fontFamily is only consulted on miss.
    // Returns null if CreateFontW fails — the caller should bail in that case.
    //
    // `quality` is a LOGFONT.lfQuality value (NONANTIALIASED_QUALITY = 3,
    // ANTIALIASED_QUALITY = 4, CLEARTYPE_QUALITY = 5) resolved upstream from
    // the source TextFormat's per-element TextRenderingMode. The default
    // 5 (CLEARTYPE_QUALITY) preserves the historical Vulkan-backend behaviour
    // for Auto callers. The field is in the cache key so two elements that
    // ask for the same family/size at different qualities coexist instead of
    // returning the wrong HFONT.
    static HFONT AcquireFont(uint32_t fontFamilyId,
                             const wchar_t* fontFamily,
                             int height,
                             int weight,
                             bool italic,
                             uint8_t quality = 5);

    // Resolves a (possibly CSS-style comma-separated) font-family string to a
    // single GDI face name CreateFontW can realize. The D3D12 backend hands the
    // family straight to DirectWrite, which parses fallback lists like
    // "Cascadia Code, Cascadia Mono, Consolas, monospace" and applies system
    // fallback. GDI's lfFaceName names ONE face (truncated at LF_FACESIZE) and
    // otherwise drops through its mapper to a serif/monospace default — the root
    // cause of the Vulkan backend rendering text in the wrong font. This mirrors
    // DirectWrite closely enough for installed faces: split on commas, map the
    // CSS generic keywords (serif / sans-serif / monospace / system-ui / …), and
    // return the FIRST candidate actually installed (probed via
    // EnumFontFamiliesExW), or "Segoe UI" if none match — matching DirectWrite's
    // sans-serif fallback rather than GDI's serif one. Results are cached per
    // input string (thread-local). Both the render path (AcquireFont) and the
    // measurement path (vulkan_resources.cpp CreateGdiFont) call this so the two
    // never disagree on the realized face.
    static std::wstring ResolveGdiFaceName(const wchar_t* fontFamily);

    // Returns the thread-local memory DC. Creates it on first use.
    static HDC AcquireMemoryDc();

    // A leased view of the thread-local DIB section. The underlying buffer
    // stays alive after the lease — the lease just tells the caller the
    // current buffer extents (capacityW/H) which may be larger than the
    // requested (w, h). Callers must memset() only the (w, h) sub-region
    // to zero, then have GDI draw into RECT{0,0,w,h}, and read back using
    // capacityW as the row stride.
    //
    // Returns a zeroed lease (dib == nullptr) on allocation failure.
    struct DibLease {
        HBITMAP dib = nullptr;
        void*   pixels = nullptr;
        int     capacityW = 0;
        int     capacityH = 0;
    };
    static DibLease AcquireDib(int width, int height);
};

} // namespace jalium

#endif // _WIN32

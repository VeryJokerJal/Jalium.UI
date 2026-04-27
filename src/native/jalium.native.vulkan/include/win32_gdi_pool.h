#pragma once

#ifdef _WIN32

#include <cstdint>

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
    // weight, italic). fontFamily is only consulted on miss. Returns null
    // if CreateFontW fails — the caller should bail in that case.
    static HFONT AcquireFont(uint32_t fontFamilyId,
                             const wchar_t* fontFamily,
                             int height,
                             int weight,
                             bool italic);

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

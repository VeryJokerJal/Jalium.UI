#ifdef _WIN32

#include "win32_gdi_pool.h"

#include <windows.h>

#include <algorithm>
#include <unordered_map>

namespace jalium {

namespace {

// Thread-local pool storage. Each render thread carries its own copy.
//
// Note: the destructors of these thread_local objects (the unordered_map's
// HFONT entries and the bare HBITMAP/HDC handles) do NOT release GDI
// objects. That's deliberate: Win32 cleans up all GDI handles owned by a
// thread when the thread exits, and the render thread's lifetime is the
// process lifetime in this codebase. Tracking explicit cleanup would add
// complexity for zero practical benefit.
thread_local std::unordered_map<uint64_t, HFONT> g_fontPool;
thread_local HDC      g_memoryDc        = nullptr;
thread_local HBITMAP  g_dib             = nullptr;
thread_local void*    g_dibPixels       = nullptr;
thread_local int      g_dibCapW         = 0;
thread_local int      g_dibCapH         = 0;
thread_local HGDIOBJ  g_oldDibInDc      = nullptr; // SelectObject's previous-bitmap return — restore-then-delete.

inline uint64_t MakeFontKey(uint32_t fontFamilyId, int height, int weight, bool italic) noexcept
{
    // 16 bits family id, 16 bits height (signed → store as uint16),
    // 16 bits weight (LOGFONT range 100..900), 1 bit italic. Plenty of room.
    return  (static_cast<uint64_t>(fontFamilyId) & 0xFFFFu)
         | ((static_cast<uint64_t>(static_cast<uint16_t>(height))   & 0xFFFFu) << 16)
         | ((static_cast<uint64_t>(static_cast<uint16_t>(weight))   & 0xFFFFu) << 32)
         |  (italic ? (1ULL << 48) : 0ULL);
}

} // namespace

HFONT Win32GdiPool::AcquireFont(uint32_t fontFamilyId,
                                const wchar_t* fontFamily,
                                int height,
                                int weight,
                                bool italic)
{
    const uint64_t key = MakeFontKey(fontFamilyId, height, weight, italic);
    auto it = g_fontPool.find(key);
    if (it != g_fontPool.end()) {
        return it->second;
    }

    HFONT font = CreateFontW(
        height,
        0, 0, 0,
        weight,
        italic ? TRUE : FALSE,
        FALSE,
        FALSE,
        DEFAULT_CHARSET,
        OUT_DEFAULT_PRECIS,
        CLIP_DEFAULT_PRECIS,
        CLEARTYPE_QUALITY,
        DEFAULT_PITCH | FF_DONTCARE,
        fontFamily);
    if (!font) {
        return nullptr;
    }
    g_fontPool.emplace(key, font);
    return font;
}

HDC Win32GdiPool::AcquireMemoryDc()
{
    if (g_memoryDc) return g_memoryDc;

    HDC screenDc = GetDC(nullptr);
    if (!screenDc) return nullptr;

    g_memoryDc = CreateCompatibleDC(screenDc);
    ReleaseDC(nullptr, screenDc);
    return g_memoryDc;
}

Win32GdiPool::DibLease Win32GdiPool::AcquireDib(int width, int height)
{
    DibLease lease;
    if (width <= 0 || height <= 0) return lease;

    // Fast path: the existing DIB is big enough.
    if (g_dib && width <= g_dibCapW && height <= g_dibCapH) {
        lease.dib       = g_dib;
        lease.pixels    = g_dibPixels;
        lease.capacityW = g_dibCapW;
        lease.capacityH = g_dibCapH;
        return lease;
    }

    // Grow path. Pad to 1.25x of the requested size (and at least the current
    // capacity) so we don't repeatedly recreate when text wraps a few pixels
    // wider on each frame.
    int newW = std::max(width,  g_dibCapW);
    int newH = std::max(height, g_dibCapH);
    newW = std::max(width,  newW + newW / 4 + 1);
    newH = std::max(height, newH + newH / 4 + 1);

    BITMAPINFO bi {};
    bi.bmiHeader.biSize        = sizeof(BITMAPINFOHEADER);
    bi.bmiHeader.biWidth       = newW;
    bi.bmiHeader.biHeight      = -newH; // top-down, matches the rest of the pipeline.
    bi.bmiHeader.biPlanes      = 1;
    bi.bmiHeader.biBitCount    = 32;
    bi.bmiHeader.biCompression = BI_RGB;

    HDC screenDc = GetDC(nullptr);
    if (!screenDc) return lease;
    void* newPixels = nullptr;
    HBITMAP newDib = CreateDIBSection(screenDc, &bi, DIB_RGB_COLORS, &newPixels, nullptr, 0);
    ReleaseDC(nullptr, screenDc);
    if (!newDib || !newPixels) {
        if (newDib) DeleteObject(newDib);
        return lease;
    }

    HDC dc = AcquireMemoryDc();
    if (g_dib && dc) {
        // Restore the original default bitmap that was in the DC, then drop
        // the old DIB. SelectObject returns the previously-selected handle,
        // which we stashed when we first selected our DIB into the DC.
        SelectObject(dc, g_oldDibInDc);
        DeleteObject(g_dib);
    }
    g_dib       = newDib;
    g_dibPixels = newPixels;
    g_dibCapW   = newW;
    g_dibCapH   = newH;
    if (dc) {
        g_oldDibInDc = SelectObject(dc, g_dib);
    }

    lease.dib       = g_dib;
    lease.pixels    = g_dibPixels;
    lease.capacityW = g_dibCapW;
    lease.capacityH = g_dibCapH;
    return lease;
}

} // namespace jalium

#endif // _WIN32

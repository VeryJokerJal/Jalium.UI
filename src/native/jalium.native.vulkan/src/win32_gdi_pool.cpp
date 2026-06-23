#ifdef _WIN32

#include "win32_gdi_pool.h"

#include <windows.h>

#include <algorithm>
#include <cwctype>
#include <mutex>
#include <string>
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

inline uint64_t MakeFontKey(uint32_t fontFamilyId, int height, int weight, bool italic, uint8_t quality) noexcept
{
    // 16 bits family id, 16 bits height (signed → store as uint16),
    // 16 bits weight (LOGFONT range 100..900), 1 bit italic, 8 bits quality.
    // Quality occupies bits 49..56 — keeps the italic bit at 48 stable so
    // existing callers that pass the historical default land on the same key
    // bucket as before once their HFONT is re-created at the new quality.
    return  (static_cast<uint64_t>(fontFamilyId) & 0xFFFFu)
         | ((static_cast<uint64_t>(static_cast<uint16_t>(height))   & 0xFFFFu) << 16)
         | ((static_cast<uint64_t>(static_cast<uint16_t>(weight))   & 0xFFFFu) << 32)
         |  (italic ? (1ULL << 48) : 0ULL)
         | ((static_cast<uint64_t>(quality) & 0xFFu) << 49);
}

// ── Font-family resolution (DirectWrite parity for the GDI text path) ─────────

// Lowercases a family string for CSS-generic keyword matching.
std::wstring ToLowerAscii(const std::wstring& s)
{
    std::wstring out;
    out.reserve(s.size());
    for (wchar_t c : s) out.push_back(static_cast<wchar_t>(towlower(c)));
    return out;
}

// Trims leading/trailing whitespace and a surrounding pair of single/double
// quotes (CSS font-family names may be quoted, e.g. 'Cascadia Code').
std::wstring TrimAndUnquote(const std::wstring& s)
{
    size_t b = 0, e = s.size();
    auto isWs = [](wchar_t c) { return c == L' ' || c == L'\t' || iswspace(c) != 0; };
    while (b < e && isWs(s[b])) ++b;
    while (e > b && isWs(s[e - 1])) --e;
    if (e - b >= 2 && ((s[b] == L'"' && s[e - 1] == L'"') ||
                       (s[b] == L'\'' && s[e - 1] == L'\''))) {
        ++b; --e;
        while (b < e && isWs(s[b])) ++b;
        while (e > b && isWs(s[e - 1])) --e;
    }
    return s.substr(b, e - b);
}

// Maps a CSS generic family keyword to a concrete installed Windows face.
// Returns nullptr when `lower` is not a recognised generic.
const wchar_t* MapGenericFamily(const std::wstring& lower)
{
    if (lower == L"monospace" || lower == L"ui-monospace")   return L"Consolas";
    if (lower == L"sans-serif" || lower == L"ui-sans-serif" ||
        lower == L"system-ui"  || lower == L"ui-rounded")    return L"Segoe UI";
    if (lower == L"serif" || lower == L"ui-serif")           return L"Times New Roman";
    if (lower == L"cursive")                                 return L"Segoe Script";
    if (lower == L"fantasy")                                 return L"Impact";
    return nullptr;
}

// Returns true if `face` names an installed GDI font family (exact face-name
// match, charset-agnostic). Empty / over-long names are treated as not found.
bool IsFontInstalled(const std::wstring& face)
{
    if (face.empty() || face.size() >= LF_FACESIZE) return false;
    HDC dc = GetDC(nullptr);
    if (!dc) return false;
    LOGFONTW lf{};
    lf.lfCharSet = DEFAULT_CHARSET;
    wcsncpy_s(lf.lfFaceName, LF_FACESIZE, face.c_str(), _TRUNCATE);
    bool found = false;
    EnumFontFamiliesExW(
        dc, &lf,
        [](const LOGFONTW*, const TEXTMETRICW*, DWORD, LPARAM lp) -> int {
            *reinterpret_cast<bool*>(lp) = true;
            return 0;  // first match is enough — stop enumeration
        },
        reinterpret_cast<LPARAM>(&found), 0);
    ReleaseDC(nullptr, dc);
    return found;
}

} // namespace

std::wstring Win32GdiPool::ResolveGdiFaceName(const wchar_t* fontFamily)
{
    // Process-wide cache (mutex-guarded): the resolution probes installed fonts
    // via EnumFontFamiliesExW, which is comparatively expensive, but the answer
    // is stable for a given input. A single global cache means each distinct
    // family string is probed at most once for the WHOLE process — not once per
    // render/measure thread — so the render path and the measurement path never
    // re-enumerate a family that any thread already resolved. (A thread_local
    // cache would re-probe on every thread that draws text, which on a busy UI
    // shows up as sustained CPU.) Returned by value so the map can't be
    // invalidated under callers.
    static std::unordered_map<std::wstring, std::wstring> g_faceCache;
    static std::mutex g_faceCacheMutex;

    std::wstring key = fontFamily ? fontFamily : L"";
    {
        std::lock_guard<std::mutex> lock(g_faceCacheMutex);
        if (auto it = g_faceCache.find(key); it != g_faceCache.end()) {
            return it->second;
        }
    }

    // Resolve outside the lock — the EnumFontFamiliesExW probe is the expensive
    // part and must not hold the cache mutex. A first-time race between threads
    // resolving the same family just probes twice and emplaces idempotently.
    std::wstring resolved;
    size_t start = 0;
    const size_t n = key.size();
    while (start <= n) {
        const size_t comma = key.find(L',', start);
        std::wstring candidate = TrimAndUnquote(
            key.substr(start, comma == std::wstring::npos ? std::wstring::npos
                                                          : comma - start));
        if (!candidate.empty()) {
            const std::wstring lower = ToLowerAscii(candidate);
            if (const wchar_t* generic = MapGenericFamily(lower)) {
                // A CSS generic terminates the list — DirectWrite maps it to its
                // platform default; we map to a concrete installed face.
                resolved = generic;
                break;
            }
            if (IsFontInstalled(candidate)) {
                resolved = candidate;
                break;
            }
        }
        if (comma == std::wstring::npos) break;
        start = comma + 1;
    }

    if (resolved.empty()) {
        // Nothing in the stack resolved. Match DirectWrite's sans-serif fallback
        // (Segoe UI) instead of letting the GDI mapper pick a serif/MS-Sans face.
        resolved = L"Segoe UI";
    }

    std::lock_guard<std::mutex> lock(g_faceCacheMutex);
    auto inserted = g_faceCache.emplace(std::move(key), std::move(resolved));
    return inserted.first->second;
}

HFONT Win32GdiPool::AcquireFont(uint32_t fontFamilyId,
                                const wchar_t* fontFamily,
                                int height,
                                int weight,
                                bool italic,
                                uint8_t quality)
{
    // Clamp out-of-range quality values to the historical default so a managed
    // bug can't make CreateFontW silently fall back to DEFAULT_QUALITY (which
    // would render the same text differently from every other element on the
    // same backend).
    if (quality > CLEARTYPE_QUALITY) {
        quality = CLEARTYPE_QUALITY;
    }
    const uint64_t key = MakeFontKey(fontFamilyId, height, weight, italic, quality);
    auto it = g_fontPool.find(key);
    if (it != g_fontPool.end()) {
        return it->second;
    }

    // Resolve the (possibly comma-separated / CSS-generic) family to a single
    // installed face so GDI doesn't substitute a serif/monospace default — see
    // ResolveGdiFaceName. The resolution is cached, so this is cheap on the hot
    // (cache-hit) path; AcquireFont itself only reaches here on an HFONT miss.
    const std::wstring face = ResolveGdiFaceName(fontFamily);

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
        quality,
        DEFAULT_PITCH | FF_DONTCARE,
        face.c_str());
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

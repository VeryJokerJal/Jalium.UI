#include "font_provider.h"
#include "font_face.h"

#include <array>
#include <cstring>
#include <cwchar>
#include <fstream>
#include <iterator>
#include <memory>
#include <mutex>
#include <unordered_map>
#include <utility>
#include <vector>

#ifndef _WIN32
#include <stdlib.h>   // realpath
#endif

namespace jalium {

namespace {

using FontBytes = std::shared_ptr<const std::vector<uint8_t>>;

// Symlinked spellings of one file (common under /system/fonts) must share a
// cache entry; an unresolvable path falls back to its literal spelling. The
// POSIX.1-2008 allocating form sidesteps PATH_MAX portability.
std::string CanonicalFontPath(const std::string& path)
{
#ifndef _WIN32
    if (char* resolved = realpath(path.c_str(), nullptr)) {
        std::string result(resolved);
        free(resolved);
        return result;
    }
#endif
    return path;
}

// Process-wide font byte cache. Each JaliumTextFormat used to re-read and
// privately own its fallback font files — ~19-32 MB per CJK collection, times
// every text control that hits fallback. Faces now share one immutable buffer
// per file: the weak map hands existing bytes to new faces for as long as any
// face is alive, and a small pinned ring keeps the most recent files loaded so
// destroy/recreate churn does not thrash disk reads.
FontBytes AcquireFontBytes(const std::string& path)
{
    struct Cache {
        std::mutex mutex;
        std::unordered_map<std::string, std::weak_ptr<const std::vector<uint8_t>>> map;
        std::array<std::pair<std::string, FontBytes>, 4> pinned;
        size_t pinnedNext = 0;

        void Pin(const std::string& key, const FontBytes& bytes) {
            for (auto& slot : pinned)
                if (slot.first == key) { slot.second = bytes; return; }
            pinned[pinnedNext] = { key, bytes };
            pinnedNext = (pinnedNext + 1) % pinned.size();
        }
    };
    static Cache cache;

    const std::string key = CanonicalFontPath(path);
    {
        std::lock_guard<std::mutex> lock(cache.mutex);
        auto it = cache.map.find(key);
        if (it != cache.map.end()) {
            if (FontBytes bytes = it->second.lock()) {
                cache.Pin(key, bytes);
                return bytes;
            }
        }
    }

    // Disk I/O runs outside the lock so concurrent loads of different fonts do
    // not serialize; a same-file race is resolved below in favor of the buffer
    // registered first.
    std::ifstream file(key, std::ios::binary);
    if (!file) return nullptr;
    auto loaded = std::make_shared<std::vector<uint8_t>>(
        (std::istreambuf_iterator<char>(file)),
         std::istreambuf_iterator<char>());
    if (loaded->empty()) return nullptr;
    FontBytes bytes = std::move(loaded);

    std::lock_guard<std::mutex> lock(cache.mutex);
    auto it = cache.map.find(key);
    if (it != cache.map.end()) {
        if (FontBytes existing = it->second.lock()) {
            cache.Pin(key, existing);
            return existing;
        }
    }
    if (cache.map.size() > 64) {   // drop entries whose bytes were all released
        for (auto e = cache.map.begin(); e != cache.map.end(); ) {
            if (e->second.expired()) e = cache.map.erase(e);
            else ++e;
        }
    }
    cache.map[key] = bytes;
    cache.Pin(key, bytes);
    return bytes;
}

std::unique_ptr<FontFace> LoadFace(const std::string& path, int faceIndex)
{
    FontBytes bytes = AcquireFontBytes(path);
    if (!bytes) return nullptr;
    return FontFace::Parse(std::move(bytes), faceIndex);
}

} // namespace

// ============================================================================
// FontProvider base class default implementation
//
// Discovery (FindFont) is platform-specific; face creation is shared: read the
// resolved font file's bytes and hand them to the self-hosted FontFace parser.
// ============================================================================

std::unique_ptr<FontFace> FontProvider::CreateFace(
    const wchar_t* familyName,
    int32_t weight,
    int32_t style)
{
    std::string path;
    int faceIndex = 0;

    if (!FindFont(familyName, weight, style, path, faceIndex))
    {
        // Try the platform default family as a fallback.
        const wchar_t* defaultFamily = GetDefaultFontFamily();
        if (defaultFamily && (!familyName || wcscmp(defaultFamily, familyName) != 0))
        {
            if (!FindFont(defaultFamily, weight, style, path, faceIndex))
                return nullptr;
        }
        else
        {
            return nullptr;
        }
    }

    return LoadFace(path, faceIndex);
}

std::unique_ptr<FontFace> FontProvider::CreateFace(const FontMatch& match)
{
    if (match.path.empty()) return nullptr;
    return LoadFace(match.path, match.faceIndex);
}

std::vector<FontProvider::FontMatch> FontProvider::FindFallbackFonts(
    const std::vector<uint32_t>&,
    const wchar_t*,
    int32_t weight,
    int32_t style)
{
    std::vector<FontMatch> result;
    const wchar_t* family = GetFallbackFontFamily();
    if (!family) return result;
    FontMatch match;
    if (FindFont(family, weight, style, match.path, match.faceIndex))
        result.push_back(std::move(match));
    return result;
}

} // namespace jalium

#include "glyph_rasterizer.h"
#include "font_face.h"
#include "font_data.h"

#include "jalium_scanline_rasterizer.h"

#define STB_IMAGE_STATIC
#define STBI_ONLY_PNG
#define STBI_NO_STDIO
#define STB_IMAGE_IMPLEMENTATION
#include "stb_image.h"

#include <algorithm>
#include <array>
#include <cmath>
#include <cstring>
#include <limits>

#if defined(JALIUM_HAS_FREETYPE_HEADERS) && defined(__linux__) && !defined(__ANDROID__)
#include <ft2build.h>
#include FT_FREETYPE_H
#include <dlfcn.h>
#include <mutex>
#endif

namespace jalium {

namespace {

using font::ByteReader;
using font::kTag_CBDT;
using font::kTag_CBLC;
using font::kTag_COLR;
using font::kTag_CPAL;

#if defined(JALIUM_HAS_FREETYPE_HEADERS) && defined(__linux__) && !defined(__ANDROID__)
// Optional runtime bridge for COLR v1. No symbol is linked at build time; if a
// deployment has no libfreetype, the native COLR-v0/CBDT paths keep working.
bool TryRasterizeRuntimeColor(
    FontFace* sourceFace,
    uint16_t glyph,
    float fontSizePx,
    RasterizedGlyph& result)
{
    struct Api {
        void* module = nullptr;
        decltype(&FT_Init_FreeType) init = nullptr;
        decltype(&FT_Done_FreeType) done = nullptr;
        decltype(&FT_New_Memory_Face) newMemoryFace = nullptr;
        decltype(&FT_Done_Face) doneFace = nullptr;
        decltype(&FT_Set_Pixel_Sizes) setPixelSizes = nullptr;
        decltype(&FT_Load_Glyph) loadGlyph = nullptr;
        decltype(&FT_Render_Glyph) renderGlyph = nullptr;
        bool ready = false;
    };
    static Api api;
    static std::once_flag once;
    std::call_once(once, [] {
        api.module = dlopen("libfreetype.so.6", RTLD_LAZY | RTLD_LOCAL);
        if (!api.module) api.module = dlopen("libfreetype.so", RTLD_LAZY | RTLD_LOCAL);
        if (!api.module) return;
#define LOAD_FT(member, symbol) api.member = reinterpret_cast<decltype(api.member)>(dlsym(api.module, symbol))
        LOAD_FT(init, "FT_Init_FreeType");
        LOAD_FT(done, "FT_Done_FreeType");
        LOAD_FT(newMemoryFace, "FT_New_Memory_Face");
        LOAD_FT(doneFace, "FT_Done_Face");
        LOAD_FT(setPixelSizes, "FT_Set_Pixel_Sizes");
        LOAD_FT(loadGlyph, "FT_Load_Glyph");
        LOAD_FT(renderGlyph, "FT_Render_Glyph");
#undef LOAD_FT
        api.ready = api.init && api.done && api.newMemoryFace && api.doneFace &&
                    api.setPixelSizes && api.loadGlyph && api.renderGlyph;
    });
    if (!api.ready || !sourceFace || !sourceFace->RawData() || sourceFace->RawSize() == 0) return false;

    FT_Library library = nullptr;
    if (api.init(&library) != 0 || !library) return false;
    FT_Face face = nullptr;
    const FT_Error createResult = api.newMemoryFace(
        library, sourceFace->RawData(), static_cast<FT_Long>(sourceFace->RawSize()),
        sourceFace->FaceIndex(), &face);
    if (createResult != 0 || !face) {
        api.done(library);
        return false;
    }
    const FT_UInt ppem = static_cast<FT_UInt>(std::clamp(std::lround(fontSizePx), 1l, 4095l));
    bool success = api.setPixelSizes(face, 0, ppem) == 0 &&
                   api.loadGlyph(face, glyph, FT_LOAD_DEFAULT | FT_LOAD_COLOR) == 0 &&
                   api.renderGlyph(face->glyph, FT_RENDER_MODE_NORMAL) == 0 &&
                   face->glyph->bitmap.pixel_mode == FT_PIXEL_MODE_BGRA &&
                   face->glyph->bitmap.width > 0 && face->glyph->bitmap.rows > 0;
    if (success) {
        const FT_Bitmap& bitmap = face->glyph->bitmap;
        result.width = static_cast<int32_t>(bitmap.width);
        result.height = static_cast<int32_t>(bitmap.rows);
        result.bearingX = face->glyph->bitmap_left;
        result.bearingY = face->glyph->bitmap_top;
        result.advanceX = sourceFace->GetAdvance(glyph) * fontSizePx / sourceFace->UnitsPerEm();
        result.pixels.resize(static_cast<size_t>(result.width) * result.height * 4);
        const int pitch = bitmap.pitch;
        for (int y = 0; y < result.height; ++y) {
            const uint8_t* row = pitch >= 0
                ? bitmap.buffer + static_cast<size_t>(y) * pitch
                : bitmap.buffer + static_cast<size_t>(result.height - 1 - y) * static_cast<size_t>(-pitch);
            for (int x = 0; x < result.width; ++x) {
                const size_t source = static_cast<size_t>(x) * 4;
                const size_t destination = (static_cast<size_t>(y) * result.width + x) * 4;
                // FT_PIXEL_MODE_BGRA is already premultiplied.
                result.pixels[destination] = row[source + 2];
                result.pixels[destination + 1] = row[source + 1];
                result.pixels[destination + 2] = row[source];
                result.pixels[destination + 3] = row[source + 3];
            }
        }
        result.isColor = true;
    }
    api.doneFace(face);
    api.done(library);
    return success;
}
#else
bool TryRasterizeRuntimeColor(FontFace*, uint16_t, float, RasterizedGlyph&) { return false; }
#endif

static uint8_t ToByte(float value)
{
    return static_cast<uint8_t>(std::clamp(std::lround(value * 255.0f), 0l, 255l));
}

// Rasterize one monochrome outline. LCD uses a true 3x horizontal coverage
// grid followed by the standard five-tap FreeType-style filter; it is not a
// shifted copy of a grayscale mask, so narrow stems retain independent RGB
// coverage. The returned bitmap always uses premultiplied RGBA coverage.
RasterizedGlyph RasterizeOutline(
    FontFace* face,
    uint32_t glyphIndex,
    float fontSizePx,
    uint8_t subpixelX,
    GlyphAntialiasMode antialiasMode)
{
    RasterizedGlyph result{};
    if (!face || glyphIndex == 0 || fontSizePx <= 0.0f) return result;

    const float upem = static_cast<float>(face->UnitsPerEm());
    if (upem <= 0.0f) return result;
    const float scale = fontSizePx / upem;
    result.advanceX = face->GetAdvance(static_cast<uint16_t>(glyphIndex)) * scale;

    float tolerance = 0.25f / scale;
    if (tolerance < 1.0f) tolerance = 1.0f;
    GlyphOutline outline;
    if (!face->GetGlyphContours(static_cast<uint16_t>(glyphIndex), tolerance, outline) ||
        outline.contours.empty()) {
        return result;
    }

    const float phase = static_cast<float>(subpixelX) * 0.125f;
    float minX = std::numeric_limits<float>::max();
    float minY = std::numeric_limits<float>::max();
    float maxX = std::numeric_limits<float>::lowest();
    float maxY = std::numeric_limits<float>::lowest();
    for (auto& contour : outline.contours) {
        for (uint32_t i = 0; i < contour.VertexCount(); ++i) {
            const float x = contour.points[i * 2] * scale + phase;
            const float y = -contour.points[i * 2 + 1] * scale;
            contour.points[i * 2] = x;
            contour.points[i * 2 + 1] = y;
            minX = std::min(minX, x); maxX = std::max(maxX, x);
            minY = std::min(minY, y); maxY = std::max(maxY, y);
        }
    }
    if (!(maxX > minX) || !(maxY > minY)) return result;

    // LCD's five-tap filter reaches two subpixels outside the outline. Two
    // logical padding pixels are cheap and prevent colored fringe clipping.
    const int padding = antialiasMode == GlyphAntialiasMode::HorizontalLcd ? 2 : 1;
    const int x0 = static_cast<int>(std::floor(minX)) - padding;
    const int y0 = static_cast<int>(std::floor(minY)) - 1;
    int x1 = static_cast<int>(std::ceil(maxX)) + padding;
    int y1 = static_cast<int>(std::ceil(maxY)) + 1;
    if (x1 - x0 > 4095) x1 = x0 + 4095;
    if (y1 - y0 > 4095) y1 = y0 + 4095;
    const int width = x1 - x0;
    const int height = y1 - y0;
    if (width <= 0 || height <= 0) return result;

    const int oversampleX = antialiasMode == GlyphAntialiasMode::HorizontalLcd ? 3 : 1;
    for (auto& contour : outline.contours) {
        for (uint32_t i = 0; i < contour.VertexCount(); ++i) {
            contour.points[i * 2] = (contour.points[i * 2] - static_cast<float>(x0)) * oversampleX;
            contour.points[i * 2 + 1] -= static_cast<float>(y0);
        }
    }

    std::vector<PixelRect> rects;
    RasterizePathToRects(outline.contours, FillRule::NonZero, rects);
    const int sampleWidth = width * oversampleX;
    std::vector<uint8_t> coverage(static_cast<size_t>(sampleWidth) * height, 0);
    for (const auto& rect : rects) {
        const int left = std::max(0, rect.x);
        const int top = std::max(0, rect.y);
        const int right = std::min(sampleWidth, rect.x + rect.w);
        const int bottom = std::min(height, rect.y + rect.h);
        const uint8_t value = ToByte(std::clamp(rect.alpha, 0.0f, 1.0f));
        for (int y = top; y < bottom; ++y)
            for (int x = left; x < right; ++x) {
                uint8_t& destination = coverage[static_cast<size_t>(y) * sampleWidth + x];
                destination = std::max(destination, value);
            }
    }

    result.width = width;
    result.height = height;
    result.bearingX = x0;
    result.bearingY = -y0;
    result.pixels.assign(static_cast<size_t>(width) * height * 4, 0);

    if (antialiasMode == GlyphAntialiasMode::HorizontalLcd) {
        constexpr std::array<int, 5> filter = {8, 77, 86, 77, 8};
        auto filtered = [&](int y, int sample) -> uint8_t {
            int sum = 0;
            for (int tap = -2; tap <= 2; ++tap) {
                const int sx = sample + tap;
                if (sx >= 0 && sx < sampleWidth)
                    sum += coverage[static_cast<size_t>(y) * sampleWidth + sx] * filter[static_cast<size_t>(tap + 2)];
            }
            return static_cast<uint8_t>(std::clamp((sum + 128) >> 8, 0, 255));
        };
        for (int y = 0; y < height; ++y) {
            for (int x = 0; x < width; ++x) {
                const uint8_t red = filtered(y, x * 3);
                const uint8_t green = filtered(y, x * 3 + 1);
                const uint8_t blue = filtered(y, x * 3 + 2);
                const size_t pixel = (static_cast<size_t>(y) * width + x) * 4;
                result.pixels[pixel] = red;
                result.pixels[pixel + 1] = green;
                result.pixels[pixel + 2] = blue;
                result.pixels[pixel + 3] = std::max({red, green, blue});
            }
        }
        result.hasSubpixel = true;
    } else {
        for (int y = 0; y < height; ++y) {
            for (int x = 0; x < width; ++x) {
                uint8_t value = coverage[static_cast<size_t>(y) * width + x];
                if (antialiasMode == GlyphAntialiasMode::Aliased)
                    value = value >= 128 ? 255 : 0;
                const size_t pixel = (static_cast<size_t>(y) * width + x) * 4;
                result.pixels[pixel] = value;
                result.pixels[pixel + 1] = value;
                result.pixels[pixel + 2] = value;
                result.pixels[pixel + 3] = value;
            }
        }
    }
    return result;
}

struct BitmapMetrics {
    uint8_t height = 0;
    uint8_t width = 0;
    int8_t bearingX = 0;
    int8_t bearingY = 0;
    uint8_t advance = 0;
};

BitmapMetrics ReadSmallMetrics(const ByteReader& data, size_t offset)
{
    return {data.U8(offset), data.U8(offset + 1), data.S8(offset + 2),
            data.S8(offset + 3), data.U8(offset + 4)};
}

BitmapMetrics ReadBigMetrics(const ByteReader& data, size_t offset)
{
    return {data.U8(offset), data.U8(offset + 1), data.S8(offset + 2),
            data.S8(offset + 3), data.U8(offset + 4)};
}

struct BitmapLocation {
    uint32_t offset = 0;
    uint32_t length = 0;
    uint16_t imageFormat = 0;
    uint8_t ppem = 0;
    BitmapMetrics metrics{};
    bool hasIndexMetrics = false;
    bool valid = false;
};

bool LocateInIndexSubtable(
    const ByteReader& cblc,
    uint32_t subtableOffset,
    uint16_t firstGlyph,
    uint16_t lastGlyph,
    uint16_t glyph,
    uint8_t ppem,
    BitmapLocation& location)
{
    if (glyph < firstGlyph || glyph > lastGlyph || !cblc.InBounds(subtableOffset, 8)) return false;
    const uint16_t indexFormat = cblc.U16(subtableOffset);
    const uint16_t imageFormat = cblc.U16(subtableOffset + 2);
    const uint32_t imageDataOffset = cblc.U32(subtableOffset + 4);
    const uint32_t relativeGlyph = glyph - firstGlyph;
    uint32_t offset1 = 0, offset2 = 0;

    switch (indexFormat) {
    case 1:
        offset1 = cblc.U32(subtableOffset + 8 + static_cast<size_t>(relativeGlyph) * 4);
        offset2 = cblc.U32(subtableOffset + 12 + static_cast<size_t>(relativeGlyph) * 4);
        break;
    case 2: {
        const uint32_t imageSize = cblc.U32(subtableOffset + 8);
        offset1 = relativeGlyph * imageSize;
        offset2 = offset1 + imageSize;
        location.metrics = ReadBigMetrics(cblc, subtableOffset + 12);
        location.hasIndexMetrics = true;
        break;
    }
    case 3:
        offset1 = cblc.U16(subtableOffset + 8 + static_cast<size_t>(relativeGlyph) * 2);
        offset2 = cblc.U16(subtableOffset + 10 + static_cast<size_t>(relativeGlyph) * 2);
        break;
    case 4: {
        const uint32_t count = cblc.U32(subtableOffset + 8);
        const size_t pairs = subtableOffset + 12;
        bool found = false;
        for (uint32_t i = 0; i < count; ++i) {
            if (cblc.U16(pairs + static_cast<size_t>(i) * 4) == glyph) {
                offset1 = cblc.U16(pairs + static_cast<size_t>(i) * 4 + 2);
                offset2 = cblc.U16(pairs + static_cast<size_t>(i + 1) * 4 + 2);
                found = true;
                break;
            }
        }
        if (!found) return false;
        break;
    }
    case 5: {
        const uint32_t imageSize = cblc.U32(subtableOffset + 8);
        location.metrics = ReadBigMetrics(cblc, subtableOffset + 12);
        location.hasIndexMetrics = true;
        const uint32_t count = cblc.U32(subtableOffset + 20);
        bool found = false;
        for (uint32_t i = 0; i < count; ++i) {
            if (cblc.U16(subtableOffset + 24 + static_cast<size_t>(i) * 2) == glyph) {
                offset1 = i * imageSize;
                offset2 = offset1 + imageSize;
                found = true;
                break;
            }
        }
        if (!found) return false;
        break;
    }
    default:
        return false;
    }
    if (offset2 <= offset1) return false;
    location.offset = imageDataOffset + offset1;
    location.length = offset2 - offset1;
    location.imageFormat = imageFormat;
    location.ppem = ppem;
    location.valid = true;
    return true;
}

BitmapLocation FindBitmapLocation(
    const ByteReader& cblc,
    uint16_t glyph,
    float requestedPpem)
{
    BitmapLocation best;
    float bestDistance = std::numeric_limits<float>::max();
    const uint32_t numSizes = cblc.U32(4);
    if (numSizes > 4096 || !cblc.InBounds(8, static_cast<size_t>(numSizes) * 48)) return best;
    for (uint32_t strike = 0; strike < numSizes; ++strike) {
        const size_t sizeTable = 8 + static_cast<size_t>(strike) * 48;
        const uint16_t startGlyph = cblc.U16(sizeTable + 40);
        const uint16_t endGlyph = cblc.U16(sizeTable + 42);
        if (glyph < startGlyph || glyph > endGlyph) continue;
        const uint32_t arrayOffset = cblc.U32(sizeTable);
        const uint32_t count = cblc.U32(sizeTable + 8);
        const uint8_t ppem = cblc.U8(sizeTable + 45);
        if (count > 65536 || !cblc.InBounds(arrayOffset, static_cast<size_t>(count) * 8)) continue;
        for (uint32_t i = 0; i < count; ++i) {
            const size_t entry = arrayOffset + static_cast<size_t>(i) * 8;
            const uint16_t first = cblc.U16(entry);
            const uint16_t last = cblc.U16(entry + 2);
            if (glyph < first || glyph > last) continue;
            BitmapLocation candidate;
            if (!LocateInIndexSubtable(cblc, arrayOffset + cblc.U32(entry + 4),
                                       first, last, glyph, ppem, candidate)) continue;
            const float distance = std::abs(static_cast<float>(std::max<uint8_t>(ppem, 1)) - requestedPpem);
            if (distance < bestDistance) {
                best = candidate;
                bestDistance = distance;
            }
        }
    }
    return best;
}

void ResizePremultipliedRgba(
    const uint8_t* source,
    int sourceWidth,
    int sourceHeight,
    int targetWidth,
    int targetHeight,
    std::vector<uint8_t>& target)
{
    target.resize(static_cast<size_t>(targetWidth) * targetHeight * 4);
    for (int y = 0; y < targetHeight; ++y) {
        const float sourceY = (y + 0.5f) * sourceHeight / targetHeight - 0.5f;
        const int y0 = std::clamp(static_cast<int>(std::floor(sourceY)), 0, sourceHeight - 1);
        const int y1 = std::min(y0 + 1, sourceHeight - 1);
        const float fy = std::clamp(sourceY - std::floor(sourceY), 0.0f, 1.0f);
        for (int x = 0; x < targetWidth; ++x) {
            const float sourceX = (x + 0.5f) * sourceWidth / targetWidth - 0.5f;
            const int x0 = std::clamp(static_cast<int>(std::floor(sourceX)), 0, sourceWidth - 1);
            const int x1 = std::min(x0 + 1, sourceWidth - 1);
            const float fx = std::clamp(sourceX - std::floor(sourceX), 0.0f, 1.0f);
            const size_t destination = (static_cast<size_t>(y) * targetWidth + x) * 4;
            for (int channel = 0; channel < 4; ++channel) {
                auto premul = [&](int sx, int sy) {
                    const size_t p = (static_cast<size_t>(sy) * sourceWidth + sx) * 4;
                    if (channel == 3) return static_cast<float>(source[p + 3]);
                    return source[p + channel] * (source[p + 3] / 255.0f);
                };
                const float top = premul(x0, y0) + (premul(x1, y0) - premul(x0, y0)) * fx;
                const float bottom = premul(x0, y1) + (premul(x1, y1) - premul(x0, y1)) * fx;
                target[destination + channel] = static_cast<uint8_t>(std::clamp(std::lround(top + (bottom - top) * fy), 0l, 255l));
            }
        }
    }
}

bool TryRasterizeCbdt(
    FontFace* face,
    uint16_t glyph,
    float fontSizePx,
    RasterizedGlyph& result)
{
    const ByteReader cblc = face->GetTable(kTag_CBLC);
    const ByteReader cbdt = face->GetTable(kTag_CBDT);
    if (cblc.Size() < 8 || cbdt.Size() < 4) return false;
    BitmapLocation location = FindBitmapLocation(cblc, glyph, fontSizePx);
    if (!location.valid || !cbdt.InBounds(location.offset, location.length)) return false;

    size_t pngOffset = location.offset;
    uint32_t pngLength = 0;
    BitmapMetrics metrics = location.metrics;
    if (location.imageFormat == 17) {
        metrics = ReadSmallMetrics(cbdt, pngOffset);
        pngLength = cbdt.U32(pngOffset + 5);
        pngOffset += 9;
    } else if (location.imageFormat == 18) {
        metrics = ReadBigMetrics(cbdt, pngOffset);
        pngLength = cbdt.U32(pngOffset + 8);
        pngOffset += 12;
    } else if (location.imageFormat == 19) {
        if (!location.hasIndexMetrics) return false;
        pngLength = cbdt.U32(pngOffset);
        pngOffset += 4;
    } else {
        return false;
    }
    if (pngLength == 0 || pngLength > location.length || !cbdt.InBounds(pngOffset, pngLength)) return false;

    int decodedWidth = 0, decodedHeight = 0, components = 0;
    stbi_uc* decoded = stbi_load_from_memory(
        cbdt.Data() + pngOffset, static_cast<int>(pngLength),
        &decodedWidth, &decodedHeight, &components, 4);
    if (!decoded || decodedWidth <= 0 || decodedHeight <= 0) {
        stbi_image_free(decoded);
        return false;
    }
    const float scale = fontSizePx / std::max<float>(location.ppem, 1.0f);
    const int width = std::clamp(static_cast<int>(std::lround(decodedWidth * scale)), 1, 4095);
    const int height = std::clamp(static_cast<int>(std::lround(decodedHeight * scale)), 1, 4095);
    ResizePremultipliedRgba(decoded, decodedWidth, decodedHeight, width, height, result.pixels);
    stbi_image_free(decoded);

    result.width = width;
    result.height = height;
    result.bearingX = static_cast<int32_t>(std::lround(metrics.bearingX * scale));
    result.bearingY = static_cast<int32_t>(std::lround(metrics.bearingY * scale));
    result.advanceX = face->GetAdvance(glyph) * fontSizePx / face->UnitsPerEm();
    result.isColor = true;
    return true;
}

struct PaletteColor { uint8_t r, g, b, a; };

PaletteColor ReadPaletteColor(const ByteReader& cpal, uint16_t paletteIndex)
{
    if (paletteIndex == 0xFFFFu) return {255, 255, 255, 255};
    if (cpal.Size() < 12) return {255, 255, 255, 255};
    const uint16_t entriesPerPalette = cpal.U16(2);
    const uint16_t numPalettes = cpal.U16(4);
    const uint16_t numRecords = cpal.U16(6);
    const uint32_t recordsOffset = cpal.U32(8);
    if (numPalettes == 0 || paletteIndex >= entriesPerPalette) return {255, 255, 255, 255};
    const uint32_t record = static_cast<uint32_t>(cpal.U16(12)) + paletteIndex;
    if (record >= numRecords || !cpal.InBounds(recordsOffset + record * 4u, 4)) return {255, 255, 255, 255};
    const size_t p = recordsOffset + record * 4u;
    return {cpal.U8(p + 2), cpal.U8(p + 1), cpal.U8(p), cpal.U8(p + 3)};
}

bool TryRasterizeColrV0(
    FontFace* face,
    uint16_t glyph,
    float fontSizePx,
    uint8_t subpixelX,
    RasterizedGlyph& result)
{
    const ByteReader colr = face->GetTable(kTag_COLR);
    const ByteReader cpal = face->GetTable(kTag_CPAL);
    if (colr.Size() < 14 || cpal.Size() < 12) return false;
    const uint16_t count = colr.U16(2);
    const uint32_t bases = colr.U32(4);
    const uint32_t layers = colr.U32(8);
    const uint16_t layerCount = colr.U16(12);
    if (!colr.InBounds(bases, static_cast<size_t>(count) * 6) ||
        !colr.InBounds(layers, static_cast<size_t>(layerCount) * 4)) return false;

    uint16_t firstLayer = 0, numberOfLayers = 0;
    size_t low = 0, high = count;
    while (low < high) {
        const size_t middle = low + (high - low) / 2;
        const uint16_t baseGlyph = colr.U16(bases + middle * 6);
        if (baseGlyph < glyph) low = middle + 1; else high = middle;
    }
    if (low >= count || colr.U16(bases + low * 6) != glyph) return false;
    firstLayer = colr.U16(bases + low * 6 + 2);
    numberOfLayers = colr.U16(bases + low * 6 + 4);
    if (numberOfLayers == 0 || static_cast<uint32_t>(firstLayer) + numberOfLayers > layerCount) return false;

    struct Layer { RasterizedGlyph glyph; PaletteColor color; };
    std::vector<Layer> rasterLayers;
    int left = std::numeric_limits<int>::max();
    int top = std::numeric_limits<int>::lowest();
    int right = std::numeric_limits<int>::lowest();
    int bottom = std::numeric_limits<int>::max();
    for (uint16_t i = 0; i < numberOfLayers; ++i) {
        const size_t record = layers + static_cast<size_t>(firstLayer + i) * 4;
        const uint16_t layerGlyph = colr.U16(record);
        Layer layer{RasterizeOutline(face, layerGlyph, fontSizePx, subpixelX,
                                     GlyphAntialiasMode::Grayscale),
                    ReadPaletteColor(cpal, colr.U16(record + 2))};
        if (layer.glyph.width <= 0 || layer.glyph.height <= 0) continue;
        left = std::min(left, layer.glyph.bearingX);
        top = std::max(top, layer.glyph.bearingY);
        right = std::max(right, layer.glyph.bearingX + layer.glyph.width);
        bottom = std::min(bottom, layer.glyph.bearingY - layer.glyph.height);
        rasterLayers.push_back(std::move(layer));
    }
    if (rasterLayers.empty() || right <= left || top <= bottom) return false;
    result.width = right - left;
    result.height = top - bottom;
    result.bearingX = left;
    result.bearingY = top;
    result.advanceX = face->GetAdvance(glyph) * fontSizePx / face->UnitsPerEm();
    result.pixels.assign(static_cast<size_t>(result.width) * result.height * 4, 0);

    for (const auto& layer : rasterLayers) {
        const int offsetX = layer.glyph.bearingX - left;
        const int offsetY = top - layer.glyph.bearingY;
        for (int y = 0; y < layer.glyph.height; ++y) {
            for (int x = 0; x < layer.glyph.width; ++x) {
                const size_t source = (static_cast<size_t>(y) * layer.glyph.width + x) * 4;
                const float coverage = layer.glyph.pixels[source + 3] / 255.0f;
                const float sourceAlpha = coverage * (layer.color.a / 255.0f);
                if (sourceAlpha <= 0.0f) continue;
                const size_t destination = (static_cast<size_t>(offsetY + y) * result.width + offsetX + x) * 4;
                const float inverse = 1.0f - sourceAlpha;
                result.pixels[destination] = static_cast<uint8_t>(std::clamp(
                    std::lround(layer.color.r * sourceAlpha + result.pixels[destination] * inverse), 0l, 255l));
                result.pixels[destination + 1] = static_cast<uint8_t>(std::clamp(
                    std::lround(layer.color.g * sourceAlpha + result.pixels[destination + 1] * inverse), 0l, 255l));
                result.pixels[destination + 2] = static_cast<uint8_t>(std::clamp(
                    std::lround(layer.color.b * sourceAlpha + result.pixels[destination + 2] * inverse), 0l, 255l));
                result.pixels[destination + 3] = static_cast<uint8_t>(std::clamp(
                    std::lround(sourceAlpha * 255.0f + result.pixels[destination + 3] * inverse), 0l, 255l));
            }
        }
    }
    result.isColor = true;
    return true;
}

} // namespace

GlyphRasterizer::GlyphRasterizer()
    : subpixelMode_(SubpixelMode::None)
{
}

GlyphRasterizer::~GlyphRasterizer() = default;

RasterizedGlyph GlyphRasterizer::Rasterize(
    FontFace* face,
    uint32_t glyphIndex,
    float fontSizePx,
    uint8_t subpixelX,
    GlyphAntialiasMode antialiasMode)
{
    RasterizedGlyph color;
    if (face && glyphIndex != 0 && fontSizePx > 0.0f && face->HasColorTables()) {
        // Bitmap strikes first: Noto Color Emoji uses CBDT/CBLC PNG records.
        // COLR/CPAL v0 remains the vector-color path. Unsupported COLR v1
        // paint graphs safely fall through to an outline only when this base
        // glyph is not present in the v0 base-glyph records.
        if (TryRasterizeCbdt(face, static_cast<uint16_t>(glyphIndex), fontSizePx, color) ||
            TryRasterizeColrV0(face, static_cast<uint16_t>(glyphIndex), fontSizePx, subpixelX, color) ||
            TryRasterizeRuntimeColor(face, static_cast<uint16_t>(glyphIndex), fontSizePx, color))
            return color;
    }
    // Preserve SetSubpixelMode for embedders that use the rasterizer directly,
    // but the atlas always supplies an explicit per-element mode.
    if (antialiasMode == GlyphAntialiasMode::Grayscale && subpixelMode_ == SubpixelMode::Horizontal)
        antialiasMode = GlyphAntialiasMode::HorizontalLcd;
    return RasterizeOutline(face, glyphIndex, fontSizePx, subpixelX, antialiasMode);
}

} // namespace jalium

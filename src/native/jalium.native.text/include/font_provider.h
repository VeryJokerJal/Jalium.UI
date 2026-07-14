#pragma once

#include <string>
#include <vector>
#include <cstdint>
#include <memory>
#include <mutex>
#include <unordered_map>

namespace jalium {

class FontFace;

// ============================================================================
// FontProvider: Abstract interface for platform-specific font discovery
// ============================================================================

class FontProvider {
public:
    struct FontMatch {
        std::string path;
        int faceIndex = 0;
        std::string family;
    };

    virtual ~FontProvider() = default;

    /// Finds the best matching font file for the given parameters.
    /// @param familyName Font family name (e.g. "Segoe UI", "Roboto")
    /// @param weight Font weight (100-900, 400 = normal)
    /// @param style Font style (0=Normal, 1=Italic, 2=Oblique)
    /// @param outPath Receives the file path to the font
    /// @param outFaceIndex Receives the face index within the font file
    /// @return true if a match was found
    virtual bool FindFont(
        const wchar_t* familyName,
        int32_t weight,
        int32_t style,
        std::string& outPath,
        int& outFaceIndex) = 0;

    /// Creates a self-hosted font face for the given font parameters.
    /// @param familyName Font family name
    /// @param weight Font weight
    /// @param style Font style
    /// @return Owned FontFace, or nullptr if not found.
    virtual std::unique_ptr<FontFace> CreateFace(
        const wchar_t* familyName,
        int32_t weight,
        int32_t style);

    /// Creates a face from an already resolved platform font match.  Fallback
    /// discovery returns exact file/index pairs; resolving the family again can
    /// otherwise select a different face when fontconfig aliases are involved.
    std::unique_ptr<FontFace> CreateFace(const FontMatch& match);

    /// Returns the platform fallback chain for a Unicode cluster.  The default
    /// implementation preserves the legacy single-family fallback for Android;
    /// Linux overrides this with FcFontSort over a cluster charset.
    virtual std::vector<FontMatch> FindFallbackFonts(
        const std::vector<uint32_t>& codepoints,
        const wchar_t* preferredFamily,
        int32_t weight,
        int32_t style);

    /// Gets the default UI font family name for the current platform.
    virtual const wchar_t* GetDefaultFontFamily() const = 0;

    /// Returns a font family with broad Unicode coverage (CJK, etc.) to use
    /// when the primary face lacks a glyph. Non-pure so platforms opt in and
    /// any provider without CJK coverage (or future ones) compile cleanly.
    /// nullptr (the default) disables glyph-level fallback.
    virtual const wchar_t* GetFallbackFontFamily() const { return nullptr; }
};

// ============================================================================
// Platform-specific FontProvider implementations
// ============================================================================

/// Linux: uses Fontconfig for font discovery
class FontProviderFontconfig : public FontProvider {
public:
    FontProviderFontconfig();
    ~FontProviderFontconfig() override;

    bool FindFont(const wchar_t* familyName, int32_t weight, int32_t style,
                  std::string& outPath, int& outFaceIndex) override;
    const wchar_t* GetDefaultFontFamily() const override;
    const wchar_t* GetFallbackFontFamily() const override;

    std::vector<FontMatch> FindFallbackFonts(
        const std::vector<uint32_t>& codepoints,
        const wchar_t* preferredFamily,
        int32_t weight,
        int32_t style) override;

private:
    void* fcConfig_ = nullptr;  // FcConfig* (opaque to avoid header dep)
    std::mutex fallbackCacheMutex_;
    std::unordered_map<std::string, std::vector<FontMatch>> fallbackCache_;
};

/// Android: discovers fonts from /system/fonts/ and fonts.xml
class FontProviderAndroid : public FontProvider {
public:
    FontProviderAndroid();
    ~FontProviderAndroid() override;

    bool FindFont(const wchar_t* familyName, int32_t weight, int32_t style,
                  std::string& outPath, int& outFaceIndex) override;
    const wchar_t* GetDefaultFontFamily() const override;
    const wchar_t* GetFallbackFontFamily() const override;

private:
    void ParseFontsXml();
    struct FontEntry {
        std::string path;
        int weight;
        int style; // 0=normal, 1=italic
        int faceIndex;
    };
    struct FontFamily {
        std::string name;
        std::vector<FontEntry> fonts;
    };
    std::vector<FontFamily> families_;
    bool parsed_ = false;
};

} // namespace jalium

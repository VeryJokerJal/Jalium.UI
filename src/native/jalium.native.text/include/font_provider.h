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

/// Android: parses the system font configuration (fonts.xml and its
/// successors/OEM variants) for named families, aliases, and per-locale
/// fallback chains; scans the font directories when no configuration parses.
class FontProviderAndroid : public FontProvider {
public:
    FontProviderAndroid();
    ~FontProviderAndroid() override;

    bool FindFont(const wchar_t* familyName, int32_t weight, int32_t style,
                  std::string& outPath, int& outFaceIndex) override;
    const wchar_t* GetDefaultFontFamily() const override;
    /// The returned pointer references a string owned by this provider
    /// (fallbackFamilyName_) and remains valid for the provider's lifetime;
    /// it is stable once the first call has built the font tables.
    const wchar_t* GetFallbackFontFamily() const override;

    std::vector<FontMatch> FindFallbackFonts(
        const std::vector<uint32_t>& codepoints,
        const wchar_t* preferredFamily,
        int32_t weight,
        int32_t style) override;

private:
    struct FontEntry {
        std::string path;
        int weight = 400;
        int style = 0;        // 0=normal, 1=italic
        int faceIndex = 0;
    };
    struct LangTag {
        std::string language; // lowercase; "und" for script-only tags
        std::string script;   // normalized like "Hans"/"Arab"; empty when absent
    };
    struct FontFamily {
        std::string name;     // empty for pure locale-fallback families
        std::string lang;     // raw lang attribute as written (empty = none)
        std::vector<LangTag> langTags;
        std::vector<FontEntry> fonts;
    };
    struct FamilyAlias {
        std::string target;   // lowercase target family name
        int weight = -1;      // -1 = no weight restriction
    };

    void EnsureParsed() const;
    void BuildFontTables() const;
    const FontFamily* FindNamedFamily(const std::string& lowerName, int& aliasWeight) const;
    static const FontEntry* SelectEntry(const FontFamily& family, int32_t weight, int32_t style,
                                        int restrictWeight);
    bool FindFontExact(const std::string& lowerFamilyName, int32_t weight, int32_t style,
                       std::string& outPath, int& outFaceIndex) const;

    // Lazily built once (BuildFontTables), read-only afterwards; mutable so the
    // const accessors can trigger the parse.
    mutable std::once_flag parseOnce_;
    mutable std::vector<FontFamily> families_;
    mutable std::unordered_map<std::string, FamilyAlias> aliases_;
    mutable std::vector<FontMatch> scanFallbacks_;  // directory-scan candidates (config-less devices)
    mutable std::wstring fallbackFamilyName_;
    mutable std::string localeLanguage_;
    mutable std::string localeScript_;
};

} // namespace jalium

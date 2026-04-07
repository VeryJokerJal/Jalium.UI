#pragma once

#include <string>
#include <vector>
#include <cstdint>

typedef struct FT_LibraryRec_* FT_Library;
typedef struct FT_FaceRec_*    FT_Face;

namespace jalium {

// ============================================================================
// FontProvider: Abstract interface for platform-specific font discovery
// ============================================================================

class FontProvider {
public:
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

    /// Creates a FreeType face for the given font parameters.
    /// @param ftLib FreeType library handle
    /// @param familyName Font family name
    /// @param weight Font weight
    /// @param style Font style
    /// @return FT_Face handle, or nullptr if not found. Caller owns the face.
    virtual FT_Face CreateFace(
        FT_Library ftLib,
        const wchar_t* familyName,
        int32_t weight,
        int32_t style);

    /// Gets the default UI font family name for the current platform.
    virtual const wchar_t* GetDefaultFontFamily() const = 0;
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

private:
    void* fcConfig_ = nullptr;  // FcConfig* (opaque to avoid header dep)
};

/// Android: discovers fonts from /system/fonts/ and fonts.xml
class FontProviderAndroid : public FontProvider {
public:
    FontProviderAndroid();
    ~FontProviderAndroid() override;

    bool FindFont(const wchar_t* familyName, int32_t weight, int32_t style,
                  std::string& outPath, int& outFaceIndex) override;
    const wchar_t* GetDefaultFontFamily() const override;

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

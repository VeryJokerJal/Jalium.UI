#if defined(__ANDROID__)

#include "font_provider.h"

#include <dirent.h>
#include <sys/stat.h>
#include <sys/system_properties.h>

#include <algorithm>
#include <climits>
#include <cstddef>
#include <cstdlib>
#include <cstring>
#include <fstream>
#include <iterator>
#include <mutex>
#include <string>
#include <utility>
#include <vector>

namespace jalium {

// ============================================================================
// Android font discovery
//
// The system font configuration is parsed from the first readable file of:
//   /system/etc/fonts.xml           (API 21+, kept through Android 16; on
//                                    HyperOS/vivo it may be a symlink into
//                                    /data that open() follows or fails on)
//   /system/etc/font_fallback.xml   (Android 15+ successor format)
//   /system/etc/fonts_default.xml   (vivo's complete stock copy)
// accumulating candidates until a config containing CJK locale-fallback
// families is found, then overlaid with /product/etc/fonts_customization.xml
// (OEM additions). If no configuration yields CJK coverage, the font
// directories are scanned for well-known CJK file names instead.
//
// Locale-fallback families (<family lang="...">, no name) drive
// FindFallbackFonts; named families and <alias> entries drive FindFont.
// ============================================================================

namespace {

// Bounds only the config-less directory-scan candidate list. The XML-config
// fallback chain built by FindFallbackFonts is deliberately unbounded: stock
// fonts.xml lists ~30 low-relevance script families (und-Arab … und-Khmr)
// before the CJK ones (~position 115), so any cap on the chain silently
// starves exactly the families glyph fallback exists to reach. The caller
// probes candidates lazily (per-cluster cmap check with a negative cache),
// so the long tail is only walked when nothing earlier covers the cluster.
constexpr size_t kMaxScanCandidates = 32;
constexpr size_t kMaxConfigFileSize = 16u * 1024u * 1024u;

const char* const kSystemFontsDir = "/system/fonts/";

const char* const kFontDirectories[] = {
    "/system/fonts/",
    "/system_ext/fonts/",
    "/product/fonts/",
    "/odm/fonts/",
    "/vendor/fonts/",
};

// ----------------------------------------------------------------------------
// Small string / file utilities
// ----------------------------------------------------------------------------

bool IsXmlSpace(char c)
{
    return c == ' ' || c == '\t' || c == '\r' || c == '\n';
}

char ToLowerAsciiChar(char c)
{
    return (c >= 'A' && c <= 'Z') ? static_cast<char>(c - 'A' + 'a') : c;
}

std::string ToLowerAscii(std::string value)
{
    for (char& c : value) c = ToLowerAsciiChar(c);
    return value;
}

std::string ToUpperAscii(std::string value)
{
    for (char& c : value)
        if (c >= 'a' && c <= 'z') c = static_cast<char>(c - 'a' + 'A');
    return value;
}

bool IsAllAlpha(const std::string& value)
{
    for (char c : value)
        if (!((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))) return false;
    return !value.empty();
}

bool IsAllDigit(const std::string& value)
{
    for (char c : value)
        if (c < '0' || c > '9') return false;
    return !value.empty();
}

std::string TrimWhitespace(const std::string& value)
{
    size_t begin = 0;
    size_t end = value.size();
    while (begin < end && IsXmlSpace(value[begin])) ++begin;
    while (end > begin && IsXmlSpace(value[end - 1])) --end;
    return value.substr(begin, end - begin);
}

bool EndsWithIgnoreCase(const std::string& value, const char* suffix)
{
    const size_t suffixLength = std::strlen(suffix);
    if (value.size() < suffixLength) return false;
    for (size_t i = 0; i < suffixLength; ++i)
    {
        if (ToLowerAsciiChar(value[value.size() - suffixLength + i]) != ToLowerAsciiChar(suffix[i]))
            return false;
    }
    return true;
}

bool FileExists(const std::string& path)
{
    struct stat st;
    return stat(path.c_str(), &st) == 0 && S_ISREG(st.st_mode);
}

bool ReadFileToString(const char* path, std::string& out)
{
    std::ifstream file(path, std::ios::binary);
    if (!file) return false;
    std::string bytes(
        (std::istreambuf_iterator<char>(file)),
         std::istreambuf_iterator<char>());
    if (bytes.empty() || bytes.size() > kMaxConfigFileSize) return false;
    if (bytes.compare(0, 3, "\xEF\xBB\xBF") == 0) bytes.erase(0, 3);
    out = std::move(bytes);
    return true;
}

// Explicit UTF-32 (Android wchar_t) → UTF-8. wcstombs goes through the process
// locale, ignores errors, and bionic rejects any invalid scalar leaving the
// buffer half-written — so convert by hand and skip invalid codepoints.
std::string WideToUtf8(const wchar_t* value)
{
    std::string utf8;
    if (!value) return utf8;
    for (const wchar_t* p = value; *p; ++p)
    {
        const uint32_t cp = static_cast<uint32_t>(*p);
        if (cp > 0x10FFFF || (cp >= 0xD800 && cp <= 0xDFFF)) continue;
        if (cp < 0x80)
        {
            utf8.push_back(static_cast<char>(cp));
        }
        else if (cp < 0x800)
        {
            utf8.push_back(static_cast<char>(0xC0 | (cp >> 6)));
            utf8.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
        }
        else if (cp < 0x10000)
        {
            utf8.push_back(static_cast<char>(0xE0 | (cp >> 12)));
            utf8.push_back(static_cast<char>(0x80 | ((cp >> 6) & 0x3F)));
            utf8.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
        }
        else
        {
            utf8.push_back(static_cast<char>(0xF0 | (cp >> 18)));
            utf8.push_back(static_cast<char>(0x80 | ((cp >> 12) & 0x3F)));
            utf8.push_back(static_cast<char>(0x80 | ((cp >> 6) & 0x3F)));
            utf8.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
        }
    }
    return utf8;
}

std::wstring Utf8ToWide(const std::string& utf8)
{
    std::wstring wide;
    size_t i = 0;
    while (i < utf8.size())
    {
        const unsigned char lead = static_cast<unsigned char>(utf8[i]);
        uint32_t cp = 0;
        size_t extra = 0;
        if (lead < 0x80) { cp = lead; }
        else if ((lead >> 5) == 0x6) { cp = lead & 0x1Fu; extra = 1; }
        else if ((lead >> 4) == 0xE) { cp = lead & 0x0Fu; extra = 2; }
        else if ((lead >> 3) == 0x1E) { cp = lead & 0x07u; extra = 3; }
        else { ++i; continue; }
        if (i + extra >= utf8.size()) break;
        bool valid = true;
        for (size_t k = 1; k <= extra; ++k)
        {
            const unsigned char cont = static_cast<unsigned char>(utf8[i + k]);
            if ((cont >> 6) != 0x2) { valid = false; break; }
            cp = (cp << 6) | (cont & 0x3Fu);
        }
        if (!valid) { ++i; continue; }
        i += extra + 1;
        if (cp > 0x10FFFF || (cp >= 0xD800 && cp <= 0xDFFF)) continue;
        wide.push_back(static_cast<wchar_t>(cp));
    }
    return wide;
}

std::string ReadSystemProperty(const char* name)
{
    char value[PROP_VALUE_MAX] = {};
    const int length = __system_property_get(name, value);
    return length > 0 ? std::string(value, static_cast<size_t>(length)) : std::string();
}

// ----------------------------------------------------------------------------
// Minimal XML pull parser
//
// Handles exactly what Android font configs need: the <?xml?> declaration,
// multi-line <!-- --> comments (ColorOS wraps entire AOSP family blocks in
// them), single/double-quoted attributes, self-closing tags, nested elements,
// text content, and the five predefined entities. Whole-file input; no
// external dependencies.
// ----------------------------------------------------------------------------

struct XmlAttribute {
    std::string name;
    std::string value;
};

struct XmlElement {
    std::string name;
    std::vector<XmlAttribute> attributes;
    std::vector<XmlElement> children;
    std::string text; // concatenated character data directly inside this element

    const std::string* Attribute(const char* attributeName) const
    {
        for (const auto& attribute : attributes)
            if (attribute.name == attributeName) return &attribute.value;
        return nullptr;
    }
};

void SkipXmlSpace(const std::string& s, size_t& pos)
{
    while (pos < s.size() && IsXmlSpace(s[pos])) ++pos;
}

// Decodes s[start..end) into out, resolving the five predefined entities;
// an unrecognized '&' sequence is kept literally (OEM files are hand-edited).
void AppendXmlText(const std::string& s, size_t start, size_t end, std::string& out)
{
    static const struct { const char* entity; size_t length; char value; } kEntities[] = {
        {"&amp;", 5, '&'}, {"&lt;", 4, '<'}, {"&gt;", 4, '>'},
        {"&quot;", 6, '"'}, {"&apos;", 6, '\''},
    };
    for (size_t i = start; i < end; )
    {
        if (s[i] == '&')
        {
            bool matched = false;
            for (const auto& entity : kEntities)
            {
                if (i + entity.length <= end && s.compare(i, entity.length, entity.entity) == 0)
                {
                    out.push_back(entity.value);
                    i += entity.length;
                    matched = true;
                    break;
                }
            }
            if (matched) continue;
        }
        out.push_back(s[i]);
        ++i;
    }
}

bool IsXmlNameStart(char c)
{
    return (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || c == '_' || c == ':' ||
           static_cast<unsigned char>(c) >= 0x80;
}

bool IsXmlNameChar(char c)
{
    return IsXmlNameStart(c) || (c >= '0' && c <= '9') || c == '-' || c == '.';
}

bool ParseXmlName(const std::string& s, size_t& pos, std::string& out)
{
    if (pos >= s.size() || !IsXmlNameStart(s[pos])) return false;
    const size_t start = pos;
    while (pos < s.size() && IsXmlNameChar(s[pos])) ++pos;
    out.assign(s, start, pos - start);
    return true;
}

bool SkipXmlComment(const std::string& s, size_t& pos)
{
    // pos is at "<!--"
    const size_t end = s.find("-->", pos + 4);
    if (end == std::string::npos) return false;
    pos = end + 3;
    return true;
}

bool ParseXmlAttribute(const std::string& s, size_t& pos, XmlAttribute& out)
{
    if (!ParseXmlName(s, pos, out.name)) return false;
    SkipXmlSpace(s, pos);
    if (pos >= s.size() || s[pos] != '=') return false;
    ++pos;
    SkipXmlSpace(s, pos);
    if (pos >= s.size() || (s[pos] != '"' && s[pos] != '\'')) return false;
    const char quote = s[pos++];
    const size_t start = pos;
    while (pos < s.size() && s[pos] != quote) ++pos;
    if (pos >= s.size()) return false;
    out.value.clear();
    AppendXmlText(s, start, pos, out.value);
    ++pos;
    return true;
}

bool ParseXmlElement(const std::string& s, size_t& pos, XmlElement& out, int depth)
{
    if (depth > 48) return false;
    if (pos >= s.size() || s[pos] != '<') return false;
    ++pos;
    if (!ParseXmlName(s, pos, out.name)) return false;

    // Attributes (unknown ones are stored and simply never consumed — Honor
    // adds supportWeightAdjustment/honortag on <family>, A15+ adds
    // supportedAxes on <font>).
    for (;;)
    {
        SkipXmlSpace(s, pos);
        if (pos >= s.size()) return false;
        if (s[pos] == '/')
        {
            if (pos + 1 >= s.size() || s[pos + 1] != '>') return false;
            pos += 2;
            return true; // self-closing
        }
        if (s[pos] == '>') { ++pos; break; }
        XmlAttribute attribute;
        if (!ParseXmlAttribute(s, pos, attribute)) return false;
        out.attributes.push_back(std::move(attribute));
    }

    // Content: interleaved text, comments, and child elements.
    for (;;)
    {
        const size_t lt = s.find('<', pos);
        if (lt == std::string::npos) return false; // unterminated element
        AppendXmlText(s, pos, lt, out.text);
        pos = lt;
        if (s.compare(pos, 4, "<!--") == 0)
        {
            if (!SkipXmlComment(s, pos)) return false;
            continue;
        }
        if (s.compare(pos, 2, "</") == 0)
        {
            pos += 2;
            std::string closeName;
            if (!ParseXmlName(s, pos, closeName)) return false;
            SkipXmlSpace(s, pos);
            if (pos >= s.size() || s[pos] != '>') return false;
            ++pos;
            return closeName == out.name;
        }
        if (s.compare(pos, 2, "<?") == 0)
        {
            const size_t end = s.find("?>", pos + 2);
            if (end == std::string::npos) return false;
            pos = end + 2;
            continue;
        }
        if (s.compare(pos, 2, "<!") == 0)
        {
            // <!DOCTYPE ...> etc. — not used by Android font configs; skip so a
            // stray one is survivable.
            const size_t end = s.find('>', pos + 2);
            if (end == std::string::npos) return false;
            pos = end + 1;
            continue;
        }
        out.children.emplace_back();
        if (!ParseXmlElement(s, pos, out.children.back(), depth + 1)) return false;
    }
}

bool ParseXmlDocument(const std::string& s, XmlElement& root)
{
    size_t pos = 0;
    for (;;)
    {
        SkipXmlSpace(s, pos);
        if (pos >= s.size() || s[pos] != '<') return false;
        if (s.compare(pos, 4, "<!--") == 0)
        {
            if (!SkipXmlComment(s, pos)) return false;
            continue;
        }
        if (s.compare(pos, 2, "<?") == 0)
        {
            const size_t end = s.find("?>", pos + 2);
            if (end == std::string::npos) return false;
            pos = end + 2;
            continue;
        }
        if (s.compare(pos, 2, "<!") == 0)
        {
            // <!DOCTYPE ...> — not used by Android font configs; skip so a
            // stray one is survivable. An internal subset ("[...]") may itself
            // contain '>', so match the closing ']' before the tag end.
            size_t scan = pos + 2;
            const size_t bracket = s.find_first_of("[>", scan);
            if (bracket != std::string::npos && s[bracket] == '[')
            {
                const size_t subsetEnd = s.find(']', bracket + 1);
                if (subsetEnd == std::string::npos) return false;
                scan = subsetEnd + 1;
            }
            const size_t end = s.find('>', scan);
            if (end == std::string::npos) return false;
            pos = end + 1;
            continue;
        }
        return ParseXmlElement(s, pos, root, 0); // trailing junk after root is ignored
    }
}

// ----------------------------------------------------------------------------
// BCP-47 handling
// ----------------------------------------------------------------------------

std::string NormalizeScript(const std::string& raw)
{
    std::string script = ToLowerAscii(raw);
    if (!script.empty() && script[0] >= 'a' && script[0] <= 'z')
        script[0] = static_cast<char>(script[0] - 'a' + 'A');
    return script;
}

// "zh-Hans" → ("zh", "Hans"); "und-Arab" → ("und", "Arab"); "zh" → ("zh", "").
std::pair<std::string, std::string> ParseBcp47Tag(const std::string& tag)
{
    std::string language;
    std::string script;
    size_t pos = 0;
    bool first = true;
    while (pos <= tag.size())
    {
        size_t end = tag.find_first_of("-_", pos);
        if (end == std::string::npos) end = tag.size();
        const std::string subtag = tag.substr(pos, end - pos);
        if (!subtag.empty())
        {
            if (first)
            {
                language = ToLowerAscii(subtag);
                first = false;
            }
            else if (subtag.size() == 4 && IsAllAlpha(subtag) && script.empty())
            {
                script = NormalizeScript(subtag);
            }
        }
        if (end == tag.size()) break;
        pos = end + 1;
    }
    return {std::move(language), std::move(script)};
}

// lang attribute is a BCP-47 list: comma-separated on API 29+, space-separated
// on API 28 and below — accept both, plus bare language tags (Huawei's
// lang="zh").
std::vector<std::pair<std::string, std::string>> ParseLangList(const std::string& lang)
{
    std::vector<std::pair<std::string, std::string>> tags;
    size_t pos = 0;
    while (pos <= lang.size())
    {
        size_t end = lang.find_first_of(", \t", pos);
        if (end == std::string::npos) end = lang.size();
        if (end > pos)
        {
            auto tag = ParseBcp47Tag(lang.substr(pos, end - pos));
            if (!tag.first.empty()) tags.push_back(std::move(tag));
        }
        if (end == lang.size()) break;
        pos = end + 1;
    }
    return tags;
}

// System locale → (language, script); a script-less "zh" resolves via the
// region: TW/HK/MO → Hant, everything else → Hans.
void ParseLocaleTag(std::string locale, std::string& language, std::string& script)
{
    const size_t comma = locale.find(',');
    if (comma != std::string::npos) locale.erase(comma); // property may hold a list
    language.clear();
    script.clear();
    std::string region;
    size_t pos = 0;
    bool first = true;
    while (pos <= locale.size())
    {
        size_t end = locale.find_first_of("-_", pos);
        if (end == std::string::npos) end = locale.size();
        const std::string subtag = locale.substr(pos, end - pos);
        if (!subtag.empty())
        {
            if (first)
            {
                language = ToLowerAscii(subtag);
                first = false;
            }
            else if (subtag.size() == 4 && IsAllAlpha(subtag) && script.empty())
            {
                script = NormalizeScript(subtag);
            }
            else if (region.empty() &&
                     ((subtag.size() == 2 && IsAllAlpha(subtag)) ||
                      (subtag.size() == 3 && IsAllDigit(subtag))))
            {
                region = ToUpperAscii(subtag);
            }
        }
        if (end == locale.size()) break;
        pos = end + 1;
    }
    if (language.empty()) language = "en";
    if (language == "zh" && script.empty())
        script = (region == "TW" || region == "HK" || region == "MO") ? "Hant" : "Hans";
}

bool IsCjkLangTag(const std::string& language, const std::string& script)
{
    if (language == "zh" || language == "ja" || language == "ko") return true;
    return script == "Hans" || script == "Hant" || script == "Hani" || script == "Bopo" ||
           script == "Hira" || script == "Kana" || script == "Jpan" || script == "Kore" ||
           script == "Hang";
}

// ----------------------------------------------------------------------------
// Font configuration extraction (fonts.xml / font_fallback.xml /
// fonts_default.xml / fonts_customization.xml)
// ----------------------------------------------------------------------------

struct ParsedFont {
    std::string path;        // resolved absolute path (stat-verified)
    int weight = 400;
    int style = 0;           // 0=normal, 1=italic
    int index = 0;
    std::string fallbackFor; // non-empty = belongs to a named family's private chain
};

struct ParsedFamily {
    std::string name;
    std::string lang;
    std::vector<ParsedFont> fonts;
};

struct ParsedAlias {
    std::string name;
    std::string to;
    int weight = -1;
};

struct ParsedFontConfig {
    std::vector<ParsedFamily> families;
    std::vector<ParsedAlias> aliases;
};

// Relative file names resolve against primaryDirectory first, then the other
// known font directories (HyperOS-style configs reference /product files with
// bare names); absolute paths are honored as written (symlinked configs under
// /data use them). Missing files yield an empty string — the entry is dropped.
std::string ResolveFontFile(const std::string& file, const char* primaryDirectory)
{
    if (file.empty()) return {};
    if (file[0] == '/') return FileExists(file) ? file : std::string();
    std::string candidate = std::string(primaryDirectory) + file;
    if (FileExists(candidate)) return candidate;
    for (const char* directory : kFontDirectories)
    {
        if (std::strcmp(directory, primaryDirectory) == 0) continue;
        candidate = std::string(directory) + file;
        if (FileExists(candidate)) return candidate;
    }
    return {};
}

int ParseIntAttribute(const XmlElement& element, const char* name, int fallback)
{
    const std::string* value = element.Attribute(name);
    if (!value || value->empty()) return fallback;
    char* end = nullptr;
    const long parsed = std::strtol(value->c_str(), &end, 10);
    return (end && *end == '\0') ? static_cast<int>(parsed) : fallback;
}

ParsedFamily ExtractFamily(const XmlElement& familyElement, const char* primaryDirectory)
{
    ParsedFamily family;
    // A16 marks platform-retired families with ignore="true" (e.g. the
    // und-Zsye NotoColorEmojiLegacy entry that precedes the real
    // NotoColorEmoji in file order); honoring the flag keeps those faces out
    // of every chain. An empty family is dropped by both callers.
    if (const std::string* ignore = familyElement.Attribute("ignore"))
    {
        if (ToLowerAscii(*ignore) == "true") return family;
    }
    if (const std::string* value = familyElement.Attribute("name")) family.name = *value;
    if (const std::string* value = familyElement.Attribute("lang")) family.lang = *value;
    // variant / OEM extras are intentionally not interpreted.
    for (const auto& child : familyElement.children)
    {
        if (child.name != "font") continue; // <axis> children are skipped here
        ParsedFont font;
        // The file name is the element text; A12+ wraps it in newline+indent,
        // and A16 interleaves <axis> children with it.
        font.path = ResolveFontFile(TrimWhitespace(child.text), primaryDirectory);
        if (font.path.empty()) continue;
        font.weight = ParseIntAttribute(child, "weight", 400);
        if (const std::string* style = child.Attribute("style"))
            font.style = (*style == "italic") ? 1 : 0;
        font.index = ParseIntAttribute(child, "index", 0);
        if (const std::string* fallbackFor = child.Attribute("fallbackFor"))
            font.fallbackFor = *fallbackFor;
        // postScriptName is never a language/face criterion: the CJK .ttc
        // entries all carry the JP face name regardless of index.
        family.fonts.push_back(std::move(font));
    }
    return family;
}

bool ExtractAlias(const XmlElement& element, ParsedAlias& out)
{
    if (const std::string* value = element.Attribute("name")) out.name = *value;
    if (const std::string* value = element.Attribute("to")) out.to = *value;
    out.weight = ParseIntAttribute(element, "weight", -1);
    return !out.name.empty() && !out.to.empty();
}

// Accepts both <familyset version="..."> (API 21+) and the version-less A15+
// root of font_fallback.xml.
bool ExtractFontConfig(const XmlElement& root, ParsedFontConfig& config)
{
    if (root.name != "familyset") return false;
    bool anyFamily = false;
    for (const auto& child : root.children)
    {
        if (child.name == "family")
        {
            ParsedFamily family = ExtractFamily(child, kSystemFontsDir);
            if (!family.fonts.empty())
            {
                config.families.push_back(std::move(family));
                anyFamily = true;
            }
        }
        else if (child.name == "alias")
        {
            ParsedAlias alias;
            if (ExtractAlias(child, alias)) config.aliases.push_back(std::move(alias));
        }
        // Unknown children (OEM extensions) are ignored.
    }
    return anyFamily;
}

bool LoadFontConfigFile(const char* path, ParsedFontConfig& config)
{
    std::string xml;
    if (!ReadFileToString(path, xml)) return false;
    XmlElement root;
    if (!ParseXmlDocument(xml, root)) return false;
    return ExtractFontConfig(root, config);
}

bool ConfigHasCjkFallback(const std::vector<ParsedFamily>& families)
{
    for (const auto& family : families)
    {
        if (family.lang.empty()) continue;
        for (const auto& tag : ParseLangList(family.lang))
            if (IsCjkLangTag(tag.first, tag.second)) return true;
    }
    return false;
}

// /product/etc/fonts_customization.xml overlay:
//   <fonts-modification version="1">
//     <family customizationType="new-named-family" name="...">   (API 30+)
//     <family customizationType="new-locale-family" lang="..."
//             operation="append|prepend|replace">                (API 35+)
// Font files are relative to /product/fonts/.
void ApplyFontsCustomization(const char* path, ParsedFontConfig& config)
{
    std::string xml;
    if (!ReadFileToString(path, xml)) return;
    XmlElement root;
    if (!ParseXmlDocument(xml, root)) return;
    if (root.name != "fonts-modification") return;

    for (const auto& child : root.children)
    {
        if (child.name == "alias")
        {
            ParsedAlias alias;
            if (ExtractAlias(child, alias)) config.aliases.push_back(std::move(alias));
            continue;
        }
        if (child.name != "family") continue;
        const std::string* type = child.Attribute("customizationType");
        if (!type) continue;
        ParsedFamily family = ExtractFamily(child, "/product/fonts/");
        if (family.fonts.empty()) continue;

        if (*type == "new-named-family")
        {
            if (!family.name.empty()) config.families.push_back(std::move(family));
        }
        else if (*type == "new-locale-family")
        {
            if (family.lang.empty()) continue;
            const std::string* operationAttr = child.Attribute("operation");
            const std::string operation = operationAttr ? *operationAttr : std::string("append");

            size_t first = config.families.size();
            size_t last = config.families.size();
            bool anyMatch = false;
            for (size_t i = 0; i < config.families.size(); ++i)
            {
                if (config.families[i].lang != family.lang) continue;
                if (!anyMatch) first = i;
                last = i;
                anyMatch = true;
            }

            if (operation == "replace" && anyMatch)
            {
                const std::string lang = family.lang;
                config.families.erase(
                    std::remove_if(config.families.begin(), config.families.end(),
                        [&lang](const ParsedFamily& existing) { return existing.lang == lang; }),
                    config.families.end());
                const size_t at = std::min(first, config.families.size());
                config.families.insert(
                    config.families.begin() + static_cast<ptrdiff_t>(at), std::move(family));
            }
            else if (operation == "prepend" && anyMatch)
            {
                config.families.insert(
                    config.families.begin() + static_cast<ptrdiff_t>(first), std::move(family));
            }
            else if (anyMatch) // append (also the default for unknown operations)
            {
                config.families.insert(
                    config.families.begin() + static_cast<ptrdiff_t>(last + 1), std::move(family));
            }
            else
            {
                config.families.push_back(std::move(family));
            }
        }
        // Unknown customizationType values are ignored.
    }
}

ParsedFontConfig LoadSystemFontConfig()
{
    static const char* const kMainConfigPaths[] = {
        "/system/etc/fonts.xml",
        "/system/etc/font_fallback.xml",
        "/system/etc/fonts_default.xml",
    };

    ParsedFontConfig config;
    for (const char* path : kMainConfigPaths)
    {
        if (ConfigHasCjkFallback(config.families)) break;
        LoadFontConfigFile(path, config);
    }
    if (!config.families.empty())
        ApplyFontsCustomization("/product/etc/fonts_customization.xml", config);
    return config;
}

// ----------------------------------------------------------------------------
// Script inference and directory-scan fallback
// ----------------------------------------------------------------------------

void AddLangCandidate(std::vector<std::pair<std::string, std::string>>& candidates,
                      const char* language, const char* script)
{
    for (const auto& existing : candidates)
        if (existing.first == language && existing.second == script) return;
    candidates.emplace_back(language, script);
}

// Maps cluster codepoints to (language, script) fallback candidates. The
// script half also matches AOSP's script-only families (lang="und-Arab" etc.).
// Uncommon scripts are deliberately not inferred — they are reached through
// the ordered remainder of the fallback chain.
std::vector<std::pair<std::string, std::string>> InferLangCandidates(
    const std::vector<uint32_t>& codepoints)
{
    std::vector<std::pair<std::string, std::string>> candidates;
    for (const uint32_t cp : codepoints)
    {
        if ((cp >= 0x4E00 && cp <= 0x9FFF) || (cp >= 0x3400 && cp <= 0x4DBF) ||
            (cp >= 0xF900 && cp <= 0xFAFF) || (cp >= 0x20000 && cp <= 0x3FFFF))
        {
            AddLangCandidate(candidates, "zh", "");
            AddLangCandidate(candidates, "ja", "");
        }
        else if ((cp >= 0x3000 && cp <= 0x303F) || (cp >= 0xFE30 && cp <= 0xFE4F) ||
                 (cp >= 0xFF00 && cp <= 0xFFEF))
        {
            // CJK punctuation, vertical forms, and fullwidth/halfwidth forms
            // are carried by the CJK faces, not the Latin default fonts.
            AddLangCandidate(candidates, "zh", "");
            AddLangCandidate(candidates, "ja", "");
        }
        else if ((cp >= 0x3040 && cp <= 0x30FF) || (cp >= 0x31F0 && cp <= 0x31FF))
        {
            AddLangCandidate(candidates, "ja", "");
        }
        else if ((cp >= 0xAC00 && cp <= 0xD7AF) || (cp >= 0x1100 && cp <= 0x11FF) ||
                 (cp >= 0x3130 && cp <= 0x318F))
        {
            AddLangCandidate(candidates, "ko", "");
        }
        else if (cp >= 0x3100 && cp <= 0x312F)
        {
            AddLangCandidate(candidates, "zh", "Hant");
        }
        else if ((cp >= 0x2190 && cp <= 0x25FF) || (cp >= 0x2600 && cp <= 0x27BF) ||
                 (cp >= 0x2B00 && cp <= 0x2BFF) || (cp >= 0x1F000 && cp <= 0x1FAFF) ||
                 cp == 0xFE0F)
        {
            // Arrows, geometric shapes, misc symbols, dingbats, and the emoji
            // planes live in the script-only und-Zsye (emoji, preferred) and
            // und-Zsym (text symbols) families of fonts.xml.
            AddLangCandidate(candidates, "und", "Zsye");
            AddLangCandidate(candidates, "und", "Zsym");
        }
        else if ((cp >= 0x0600 && cp <= 0x06FF) || (cp >= 0x0750 && cp <= 0x077F))
        {
            AddLangCandidate(candidates, "ar", "Arab");
        }
        else if (cp >= 0x0590 && cp <= 0x05FF)
        {
            AddLangCandidate(candidates, "he", "Hebr");
        }
        else if (cp >= 0x0E00 && cp <= 0x0E7F)
        {
            AddLangCandidate(candidates, "th", "Thai");
        }
        else if (cp >= 0x0900 && cp <= 0x097F)
        {
            AddLangCandidate(candidates, "hi", "Deva");
        }
    }
    return candidates;
}

// NotoSansCJK/NotoSerifCJK/SECCJK .ttc face layout (byte-level verified;
// Samsung replicates it): 0=ja, 1=ko, 2=Hans, 3=Hant.
int CjkCollectionFaceIndex(const std::string& language, const std::string& script)
{
    if (language == "ja") return 0;
    if (language == "ko") return 1;
    if (script == "Hant") return 3;
    return 2;
}

// Known CJK file-name prefixes across OEM ROMs, ordered by hit rate.
const char* const kCjkFilePrefixes[] = {
    "NotoSansCJK",
    "SECCJK",                  // Samsung
    "MiSansVF",                // Xiaomi HyperOS
    "MiSansTCVF",
    "SysSans-Hans",            // ColorOS
    "SysSans-Hant",
    "SysFont",
    "DroidSansFallbackMonster",// vivo
    "DroidSansFallbackBBK",
    "HYQiHei",
    "HONORSansVFCN",           // Honor
    "HONORSansVFTC",
    "DroidSansChinese",        // Huawei family
    "HwHant",
    "HwChinese",
    "HarmonyOS_Sans_SC",
    "HarmonyOS_Sans_TC",
    "MiLanProVF",              // legacy MIUI
    "OPPOSans",                // legacy ColorOS
    "SourceHanSans",
    "NotoSansSC",              // Android 6
    "NotoSansTC",
    "NotoSansHans",            // Android 5
    "NotoSansHant",
    "DroidSansFallback",       // 4.x / low-end
    "NotoSerifCJK",
};

// Last-resort discovery when no XML configuration produced CJK coverage:
// enumerate the font directories for well-known CJK file names. Coverage is
// decided later by the layout's per-cluster cmap check, so candidates are
// returned optimistically.
std::vector<FontProvider::FontMatch> ScanFontDirectoriesForCjk(
    const std::string& language, const std::string& script)
{
    struct Candidate {
        int prefixRank;
        int nameBias;
        int directoryRank;
        std::string fileName;
        FontProvider::FontMatch match;
    };
    std::vector<Candidate> candidates;

    int directoryRank = 0;
    for (const char* directoryPath : kFontDirectories)
    {
        DIR* directory = opendir(directoryPath);
        if (!directory) { ++directoryRank; continue; }
        while (dirent* entry = readdir(directory))
        {
            const std::string fileName = entry->d_name;
            if (fileName.empty() || fileName[0] == '.') continue;
            if (!EndsWithIgnoreCase(fileName, ".ttf") && !EndsWithIgnoreCase(fileName, ".otf") &&
                !EndsWithIgnoreCase(fileName, ".ttc") && !EndsWithIgnoreCase(fileName, ".otc"))
                continue;
            int prefixRank = -1;
            int rank = 0;
            for (const char* prefix : kCjkFilePrefixes)
            {
                if (fileName.compare(0, std::strlen(prefix), prefix) == 0)
                {
                    prefixRank = rank;
                    break;
                }
                ++rank;
            }
            if (prefixRank < 0) continue;

            Candidate candidate;
            candidate.prefixRank = prefixRank;
            candidate.nameBias = fileName.find("Regular") != std::string::npos ? 0 : 1;
            candidate.directoryRank = directoryRank;
            candidate.fileName = fileName;
            candidate.match.path = std::string(directoryPath) + fileName;
            const char* prefix = kCjkFilePrefixes[prefixRank];
            const bool localeIndexedCollection =
                (std::strcmp(prefix, "NotoSansCJK") == 0 ||
                 std::strcmp(prefix, "NotoSerifCJK") == 0 ||
                 std::strcmp(prefix, "SECCJK") == 0) &&
                EndsWithIgnoreCase(fileName, ".ttc");
            candidate.match.faceIndex =
                localeIndexedCollection ? CjkCollectionFaceIndex(language, script) : 0;
            candidate.match.family = fileName.substr(0, fileName.find_last_of('.'));
            candidates.push_back(std::move(candidate));
        }
        closedir(directory);
        ++directoryRank;
    }

    std::sort(candidates.begin(), candidates.end(), [](const Candidate& a, const Candidate& b) {
        if (a.prefixRank != b.prefixRank) return a.prefixRank < b.prefixRank;
        if (a.nameBias != b.nameBias) return a.nameBias < b.nameBias;
        if (a.directoryRank != b.directoryRank) return a.directoryRank < b.directoryRank;
        return a.fileName < b.fileName;
    });

    std::vector<FontProvider::FontMatch> matches;
    for (auto& candidate : candidates)
    {
        if (matches.size() >= kMaxScanCandidates) break;
        matches.push_back(std::move(candidate.match));
    }
    return matches;
}

} // namespace

// ============================================================================
// FontProviderAndroid
// ============================================================================

FontProviderAndroid::FontProviderAndroid() = default;

FontProviderAndroid::~FontProviderAndroid() = default;

void FontProviderAndroid::EnsureParsed() const
{
    std::call_once(parseOnce_, [this] { BuildFontTables(); });
}

void FontProviderAndroid::BuildFontTables() const
{
    // call_once retries after an exception; drop any half-built state first.
    families_.clear();
    aliases_.clear();
    scanFallbacks_.clear();

    std::string locale = ReadSystemProperty("persist.sys.locale");
    if (locale.empty()) locale = ReadSystemProperty("ro.product.locale");
    if (locale.empty()) locale = "en-US";
    ParseLocaleTag(std::move(locale), localeLanguage_, localeScript_);

    ParsedFontConfig config = LoadSystemFontConfig();

    families_.reserve(config.families.size());
    for (auto& parsedFamily : config.families)
    {
        FontFamily family;
        family.name = std::move(parsedFamily.name);
        family.lang = std::move(parsedFamily.lang);
        for (auto& tag : ParseLangList(family.lang))
            family.langTags.push_back({std::move(tag.first), std::move(tag.second)});
        for (auto& font : parsedFamily.fonts)
        {
            // fallbackFor entries extend a *named* family's private chain
            // (e.g. serif → NotoSerifCJK) and must not enter the default
            // chain; unknown values (Honor's "ChineseBigSizeL") drop out the
            // same way, silently.
            if (!font.fallbackFor.empty()) continue;
            family.fonts.push_back({std::move(font.path), font.weight, font.style, font.index});
        }
        if (family.fonts.empty()) continue;
        families_.push_back(std::move(family));
    }
    for (const auto& alias : config.aliases)
        aliases_.emplace(ToLowerAscii(alias.name), FamilyAlias{ToLowerAscii(alias.to), alias.weight});

    const auto isCjkFamily = [](const FontFamily& family) {
        for (const auto& tag : family.langTags)
            if (IsCjkLangTag(tag.language, tag.script)) return true;
        return false;
    };
    const auto hasCjkFamily = [this, &isCjkFamily] {
        for (const auto& family : families_)
            if (isCjkFamily(family)) return true;
        return false;
    };

    if (!hasCjkFamily())
    {
        if (families_.empty())
        {
            // No parseable configuration at all: probe the classic AOSP file
            // names so named lookup keeps working with real weights.
            const auto addProbedFont = [this](const char* familyName, const char* file,
                                              int weight, int style) {
                std::string path = std::string(kSystemFontsDir) + file;
                if (!FileExists(path)) return;
                for (auto& family : families_)
                {
                    if (family.name == familyName)
                    {
                        family.fonts.push_back({std::move(path), weight, style, 0});
                        return;
                    }
                }
                FontFamily family;
                family.name = familyName;
                family.fonts.push_back({std::move(path), weight, style, 0});
                families_.push_back(std::move(family));
            };
            addProbedFont("Roboto", "Roboto-Thin.ttf", 100, 0);
            addProbedFont("Roboto", "Roboto-ThinItalic.ttf", 100, 1);
            addProbedFont("Roboto", "Roboto-Light.ttf", 300, 0);
            addProbedFont("Roboto", "Roboto-LightItalic.ttf", 300, 1);
            addProbedFont("Roboto", "Roboto-Regular.ttf", 400, 0);
            addProbedFont("Roboto", "Roboto-Italic.ttf", 400, 1);
            addProbedFont("Roboto", "Roboto-Medium.ttf", 500, 0);
            addProbedFont("Roboto", "Roboto-MediumItalic.ttf", 500, 1);
            addProbedFont("Roboto", "Roboto-Bold.ttf", 700, 0);
            addProbedFont("Roboto", "Roboto-BoldItalic.ttf", 700, 1);
            addProbedFont("Roboto", "Roboto-Black.ttf", 900, 0);
            addProbedFont("Roboto", "Roboto-BlackItalic.ttf", 900, 1);
            addProbedFont("Noto Serif", "NotoSerif-Regular.ttf", 400, 0);
            addProbedFont("Noto Serif", "NotoSerif-Bold.ttf", 700, 0);
            addProbedFont("Noto Serif", "NotoSerif-Italic.ttf", 400, 1);
            addProbedFont("Noto Serif", "NotoSerif-BoldItalic.ttf", 700, 1);
            addProbedFont("Droid Sans Mono", "DroidSansMono.ttf", 400, 0);
            aliases_.emplace("sans-serif", FamilyAlias{"roboto", -1});
            aliases_.emplace("serif", FamilyAlias{"noto serif", -1});
            aliases_.emplace("monospace", FamilyAlias{"droid sans mono", -1});
        }

        scanFallbacks_ = ScanFontDirectoriesForCjk(localeLanguage_, localeScript_);
        if (!scanFallbacks_.empty())
        {
            FontFamily family;
            family.name = "Noto Sans CJK";
            for (const auto& match : scanFallbacks_)
            {
                family.fonts.push_back({match.path, 400, 0, match.faceIndex});
                if (family.fonts.size() >= 4) break;
            }
            families_.push_back(std::move(family));
        }
    }

    // Keep "Noto Sans CJK" resolvable by name — the historical public FindFont
    // contract — pointing at the best locale-ranked CJK fallback family.
    int aliasWeight = -1;
    if (!FindNamedFamily("noto sans cjk", aliasWeight))
    {
        const FontFamily* bestCjk = nullptr;
        int bestScore = 0;
        for (const auto& family : families_)
        {
            if (!isCjkFamily(family)) continue;
            int score = 1;
            for (const auto& tag : family.langTags)
            {
                if (tag.language != localeLanguage_) continue;
                score = (!tag.script.empty() && tag.script == localeScript_) ? 3 : 2;
                if (score == 3) break;
            }
            if (score > bestScore)
            {
                bestScore = score;
                bestCjk = &family;
            }
        }
        if (bestCjk)
        {
            FontFamily named;
            named.name = "Noto Sans CJK";
            named.fonts = bestCjk->fonts;
            families_.push_back(std::move(named));
        }
    }

    fallbackFamilyName_ = L"Noto Sans CJK";
    for (const auto& family : families_)
    {
        if (family.name.empty() || !isCjkFamily(family)) continue;
        std::wstring name = Utf8ToWide(family.name);
        if (!name.empty()) fallbackFamilyName_ = std::move(name);
        break;
    }
}

const FontProviderAndroid::FontFamily* FontProviderAndroid::FindNamedFamily(
    const std::string& lowerName, int& aliasWeight) const
{
    aliasWeight = -1;
    if (lowerName.empty()) return nullptr;
    std::string target = lowerName;
    for (int hop = 0; hop < 4; ++hop) // alias chains are short; bound cycles
    {
        for (const auto& family : families_)
        {
            if (!family.name.empty() && ToLowerAscii(family.name) == target)
                return &family;
        }
        const auto alias = aliases_.find(target);
        if (alias == aliases_.end()) return nullptr;
        if (alias->second.weight >= 0) aliasWeight = alias->second.weight;
        target = alias->second.target;
    }
    return nullptr;
}

const FontProviderAndroid::FontEntry* FontProviderAndroid::SelectEntry(
    const FontFamily& family, int32_t weight, int32_t style, int restrictWeight)
{
    const int wantItalic = (style == 1 || style == 2) ? 1 : 0;
    const FontEntry* best = nullptr;
    int bestScore = INT_MAX;
    for (const auto& entry : family.fonts)
    {
        if (restrictWeight >= 0 && entry.weight != restrictWeight) continue;
        const int score = std::abs(entry.weight - static_cast<int>(weight)) +
                          (entry.style != wantItalic ? 1000 : 0);
        if (score < bestScore)
        {
            bestScore = score;
            best = &entry;
        }
    }
    if (!best && restrictWeight >= 0)
        return SelectEntry(family, weight, style, -1); // alias weight had no exact entry
    return best;
}

bool FontProviderAndroid::FindFontExact(
    const std::string& lowerFamilyName,
    int32_t weight,
    int32_t style,
    std::string& outPath,
    int& outFaceIndex) const
{
    int aliasWeight = -1;
    const FontFamily* family = FindNamedFamily(lowerFamilyName, aliasWeight);
    if (!family) return false;
    const FontEntry* entry = SelectEntry(*family, weight, style, aliasWeight);
    if (!entry) return false;
    outPath = entry->path;
    outFaceIndex = entry->faceIndex;
    return true;
}

bool FontProviderAndroid::FindFont(
    const wchar_t* familyName,
    int32_t weight,
    int32_t style,
    std::string& outPath,
    int& outFaceIndex)
{
    EnsureParsed();

    if (!familyName) return false;

    int aliasWeight = -1;
    const FontFamily* family = FindNamedFamily(ToLowerAscii(WideToUtf8(familyName)), aliasWeight);
    if (!family)
    {
        // Unknown family: keep the historical contract of resolving to the
        // device default (UI code passes Windows family names). The first
        // family of the configuration *is* the platform default.
        aliasWeight = -1;
        if (!families_.empty())
        {
            family = &families_.front();
        }
        else
        {
            int ignored = -1;
            family = FindNamedFamily("roboto", ignored);
        }
    }
    if (family)
    {
        if (const FontEntry* entry = SelectEntry(*family, weight, style, aliasWeight))
        {
            outPath = entry->path;
            outFaceIndex = entry->faceIndex;
            return true;
        }
    }

    // Last resort: the one file every Android image ships.
    outPath = "/system/fonts/Roboto-Regular.ttf";
    outFaceIndex = 0;
    return FileExists(outPath);
}

std::vector<FontProvider::FontMatch> FontProviderAndroid::FindFallbackFonts(
    const std::vector<uint32_t>& codepoints,
    const wchar_t* preferredFamily,
    int32_t weight,
    int32_t style)
{
    EnsureParsed();

    // The candidate list is unbounded on purpose (dedup by path#faceIndex
    // only): see the kMaxScanCandidates comment — a cap let the ~30 leading
    // script families crowd the CJK families out of the chain entirely.
    std::vector<FontMatch> result;
    const auto pushMatch = [&result](const std::string& path, int faceIndex,
                                     const std::string& familyLabel) {
        for (const auto& existing : result)
            if (existing.path == path && existing.faceIndex == faceIndex) return;
        FontMatch match;
        match.path = path;
        match.faceIndex = faceIndex;
        match.family = familyLabel;
        result.push_back(std::move(match));
    };

    // 1) Whatever the caller's family resolves to exactly. No default-family
    //    masking here: a miss must fall through to real fallback data (the
    //    historical silent Roboto substitution is what blanked CJK).
    if (preferredFamily && *preferredFamily)
    {
        const std::string utf8Preferred = WideToUtf8(preferredFamily);
        std::string path;
        int faceIndex = 0;
        if (FindFontExact(ToLowerAscii(utf8Preferred), weight, style, path, faceIndex))
            pushMatch(path, faceIndex, utf8Preferred);
    }

    const auto tagScore = [](const LangTag& tag, const std::string& language,
                             const std::string& script) {
        if (tag.language == "und") // script-only family (lang="und-Arab" etc.)
            return (!script.empty() && tag.script == script) ? 2 : 0;
        if (language.empty() || tag.language != language) return 0;
        if (!tag.script.empty() && !script.empty())
            return tag.script == script ? 2 : 0;
        return 1; // bare-language tag (Huawei lang="zh") or script-less request
    };
    const auto familyScore = [&tagScore](const FontFamily& family, const std::string& language,
                                         const std::string& script) {
        int best = 0;
        for (const auto& tag : family.langTags)
        {
            const int score = tagScore(tag, language, script);
            if (score > best) best = score;
        }
        return best;
    };

    std::vector<char> used(families_.size(), 0);
    const auto pushFamilies = [&](auto&& qualifies) {
        for (size_t i = 0; i < families_.size(); ++i)
        {
            if (used[i]) continue;
            const FontFamily& family = families_[i];
            if (!qualifies(i, family)) continue;
            used[i] = 1;
            if (const FontEntry* entry = SelectEntry(family, weight, style, -1))
                pushMatch(entry->path, entry->faceIndex,
                          !family.name.empty() ? family.name : family.lang);
        }
    };

    // 2) Families matching the system locale; a full language+script match
    //    outranks language-only so a zh-TW device gets the Hant faces (ttc
    //    index 3) before anything else.
    pushFamilies([&](size_t, const FontFamily& family) {
        return familyScore(family, localeLanguage_, localeScript_) >= 2;
    });
    pushFamilies([&](size_t, const FontFamily& family) {
        return familyScore(family, localeLanguage_, localeScript_) >= 1;
    });

    // 3) Families matching scripts inferred from the cluster itself, so e.g.
    //    kana on an en-US device still reaches the ja faces.
    for (const auto& candidate : InferLangCandidates(codepoints))
    {
        pushFamilies([&](size_t, const FontFamily& family) {
            return familyScore(family, candidate.first, candidate.second) >= 1;
        });
    }

    // 4) The device default family plus every remaining unnamed fallback
    //    family, in file order (mirrors the platform chain; named non-default
    //    families are only reachable explicitly). This tier appends the full
    //    remainder — no slot budget — so late chain entries (the CJK families
    //    sit ~115 deep in stock fonts.xml) always stay reachable.
    pushFamilies([&](size_t index, const FontFamily& family) {
        return index == 0 || family.name.empty();
    });

    // Directory-scan candidates exist only when no XML configuration produced
    // CJK coverage; they carry locale-selected .ttc face indices.
    for (const auto& match : scanFallbacks_)
        pushMatch(match.path, match.faceIndex, match.family);

    return result;
}

const wchar_t* FontProviderAndroid::GetDefaultFontFamily() const
{
    return L"Roboto";
}

const wchar_t* FontProviderAndroid::GetFallbackFontFamily() const
{
    // First named CJK family from the parsed configuration, else the classic
    // AOSP name (registered as a synthetic family so FindFont resolves it).
    EnsureParsed();
    return fallbackFamilyName_.c_str();
}

} // namespace jalium

#endif // __ANDROID__

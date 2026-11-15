#include "jalium_unicode_casing.h"
#include <cstdio>
#include <iostream>
#include <stdexcept>
#include <sstream>

namespace {
using namespace jalium::unicode_casing;
int checks = 0;
void Check(bool value, const char* message) {
    if (!value) throw std::runtime_error(message);
    ++checks;
}
void Map(std::u16string_view source, std::u16string_view expected, Case mode,
    std::string_view language = "en", size_t start = 0, size_t length = std::u16string::npos) {
    Context context(source);
    Check(context.Transform(start, length == std::u16string::npos ? source.size() - start : length, mode, language) == expected,
        "Unicode contextual mapping differs from explicit expected text");
}
void Cases() {
    auto fragment = [](std::u16string_view source, uint32_t flags, std::u16string_view expected,
        Case mode = Case::Lower, std::string_view language = "el") {
        Context context(source, flags);
        Check(context.Transform(0, source.size(), mode, language) == expected, "Fragment lost its outer Unicode context");
    };
    fragment(u"Σ", 1, u"ς"); fragment(u"Σ", 3, u"σ"); fragment(u"Σ", 2, u"σ");
    fragment(u" Σ", 1, u" σ"); fragment(u"Σ ", 3, u"ς ");
    fragment(u"'Σ", 1, u"'ς"); fragment(u"Σ'", 3, u"σ'");
    fragment(u"\u0307", 8, u"", Case::Upper, "lt");
    fragment(u"\u0307", 4, u"", Case::Lower, "tr");
    fragment(u"I", 16, u"i", Case::Lower, "tr");
    fragment(u"I", 32, u"i\u0307", Case::Lower, "lt");
    fragment(u"I ", 32, u"i ", Case::Lower, "lt");
    bool invalidBoundary = false;
    try { Context invalid(u"Σ", 64); } catch (const std::invalid_argument&) { invalidBoundary = true; }
    Check(invalidBoundary, "Unknown fragment context accepted");
    Map(u"Straße ﬃ 𐐨", u"STRASSE FFI 𐐀", Case::Upper);
    Map(u"İ ẞ 𐐀", u"i\u0307 ß 𐐨", Case::Lower);
    Map(u"ΟΣ", u"ος", Case::Lower);
    Map(u"Σ", u"σ", Case::Lower);
    Map(u"ΟΣΑ", u"οσα", Case::Lower);
    Map(u"Ο\u0301Σ\u0301", u"ο\u0301ς\u0301", Case::Lower);
    Map(u"Ο'Σ", u"ο'ς", Case::Lower);
    Map(u"ΟΣ'Α", u"οσ'α", Case::Lower);
    Map(u"ΟΣ. Σ", u"ος. σ", Case::Lower);
    Map(u"ΟΣ\nΑ", u"ος\nα", Case::Lower);
    Map(u"ⅠΣ", u"ⅰς", Case::Lower); // Cased Nl, not an upper/lower letter category.
    Map(u"Σⅰ", u"σⅰ", Case::Lower);
    Map(u"Ο\u0345Σ", u"ο\u0345ς", Case::Lower); // Cased AND Case_Ignorable.
    Map(u"Ο\u0345ΣΑ", u"ο\u0345σα", Case::Lower);
    Map(u"ΟΣ", u"ς", Case::Lower, "el", 1, 1); // Context survives a style/run boundary.
    Map(u"ΟΣΑ", u"σ", Case::Lower, "el", 1, 1);
    Map(u"ΟΣ\u0301", u"ς", Case::Lower, "el", 1, 1);
    Map(u"iı Iİ", u"İI Iİ", Case::Upper, "TR-latN-TR");
    Map(u"IİI\u0307", u"ıii", Case::Lower, "az-Latn");
    Map(u"i", u"I", Case::Upper, "az-Cyrl");
    Map(u"I\u0323\u0307", u"i\u0323", Case::Lower, "tr");
    Map(u"I\u0301\u0307", u"ı\u0301\u0307", Case::Lower, "tr");
    Map(u"I\u0307", u"", Case::Lower, "tr", 1, 1);
    Map(u"I\u0323\u0307", u"i", Case::Lower, "tr", 0, 1);
    Map(u"i\u0307j\u0307", u"IJ", Case::Upper, "lt");
    Map(u"i\u0323\u0307", u"I\u0323", Case::Upper, "lt");
    Map(u"i\u0301\u0307", u"I\u0301\u0307", Case::Upper, "lt");
    Map(u"i\u0307", u"", Case::Upper, "lt", 1, 1);
    Map(u"I\u0301 J\u0323\u0301 Į\u0300", u"i\u0307\u0301 j\u0307\u0323\u0301 į\u0307\u0300", Case::Lower, "lt");
    Map(u"Ì Í Ĩ", u"i\u0307\u0300 i\u0307\u0301 i\u0307\u0303", Case::Lower, "lt");
    Map(u"I\u0301", u"i\u0307", Case::Lower, "lt", 0, 1);
    Map(u"ⅰⓐ\u0345", u"ⅠⒶΙ", Case::Upper);
    Map(u"ⅠⒶ", u"ⅰⓐ", Case::Lower);
    Map(u"ǅ", u"ǆ", Case::Lower);
    Map(u"ǅ", u"Ǆ", Case::Upper);
    Map(u"中文 012 😀", u"中文 012 😀", Case::Upper);
    Map(u"i", u"I", Case::Upper, "unknown-language");
    Map(std::u16string{0xD800, 'a', 0xDC00}, std::u16string{0xD800, 'A', 0xDC00}, Case::Upper);
    for (auto value : {0x2170u, 0x24D0u, 0x0345u, 0x1D2Cu}) {
        Check(IsLowercase(value), "Unicode Lowercase outside Ll was omitted");
        Check(IsCased(value), "Unicode Cased classification was omitted");
    }
    for (auto value : {0x2160u, 0x24B6u, 0x01C5u}) {
        Check(IsUppercase(value), "Unicode upper/titlecase classification was omitted");
        Check(IsCased(value), "Unicode upper/titlecase is not Cased");
    }
    for (auto value : {0x4E2Du, 0x1F600u, 0x30u, 0x0301u, 0xD800u})
        Check(!IsCased(value) && !IsLowercase(value) && !IsUppercase(value), "Caseless character was classified as cased");
    // A long combining sequence exercises the linear context precomputation.
    std::u16string longText = u"Ο"; longText.append(40000, 0x0301); longText.push_back(0x03A3);
    Map(longText, u"ς", Case::Lower, "el", longText.size() - 1, 1);
    Context supplementary(u"𐐨");
    for (auto range : {std::pair<size_t, size_t>{0, 1}, {1, 1}}) {
        bool rejected = false;
        try { supplementary.Transform(range.first, range.second, Case::Upper, "en"); }
        catch (const std::invalid_argument&) { rejected = true; }
        Check(rejected, "Casing accepted a range inside a surrogate pair");
    }
}

std::u16string Decode(std::string_view input) {
    std::istringstream stream{std::string(input)}; uint32_t value; std::u16string result;
    while (stream >> std::hex >> value) {
        if (value > 0x10FFFF) throw std::runtime_error("Invalid oracle codepoint");
        if (value <= 0xFFFF) result.push_back(static_cast<char16_t>(value));
        else { value -= 0x10000; result.push_back(static_cast<char16_t>(0xD800 + (value >> 10))); result.push_back(static_cast<char16_t>(0xDC00 + (value & 0x3FF))); }
    }
    return result;
}
void Oracle() {
    // Independent UCD-derived vectors arrive over stdin; no product table is
    // consulted to choose expected results in this mode.
    std::string line;
    while (std::getline(std::cin, line)) {
        auto first = line.find(';'), second = line.find(';', first + 1);
        if (first == std::string::npos || second == std::string::npos) throw std::runtime_error("Malformed oracle record");
        auto mode = line[0] == 'U' ? Case::Upper : Case::Lower;
        auto source = Decode(std::string_view(line).substr(first + 1, second - first - 1));
        if (line[0] == 'P') {
            uint32_t value = 0;
            std::istringstream point(line.substr(first + 1, second - first - 1)); point >> std::hex >> value;
            bool lower, upper, cased; std::istringstream flags(line.substr(second + 1)); flags >> lower >> upper >> cased;
            Check(IsLowercase(value) == lower && IsUppercase(value) == upper && IsCased(value) == cased,
                "Native casing property differs from the independent UCD oracle");
            continue;
        }
        auto expected = Decode(std::string_view(line).substr(second + 1));
        Context context(source);
        Check(context.Transform(0, source.size(), mode, "en") == expected, "Native case mapping differs from the independent UCD oracle");
    }
}
}
int main(int argc, char** argv) {
    try {
        if (argc == 2 && std::string_view(argv[1]) == "--oracle") Oracle(); else Cases();
        std::printf("Portable Unicode casing: %d checks passed\n", checks); return 0;
    } catch (const std::exception& error) { std::fprintf(stderr, "Unicode casing failed after %d checks: %s\n", checks, error.what()); return 1; }
}

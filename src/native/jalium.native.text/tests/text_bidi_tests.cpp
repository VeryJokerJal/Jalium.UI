#include "text_bidi.h"
#include <iostream>
#include <string>
#include <vector>

namespace {
int failures = 0, cases = 0;
void Check(bool condition, const char* message) { ++cases; if (!condition) { ++failures; std::cerr << "FAIL: " << message << '\n'; } }
std::vector<jalium::ShapedGlyph> Glyphs(const std::wstring& source, const jalium::TextBidi& bidi)
{
    std::vector<jalium::ShapedGlyph> glyphs;
    for (uint32_t i = 0; i < source.size(); ++i) {
        if (jalium::TextBidi::IsFormattingControl(uint32_t(source[i]))) continue;
        jalium::ShapedGlyph glyph{}; glyph.glyphIndex = uint32_t(bidi.MirroredText()[i]);
        glyph.cluster = i; glyph.clusterEnd = i + 1; glyph.advanceX = 1; glyphs.push_back(glyph);
    }
    return glyphs;
}
void Order(const std::wstring& text, int direction, const std::vector<uint32_t>& expected, const char* message)
{
    jalium::TextBidi bidi(text.data(), uint32_t(text.size()), direction);
    auto glyphs = Glyphs(text, bidi); bidi.Reorder(glyphs, 0, uint32_t(text.size()));
    std::vector<uint32_t> actual; for (const auto& glyph : glyphs) actual.push_back(glyph.cluster);
    Check(actual == expected, message);
    if (actual != expected) { for (auto value : actual) std::cerr << value << ','; std::cerr << '\n'; }
}
}
int main()
{
    Order(L"مرحبا CSS", -1, {6,7,8,5,4,3,2,1,0}, "first strong RTL preserves Latin word order");
    Order(L"مرحبا CSS", 0, {4,3,2,1,0,5,6,7,8}, "explicit LTR paragraph differs from first-strong RTL");
    Order(L"مرحبا CSS", 1, {6,7,8,5,4,3,2,1,0}, "explicit RTL uses visual directional runs");
    Order(L"אבג 123", 0, {4,5,6,3,2,1,0}, "European digits keep LTR order within Hebrew");
    Order(L"CSS", 1, {0,1,2}, "RTL paragraph never reverses a Latin-only word");
    Order(L"A\u2067אב\u2069B", 0, {0,3,2,5}, "RTL isolate preserves outside LTR ordering");
    Order(L"\u202ECSS\u202C", 0, {3,2,1}, "RLO overrides implicit Latin direction");
    Order(L"א\u05B0ב", 1, {2,0,1}, "L3 keeps a nonspacing mark with its base");
    {
        const std::wstring text = L"אב (CSS)"; jalium::TextBidi bidi(text.data(), text.size(), 1);
        auto glyphs = Glyphs(text,bidi); bidi.Reorder(glyphs,0,text.size());
        std::wstring paint; for(const auto& glyph:glyphs)paint.push_back(wchar_t(glyph.glyphIndex));
        Check(paint == L"(CSS) בא", "paired brackets are mirrored after level resolution");
    }
    {
        const std::wstring text = L"א CSS abc"; jalium::TextBidi bidi(text.data(),text.size(),1);
        auto all=Glyphs(text,bidi); std::vector<jalium::ShapedGlyph> second(all.begin()+2,all.end());
        bidi.Reorder(second,2,text.size());
        Check(second.front().cluster==2 && second.back().cluster==8, "wrapped LTR run retains original paragraph context");
        Check(second.front().bidiLevel==2 && second.back().bidiLevel==2, "wrapped line keeps resolved paragraph level instead of redetecting direction");
        auto first=std::vector<jalium::ShapedGlyph>(all.begin(),all.begin()+2);bidi.Reorder(first,0,2);
        Check(first.front().cluster==1 && first.back().cluster==0, "line-end whitespace uses L1 paragraph-level reset");
    }
    {
        const std::wstring text=L"אב\nCSS";jalium::TextBidi bidi(text.data(),text.size());auto all=Glyphs(text,bidi);
        std::vector<jalium::ShapedGlyph> second(all.begin()+3,all.end());bidi.Reorder(second,3,text.size());
        Check(second.front().cluster==3&&second.back().cluster==5,"new paragraph derives its own base direction");
    }
    std::cout<<"{\"count\":"<<cases<<",\"failed\":"<<failures<<"}\n";return failures?1:0;
}

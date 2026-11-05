#include "font_provider.h"
#include "text_engine.h"
#include "text_layout.h"
#include "jalium_text_glyph_ranges.h"
#include "jalium_api.h"
#include <cmath>
#include <iostream>
#include <memory>
#include <string>

namespace {
int failures=0,cases=0;
void Check(bool value,const char* label){++cases;if(!value){++failures;std::cerr<<"FAIL: "<<label<<'\n';}}
bool Near(float a,float b){return std::abs(a-b)<.005f;}
JaliumTextHitTestResult Caret(jalium::JaliumTextFormat& format,const std::wstring& text,uint32_t index,bool trailing,float width=800)
{
    JaliumTextHitTestResult result{};Check(format.HitTestTextPosition(text.c_str(),text.size(),width,600,index,trailing,&result)==JALIUM_OK,"source caret query succeeds");return result;
}
void RoundTrip(jalium::JaliumTextFormat& format,const std::wstring& text,float width=800)
{
    for(uint32_t index=0;index<text.size();++index){
        if(text[index]==L'\r'||text[index]==L'\n')continue;
        auto leading=Caret(format,text,index,false,width),trailing=Caret(format,text,index,true,width);
        if(Near(leading.caretX,trailing.caretX))continue;
        for(bool end:{false,true}){
            const float x=end?.25f*leading.caretX+.75f*trailing.caretX:.75f*leading.caretX+.25f*trailing.caretX;
            JaliumTextHitTestResult hit{};Check(format.HitTestPoint(text.c_str(),text.size(),width,600,x,leading.caretY+leading.caretHeight*.5f,&hit)==JALIUM_OK,"point query succeeds");
            Check(hit.textPosition==index&&bool(hit.isTrailingHit)==end,"visual point returns original logical edge");
            Check(Near(hit.caretX,end?trailing.caretX:leading.caretX),"point query returns the corresponding caret coordinate");
        }
    }
}
}
int main()
{
    jalium::TextEngine engine;Check(engine.Initialize()==JALIUM_OK,"real text engine initializes");
    std::unique_ptr<jalium::TextFormat> owner(engine.CreateTextFormat(L"DejaVu Sans",24,400,0));
    auto* format=dynamic_cast<jalium::JaliumTextFormat*>(owner.get());Check(format!=nullptr,"self-hosted format created");if(!format)return 1;
    auto* handle=reinterpret_cast<JaliumTextFormat*>(format);
    Check(jalium_text_format_set_direction(nullptr,0)==JALIUM_ERROR_INVALID_ARGUMENT,"null direction handle rejected");
    Check(jalium_text_format_set_direction(handle,3)==JALIUM_ERROR_INVALID_ARGUMENT,"invalid paragraph direction rejected");
    for(int direction:{0,1,2}){
        const auto generation=format->GetStyleGeneration();
        Check(jalium_text_format_set_direction(handle,direction)==JALIUM_OK,"C ABI exposes optional paragraph direction");
        Check(format->GetStyleGeneration()>=generation,"direction is part of native style generation");
        const std::wstring mixed=L"مرحبا CSS";
        const auto c=Caret(*format,mixed,6,false),s1=Caret(*format,mixed,7,false),s2=Caret(*format,mixed,8,false);
        Check(c.caretX<s1.caretX&&s1.caretX<s2.caretX,"Latin CSS is never reversed by an Arabic majority");
        const auto arabic=Caret(*format,mixed,0,false),arabicEnd=Caret(*format,mixed,0,true);
        Check(arabic.caretX>arabicEnd.caretX,"RTL leading and trailing edges face opposite physical directions");
        Check(direction==0?c.caretX>arabic.caretX:c.caretX<arabic.caretX,"explicit CSS base direction changes run placement");
        RoundTrip(*format,mixed);RoundTrip(*format,L"אבג 123 CSS");RoundTrip(*format,L"CSS 中文 אב");
        format->SetWordWrapping(JALIUM_WORD_WRAP_CHARACTER);RoundTrip(*format,L"CSS אבג 123 xyz",75);format->SetWordWrapping(JALIUM_WORD_WRAP_NONE);
    }
    for(int horizontal:{JALIUM_TEXT_ALIGN_LEADING,JALIUM_TEXT_ALIGN_TRAILING,JALIUM_TEXT_ALIGN_CENTER})
        for(int vertical:{JALIUM_PARAGRAPH_ALIGN_NEAR,JALIUM_PARAGRAPH_ALIGN_FAR,JALIUM_PARAGRAPH_ALIGN_CENTER}) {
            format->SetAlignment(horizontal);format->SetParagraphAlignment(vertical);
            const std::wstring text=L"CSS אבג 123";JaliumTextMetrics aligned{};
            format->MeasureText(text.data(),text.size(),800,600,&aligned);
            const float expectedTop=vertical==JALIUM_PARAGRAPH_ALIGN_NEAR?0:
                (600-aligned.height)*(vertical==JALIUM_PARAGRAPH_ALIGN_CENTER?.5f:1.f);
            const float expectedLeft=horizontal==JALIUM_TEXT_ALIGN_LEADING?0:
                (800-aligned.widthIncludingTrailingWhitespace)*(horizontal==JALIUM_TEXT_ALIGN_CENTER?.5f:1.f);
            const auto origin=Caret(*format,text,0,false);
            Check(Near(origin.caretX,expectedLeft)&&Near(origin.caretY,expectedTop),"caret origin follows independent alignment formula");
            RoundTrip(*format,text);
        }
    format->SetAlignment(JALIUM_TEXT_ALIGN_LEADING);format->SetParagraphAlignment(JALIUM_PARAGRAPH_ALIGN_NEAR);
    {
        format->SetTextDirection(1);const std::u16string text=u"א\U0001F600 CSS";
        JaliumTextHitTestResult c{},next{};
        Check(jalium_text_format_hit_test_text_position(handle,reinterpret_cast<const wchar_t*>(text.data()),text.size(),800,600,4,0,&c)==JALIUM_OK,"UTF-16 caret after supplementary scalar succeeds");
        Check(jalium_text_format_hit_test_text_position(handle,reinterpret_cast<const wchar_t*>(text.data()),text.size(),800,600,5,0,&next)==JALIUM_OK,"UTF-16 next Latin caret succeeds");
        Check(c.textPosition==4&&next.textPosition==5&&c.caretX<next.caretX,"C ABI preserves UTF-16 offsets through bidi and font fallback");
        JaliumTextHitTestResult hit{};
        Check(jalium_text_format_hit_test_point(handle,reinterpret_cast<const wchar_t*>(text.data()),text.size(),800,600,c.caretX+(next.caretX-c.caretX)*.25f,c.caretY+c.caretHeight*.5f,&hit)==JALIUM_OK&&hit.textPosition==4,"UTF-16 point hit maps back to source after supplementary scalar");
    }
    format->SetTextDirection(1);
    std::string actual,expected;float w1=0,b1=0,w2=0,b2=0;
    Check(format->BuildOutlinePath(L"אב (CSS)",8,actual,w1,b1),"mixed bracket outline built");
    format->SetTextDirection(0);const std::wstring forced=L"\u202D(CSS) בא\u202C";
    Check(format->BuildOutlinePath(forced.data(),forced.size(),expected,w2,b2),"independent explicit visual-order outline built");
    Check(actual==expected&&Near(w1,w2)&&Near(b1,b2),"painted brackets and glyph order equal explicit visual reference");
    {
        const std::wstring text=L"A\u2067אב\u2069B";format->SetTextDirection(0);
        const auto aEnd=Caret(*format,text,0,true),isolate=Caret(*format,text,1,false);
        Check(Near(aEnd.caretX,isolate.caretX),"invisible isolate does not become part of the preceding glyph cluster");
        const std::wstring start=L"\u2067אב\u2069";
        const auto control=Caret(*format,start,0,false),first=Caret(*format,start,1,false);
        Check(Near(control.caretX,first.caretX),"leading formatting-only position uses following logical edge");
    }
    const std::wstring lines=L"ffi\r\nCSS\nאב";
    auto first=Caret(*format,lines,0,false),second=Caret(*format,lines,5,false),third=Caret(*format,lines,9,false);
    Check(Near(second.caretY,first.caretHeight)&&Near(third.caretY,first.caretHeight*2),"ligatures and CRLF preserve logical line indices");
    JaliumTextMetrics metrics{};format->MeasureText(L"CSS\n",4,800,600,&metrics);
    Check(Near(metrics.height,first.caretHeight*2),"trailing hard break retains an empty final line");
    auto end=Caret(*format,L"CSS\n",4,false);Check(Near(end.caretY,first.caretHeight)&&Near(end.caretX,0),"empty final-line caret is stable");
    {
        jalium::TextGlyphRanges ranges;
        uint32_t spans[]{3, 1, 0, 2, 1, 2};
        Check(ranges.Set(spans, 3, 1) && ranges.Count() == 1 && ranges.Data()[0] == 0 && ranges.Data()[1] == 4,
            "overlapping UTF-16 filter intervals form one union");
        uint32_t bad[]{UINT32_MAX, 2};
        Check(!ranges.Set(bad, 1, 1) && ranges.Count() == 1, "invalid filter update preserves the current state");
        Check(!ranges.Set(nullptr, 1, 1) && !ranges.Set(nullptr, 0, 3), "invalid filter pointer and mode rejected");
        const std::wstring source = L"A\U0001F600אבB";
        format->SetTextDirection(1);
        std::vector<jalium::TextGlyphQuad> full, selected, complementary, hidden;
        auto paint = [&](std::vector<jalium::TextGlyphQuad>& output) {
            format->GenerateGlyphQuads(source.data(), source.size(), 800, 600, 1, 0, 0, 1, 0, 0,
                output, 1, -1, ranges.Data(), ranges.Count(), ranges.Mode());
        };
        ranges.Set(nullptr, 0, 0); paint(full);
        uint32_t emoji[]{1, 2}; ranges.Set(emoji, 1, 1); paint(selected);
        ranges.Set(emoji, 1, 2); paint(complementary);
        Check(!selected.empty() && selected.size() + complementary.size() == full.size(),
            "supplementary scalar UTF-16 filter partitions the shaped bidi glyphs");
        bool exact = true;
        for (const auto& quad : selected) {
            bool found = false;
            for (const auto& original : full)
                if (Near(quad.posX, original.posX) && Near(quad.posY, original.posY) &&
                    Near(quad.sizeX, original.sizeX) && Near(quad.sizeY, original.sizeY)) found = true;
            exact &= found;
        }
        Check(exact, "filtering preserves full-paragraph glyph placement");
        ranges.Set(nullptr, 0, 1); paint(hidden); Check(hidden.empty(), "empty include filter paints no glyphs");
        const std::wstring contextual = L"אב CSS\u2028DEF 123";
        const auto wrapped = Caret(*format, contextual, 7, false);
        Check(Near(wrapped.caretY, wrapped.caretHeight), "line separator retains the paragraph direction on the second row");
    }
    std::cout<<"{\"count\":"<<cases<<",\"failed\":"<<failures<<"}\n";return failures?1:0;
}

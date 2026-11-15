#include "shared_text_paragraph.h"
#include "text_engine.h"
#include "jalium_api.h"
#include <chrono>
#include <cmath>
#include <iostream>
#include <memory>
#include <thread>
#include <vector>

namespace {
unsigned checks = 0, failures = 0;
void Check(bool ok, const char* label) { ++checks; if (!ok) { ++failures; std::cerr << "FAIL: " << label << '\n'; } }
bool Near(float a, float b) { return std::abs(a-b) < .005f; }
JaliumTextParagraph* Create(jalium::TextFormat* format, const std::u16string& text, int direction, int alignment = 0, float width = 800, int wrap = 1)
{
    JaliumTextSpan span{reinterpret_cast<JaliumTextFormat*>(format), 0, static_cast<uint32_t>(text.size()), 1, .2f, .1f, 1};
    return jalium_text_paragraph_create_with_wrapping(reinterpret_cast<JaliumTextFormat*>(format),
        reinterpret_cast<const uint16_t*>(text.data()), text.size(), text.empty() ? nullptr : &span, text.empty() ? 0 : 1,
        width, 0, alignment, direction, wrap);
}
}
int main()
{
    jalium::TextEngine engine; Check(engine.Initialize() == JALIUM_OK, "initialize actual self-hosted engine");
    std::unique_ptr<jalium::TextFormat> format(engine.CreateTextFormat(L"DejaVu Sans", 24, 400, 0));
    auto* concrete = dynamic_cast<jalium::JaliumTextFormat*>(format.get()); Check(concrete != nullptr, "create actual text format");
    if (!concrete) return 1;
    format->SetWordWrapping(JALIUM_WORD_WRAP_NONE);
    const std::u16string text = u"CSS אב 123 中文";
    for (int direction : {-1,0,1}) for (int alignment : {0,1,2}) {
        std::unique_ptr<jalium::TextParagraph> paragraph(reinterpret_cast<jalium::TextParagraph*>(Create(format.get(), text, direction, alignment)));
        Check(paragraph != nullptr, "C ABI creates a shared immutable paragraph"); if (!paragraph) continue;
        concrete->SetTextDirection(direction < 0 ? 2 : direction); concrete->SetAlignment(alignment);
        JaliumParagraphLineMetrics line{}; Check(paragraph->GetLine(0, &line) == JALIUM_OK && paragraph->LineCount() == 1, "one line metrics available");
        for (uint32_t source = 0; source < text.size(); ++source) {
            JaliumTextHitTestResult leading{}, trailing{};
            Check(jalium_text_format_hit_test_text_position(reinterpret_cast<JaliumTextFormat*>(format.get()), reinterpret_cast<const wchar_t*>(text.data()), text.size(), 800, 600, source, 0, &leading) == JALIUM_OK, "independent format leading caret");
            Check(jalium_text_format_hit_test_text_position(reinterpret_cast<JaliumTextFormat*>(format.get()), reinterpret_cast<const wchar_t*>(text.data()), text.size(), 800, 600, source, 1, &trailing) == JALIUM_OK, "independent format trailing caret");
            JaliumParagraphCaret a{},b{};
            Check(paragraph->GetCaret(0, source, false, &a) == JALIUM_OK && Near(a.x,leading.caretX), "paragraph leading edge matches independent layout");
            Check(paragraph->GetCaret(0, source+1, true, &b) == JALIUM_OK && Near(b.x,trailing.caretX), "paragraph trailing edge matches independent layout");
            if (Near(a.x,b.x)) continue;
            for (float t : {.2f,.8f}) {
                JaliumParagraphCaret hit{};
                Check(paragraph->HitTest(0,a.x+(b.x-a.x)*t,&hit)==JALIUM_OK && hit.textPosition==(t<.5f ? source : source+1), "physical glyph cell hit preserves logical edge");
            }
            uint32_t count=0; Check(paragraph->GetSelection(0,source,1,nullptr,0,&count)==JALIUM_OK && count>0, "source selection produces geometry");
        }
    }
    concrete->SetAlignment(0); concrete->SetTextDirection(0);
    for (const auto& source : {std::u16string(u"\u202EAB C\u2028D EF\u202C"),std::u16string(u"AB C\r\n\r\nD EF")}) {
        for (int direction : {-1,0,1}) {
            JaliumTextSpan span{reinterpret_cast<JaliumTextFormat*>(format.get()),0,static_cast<uint32_t>(source.size()),1,.2f,.1f,1};
            std::vector<JaliumTextAdvanceUnit> units;
            for (uint32_t i=0;i<source.size();++i) {
                const uint32_t cp=source[i];
                const bool ignored=cp==0x202E||cp==0x202C||cp==0x2028||cp=='\r'||cp=='\n';
                units.push_back({i,1,8,cp==' '?2.5f:0,cp==' '?2.5f:0,ignored?1u:cp==' '?4u:0u});
            }
            std::unique_ptr<jalium::TextParagraph> natural(reinterpret_cast<jalium::TextParagraph*>(Create(format.get(),source,direction)));
            std::unique_ptr<jalium::TextParagraph> spaced(reinterpret_cast<jalium::TextParagraph*>(jalium_text_paragraph_create_context_advanced(
                reinterpret_cast<JaliumTextFormat*>(format.get()),reinterpret_cast<const uint16_t*>(source.data()),source.size(),&span,1,units.data(),units.size(),800,0,direction)));
            Check(natural&&spaced,"advanced C ABI retains multiple hard and soft rows");
            if (!natural||!spaced) continue;
            Check(natural->LineCount()==spaced->LineCount(),"spacing preserves all paragraph rows including empty rows");
            for (uint32_t row=0;row<spaced->LineCount();++row) {
                JaliumParagraphLineMetrics before{},after{};natural->GetLine(row,&before);spaced->GetLine(row,&after);
                uint32_t visible=0,separators=0;
                for (uint32_t i=before.line.textPosition;i<before.line.textPosition+before.line.length;++i)
                    if (!(units[i].flags&1)) {++visible;if(source[i]==' ')++separators;}
                Check(Near(after.line.width,before.line.width+8*float(visible?visible-1:0)+5*separators),"each row has internal tracking and separator advance without edge halves");
                Check(Near(before.line.y,after.line.y)&&Near(before.baseline,after.baseline),"spacing preserves row baselines and vertical geometry");
                for (uint32_t i=after.line.textPosition;i<after.line.textPosition+after.line.length;++i) {
                    if (units[i].flags&1)continue;
                    JaliumParagraphCaret a{},b{},hit{};
                    Check(spaced->GetCaret(row,i,false,&a)==JALIUM_OK&&spaced->GetCaret(row,i+1,true,&b)==JALIUM_OK,"advanced source edges are queryable");
                    if (Near(a.x,b.x))continue;
                    Check(spaced->HitTest(row,a.x+(b.x-a.x)*.2f,&hit)==JALIUM_OK&&hit.textPosition==i,"advanced hit maps the physical cell back to its logical source");
                }
            }
            auto* shared=dynamic_cast<jalium::SharedTextParagraph*>(spaced.get());
            std::vector<jalium::TextGlyphQuad> quads;
            shared->DrawingFormat()->GenerateGlyphQuadsForEngine(&engine,shared->Text(),shared->TextLength(),shared->Width(),shared->Height(),1,0,0,1,0,0,quads,1,-1,nullptr,0,0);
            Check(!quads.empty(),"advanced retained glyphs still rasterize from the frozen layout");
        }
    }
    {
        const std::u16string source=u"A🙂B";
        JaliumTextSpan span{reinterpret_cast<JaliumTextFormat*>(format.get()),0,static_cast<uint32_t>(source.size()),1,0,0,1};
        const JaliumTextAdvanceUnit invalid[]{{0,2,2,0,0,0},{2,2,2,0,0,0}};
        auto* rejected=jalium_text_paragraph_create_advanced(reinterpret_cast<JaliumTextFormat*>(format.get()),
            reinterpret_cast<const uint16_t*>(source.data()),source.size(),&span,1,invalid,2,800,0,0);
        Check(rejected==nullptr,"advanced unit boundaries cannot divide a UTF-16 scalar");
        if (rejected)jalium_text_paragraph_destroy(rejected);
    }
    {
        const std::u16string source=u"ABC";
        JaliumTextSpan span{reinterpret_cast<JaliumTextFormat*>(format.get()),0,3,1,0,0,1};
        const JaliumTextAdvanceUnit units[]{{0,1,100,0,0,0},{1,1,100,0,0,0},{2,1,100,0,0,0}};
        std::unique_ptr<jalium::TextParagraph> wide(reinterpret_cast<jalium::TextParagraph*>(jalium_text_paragraph_create_advanced(
            reinterpret_cast<JaliumTextFormat*>(format.get()),reinterpret_cast<const uint16_t*>(source.data()),3,&span,1,units,3,1,0,0)));
        Check(wide!=nullptr,"advanced no-wrap width may exceed its wrapping constraint");
        if(wide){JaliumParagraphLineMetrics line{};wide->GetLine(0,&line);auto* shared=dynamic_cast<jalium::SharedTextParagraph*>(wide.get());
            Check(shared->Width()>=line.line.width+48,"expanded advances retain the same glyph overhang margin as natural paragraphs");}
    }
    {
        const std::u16string source=u"\u202EAB \u2028AB\u202C", reference=u"\u202EAB\u202C";
        auto make=[&](const std::u16string& value,bool hideSpace) {
            JaliumTextSpan span{reinterpret_cast<JaliumTextFormat*>(format.get()),0,static_cast<uint32_t>(value.size()),1,0,0,1};
            std::vector<JaliumTextAdvanceUnit> units;
            for(uint32_t i=0;i<value.size();i++)units.push_back({i,1,2,0,0,value[i]==0x202E||value[i]==0x202C||value[i]==0x2028||(hideSpace&&value[i]==' ')?1u:0u});
            return std::unique_ptr<jalium::TextParagraph>(reinterpret_cast<jalium::TextParagraph*>(jalium_text_paragraph_create_advanced(
                reinterpret_cast<JaliumTextFormat*>(format.get()),reinterpret_cast<const uint16_t*>(value.data()),value.size(),&span,1,units.data(),units.size(),800,0,1)));
        };
        auto actual=make(source,true), expected=make(reference,false);
        Check(actual&&expected,"collapsed RTL row and independent whitespace-free paragraph create");
        if(actual&&expected){
            JaliumParagraphLineMetrics line{};actual->GetLine(0,&line);Check(line.line.x>1,"the ignored RTL separator moves the native used-row origin");
            auto quads=[&](jalium::TextParagraph* paragraph,float scale){auto* shared=dynamic_cast<jalium::SharedTextParagraph*>(paragraph);std::vector<jalium::TextGlyphQuad> output;
                const uint32_t range[]{1,2};shared->DrawingFormat()->GenerateGlyphQuadsForEngine(&engine,shared->Text(),shared->TextLength(),shared->Width(),shared->Height(),1,0,0,1,0,0,output,scale,-1,range,1,1);return output;};
            for(float scale:{1.f,2.f}){auto a=quads(actual.get(),scale),b=quads(expected.get(),scale);Check(a.size()==2&&a.size()==b.size(),"independent row references retain the same glyphs at both DPI scales");
                for(size_t i=0;i<std::min(a.size(),b.size());i++)Check(Near(a[i].posX,b[i].posX),"the retained pen is normalized before snapping at both DPI scales");}
        }
    }
    {
        const JaliumFontFeature features[]{{0x6C696761,1},{0x6B65726E,1}};
        Check(concrete->SetFontFeatures(features,2)==JALIUM_OK,"enable real ligature and kerning features");
        const std::u16string source=u"AVfi";
        JaliumTextSpan colors[4];
        for (uint32_t i=0;i<4;++i) colors[i]={reinterpret_cast<JaliumTextFormat*>(format.get()),i,1,i*.2f,0,1,.5f};
        std::unique_ptr<jalium::TextParagraph> shaped(reinterpret_cast<jalium::TextParagraph*>(jalium_text_paragraph_create_with_wrapping(
            reinterpret_cast<JaliumTextFormat*>(format.get()),reinterpret_cast<const uint16_t*>(source.data()),source.size(),colors,4,800,0,0,0,1)));
        std::unique_ptr<jalium::TextParagraph> plain(reinterpret_cast<jalium::TextParagraph*>(Create(format.get(),source,0)));
        Check(shaped && plain,"color-only spans retain a full shaped paragraph");
        if (shaped && plain) {
            JaliumParagraphLineMetrics a{},b{};shaped->GetLine(0,&a);plain->GetLine(0,&b);
            Check(Near(a.line.width,b.line.width),"paint boundaries preserve continuous kerning and ligatures");
            auto* shared=dynamic_cast<jalium::SharedTextParagraph*>(shaped.get());
            auto quads=[&](const uint32_t* ranges,uint32_t count,int visibility) {
                std::vector<jalium::TextGlyphQuad> output;
                shared->DrawingFormat()->GenerateGlyphQuadsForEngine(&engine,shared->Text(),shared->TextLength(),shared->Width(),shared->Height(),1,0,0,.5f,0,0,output,1,-1,ranges,count,visibility);
                return output.size();
            };
            const auto whole=quads(nullptr,0,0);Check(whole==3,"the independent font actually forms an fi ligature");
            jalium::TextGlyphRanges filter;
            auto runs=shared->PaintRuns(0,0,source.size(),filter);size_t painted=0;
            for(const auto& run:runs) painted+=quads(run.ranges.data(),run.ranges.size()/2,1);
            Check(painted==whole,"translucent color boundaries paint each glyph once");
            Check(shared->PaintRuns(0,2,1,filter).empty(),"a crossing ligature uses its final character's paint owner");
            const uint32_t firstHalf[]{2,1};filter.Set(firstHalf,1,1);
            runs=shared->PaintRuns(0,0,source.size(),filter);painted=0;
            for(const auto& run:runs) painted+=quads(run.ranges.data(),run.ranges.size()/2,1);
            Check(painted==1,"include visibility retains the whole crossing ligature once");
            filter.Set(firstHalf,1,2);runs=shared->PaintRuns(0,0,source.size(),filter);painted=0;
            for(const auto& run:runs) painted+=quads(run.ranges.data(),run.ranges.size()/2,1);
            Check(painted==whole-1,"exclude visibility removes the whole crossing ligature");
            const JaliumTextAdvanceUnit advances[]{{0,1,4,0,0,0},{1,1,4,0,0,0},{2,1,4,0,0,0},{3,1,4,0,0,0}};
            std::unique_ptr<jalium::TextParagraph> spaced(reinterpret_cast<jalium::TextParagraph*>(jalium_text_paragraph_create_context_advanced(
                reinterpret_cast<JaliumTextFormat*>(format.get()),reinterpret_cast<const uint16_t*>(source.data()),source.size(),colors,4,advances,4,800,0,0)));
            Check(spaced!=nullptr,"advanced spacing preserves an explicitly enabled optional ligature");
            if(spaced){
                JaliumParagraphLineMetrics line{};spaced->GetLine(0,&line);Check(Near(line.line.width,b.line.width+12),"forced ligature cells retain all internal tracking");
                auto* shifted=dynamic_cast<jalium::SharedTextParagraph*>(spaced.get());std::vector<jalium::TextGlyphQuad> output;
                shifted->DrawingFormat()->GenerateGlyphQuadsForEngine(&engine,shifted->Text(),shifted->TextLength(),shifted->Width(),shifted->Height(),1,0,0,.5f,0,0,output,1,-1,nullptr,0,0);
                Check(output.size()==whole,"advanced translation never splits a retained glyph cluster");
                jalium::TextGlyphRanges visible;Check(shifted->PaintRuns(0,2,1,visible).empty(),"advanced paint still belongs to the final ligature source");
                Check(shifted->PaintRuns(0,3,1,visible).size()==1,"advanced paint includes the crossing ligature exactly once");
            }
        }
    }
    {
        std::unique_ptr<jalium::TextFormat> phaseFormat(engine.CreateTextFormat(L"DejaVu Sans",24,400,0));
        for (int direction : {0,1}) for (float shift : {.375f,-.625f,2.875f}) for (float scale : {1.f,2.f}) {
            const std::u16string source=direction ? u"אבג" : u"ABC";
            JaliumTextSpan span{reinterpret_cast<JaliumTextFormat*>(phaseFormat.get()),0,3,1,0,0,1};
            std::vector<JaliumTextAdvanceUnit> units{{0,1,0,0,0,0},{1,1,0,0,0,0},{2,1,0,0,0,0}};
            if(direction) units.back().trailing=shift; else units.front().leading=shift;
            std::unique_ptr<jalium::TextParagraph> plain(reinterpret_cast<jalium::TextParagraph*>(Create(phaseFormat.get(),source,direction)));
            std::unique_ptr<jalium::TextParagraph> spaced(reinterpret_cast<jalium::TextParagraph*>(jalium_text_paragraph_create_advanced(
                reinterpret_cast<JaliumTextFormat*>(phaseFormat.get()),reinterpret_cast<const uint16_t*>(source.data()),3,&span,1,
                units.data(),units.size(),800,0,direction)));
            auto* base=dynamic_cast<jalium::SharedTextParagraph*>(plain.get());
            auto* moved=dynamic_cast<jalium::SharedTextParagraph*>(spaced.get());
            Check(base && moved,"fractional advance retains both paragraphs"); if(!base || !moved)continue;
            std::vector<jalium::TextGlyphQuad> expected,actual;
            base->DrawingFormat()->GenerateGlyphQuadsForEngine(&engine,base->Text(),base->TextLength(),base->Width(),base->Height(),1,0,0,1,
                shift*scale,0,expected,scale,-1,nullptr,0,0);
            moved->DrawingFormat()->GenerateGlyphQuadsForEngine(&engine,moved->Text(),moved->TextLength(),moved->Width(),moved->Height(),1,0,0,1,
                0,0,actual,scale,-1,nullptr,0,0);
            bool same=expected.size()==actual.size() && !expected.empty();
            for(size_t i=0;same && i<expected.size();i++){
                const auto& a=expected[i];const auto& b=actual[i];
                same=Near(a.posX,b.posX)&&Near(a.posY,b.posY)&&Near(a.sizeX,b.sizeX)&&Near(a.sizeY,b.sizeY)&&
                    Near(a.uvMinX,b.uvMinX)&&Near(a.uvMinY,b.uvMinY)&&Near(a.uvMaxX,b.uvMaxX)&&Near(a.uvMaxY,b.uvMaxY);
            }
            Check(same,"retained fractional glyph offsets select the same raster phase and quad as an independent origin");
        }
    }
    for (const auto& value : {std::u16string(u"A🙂e\u0301B"),std::u16string(u"\u202Eabc DEF 123\u202C"),std::u16string(u"A\u2067אב\u2069B")}) {
        auto* paragraph = Create(format.get(),value,0); Check(paragraph != nullptr,"controls and graphemes create a paragraph");
        if (!paragraph) continue;
        JaliumParagraphCaret caret{};
        if (value[1] == 0xD83D) {
            Check(jalium_text_paragraph_get_caret(paragraph,0,2,0,&caret)==JALIUM_OK && caret.textPosition==1,"low surrogate normalizes to complete emoji");
            Check(jalium_text_paragraph_get_caret(paragraph,0,4,0,&caret)==JALIUM_OK && caret.textPosition==3,"combining mark normalizes to base grapheme");
        }
        uint32_t count=0; Check(jalium_text_paragraph_get_selection(paragraph,0,0,value.size(),nullptr,0,&count)==JALIUM_OK,"full selection uses original UTF-16 positions");
        jalium_text_paragraph_destroy(paragraph);
    }
    const std::u16string breaks=u"CSS\r\nאב\n";
    auto* paragraph=Create(format.get(),breaks,1); Check(paragraph != nullptr,"hard paragraphs create layout");
    if (paragraph) {
        Check(jalium_text_paragraph_line_count(paragraph)==3,"CRLF and trailing hard break retain three lines");
        JaliumParagraphCaret caret{};
        Check(jalium_text_paragraph_get_caret(paragraph,0,4,0,&caret)==JALIUM_OK && caret.textPosition==3,"CRLF is a single grapheme");
        JaliumParagraphLineMetrics last{};jalium_text_paragraph_get_line(paragraph,2,&last);
        Check(last.line.textPosition==breaks.size() && last.line.length==0,"empty last line keeps source end");
        jalium_text_paragraph_destroy(paragraph);
    }
    // Snapshot style ownership and global bidi context across mixed font spans.
    auto other=std::unique_ptr<jalium::TextFormat>(engine.CreateTextFormat(L"DejaVu Serif",32,700,0));
    const std::u16string styled=u"CSS אב END";
    JaliumTextSpan styles[]{{reinterpret_cast<JaliumTextFormat*>(format.get()),0,4,1,0,0,1},
        {reinterpret_cast<JaliumTextFormat*>(other.get()),4,2,0,0,1,.7f},
        {reinterpret_cast<JaliumTextFormat*>(format.get()),6,4,1,0,0,1}};
    paragraph=jalium_text_paragraph_create_with_wrapping(reinterpret_cast<JaliumTextFormat*>(format.get()),reinterpret_cast<const uint16_t*>(styled.data()),styled.size(),styles,3,800,40,0,1,1);
    Check(paragraph!=nullptr,"mixed font sizes and paint spans share a paragraph");
    if (paragraph) {
        JaliumParagraphCaret before{},after{};jalium_text_paragraph_get_caret(paragraph,0,0,0,&before);
        concrete->SetTextDirection(0);concrete->SetAlignment(2);format.reset();other.reset();
        Check(jalium_text_paragraph_get_caret(paragraph,0,0,0,&after)==JALIUM_OK && Near(before.x,after.x),"format mutation and disposal cannot alter the snapshot");
        uint32_t count=0;Check(jalium_text_paragraph_get_fragments(paragraph,0,nullptr,0,&count)==JALIUM_OK && count>=3,"mixed font fragments retain source spans");
        auto* shared=dynamic_cast<jalium::SharedTextParagraph*>(reinterpret_cast<jalium::TextParagraph*>(paragraph));
        std::vector<jalium::TextGlyphQuad> quads;
        shared->DrawingFormat()->GenerateGlyphQuadsForEngine(&engine,shared->Text(),shared->TextLength(),shared->Width(),shared->Height(),1,0,0,1,0,0,quads,2,-1,nullptr,0,0);
        Check(!quads.empty(),"retained font snapshots still rasterize after creating formats are destroyed");
        jalium_text_paragraph_destroy(paragraph);
    }
    format.reset(engine.CreateTextFormat(L"DejaVu Sans",20,400,0));
    const std::u16string longText(100000,u'A');
    auto start=std::chrono::steady_clock::now();paragraph=Create(format.get(),longText,0);
    Check(paragraph!=nullptr,"very long paragraph creates once");
    auto built=std::chrono::steady_clock::now();
    if (paragraph) {
        auto read=[&]() { for (uint32_t i=0;i<100000;++i) { JaliumParagraphCaret c{};jalium_text_paragraph_get_caret(paragraph,0,i,false,&c);JaliumParagraphCaret h{};jalium_text_paragraph_hit_test(paragraph,0,c.x,&h); } };
        std::thread worker(read);read();worker.join();
        auto queried=std::chrono::steady_clock::now();
        auto milliseconds=[](auto a,auto b){return std::chrono::duration<double,std::milli>(b-a).count();};
        std::cout<<"{\"build_ms\":"<<milliseconds(start,built)<<",\"query_ms\":"<<milliseconds(built,queried)<<",\"text_length\":100000,\"query_calls\":400000}\n";
        Check(milliseconds(built,queried)<30000,"repeated and concurrent queries do not reshape or rescan the text");
        jalium_text_paragraph_destroy(paragraph);
    }
    std::cout<<"{\"count\":"<<checks<<",\"failed\":"<<failures<<"}\n";return failures?1:0;
}

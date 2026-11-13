#include "jalium_api.h"
#include "metal_font_position.h"
#import <AppKit/AppKit.h>
#include <cstdio>
#include <cmath>
#include <memory>
#include <stdexcept>
#include <string>

namespace {
int checks=0;
void Check(bool value,const char* message){if(!value)throw std::runtime_error(message);++checks;}
void Near(double a,double b,const char* message){if(std::abs(a-b)>.003){std::fprintf(stderr,"%s: %g / %g\n",message,a,b);throw std::runtime_error(message);}++checks;}
const wchar_t* Managed(const char16_t* value){return reinterpret_cast<const wchar_t*>(value);}
using Format=std::unique_ptr<JaliumTextFormat,decltype(&jalium_text_format_destroy)>;
using Paragraph=std::unique_ptr<JaliumTextParagraph,decltype(&jalium_text_paragraph_destroy)>;
void Native(JaliumContext* context){
    for(bool sub:{false,true})for(bool native:{false,true})for(bool synthesis:{false,true}){
        auto family=native?u"Seravek":u"Arial";uint32_t variant=sub?1:2;
        Format format(jalium_text_format_create(context,Managed(family),32,400,0),jalium_text_format_destroy);
        Check(format && jalium_text_format_set_font_position(format.get(),variant,synthesis?1:0,0)==0,"Position format unavailable");
        std::u16string source=u"123";JaliumTextSpan span{format.get(),0,3,0,0,0,1};
        Paragraph paragraph(jalium_text_paragraph_create(format.get(),reinterpret_cast<const uint16_t*>(source.data()),3,&span,1,10000,44,0,0),jalium_text_paragraph_destroy);
        uint8_t modes[3]{99,99,99};uint8_t expected=native?1:synthesis?2:3;
        Check(paragraph && jalium_text_paragraph_get_position_modes(paragraph.get(),modes,3)==0,"Position query failed");
        Check(modes[0]==expected && modes[1]==expected && modes[2]==expected,"Incomplete source position modes");
        uint8_t sentinel=99;Check(jalium_text_paragraph_get_position_modes(paragraph.get(),&sentinel,1)==JALIUM_ERROR_INVALID_ARGUMENT && sentinel==99,"Invalid mode count wrote output");
        Format reference(jalium_text_format_create(context,Managed(family),expected==2?20.8f:32,400,0),jalium_text_format_destroy);
        JaliumFontFeature feature{sub?0x73756273u:0x73757073u,expected==1?1u:0u};Check(jalium_text_format_set_font_features(reference.get(),&feature,1)==0,"Position reference failed");
        JaliumTextMetrics wanted{},direct{};Check(jalium_text_format_measure_text(reference.get(),Managed(source.c_str()),3,10000,1000,&wanted)==0,"Reference measure failed");
        Check(jalium_text_format_measure_text(format.get(),Managed(source.c_str()),3,10000,1000,&direct)==0,"Position measure failed");
        Near(direct.width,wanted.width,"Direct position advance differs");
        Format ordinary(jalium_text_format_create(context,Managed(family),32,400,0),jalium_text_format_destroy);
        JaliumTextHitTestResult before{},after{};
        Check(jalium_text_format_hit_test_text_position(ordinary.get(),Managed(source.c_str()),3,10000,1000,0,0,&before)==0 &&
            jalium_text_format_hit_test_text_position(format.get(),Managed(source.c_str()),3,10000,1000,0,0,&after)==0,"Direct position caret failed");
        Near(after.caretHeight,before.caretHeight,"Direct position changed the source caret height");
        Near(after.caretY,before.caretY,"Direct position changed the source caret origin");
        JaliumParagraphLineMetrics line{};Check(jalium_text_paragraph_get_line(paragraph.get(),0,&line)==0,"Position line failed");
        Near(line.line.width,wanted.width,"Paragraph position advance differs");Near(line.line.height,44,"Position changed line height");
        Check(line.line.textPosition==0 && line.line.length==3,"Position changed source offsets");
        for(uint32_t position:{0u,1u,3u}){JaliumParagraphCaret caret{};Check(jalium_text_paragraph_get_caret(paragraph.get(),0,position,0,&caret)==0 && caret.textPosition==position,"Position changed source caret");}
        JaliumTextRangeMetrics cell{};uint32_t count=0;Check(jalium_text_paragraph_get_selection(paragraph.get(),0,0,3,&cell,1,&count)==0 && count==1 && cell.length==3,"Position changed source selection");
    }
    Format one(jalium_text_format_create(context,Managed(u"Seravek"),32,400,0),jalium_text_format_destroy);
    Format two(jalium_text_format_create(context,Managed(u"Arial"),32,400,0),jalium_text_format_destroy);
    Check(jalium_text_format_set_font_position(one.get(),2,1,0)==0 && jalium_text_format_set_font_position(two.get(),2,1,0)==0,"Mixed position formats failed");
    std::u16string source=u"123x";JaliumTextSpan spans[]={{one.get(),0,3,0,0,0,1},{two.get(),3,1,1,0,0,1}};
    Paragraph mixed(jalium_text_paragraph_create(one.get(),reinterpret_cast<const uint16_t*>(source.data()),4,spans,2,10000,44,0,0),jalium_text_paragraph_destroy);
    uint8_t modes[4]{};Check(mixed && jalium_text_paragraph_get_position_modes(mixed.get(),modes,4)==0,"Mixed query failed");
    Check(modes[0]==2 && modes[1]==2 && modes[2]==2 && modes[3]==2,"A partial font run mixed native and synthetic forms");
    std::u16string isolated=u"\u2066123\u00AD\u200C\u2069";JaliumTextSpan isolatedSpan{one.get(),0,7,0,0,0,1};
    Paragraph isolatedParagraph(jalium_text_paragraph_create(one.get(),reinterpret_cast<const uint16_t*>(isolated.data()),7,&isolatedSpan,1,10000,44,0,0),jalium_text_paragraph_destroy);
    uint8_t isolatedModes[7]{};Check(isolatedParagraph && jalium_text_paragraph_get_position_modes(isolatedParagraph.get(),isolatedModes,7)==0,"Isolated position query failed");
    for(int i=0;i<7;++i)Check(isolatedModes[i]==1,"Format controls forced synthetic position fallback");
    Check(jalium_text_format_set_font_position(one.get(),3,1,0)==JALIUM_ERROR_INVALID_ARGUMENT,"Unknown position accepted");
    Check(jalium_text_format_set_font_position(one.get(),2,4,0)==JALIUM_ERROR_INVALID_ARGUMENT,"Malformed position override accepted");
    Check(jalium_text_format_set_font_position(one.get(),2,1,4)==JALIUM_ERROR_INVALID_ARGUMENT,"Unknown run mode accepted");
}
}
int main(){@autoreleasepool{try{for(auto backend:{JALIUM_BACKEND_METAL,JALIUM_BACKEND_SOFTWARE}){
    std::unique_ptr<JaliumContext,decltype(&jalium_context_destroy)> context(jalium_context_create(backend),jalium_context_destroy);
    Check(context!=nullptr,"Position context failed");Native(context.get());
}Check(jalium_text_format_set_font_position(nullptr,0,0,0)==JALIUM_ERROR_INVALID_ARGUMENT,"Null position format accepted");
Check(jalium_text_paragraph_get_position_modes(nullptr,nullptr,0)==JALIUM_ERROR_INVALID_ARGUMENT,"Null position paragraph accepted");
std::printf("Font position: %d checks passed\n",checks);return 0;}catch(const std::exception& error){std::fprintf(stderr,"Font position failed after %d checks: %s\n",checks,error.what());return 1;}}}

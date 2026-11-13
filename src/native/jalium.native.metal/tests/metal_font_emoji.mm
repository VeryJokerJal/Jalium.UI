#include "jalium_api.h"
#include "metal_font_emoji.h"
#import <AppKit/AppKit.h>
#include <cmath>
#include <cstdio>
#include <memory>
#include <stdexcept>
#include <string>
namespace {
int checks = 0;
void Check(bool value, const char* message) { if (!value) throw std::runtime_error(message); ++checks; }
void Near(double a, double b, const char* message) { if (std::abs(a-b) > .003) { std::fprintf(stderr,"%s: %g / %g\n",message,a,b); throw std::runtime_error(message); } ++checks; }
const wchar_t* Managed(const char16_t* text) { return reinterpret_cast<const wchar_t*>(text); }
using Format = std::unique_ptr<JaliumTextFormat, decltype(&jalium_text_format_destroy)>;
using Paragraph = std::unique_ptr<JaliumTextParagraph, decltype(&jalium_text_paragraph_destroy)>;
void Native(JaliumContext* context) {
    for (uint32_t mode : {1u,2u,3u}) for (bool explicitVs : {false,true}) for (bool caps : {false,true}) for (bool position : {false,true}) {
        Format actual(jalium_text_format_create(context,Managed(u"Arial"),32,400,0),jalium_text_format_destroy);
        Format reference(jalium_text_format_create(context,Managed(u"Arial"),32,400,0),jalium_text_format_destroy);
        Check(actual && reference && jalium_text_format_set_font_emoji(actual.get(),mode,0)==0,"Emoji format failed");
        if(caps) Check(jalium_text_format_set_font_caps(actual.get(),2,1,nullptr,0)==0 && jalium_text_format_set_font_caps(reference.get(),2,1,nullptr,0)==0,"Caps combination failed");
        if(position) Check(jalium_text_format_set_font_position(actual.get(),2,1,0)==0 && jalium_text_format_set_font_position(reference.get(),2,1,0)==0,"Position combination failed");
        std::u16string source=u"aß ♥⌚ 😀"; std::u16string painted;
        for(char16_t c:source) {
            painted+=c;
            if(jalium::emoji_data::Contains(jalium::emoji_data::Participating,c)) {
                char16_t selector=explicitVs ? (mode==1?0xfe0f:0xfe0e) : jalium::emoji_data::Selector(c,mode);
                painted+=selector;
            }
        }
        if(explicitVs)source=painted;
        JaliumTextMetrics before{},after{};
        Check(jalium_text_format_measure_text(actual.get(),Managed(source.c_str()),source.size(),10000,1000,&after)==0 &&
            jalium_text_format_measure_text(reference.get(),Managed(painted.c_str()),painted.size(),10000,1000,&before)==0,"Emoji direct measure failed");
        Near(after.width,before.width,"Emoji direct width differs from selectors");
        JaliumTextHitTestResult hit{},wanted{};
        Check(jalium_text_format_hit_test_text_position(actual.get(),Managed(source.c_str()),source.size(),10000,1000,source.size(),0,&hit)==0 &&
            jalium_text_format_hit_test_text_position(reference.get(),Managed(painted.c_str()),painted.size(),10000,1000,painted.size(),0,&wanted)==0,"Emoji direct caret failed");
        Check(hit.textPosition==source.size(),"Emoji direct caret changed source index"); Near(hit.caretX,wanted.caretX,"Emoji direct caret differs");
        JaliumTextSpan span{actual.get(),0,static_cast<uint32_t>(source.size()),0,0,0,1};
        JaliumTextSpan expected{reference.get(),0,static_cast<uint32_t>(painted.size()),0,0,0,1};
        Paragraph paragraph(jalium_text_paragraph_create(actual.get(),reinterpret_cast<const uint16_t*>(source.data()),source.size(),&span,1,10000,44,0,0),jalium_text_paragraph_destroy);
        Paragraph literal(jalium_text_paragraph_create(reference.get(),reinterpret_cast<const uint16_t*>(painted.data()),painted.size(),&expected,1,10000,44,0,0),jalium_text_paragraph_destroy);
        JaliumParagraphLineMetrics line{},other{};
        Check(paragraph && literal && jalium_text_paragraph_get_line(paragraph.get(),0,&line)==0 && jalium_text_paragraph_get_line(literal.get(),0,&other)==0,"Emoji paragraph failed");
        Near(line.line.width,other.line.width,"Emoji paragraph width differs"); Near(line.baseline,other.baseline,"Emoji paragraph baseline differs");
        Check(line.line.length==source.size(),"Emoji paragraph changed UTF-16 length");
        JaliumTextRangeMetrics selection{};uint32_t count=0;
        Check(jalium_text_paragraph_get_selection(paragraph.get(),0,0,source.size(),&selection,1,&count)==0 && count==1 && selection.length==source.size(),"Emoji selection changed source indices");
    }
    Format format(jalium_text_format_create(context,Managed(u"Arial"),32,400,0),jalium_text_format_destroy);
    Check(jalium_text_format_set_font_emoji(format.get(),4,0)==JALIUM_ERROR_INVALID_ARGUMENT,"Invalid emoji variant accepted");
    Check(jalium_text_format_set_font_emoji(format.get(),0,3)==JALIUM_ERROR_INVALID_ARGUMENT,"Invalid following selector accepted");
}
}
int main(){@autoreleasepool{try{
    for(auto backend:{JALIUM_BACKEND_METAL,JALIUM_BACKEND_SOFTWARE}){
        std::unique_ptr<JaliumContext,decltype(&jalium_context_destroy)> context(jalium_context_create(backend),jalium_context_destroy);
        Check(context!=nullptr,"Emoji context failed");Native(context.get());
    }
    Check(jalium_text_format_set_font_emoji(nullptr,0,0)==JALIUM_ERROR_INVALID_ARGUMENT,"Null emoji format accepted");
    std::printf("Font emoji: %d checks passed\n",checks);return 0;
}catch(const std::exception& error){std::fprintf(stderr,"Font emoji failed after %d checks: %s\n",checks,error.what());return 1;}}}

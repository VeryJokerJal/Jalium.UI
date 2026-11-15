#include "jalium_api.h"
#include "metal_font_position.h"
#include "metal_font_caps.h"
#import <AppKit/AppKit.h>
#include <cstdio>
#include <cmath>
#include <memory>
#include <stdexcept>
#include <vector>
namespace {
int checks=0;
void Check(bool value,const char* message){if(!value)throw std::runtime_error(message);++checks;}
void Near(double a,double b,const char* message){if(std::abs(a-b)>.003){std::fprintf(stderr,"%s %g / %g\n",message,a,b);throw std::runtime_error(message);}++checks;}
const wchar_t* Managed(const char16_t* text){return reinterpret_cast<const wchar_t*>(text);}
using Format=std::unique_ptr<JaliumTextFormat,decltype(&jalium_text_format_destroy)>;
using Paragraph=std::unique_ptr<JaliumTextParagraph,decltype(&jalium_text_paragraph_destroy)>;
int64_t Attribute(CFAttributedStringRef text,CFStringRef key){
 auto value=(CFNumberRef)CFAttributedStringGetAttribute(text,0,key,nullptr);int64_t result=-1;
 if(value)CFNumberGetValue(value,kCFNumberSInt64Type,&result);return result;
}
void Metadata(CTFontRef font){
 for(int64_t index:{0LL,1LL,2LL,3LL,2147483647LL,4294967295LL}){
  for(uint32_t variant:{1u,2u})for(uint32_t mode:{1u,2u}){
   auto text=CFAttributedStringCreateMutable(kCFAllocatorDefault,0);CFAttributedStringReplaceString(text,CFRangeMake(0,0),CFSTR("x"));CFAttributedStringSetAttribute(text,CFRangeMake(0,1),kCTFontAttributeName,font);
   jalium::SetApplePositionAttributes(text,CFRangeMake(0,1),variant,3|(index?4:0),mode,index);
   Check(Attribute(text,CFSTR("JaliumPositionFeatureOverride"))==index,"Position encoding truncated an exact feature index");
   auto output=jalium::CreateApplePositionText(text);Check(output!=nullptr,"Position transform unavailable");Check(Attribute(output,CFSTR("JaliumPositionFeatureOverride"))==index,"Position coerced exact feature index");CFRelease(output);CFRelease(text);
  }
  auto text=CFAttributedStringCreateMutable(kCFAllocatorDefault,0);CFAttributedStringReplaceString(text,CFRangeMake(0,0),CFSTR("A"));CFAttributedStringSetAttribute(text,CFRangeMake(0,1),kCTFontAttributeName,font);
  jalium::SetAppleCapsAttributes(text,CFRangeMake(0,1),5,true,CFSTR("en-US"),index);jalium::AppleCapsMap map;auto output=jalium::CreateAppleCapsText(text,map);
  Check(output!=nullptr,"Unicase transform unavailable");Check(Attribute(output,CFSTR("JaliumCapsSmcpOverride"))==index,"Unicase coerced exact feature index");CFRelease(output);CFRelease(text);
 }
}
void Native(JaliumContext* context){
 for(uint32_t variant:{1u,2u})for(bool synthetic:{false,true})for(uint32_t index:{0u,1u,2u,3u}){
  Format actual(jalium_text_format_create(context,Managed(u"priority-fixture"),32,400,0),jalium_text_format_destroy);
  Format expected(jalium_text_format_create(context,Managed(u"priority-fixture"),synthetic?20.8f:32,400,0),jalium_text_format_destroy);
  JaliumFontFeature feature{variant==1?0x73756273u:0x73757073u,index};Check(actual&&expected,"Priority format failed");
  Check(jalium_text_format_set_font_position(actual.get(),variant,3|(index?4:0),0)==0,"Position config failed");
  Check(jalium_text_format_set_font_features(actual.get(),&feature,1)==0&&jalium_text_format_set_font_features(expected.get(),&feature,1)==0,"Feature config failed");
  auto source=synthetic?u"xz":u"x";uint32_t length=synthetic?2:1;JaliumTextMetrics a{},b{};
  Check(jalium_text_format_measure_text(actual.get(),Managed(source),length,10000,1000,&a)==0&&jalium_text_format_measure_text(expected.get(),Managed(source),length,10000,1000,&b)==0,"Priority measure failed");Near(a.width,b.width,"Direct priority differs from raw native font reference");
  JaliumTextSpan span{actual.get(),0,length,0,0,0,1};Paragraph paragraph(jalium_text_paragraph_create(actual.get(),reinterpret_cast<const uint16_t*>(source),length,&span,1,10000,44,0,0),jalium_text_paragraph_destroy);
  JaliumParagraphLineMetrics line{};Check(paragraph&&jalium_text_paragraph_get_line(paragraph.get(),0,&line)==0,"Priority paragraph failed");Near(line.line.width,b.width,"Paragraph priority differs from raw reference");
  uint8_t modes[2]{};Check(jalium_text_paragraph_get_position_modes(paragraph.get(),modes,length)==0&&modes[0]==(synthetic?2:1),"Priority mode changed");
  JaliumFontFeature invalid{0,index};Check(jalium_text_format_set_font_features(actual.get(),&invalid,1)==JALIUM_ERROR_INVALID_ARGUMENT,"Invalid replacement accepted");
  Check(jalium_text_format_measure_text(actual.get(),Managed(source),length,10000,1000,&a)==0,"Measure after rejected replacement failed");Near(a.width,b.width,"Rejected replacement changed exact selector");
 }
}
}
int main(int argc,char**argv){@autoreleasepool{try{
 Check(argc==2,"Fixture argument missing");auto data=[NSData dataWithContentsOfFile:[NSString stringWithUTF8String:argv[1]]];Check(data!=nil,"Fixture missing");
 auto resource=jalium_font_resource_register(Managed(u"priority-fixture"),(const uint8_t*)data.bytes,(uint32_t)data.length);Check(resource!=nullptr,"Private font registration failed");
 auto provider=CGDataProviderCreateWithCFData((__bridge CFDataRef)data);auto graphics=CGFontCreateWithDataProvider(provider);auto font=CTFontCreateWithGraphicsFont(graphics,32,nullptr,nullptr);Check(font!=nullptr,"Metadata fixture failed");Metadata(font);CFRelease(font);CFRelease(graphics);CFRelease(provider);
 for(auto backend:{JALIUM_BACKEND_METAL,JALIUM_BACKEND_SOFTWARE}){std::unique_ptr<JaliumContext,decltype(&jalium_context_destroy)> context(jalium_context_create(backend),jalium_context_destroy);Check(context!=nullptr,"Priority context failed");Native(context.get());}
 jalium_font_resource_release(resource);std::printf("Feature priority: %d checks passed\n",checks);return 0;
}catch(const std::exception& error){std::fprintf(stderr,"Feature priority failed after %d checks: %s\n",checks,error.what());return 1;}}}

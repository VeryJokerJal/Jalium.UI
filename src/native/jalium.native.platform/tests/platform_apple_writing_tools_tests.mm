#import <AppKit/AppKit.h>
#import <CoreText/CoreText.h>
#include "jalium_platform.h"
#include <dlfcn.h>
#include <algorithm>
#include <cmath>
#include <cstdio>
#include <functional>
#include <stdexcept>
#include <string>
#include <thread>
#include <vector>

#if defined(__MAC_OS_X_VERSION_MAX_ALLOWED) && __MAC_OS_X_VERSION_MAX_ALLOWED >= 150200
static int passed, failed;
static void Require(bool value, const char* message) { if (!value) throw std::runtime_error(message); }
static NSString* Text16(NSData* bytes)
{ return [[NSString alloc] initWithBytes:bytes.bytes length:bytes.length encoding:NSUTF16LittleEndianStringEncoding]; }
static NSString* Base64(NSString* text)
{
    std::vector<unichar> units(text.length); [text getCharacters:units.data() range:NSMakeRange(0,text.length)];
    return [[NSData dataWithBytes:units.data() length:units.size()*2] base64EncodedStringWithOptions:0];
}
static NSDictionary* EmptyCellRecord(NSUInteger start, NSUInteger length, NSUInteger column, id empty, NSUInteger columns=2)
{
    return @{@"start":@(start),@"length":@(length),@"emptyCell":empty,@"lists":@[],
        @"tables":@[@{@"id":@"empty-table",@"row":@0,@"column":@(column),@"rows":@1,@"columns":@1,@"tableColumns":@(columns)}],
        @"ancestors":@[@{@"kind":@"table",@"index":@0}]};
}
static NSString* LanguageStyles(NSString* text, id language)
{
    NSDictionary* run = @{@"start":@0, @"length":@(text.length), @"family":@"Helvetica Neue", @"size":@21,
        @"weight":@400, @"italic":@0, @"underline":@0, @"strike":@0, @"alignment":@0, @"direction":@-1,
        @"language":language ?: NSNull.null};
    NSDictionary* packet = @{@"version":@1, @"text16":Base64(text), @"runs":@[run]};
    return [[NSString alloc] initWithData:[NSJSONSerialization dataWithJSONObject:packet options:0 error:nil] encoding:NSUTF8StringEncoding];
}
static NSString* BackgroundStyles(NSString* text, id background, NSString* key = @"background")
{
    NSData* data=[LanguageStyles(text,NSNull.null) dataUsingEncoding:NSUTF8StringEncoding];
    NSMutableDictionary* packet=[NSJSONSerialization JSONObjectWithData:data options:NSJSONReadingMutableContainers error:nil];
    if(background)packet[@"runs"][0][key]=background;
    return [[NSString alloc]initWithData:[NSJSONSerialization dataWithJSONObject:packet options:0 error:nil] encoding:NSUTF8StringEncoding];
}
static uint32_t BackgroundArgb(NSColor* input)
{
    NSColor* value=[input colorUsingColorSpace:NSColorSpace.sRGBColorSpace];
    Require(value,"background resolves into sRGB");
    auto byte=[](CGFloat n){return static_cast<uint32_t>(std::lround(n*255));};
    return byte(value.alphaComponent)<<24|byte(value.redComponent)<<16|byte(value.greenComponent)<<8|byte(value.blueComponent);
}
struct Fixture {
    JaliumPlatformWindow* window;
    NSView* view;
    NSWritingToolsCoordinator* coordinator;
    id<NSWritingToolsCoordinatorDelegate> delegate;
    NSString* text = @"first\nab🙂中文 tail\nlast";
    NSString* editor = @"first-editor";
    NSRange selection = NSMakeRange(8,2), lastRange{};
    bool canRead = true, canEdit = true, uppercase = false, geometryInvalid = false;
    int selected = 0, replacements = 0, prepared = 0, finished = 0, stopped = 0, snapshotQueries = 0, capabilityQueries = 0;
    NSArray* ranges;
    NSString* animationKey;
    NSArray* structure;
    NSDictionary* proposal;
    NSDictionary* packet;
    NSString* styles;
    bool wrongSession = false, clearStylesOnReplace = false;
    std::function<void(int)> after;
    explicit Fixture(uint32_t style, bool optIn = true)
    {
        JaliumWindowParams p{};p.title=reinterpret_cast<const JaliumUtf16Char*>(u"Jalium Writing Tools SDK");
        p.x=80;p.y=80;p.width=420;p.height=260;p.style=style;
        window=jalium_window_create(&p);Require(window,"create native owner");
        view=(__bridge NSView*)reinterpret_cast<void*>(jalium_window_get_native_handle(window));
        jalium_window_set_event_callback(window,Event,this);jalium_apple_window_show(window,0);
        // Resolve dynamically so the exact same fixture can inspect an older
        // payload which predates the opt-in entry point.
        auto enable = reinterpret_cast<int32_t(*)(JaliumPlatformWindow*,int32_t)>(dlsym(RTLD_DEFAULT,"jalium_apple_window_enable_text_services"));
        if (enable && optIn) enable(window,1);
        Publish();coordinator=view.writingToolsCoordinator;delegate=coordinator.delegate;
        if (optIn && (!coordinator || !delegate)) {
            jalium_window_set_event_callback(window,nullptr,nullptr);jalium_window_destroy(window);window=nullptr;
            Require(false,"custom view has an actual SDK coordinator and delegate");
        }
    }
    ~Fixture(){if(window){jalium_window_set_event_callback(window,nullptr,nullptr);jalium_window_destroy(window);}}
    void Publish(){jalium_window_update_ime_context(window,1,text.UTF8String,0,0,10,10,2,20);}
    NSDictionary* Snapshot()
    {
        NSMutableDictionary* value=[@{@"editor":editor,@"text16":Base64(text),
            @"selection":@[@(selection.location),@(selection.length)],
            @"visible":text.length>=18 ? @[@6,@12] : @[@0,@(text.length)]} mutableCopy];
        if(structure)value[@"structure"]=structure;
        if(styles)value[@"styles"]=styles;return value;
    }
    NSWritingToolsCoordinatorContext* Context(NSWritingToolsCoordinatorContextScope scope=NSWritingToolsCoordinatorContextScopeUserSelection)
    {
        __block NSWritingToolsCoordinatorContext* result=nil;__block int calls=0;
        [delegate writingToolsCoordinator:coordinator requestsContextsForScope:scope completion:^(NSArray* values){++calls;result=values.firstObject;}];
        Require(calls==1,"context completion exactly once");return result;
    }
    NSAttributedString* Replace(NSWritingToolsCoordinatorContext* context, NSRange range, NSString* value)
    {return ReplaceAttributed(context,range,[[NSAttributedString alloc]initWithString:value]);}
    NSAttributedString* ReplaceAttributed(NSWritingToolsCoordinatorContext* context, NSRange range, NSAttributedString* value)
    {
        __block NSAttributedString* accepted=nil;__block int calls=0;
        [delegate writingToolsCoordinator:coordinator replaceRange:range inContext:context
            proposedText:value reason:NSWritingToolsCoordinatorTextReplacementReasonNoninteractive
            animationParameters:nil completion:^(NSAttributedString* value){++calls;accepted=value;}];
        Require(calls==1,"replacement completion exactly once");return accepted;
    }
    static void Event(const JaliumPlatformEvent* event,void* user)
    {
        if(event->type!=JALIUM_EVENT_TEXT_SERVICES_REQUEST)return;
        auto& f=*static_cast<Fixture*>(user);auto& r=*event->textServicesRequest.request;
        NSData* input=[NSData dataWithBytes:r.utf8Payload length:strlen(r.utf8Payload)];
        NSDictionary* args=[NSJSONSerialization JSONObjectWithData:input options:0 error:nil]?:@{};
        NSDictionary* response=@{@"editor":f.editor};
        if(r.kind==0)++f.capabilityQueries;
        if(r.kind==0)response=@{@"editor":f.editor,@"canRead":@(f.canRead),@"canEdit":@(f.canEdit),@"resultOptions":@1};
        if(r.kind==1){++f.snapshotQueries;response=f.Snapshot();}
        if(r.kind==2 && f.canEdit){
            f.packet=args;
            f.proposal=args[@"proposal"];
            ++f.replacements;f.lastRange=NSMakeRange(r.start,r.length);
            NSString* value=Text16([[NSData alloc]initWithBase64EncodedString:args[@"text16"] options:0]);
            if(f.uppercase)value=value.uppercaseString;
            f.text=[f.text stringByReplacingCharactersInRange:f.lastRange withString:value];
            // This fixture's text-only backend cannot publish the old styled
            // snapshot after a replacement has changed its text.
            if(f.clearStylesOnReplace)f.styles=nil;
            f.Publish();
            response=@{@"editor":f.editor,@"applied":@YES,@"text16":Base64(value)};
        }
        if(r.kind==3){++f.selected;f.ranges=args[@"ranges"];response=@{@"editor":f.editor,@"applied":@YES};}
        if(r.kind==4)response=@{@"editor":f.editor,@"index":@8,@"rects":f.geometryInvalid ? @[@[@"invalid",@0,@1,@1]] : @[@[@20,@40,@60,@20],@[@30,@60,@40,@20]]};
        if(r.kind==5){
            NSBitmapImageRep* image=[[NSBitmapImageRep alloc]initWithBitmapDataPlanes:nullptr pixelsWide:2 pixelsHigh:2
                bitsPerSample:8 samplesPerPixel:4 hasAlpha:YES isPlanar:NO colorSpaceName:NSDeviceRGBColorSpace bytesPerRow:8 bitsPerPixel:32];
            memset(image.bitmapData,128,16);
            NSData* png=[image representationUsingType:NSBitmapImageFileTypePNG properties:@{}];
            response=@{@"editor":f.editor,@"png":[png base64EncodedStringWithOptions:0],@"frame":@[@20,@40,@60,@20],@"rects":@[@[@20,@40,@60,@20]]};
        }
        if(r.kind==6){++f.prepared;f.animationKey=args[@"animationKey"];}
        if(r.kind==7){++f.finished;Require([f.animationKey isEqual:args[@"animationKey"]],"finish uses stable presentation token");}
        if(r.kind==8 && [args[@"stopped"] boolValue])++f.stopped;
        NSMutableDictionary* reply=[response mutableCopy];
        if(args[@"session"])reply[@"session"]=f.wrongSession ? NSUUID.UUID.UUIDString : args[@"session"];
        NSData* output=[NSJSONSerialization dataWithJSONObject:reply options:0 error:nil];
        r.respond(r.responseContext,static_cast<const char*>(output.bytes),output.length);
        if(f.after){auto cb=std::move(f.after);f.after={};cb(r.kind);}
    }
};
static void Run(const char* name,uint32_t style,const std::function<void()>& work)
{
    @autoreleasepool{try{work();++passed;std::printf("PASS %s: %s\n",style?"Native":"Custom",name);}
        catch(const std::exception& e){++failed;std::fprintf(stderr,"FAIL %s: %s: %s\n",style?"Native":"Custom",name,e.what());}}
}
int main()
{
    @autoreleasepool {
        [NSApplication sharedApplication];[NSApp setActivationPolicy:NSApplicationActivationPolicyAccessory];[NSApp finishLaunching];
        if (!NSClassFromString(@"NSWritingToolsCoordinator")) { std::puts("SKIP Writing Tools requires macOS 15.2"); return 77; }
        jalium_platform_init();Dl_info info{};dladdr(reinterpret_cast<void*>(&jalium_window_create),&info);
        std::printf("LOADED_PLATFORM %s\n",info.dli_fname?:"unknown");
        for(uint32_t style:{uint32_t(0),uint32_t(JALIUM_WINDOW_STYLE_TITLEBAR|JALIUM_WINDOW_STYLE_RESIZABLE)}){
            for (NSNumber* color in @[@0u,@0x00112233u,@0x01123456u,@0x7f123456u,@0xff336699u,@0xffffffffu]) {
                std::string name="background context and proposal ARGB "+std::to_string(color.unsignedIntValue);
                Run(name.c_str(),style,[&]{
                    Fixture f(style);f.styles=BackgroundStyles(f.text,color);f.clearStylesOnReplace=true;
                    auto* context=f.Context(NSWritingToolsCoordinatorContextScopeFullDocument);Require(context,"background context");
                    id value=[context.attributedString attribute:NSBackgroundColorAttributeName atIndex:0 effectiveRange:nil];
                    Require([value isKindOfClass:NSColor.class],"the context carries a native background color");
                    Require(BackgroundArgb(value)==color.unsignedIntValue,"context preserves RGB and alpha, including explicit zero");
                    Require(f.ReplaceAttributed(context,NSMakeRange(0,f.text.length),context.attributedString),"unchanged attributed text accepted");
                    Require([f.proposal[@"runs"][0][@"background"] unsignedIntValue]==color.unsignedIntValue && f.proposal[@"runs"][0][@"background"],"proposal preserves background color");
                    Require([f.packet[@"source"][@"runs"][0][@"background"] unsignedIntValue]==color.unsignedIntValue && f.packet[@"source"][@"runs"][0][@"background"],"source metadata preserves its background");
                });
            }
            for (id invalid in @[@(-1),@4294967296ULL,@0.5,@"red",@[],NSNull.null,@YES]) {
                std::string name="background rejects invalid wire color "+std::string([[invalid description] UTF8String]);
                Run(name.c_str(),style,[&]{Fixture f(style);f.styles=BackgroundStyles(f.text,invalid);Require(!f.Context(NSWritingToolsCoordinatorContextScopeFullDocument) && f.replacements==0,"invalid background cannot enter the SDK context");});
            }
            for (NSString* key in @[@"foreground",@"underlineColor",@"strikeColor"]) {
                std::string name="background color contract rejects Boolean "+std::string(key.UTF8String);
                Run(name.c_str(),style,[&]{Fixture f(style);f.styles=BackgroundStyles(f.text,@YES,key);Require(!f.Context(NSWritingToolsCoordinatorContextScopeFullDocument),"Boolean is not an ARGB integer");});
            }
            Run("background refresh observes style changes without a text edit",style,[&]{
                Fixture f(style);f.styles=BackgroundStyles(f.text,@0xff123456u);
                auto* first=f.Context(NSWritingToolsCoordinatorContextScopeFullDocument);Require(first,"initial background context");
                f.styles=BackgroundStyles(f.text,@0xff654321u);
                auto* next=f.Context(NSWritingToolsCoordinatorContextScopeFullDocument);Require(next,"renewed background context");
                NSColor* value=[next.attributedString attribute:NSBackgroundColorAttributeName atIndex:0 effectiveRange:nil];
                Require(value && BackgroundArgb(value)==0xff654321u,"the refreshed context carries the latest brush");
            });
            Run("background absence stays absent",style,[&]{
                Fixture f(style);f.styles=BackgroundStyles(f.text,nil);
                auto* context=f.Context(NSWritingToolsCoordinatorContextScopeFullDocument);Require(context,"uncolored context");
                Require(![context.attributedString attribute:NSBackgroundColorAttributeName atIndex:0 effectiveRange:nil],"no default surface color is invented");
            });
            for (NSString* language in @[@"zh-Hans", @"ja-JP", @"ar", @""]) {
                std::string name = "language context attributes: " + std::string(language.UTF8String);
                Run(name.c_str(), style, [&]{
                    Fixture f(style); f.styles = LanguageStyles(f.text, language);
                    auto* context = f.Context(NSWritingToolsCoordinatorContextScopeFullDocument); Require(context, "tagged native context");
                    Require([[context.attributedString attribute:NSLanguageIdentifierAttributeName atIndex:0 effectiveRange:nil] isEqual:language], "Foundation language retained");
                    id shaping = [context.attributedString attribute:(__bridge NSString*)kCTLanguageAttributeName atIndex:0 effectiveRange:nil];
                    Require([shaping isEqual:language], "CoreText reads the same language, including explicit empty");
                });
            }
            for (bool coreTextOnly : {false, true}) for (NSString* language in @[@"zh-Hant", @"ja-JP", @"ar"]) {
                std::string name = std::string("language replacement ") + (coreTextOnly ? "CoreText: " : "Foundation: ") + language.UTF8String;
                Run(name.c_str(), style, [&]{
                    Fixture f(style); f.styles = LanguageStyles(f.text, @"en-GB"); f.clearStylesOnReplace = true;
                    auto* context = f.Context(NSWritingToolsCoordinatorContextScopeFullDocument); Require(context, "source language context");
                    NSString* key = coreTextOnly ? (__bridge NSString*)kCTLanguageAttributeName : NSLanguageIdentifierAttributeName;
                    auto* value = [[NSAttributedString alloc] initWithString:@"语言🙂" attributes:@{key:language}];
                    Require(f.ReplaceAttributed(context, NSMakeRange(0, 1), value), "tagged native replacement accepted");
                    Require([f.proposal[@"runs"][0][@"language"] isEqual:language], "proposal carries the language");
                    Require([f.packet[@"source"][@"runs"][0][@"language"] isEqual:@"en-GB"], "source snapshot retains its own language");
                });
            }
            Run("language adjacent runs retain independent UTF-16 ranges", style, [&]{
                Fixture f(style); auto* context = f.Context(NSWritingToolsCoordinatorContextScopeFullDocument);
                auto* value = [[NSMutableAttributedString alloc] initWithString:@"汉🙂日本語"];
                [value addAttribute:NSLanguageIdentifierAttributeName value:@"zh-Hans" range:NSMakeRange(0,3)];
                [value addAttribute:(__bridge NSString*)kCTLanguageAttributeName value:@"ja-JP" range:NSMakeRange(3,3)];
                Require(f.ReplaceAttributed(context, NSMakeRange(0, 1), value), "mixed-language native replacement accepted");
                NSArray* runs=f.proposal[@"runs"];Require(runs.count==2,"languages keep separate native runs");
                Require([runs[0][@"start"] integerValue]==0 && [runs[0][@"length"] integerValue]==3 && [runs[0][@"language"] isEqual:@"zh-Hans"],"Chinese and emoji range retained");
                Require([runs[1][@"start"] integerValue]==3 && [runs[1][@"length"] integerValue]==3 && [runs[1][@"language"] isEqual:@"ja-JP"],"Japanese range retained");
            });
            Run("language explicit empty proposal preserves clearing", style, [&]{
                Fixture f(style); auto* context = f.Context(NSWritingToolsCoordinatorContextScopeFullDocument);
                auto* value = [[NSAttributedString alloc] initWithString:@"unknown" attributes:@{NSLanguageIdentifierAttributeName:@""}];
                Require(f.ReplaceAttributed(context, NSMakeRange(0, 1), value), "explicit empty language accepted");
                Require([f.proposal[@"runs"][0][@"language"] isEqual:@""], "empty language is distinct from absent");
            });
            Run("language null source leaves native attributes unset", style, [&]{
                Fixture f(style); f.styles = LanguageStyles(f.text, NSNull.null);
                auto* context = f.Context(NSWritingToolsCoordinatorContextScopeFullDocument); Require(context, "null-language context accepted");
                Require(![context.attributedString attribute:NSLanguageIdentifierAttributeName atIndex:0 effectiveRange:nil] &&
                    ![context.attributedString attribute:(__bridge NSString*)kCTLanguageAttributeName atIndex:0 effectiveRange:nil], "null language inherits system behavior");
            });
            Run("language untagged proposal stays untagged", style, [&]{
                Fixture f(style); auto* context = f.Context(NSWritingToolsCoordinatorContextScopeFullDocument);
                Require(f.Replace(context, NSMakeRange(0, 1), @"plain"), "untagged replacement accepted");
                Require(!f.proposal[@"runs"][0][@"language"], "no language synthesized for untagged text");
            });
            Run("partial source identities survive context renewal",style,[&]{
                for (bool table:{false,true}) {
                    Fixture f(style);f.text=@"lead\nabc\ntrail";f.selection=NSMakeRange(6,1);
                    NSDictionary* container=table?@{@"id":@"table",@"row":@0,@"column":@0,@"rows":@1,@"columns":@1,@"tableColumns":@1}:
                        @{@"id":@"list",@"format":@"{decimal}",@"start":@7};
                    f.structure=@[@{@"start":@5,@"length":@4,@"origin":@"paragraph",@"lists":table?@[]:@[container],
                        @"tables":table?@[container]:@[],@"ancestors":@[@{@"kind":table?@"table":@"list",@"index":@0,
                            @"origin":@"owner"}]}];
                    auto* first=f.Context(NSWritingToolsCoordinatorContextScopeFullDocument);Require(first,"first rich context");
                    NSMutableAttributedString* value=[[first.attributedString attributedSubstringFromRange:NSMakeRange(6,1)] mutableCopy];
                    [value replaceCharactersInRange:NSMakeRange(0,value.length) withString:@"new"];
                    Require(f.Context(NSWritingToolsCoordinatorContextScopeFullDocument),"renewed rich context");
                    Require(f.ReplaceAttributed(first,NSMakeRange(6,1),value),"old live context replacement");
                    NSDictionary* source=f.packet[@"source"];
                    Require([f.packet[@"sourceStart"] unsignedIntegerValue]==6 &&
                        [Text16([[NSData alloc]initWithBase64EncodedString:source[@"text16"] options:0]) isEqual:@"b"],"source snapshot must use the absolute original range");
                    NSDictionary* old=source[@"paragraphs"][0], *updated=f.proposal[@"paragraphs"][0];
                    Require([old[@"origin"] isEqual:@"paragraph"] && [updated[@"origin"] isEqual:old[@"origin"]],"paragraph provenance changed across refresh");
                    Require([old[@"ancestors"][0][@"origin"] isEqual:@"owner"] &&
                        [updated[@"ancestors"][0][@"origin"] isEqual:old[@"ancestors"][0][@"origin"]],"owner provenance changed across refresh");
                    NSString* key=table?@"tables":@"lists";
                    Require(![old[key][0][@"id"] isEqual:updated[key][0][@"id"]],"fixture must cross native and managed identity spaces");
                }
            });
            Run("insertion source anchors are bounded at both document ends",style,[&]{
                for(NSUInteger at:{NSUInteger(0),NSUInteger(2),NSUInteger(4)}) {
                    Fixture f(style);f.text=@"abc\n";f.selection=NSMakeRange(at,0);
                    f.structure=@[@{@"start":@0,@"length":@4,@"origin":@"paragraph",@"lists":@[],@"tables":@[],@"ancestors":@[]}];
                    auto* context=f.Context(NSWritingToolsCoordinatorContextScopeFullDocument);Require(context,"insertion context");
                    auto* value=[[NSMutableAttributedString alloc]initWithString:@"X" attributes:[context.attributedString attributesAtIndex:0 effectiveRange:nil]];
                    Require(f.ReplaceAttributed(context,NSMakeRange(at,0),value),"insertion completes");
                    Require([f.packet[@"source"][@"text16"] isKindOfClass:NSString.class],"insertion source snapshot is missing");
                    Require([f.packet[@"sourceStart"] unsignedIntegerValue]==std::min(at,NSUInteger(3)),"source anchor is outside original document");
                    NSString* original=Text16([[NSData alloc]initWithBase64EncodedString:f.packet[@"source"][@"text16"] options:0]);
                    Require(original.length==1 && [original isEqual:[@"abc\n" substringWithRange:NSMakeRange(std::min(at,NSUInteger(3)),1)]],"insertion source is not the original anchor character");
                }
            });
            Run("source snapshot does not inherit mutable proposal list numbering",style,[&]{
                Fixture f(style);f.text=@"abc\n";f.selection=NSMakeRange(1,1);
                f.structure=@[@{@"start":@0,@"length":@4,@"origin":@"paragraph",@"lists":@[@{@"id":@"list",@"format":@"{decimal}",@"start":@7}],
                    @"tables":@[],@"ancestors":@[@{@"kind":@"list",@"index":@0,@"origin":@"item"}]}];
                auto* context=f.Context(NSWritingToolsCoordinatorContextScopeFullDocument);Require(context,"numbering context");
                NSParagraphStyle* paragraph=[context.attributedString attribute:NSParagraphStyleAttributeName atIndex:1 effectiveRange:nil];
                paragraph.textLists.lastObject.startingItemNumber=99;
                auto* value=[context.attributedString attributedSubstringFromRange:NSMakeRange(1,1)];
                Require(f.ReplaceAttributed(context,NSMakeRange(1,1),value),"numbering replacement completes");
                Require([f.packet[@"source"][@"paragraphs"][0][@"lists"][0][@"start"] intValue]==7 &&
                    [f.proposal[@"paragraphs"][0][@"lists"][0][@"start"] intValue]==99,"mutable native list rewrote original source metadata");
            });
            Run("malformed paragraph and owner origins reject the native snapshot",style,[&]{
                for(id origin in @[NSNull.null,@0,@YES,@"",[@"x" stringByPaddingToLength:1025 withString:@"x" startingAtIndex:0]])
                for(bool owner:{false,true}) {
                    Fixture f(style);f.text=@"abc\n";f.selection=NSMakeRange(0,0);
                    NSMutableDictionary* record=[@{@"start":@0,@"length":@4,@"lists":@[],@"tables":@[],@"ancestors":@[]} mutableCopy];
                    if(owner) { record[@"lists"]=@[@{@"id":@"list",@"format":@"{decimal}",@"start":@1}];
                        record[@"ancestors"]=@[@{@"kind":@"list",@"index":@0,@"origin":origin}]; }
                    else record[@"origin"]=origin;
                    f.structure=@[record];Require(!f.Context(NSWritingToolsCoordinatorContextScopeFullDocument),"invalid origin entered a native context");
                }
            });
            Run("new inner lists retain the existing outer table provenance",style,[&]{
                Fixture f(style);f.text=@"abc\n";f.selection=NSMakeRange(1,1);
                auto* record=[EmptyCellRecord(0,4,0,@NO,1) mutableCopy];record[@"origin"]=@"paragraph";
                record[@"ancestors"]=@[@{@"kind":@"table",@"index":@0,@"origin":@"cell"}];f.structure=@[record];
                auto* context=f.Context(NSWritingToolsCoordinatorContextScopeFullDocument);Require(context,"outer table context");
                NSMutableDictionary* attrs=[[context.attributedString attributesAtIndex:1 effectiveRange:nil] mutableCopy];
                NSMutableParagraphStyle* paragraph=[attrs[NSParagraphStyleAttributeName] mutableCopy];
                paragraph.textLists=@[[[NSTextList alloc]initWithMarkerFormat:@"{decimal}" options:0]];
                attrs[NSParagraphStyleAttributeName]=paragraph;
                auto* value=[[NSAttributedString alloc]initWithString:@"new" attributes:attrs];
                Require(f.ReplaceAttributed(context,NSMakeRange(1,1),value),"nested list replacement");
                NSArray* ancestors=f.proposal[@"paragraphs"][0][@"ancestors"];
                Require(ancestors.count==2 && [ancestors[0][@"origin"] isEqual:@"cell"] &&
                    [ancestors[1][@"kind"] isEqual:@"list"] && !ancestors[1][@"origin"],"new list lost its original outer table identity");
            });
            Run("empty and box native list markers retain their format",style,[&]{
                for (NSString* format in @[@"",@"{box}",@"{square}",@"{disc}"]) {
                    Fixture f(style); f.text=@"中文🙂\n"; f.selection=NSMakeRange(0,0);
                    f.structure=@[@{@"start":@0,@"length":@(f.text.length),@"lists":@[@{@"id":@"list",@"format":format,@"start":@4}],
                        @"tables":@[],@"ancestors":@[@{@"kind":@"list",@"index":@0,@"item":@"item"}]}];
                    auto* context=f.Context(NSWritingToolsCoordinatorContextScopeFullDocument);Require(context,"marker context");
                    NSParagraphStyle* paragraph=[context.attributedString attribute:NSParagraphStyleAttributeName atIndex:0 effectiveRange:nil];
                    NSTextList* list=paragraph.textLists.lastObject;
                    Require([list.markerFormat isEqual:format],"native list marker format changed");
                    Require((![list markerForItemNumber:4].length)==(!format.length),"empty marker fabricated a visible marker");
                    f.ReplaceAttributed(context,NSMakeRange(0,f.text.length),context.attributedString);
                    Require([f.proposal[@"paragraphs"][0][@"lists"][0][@"format"] isEqual:format],"proposal changed the list marker format");
                }
            });
            Run("authored inline separators retain one paragraph and exact UTF16",style,[&]{
                for (NSString* ending in @[@"\n",@"\r",@"\r\n",@"\u2029"]) for (bool table:{false,true}) {
                    Fixture f(style); f.text=[NSString stringWithFormat:@"a🙂%@b\n",ending];f.selection=NSMakeRange(0,0);
                    NSMutableArray* breaks=[NSMutableArray new];for(NSUInteger i=0;i<ending.length;++i)[breaks addObject:@(3+i)];
                    NSMutableDictionary* record=[(table?EmptyCellRecord(0,f.text.length,0,@NO,1):
                        @{@"start":@0,@"length":@(f.text.length),@"lists":@[],@"tables":@[],@"ancestors":@[]}) mutableCopy];
                    record[@"inlineBreaks"]=breaks;f.structure=@[record];
                    auto* context=f.Context(NSWritingToolsCoordinatorContextScopeFullDocument);Require(context,"inline-break context");
                    Require([context.attributedString.string isEqual:f.text],"inline-break export rewrote source characters");
                    f.ReplaceAttributed(context,NSMakeRange(0,f.text.length),context.attributedString);
                    NSArray* rows=f.proposal[@"paragraphs"];
                    Require(rows.count==1 && [rows[0][@"length"] unsignedIntegerValue]==f.text.length &&
                        [rows[0][@"inlineBreaks"] isEqual:breaks],"authored inline break became a paragraph boundary");
                }
            });
            Run("unmarked line endings keep native paragraph semantics",style,[&]{
                Fixture f(style);auto* context=f.Context();
                f.ReplaceAttributed(context,NSMakeRange(2,2),[[NSAttributedString alloc]initWithString:@"a\nb\n"]);
                Require([f.proposal[@"paragraphs"] count]==2 && !f.proposal[@"paragraphs"][0][@"inlineBreaks"],"new paragraphs were folded into one source paragraph");
            });
            Run("private inline break values cannot mark ordinary text",style,[&]{
                Fixture f(style);auto* context=f.Context();
                auto* value=[[NSMutableAttributedString alloc]initWithString:@"a\nb\n"];
                [value addAttribute:@"JaliumWritingToolsInlineBreak" value:@YES range:NSMakeRange(0,1)];
                [value addAttribute:@"JaliumWritingToolsInlineBreak" value:@1 range:NSMakeRange(1,1)];
                f.ReplaceAttributed(context,NSMakeRange(2,2),value);
                Require([f.proposal[@"paragraphs"] count]==2 && !f.proposal[@"paragraphs"][0][@"inlineBreaks"],"invalid private attributes altered paragraph boundaries");
            });
            Run("malformed inline-break metadata rejects the snapshot",style,[&]{
                for(id invalid in @[@[@-1],@[@99],@[@0],@[@1,@1],@[@3,@1],@[@1.5],@[@YES],@"1"]) {
                    Fixture f(style);f.text=@"a\nb\n";f.selection=NSMakeRange(0,0);
                    f.structure=@[@{@"start":@0,@"length":@4,@"lists":@[],@"tables":@[],@"inlineBreaks":invalid}];
                    Require(!f.Context(NSWritingToolsCoordinatorContextScopeFullDocument),"invalid inline-break positions reached the SDK");
                }
            });
            Run("partial proposals retain a final authored inline break",style,[&]{
                Fixture f(style);f.text=@"a\nb\n";f.selection=NSMakeRange(0,0);
                f.structure=@[@{@"start":@0,@"length":@4,@"lists":@[],@"tables":@[],@"inlineBreaks":@[@1]}];
                auto* context=f.Context(NSWritingToolsCoordinatorContextScopeFullDocument);Require(context,"partial context");
                f.ReplaceAttributed(context,NSMakeRange(0,2),[context.attributedString attributedSubstringFromRange:NSMakeRange(0,2)]);
                Require([f.proposal[@"paragraphs"] count]==1 && [f.proposal[@"paragraphs"][0][@"inlineBreaks"] isEqual:@[@1]],"clipped proposal lost its inline ending");
            });
            Run("replacing a tagged newline preserves newly authored letters",style,[&]{
                Fixture f(style);f.text=@"a\nb\n";f.selection=NSMakeRange(0,0);
                f.structure=@[@{@"start":@0,@"length":@4,@"lists":@[],@"tables":@[],@"inlineBreaks":@[@1]}];
                auto* context=f.Context(NSWritingToolsCoordinatorContextScopeFullDocument);Require(context,"tagged context");
                auto* value=[context.attributedString mutableCopy];[value replaceCharactersInRange:NSMakeRange(1,1) withString:@"z"];
                f.ReplaceAttributed(context,NSMakeRange(0,f.text.length),value);
                Require([f.proposal[@"paragraphs"] count]==1 && !f.proposal[@"paragraphs"][0][@"inlineBreaks"] &&
                    [Text16([[NSData alloc]initWithBase64EncodedString:f.proposal[@"text16"] options:0]) isEqual:@"azb\n"],"stale newline tag changed new text");
            });
            Run("legacy clients retain their original event stream",style,[&]{Fixture f(style,false);Require(!f.coordinator && f.capabilityQueries==0,"text services require explicit opt-in");});
            Run("opt-in rejects worker threads before touching AppKit",style,[&]{Fixture f(style,false);auto enable=reinterpret_cast<int32_t(*)(JaliumPlatformWindow*,int32_t)>(dlsym(RTLD_DEFAULT,"jalium_apple_window_enable_text_services"));Require(enable,"opt-in entry point");int32_t result=0;std::thread worker([&]{result=enable(f.window,1);});worker.join();Require(result==JALIUM_ERROR_INVALID_STATE && !f.view.writingToolsCoordinator,"worker thread opt-in");});
            Run("opt-in rejects a retired owner",style,[&]{Fixture f(style,false);auto enable=reinterpret_cast<int32_t(*)(JaliumPlatformWindow*,int32_t)>(dlsym(RTLD_DEFAULT,"jalium_apple_window_enable_text_services"));Require(enable,"opt-in entry point");auto* old=f.window;f.window=nullptr;jalium_window_destroy(old);Require(enable(old,1)==JALIUM_ERROR_INVALID_STATE,"retired owner opt-in");});
            Run("complete behavior retains the actual view",style,[&]{Fixture f(style);Require(f.coordinator.view==f.view && f.coordinator.preferredBehavior==NSWritingToolsBehaviorComplete,"coordinator attached in complete mode");});
            Run("selection context preserves paragraph and UTF16 offsets",style,[&]{Fixture f(style);auto* c=f.Context();Require([c.attributedString.string isEqual:@"ab🙂中文 tail\n"] && NSEqualRanges(c.range,NSMakeRange(2,2)),"selection excerpt or offset");});
            Run("full document scope preserves all text",style,[&]{Fixture f(style);auto* c=f.Context(NSWritingToolsCoordinatorContextScopeFullDocument);Require([c.attributedString.string isEqual:f.text] && c.range.length==f.text.length,"full context");});
            Run("collapsed selection evaluates its paragraph",style,[&]{Fixture f(style);f.selection=NSMakeRange(8,0);auto* c=f.Context();Require(c.range.location==0 && c.range.length==c.attributedString.length,"paragraph scope");});
            Run("context replacement maps to original storage",style,[&]{Fixture f(style);auto* c=f.Context();auto* a=f.Replace(c,NSMakeRange(2,2),@"X");Require([a.string isEqual:@"X"] && NSEqualRanges(f.lastRange,NSMakeRange(8,2)) && [f.text containsString:@"abX中文"],"mapped edit");});
            Run("replacement completion reports actual accepted text",style,[&]{Fixture f(style);f.uppercase=true;auto* a=f.Replace(f.Context(),NSMakeRange(2,2),@"x");Require([a.string isEqual:@"X"],"actual replacement acknowledgement");});
            Run("attributed proposals preserve fonts colors and UTF16",style,[&]{
                Fixture f(style);auto* c=f.Context();NSFont* font=[NSFont fontWithName:@"HelveticaNeue-BoldItalic" size:23];
                auto* value=[[NSAttributedString alloc]initWithString:@"Miii 中文🙂" attributes:@{NSFontAttributeName:font,
                    NSForegroundColorAttributeName:NSColor.redColor,NSUnderlineStyleAttributeName:@1}];
                auto* accepted=f.ReplaceAttributed(c,NSMakeRange(2,2),value);auto* run=[f.proposal[@"runs"] firstObject];
                Require([accepted.string isEqual:value.string] && [Text16([[NSData alloc]initWithBase64EncodedString:f.proposal[@"text16"] options:0]) isEqual:value.string]
                    && [run[@"family"] isEqual:font.familyName] && ![font.familyName isEqual:font.fontName]
                    && [run[@"italic"] boolValue] && [run[@"weight"] integerValue]>=600 && [run[@"size"] doubleValue]==23
                    && [run[@"foreground"] unsignedIntValue]==0xffff0000u && [run[@"underline"] intValue]==1,"styled proposal");});
            Run("paragraph alignment wire values are independent of the native ABI",style,[&]{
                const NSTextAlignment alignments[] = {NSTextAlignmentLeft, NSTextAlignmentRight, NSTextAlignmentCenter, NSTextAlignmentJustified};
                for (NSInteger alignment=0;alignment<4;++alignment) for (NSInteger direction=0;direction<2;++direction) {
                    Fixture f(style); NSDictionary* run=@{@"start":@0,@"length":@(f.text.length),@"family":@"Arial",@"size":@18,
                        @"weight":@400,@"italic":@0,@"underline":@0,@"strike":@0,@"alignment":@(alignment),@"direction":@(direction),@"language":@"zh-CN"};
                    NSData* encoded=[NSJSONSerialization dataWithJSONObject:@{@"version":@1,@"text16":Base64(f.text),@"runs":@[run]} options:0 error:nil];
                    f.styles=[[NSString alloc]initWithData:encoded encoding:NSUTF8StringEncoding];
                    auto* context=f.Context(NSWritingToolsCoordinatorContextScopeFullDocument);
                    NSParagraphStyle* exported=[context.attributedString attribute:NSParagraphStyleAttributeName atIndex:0 effectiveRange:nil];
                    Require(exported.alignment==alignments[alignment] && exported.baseWritingDirection==(NSWritingDirection)direction,"managed styles export to the actual native paragraph alignment");
                    auto* paragraph=[NSMutableParagraphStyle new];paragraph.alignment=alignments[alignment];paragraph.baseWritingDirection=(NSWritingDirection)direction;
                    f.styles=nil;
                    f.ReplaceAttributed(context,NSMakeRange(6,2),[[NSAttributedString alloc]initWithString:@"新文🙂" attributes:@{NSParagraphStyleAttributeName:paragraph}]);
                    NSDictionary* imported=[f.proposal[@"paragraphs"] firstObject];
                    Require([imported[@"alignment"] integerValue]==alignment && [imported[@"direction"] integerValue]==direction,"actual native paragraph alignment imports to stable managed values");
                }
            });
            Run("list proposals separate native markers from content",style,[&]{
                Fixture f(style);auto* list=[[NSTextList alloc]initWithMarkerFormat:@"{decimal}." options:0];list.startingItemNumber=3;
                auto* paragraph=[NSMutableParagraphStyle new];paragraph.textLists=@[list];
                NSString* plain=[NSString stringWithFormat:@"\t%@\tone\n\t%@\ttwo\n",[list markerForItemNumber:3],[list markerForItemNumber:4]];
                f.ReplaceAttributed(f.Context(),NSMakeRange(2,2),[[NSAttributedString alloc]initWithString:plain attributes:@{NSParagraphStyleAttributeName:paragraph}]);
                NSArray* rows=f.proposal[@"paragraphs"];
                Require(rows.count==2 && [rows[0][@"skipPrefix"] intValue]>0 && [rows[1][@"skipPrefix"] intValue]>0
                    && [rows[0][@"lists"][0][@"start"] intValue]==3,"list metadata or marker projection");});
            Run("table proposals retain distinct cells in one paragraph",style,[&]{
                Fixture f(style);auto* table=[NSTextTable new];table.numberOfColumns=2;
                auto* value=[[NSMutableAttributedString alloc]initWithString:@"a\tb\n"];
                for(NSInteger column=0;column<2;++column){auto* paragraph=[NSMutableParagraphStyle new];
                    paragraph.textBlocks=@[[[NSTextTableBlock alloc]initWithTable:table startingRow:0 rowSpan:1 startingColumn:column columnSpan:1]];
                    [value addAttribute:NSParagraphStyleAttributeName value:paragraph range:NSMakeRange(column*2,2)];}
                f.ReplaceAttributed(f.Context(),NSMakeRange(2,2),value);NSArray* rows=f.proposal[@"paragraphs"];
                Require(rows.count==2 && [rows[0][@"tables"][0][@"column"] intValue]==0 && [rows[1][@"tables"][0][@"column"] intValue]==1,"distinct native table cells");});
            Run("rich snapshots preserve structure and exclude virtual separators",style,[&]{
                Fixture f(style);f.text=@"a\n\tb\n\t\n";f.selection=NSMakeRange(0,1);
                f.structure=@[@{@"start":@0,@"length":@2,@"lists":@[],@"tables":@[@{@"id":@"t",@"row":@0,@"column":@0,@"rows":@1,@"columns":@1,@"tableColumns":@2}]},
                    @{@"start":@3,@"length":@2,@"lists":@[],@"tables":@[@{@"id":@"t",@"row":@0,@"column":@1,@"rows":@1,@"columns":@1,@"tableColumns":@2}]}];
                auto* c=f.Context(NSWritingToolsCoordinatorContextScopeFullDocument);
                auto* first=[c.attributedString attribute:NSParagraphStyleAttributeName atIndex:0 effectiveRange:nil];
                auto* second=[c.attributedString attribute:NSParagraphStyleAttributeName atIndex:3 effectiveRange:nil];
                Require([c.attributedString.string isEqual:f.text] && [first textBlocks].count==1 && [second textBlocks].count==1
                    && [[c.attributedString attribute:NSWritingToolsExclusionAttributeName atIndex:2 effectiveRange:nil] boolValue]
                    && ![c.attributedString attribute:NSWritingToolsExclusionAttributeName atIndex:0 effectiveRange:nil],"rich structure or UTF16 exclusion");});
            Run("mixed container ancestors survive unchanged attributed proposals",style,[&]{
                for (NSString* kinds in @[@"LT",@"TL",@"LTLT",@"TLT"]) {
                    Fixture f(style); f.text=@"内容🙂\n"; f.selection=NSMakeRange(0,0);
                    NSMutableArray* lists=[NSMutableArray new], *tables=[NSMutableArray new], *path=[NSMutableArray new];
                    for (NSUInteger i=0;i<kinds.length;++i) {
                        bool list=[kinds characterAtIndex:i]=='L'; NSUInteger index=list?lists.count:tables.count;
                        if (list) {
                            [lists addObject:@{@"id":[NSString stringWithFormat:@"list-%lu",(unsigned long)index],@"format":@"{decimal}.",@"start":@3}];
                            [path addObject:@{@"kind":@"list",@"index":@(index),@"item":[NSString stringWithFormat:@"item-%lu",(unsigned long)index]}];
                        } else {
                            [tables addObject:@{@"id":[NSString stringWithFormat:@"table-%lu",(unsigned long)index],@"row":@0,@"column":@0,@"rows":@1,@"columns":@1,@"tableColumns":@1}];
                            [path addObject:@{@"kind":@"table",@"index":@(index)}];
                        }
                    }
                    f.structure=@[@{@"start":@0,@"length":@(f.text.length),@"lists":lists,@"tables":tables,@"ancestors":path}];
                    auto* context=f.Context(NSWritingToolsCoordinatorContextScopeFullDocument);Require(context,"source context");
                    auto* accepted=f.ReplaceAttributed(context,NSMakeRange(0,f.text.length),[context.attributedString copy]);
                    NSArray* rows=f.proposal[@"paragraphs"];Require(accepted && rows.count==1,"unchanged completion or paragraph count");
                    Require([rows[0][@"ancestors"] isEqual:path] && [rows[0][@"skipPrefix"] intValue]==0,"interleaved ancestors and item ownership");
                }
            });
            Run("paragraphs in one source list item retain a shared owner",style,[&]{
                Fixture f(style); f.text=@"a\nb\n";f.selection=NSMakeRange(0,0);
                NSArray* lists=@[@{@"id":@"list",@"format":@"{decimal}.",@"start":@3}];
                NSArray* path=@[@{@"kind":@"list",@"index":@0,@"item":@"same-item"}];
                f.structure=@[@{@"start":@0,@"length":@2,@"lists":lists,@"tables":@[],@"ancestors":path},
                    @{@"start":@2,@"length":@2,@"lists":lists,@"tables":@[],@"ancestors":path}];
                auto* context=f.Context(NSWritingToolsCoordinatorContextScopeFullDocument);
                f.ReplaceAttributed(context,NSMakeRange(0,f.text.length),context.attributedString);NSArray* rows=f.proposal[@"paragraphs"];
                Require(rows.count==2 && [rows[0][@"ancestors"][0][@"item"] isEqual:@"same-item"] &&
                    [rows[1][@"ancestors"][0][@"item"] isEqual:@"same-item"],"multi-paragraph item ownership");
            });
            Run("source virtual table separators have explicit wire boundaries",style,[&]{
                Fixture f(style);NSString* left=@"left\n",*right=@"right\n";f.text=[NSString stringWithFormat:@"%@\t%@\t\n",left,right];f.selection=NSMakeRange(0,0);
                NSArray* path=@[@{@"kind":@"table",@"index":@0}];
                f.structure=@[@{@"start":@0,@"length":@(left.length),@"lists":@[],@"tables":@[@{@"id":@"t",@"row":@0,@"column":@0,@"rows":@1,@"columns":@1,@"tableColumns":@2}],@"ancestors":path},
                    @{@"start":@(left.length+1),@"length":@(right.length),@"lists":@[],@"tables":@[@{@"id":@"t",@"row":@0,@"column":@1,@"rows":@1,@"columns":@1,@"tableColumns":@2}],@"ancestors":path}];
                auto* context=f.Context(NSWritingToolsCoordinatorContextScopeFullDocument);
                f.ReplaceAttributed(context,NSMakeRange(0,f.text.length),context.attributedString);NSArray* rows=f.proposal[@"paragraphs"];
                Require(rows.count==4 && [rows[1][@"storageSeparator"] boolValue] && [rows[3][@"storageSeparator"] boolValue],"virtual storage records");
                Require([rows[0][@"tables"][0][@"column"] intValue]==0 && [rows[2][@"tables"][0][@"column"] intValue]==1 &&
                    [rows[2][@"start"] unsignedIntegerValue]==left.length+1,"real cell metadata survives separators");
            });
            Run("authored source text resembling a marker is not removed",style,[&]{
                Fixture f(style);f.text=@"\t3.\t这也是用户文字🙂\n";f.selection=NSMakeRange(0,0);
                f.structure=@[@{@"start":@0,@"length":@(f.text.length),@"lists":@[@{@"id":@"list",@"format":@"{decimal}.",@"start":@3}],@"tables":@[],
                    @"ancestors":@[@{@"kind":@"list",@"index":@0,@"item":@"item"}]}];
                auto* context=f.Context(NSWritingToolsCoordinatorContextScopeFullDocument);f.ReplaceAttributed(context,NSMakeRange(0,f.text.length),context.attributedString);
                NSDictionary* row=[f.proposal[@"paragraphs"] firstObject];Require([row[@"skipPrefix"] integerValue]==0 && [row[@"ancestors"] count]==1,"authored marker-looking prefix");
            });
            Run("raw empty cell retains native table provenance and exclusion",style,[&]{
                Fixture f(style); f.text=@"\tX\n\t\n"; f.selection=NSMakeRange(1,1);
                f.structure=@[EmptyCellRecord(0,1,0,@YES),EmptyCellRecord(1,2,1,@NO)];
                auto* context=f.Context(NSWritingToolsCoordinatorContextScopeFullDocument);
                Require(context && [context.attributedString.string isEqual:f.text],"empty cell context projection");
                auto* paragraph=[context.attributedString attribute:NSParagraphStyleAttributeName atIndex:0 effectiveRange:nil];
                Require([paragraph textBlocks].count==1 &&
                    [[context.attributedString attribute:NSWritingToolsExclusionAttributeName atIndex:0 effectiveRange:nil] boolValue] &&
                    ![context.attributedString attribute:NSWritingToolsExclusionAttributeName atIndex:1 effectiveRange:nil],"empty slot is structured storage, authored neighbor stays editable");
                f.ReplaceAttributed(context,NSMakeRange(0,f.text.length),context.attributedString);
                NSArray* rows=f.proposal[@"paragraphs"];
                Require(rows.count==3 && [rows[0][@"emptyCell"] boolValue] && !rows[0][@"storageSeparator"] &&
                    [rows[1][@"tables"][0][@"column"] intValue]==1 && [rows[2][@"storageSeparator"] boolValue],"empty cell is distinct from an omitted separator");
            });
            Run("adjacent raw empty cells remain distinct native records",style,[&]{
                Fixture f(style); f.text=@"\t\t\n"; f.selection=NSMakeRange(0,0);
                f.structure=@[EmptyCellRecord(0,1,0,@YES),EmptyCellRecord(1,1,1,@YES)];
                auto* context=f.Context(NSWritingToolsCoordinatorContextScopeFullDocument); Require(context,"all-empty source context");
                f.ReplaceAttributed(context,NSMakeRange(0,f.text.length),context.attributedString); NSArray* rows=f.proposal[@"paragraphs"];
                Require(rows.count==3 && [rows[0][@"emptyCell"] boolValue] && [rows[1][@"emptyCell"] boolValue] &&
                    [rows[0][@"tables"][0][@"column"] intValue]==0 && [rows[1][@"tables"][0][@"column"] intValue]==1,"all-empty cells kept separate");
            });
            Run("copied empty provenance cannot omit newly authored characters",style,[&]{
                Fixture f(style); f.text=@"\tX\n\t\n"; f.selection=NSMakeRange(1,1);
                f.structure=@[EmptyCellRecord(0,1,0,@YES),EmptyCellRecord(1,2,1,@NO)];
                auto* context=f.Context(NSWritingToolsCoordinatorContextScopeFullDocument);
                NSMutableAttributedString* value=[context.attributedString mutableCopy];
                NSDictionary* attributes=[value attributesAtIndex:0 effectiveRange:nil];
                [value replaceCharactersInRange:NSMakeRange(0,1) withString:@"Q"];
                [value setAttributes:attributes range:NSMakeRange(0,1)];
                [value addAttribute:@"JaliumWritingToolsEmptyCell" value:@YES range:NSMakeRange(0,1)];
                f.ReplaceAttributed(context,NSMakeRange(0,f.text.length),value); NSDictionary* row=[f.proposal[@"paragraphs"] firstObject];
                Require(!row[@"emptyCell"] && !row[@"storageSeparator"] &&
                    [Text16([[NSData alloc]initWithBase64EncodedString:f.proposal[@"text16"] options:0]) hasPrefix:@"Q"],"authored glyph survives copied provenance");
            });
            Run("changed native table style retires empty-cell provenance",style,[&]{
                Fixture f(style); f.text=@"\tX\n\t\n"; f.selection=NSMakeRange(1,1);
                f.structure=@[EmptyCellRecord(0,1,0,@YES),EmptyCellRecord(1,2,1,@NO)];
                auto* context=f.Context(NSWritingToolsCoordinatorContextScopeFullDocument); NSMutableAttributedString* value=[context.attributedString mutableCopy];
                NSMutableParagraphStyle* paragraph=[[value attribute:NSParagraphStyleAttributeName atIndex:0 effectiveRange:nil] mutableCopy];
                paragraph.textBlocks=@[]; [value addAttribute:NSParagraphStyleAttributeName value:paragraph range:NSMakeRange(0,1)];
                [value addAttribute:@"JaliumWritingToolsEmptyCell" value:@YES range:NSMakeRange(0,1)];
                f.ReplaceAttributed(context,NSMakeRange(0,f.text.length),value); NSDictionary* row=[f.proposal[@"paragraphs"] firstObject];
                Require(!row[@"emptyCell"] && !row[@"ancestors"] && [row[@"tables"] count]==0,"stale private empty marker does not rebuild the old table");
            });
            Run("malformed empty-cell source records cannot create contexts",style,[&]{
                for (int problem=0;problem<5;++problem) {
                    Fixture f(style); f.text=problem==2?@"QX\n\t\n":@"\tX\n\t\n";f.selection=NSMakeRange(1,1);
                    NSMutableDictionary* first=[EmptyCellRecord(0,problem==4?2:1,0,@YES) mutableCopy];
                    if(problem==0)first[@"emptyCell"]=@1;
                    if(problem==1)first[@"emptyCell"]=@"true";
                    if(problem==3){first[@"tables"]=@[];first[@"ancestors"]=@[];}
                    f.structure=@[first,EmptyCellRecord(problem==4?2:1,problem==4?1:2,1,@NO)];
                    Require(!f.Context(NSWritingToolsCoordinatorContextScopeFullDocument),"invalid empty metadata created a context");
                }
            });
            Run("real empty paragraph is not converted to a zero-block cell",style,[&]{
                Fixture f(style); f.text=@"\n\t\n"; f.selection=NSMakeRange(0,0);
                f.structure=@[EmptyCellRecord(0,1,0,@NO,1)];
                auto* context=f.Context(NSWritingToolsCoordinatorContextScopeFullDocument); Require(context,"empty paragraph context");
                f.ReplaceAttributed(context,NSMakeRange(0,f.text.length),context.attributedString); NSDictionary* row=[f.proposal[@"paragraphs"] firstObject];
                Require(!row[@"emptyCell"] && !row[@"storageSeparator"] && [row[@"length"] intValue]==1 &&
                    [row[@"tables"] count]==1,"authored empty paragraph retains paragraph semantics");
            });
            Run("font runs do not manufacture new paragraphs",style,[&]{
                for (bool table : {false,true}) {
                    Fixture f(style);f.text=@"Miii 中文 e\u0301 🙂\n";f.selection=NSMakeRange(0,0);
                    f.structure=table?@[EmptyCellRecord(0,f.text.length,0,@NO,1)]:
                        @[@{@"start":@0,@"length":@(f.text.length),@"lists":@[],@"tables":@[],@"ancestors":@[]}];
                    auto* context=f.Context(NSWritingToolsCoordinatorContextScopeFullDocument);Require(context,"formatted source context");
                    NSMutableAttributedString* value=[context.attributedString mutableCopy];
                    [value addAttribute:NSFontAttributeName value:[NSFontManager.sharedFontManager convertFont:[NSFont systemFontOfSize:18] toHaveTrait:NSItalicFontMask] range:NSMakeRange(0,value.length-1)];
                    f.ReplaceAttributed(context,NSMakeRange(0,f.text.length),value);NSArray* rows=f.proposal[@"paragraphs"];
                    Require(rows.count==1 && [rows[0][@"length"] unsignedIntegerValue]==value.length &&
                        !rows[0][@"storageSeparator"],"font boundary was exported as a paragraph boundary");
                }
            });
            Run("ordinary excluded content is not a virtual storage separator",style,[&]{
                Fixture f(style);auto* value=[[NSAttributedString alloc]initWithString:@"保留🙂" attributes:@{NSWritingToolsExclusionAttributeName:@YES}];
                f.ReplaceAttributed(f.Context(),NSMakeRange(2,2),value);NSDictionary* row=[f.proposal[@"paragraphs"] firstObject];
                Require(!row[@"storageSeparator"] && [Text16([[NSData alloc]initWithBase64EncodedString:f.proposal[@"text16"] options:0]) isEqual:value.string],"authored excluded text preserved");
            });
            Run("changed native containers do not reuse stale private ancestry",style,[&]{
                Fixture f(style);f.text=@"a\n";f.selection=NSMakeRange(0,0);
                f.structure=@[@{@"start":@0,@"length":@2,@"lists":@[@{@"id":@"list",@"format":@"{decimal}.",@"start":@3}],@"tables":@[],
                    @"ancestors":@[@{@"kind":@"list",@"index":@0,@"item":@"item"}]}];
                auto* context=f.Context(NSWritingToolsCoordinatorContextScopeFullDocument);NSMutableAttributedString* value=[context.attributedString mutableCopy];
                [value addAttribute:NSParagraphStyleAttributeName value:NSParagraphStyle.defaultParagraphStyle range:NSMakeRange(0,value.length)];
                f.ReplaceAttributed(context,NSMakeRange(0,f.text.length),value);NSDictionary* row=[f.proposal[@"paragraphs"] firstObject];
                Require(!row[@"ancestors"] && ![row[@"lists"] count],"stale carrier rejected after native style replacement");
            });
            Run("invalid managed ancestry cannot create a native context",style,[&]{
                for (NSArray* path in @[@[],@[@{@"kind":@"unknown",@"index":@0}],@[@{@"kind":@"list",@"index":@1}],
                    @[@{@"kind":@"list",@"index":@0,@"item":@42}],@[@{@"kind":@"list",@"index":@YES}]]) {
                    Fixture f(style);f.text=@"a\n";f.selection=NSMakeRange(0,0);
                    f.structure=@[@{@"start":@0,@"length":@2,@"lists":@[@{@"id":@"list",@"format":@"{decimal}.",@"start":@3}],@"tables":@[],@"ancestors":path}];
                    Require(!f.Context(NSWritingToolsCoordinatorContextScopeFullDocument),"malformed source ancestry rejected");
                }
            });
            Run("repeated content focus replaces retired writing tools ownership",style,[&]{
                Fixture f(style);auto focus=reinterpret_cast<int32_t(*)(JaliumPlatformWindow*)>(dlsym(RTLD_DEFAULT,"jalium_apple_window_set_keyboard_focus"));Require(focus,"keyboard focus entry point");
                Require(focus(f.window)==JALIUM_OK,"prime the content responder");
                f.coordinator=f.view.writingToolsCoordinator;f.delegate=f.coordinator.delegate;
                auto* original=f.Context(NSWritingToolsCoordinatorContextScopeFullDocument);Require(original,"primed SDK context");
                NSWritingToolsCoordinator* previous=f.coordinator;id<NSWritingToolsCoordinatorDelegate> previousClient=f.delegate;
                Require(focus(f.window)==JALIUM_OK,"repeat focus on the existing content responder");
                f.coordinator=f.view.writingToolsCoordinator;f.delegate=f.coordinator.delegate;
                Require(f.coordinator && f.coordinator!=previous && f.Context(NSWritingToolsCoordinatorContextScopeFullDocument),"successor SDK session is usable after repeated focus");
                __block NSArray* old=nil;[previousClient writingToolsCoordinator:previous requestsContextsForScope:NSWritingToolsCoordinatorContextScopeFullDocument completion:^(NSArray* values){old=values;}];
                Require(old && !old.count,"retired coordinator cannot expose a replacement session");
            });
            Run("content focus refresh revalidates a callback hiding the owner",style,[&]{
                Fixture f(style);auto focus=reinterpret_cast<int32_t(*)(JaliumPlatformWindow*)>(dlsym(RTLD_DEFAULT,"jalium_apple_window_set_keyboard_focus"));Require(focus,"keyboard focus entry point");
                Require(focus(f.window)==JALIUM_OK,"prime native content focus");
                f.after=[&](int kind){if(kind==0)jalium_window_hide(f.window);};
                Require(focus(f.window)==JALIUM_ERROR_INVALID_STATE && !f.view.window.visible && !f.view.writingToolsCoordinator,"refresh callback hiding a window retires the outer focus request");
            });
            Run("read-only storage completes replacement without edits",style,[&]{Fixture f(style);f.canEdit=false;NSString* before=f.text;Require(!f.Replace(f.Context(),NSMakeRange(2,2),@"x") && [f.text isEqual:before] && !f.replacements,"read-only edit");});
            Run("rejected proposals still report a callback's own edit",style,[&]{Fixture f(style);auto* context=f.Context();int snapshots=f.snapshotQueries;f.canEdit=false;
                f.after=[&](int kind){if(kind==2){f.text=[f.text stringByAppendingString:@" user"];f.Publish();}};
                Require(!f.Replace(context,NSMakeRange(2,2),@"x") && f.snapshotQueries==snapshots+1 && [f.text hasSuffix:@" user"],"callback edit must be synchronized after rejecting the proposal");});
            Run("foreign session reply cannot create a context",style,[&]{Fixture f(style);f.wrongSession=true;Require(!f.Context(),"mismatched coordinator session");});
            Run("legacy point lookup maps into the latest context",style,[&]{Fixture f(style);auto* c=f.Context();__block NSRange range=NSMakeRange(NSNotFound,0);__block NSUUID* identifier=nil;
                [f.delegate writingToolsCoordinator:f.coordinator requestsRangeInContextWithIdentifierForPoint:NSMakePoint(1,1) completion:^(NSRange r,NSUUID* uuid){range=r;identifier=uuid;}];
                Require(NSEqualRanges(range,NSMakeRange(2,0)) && [identifier isEqual:c.identifier],"legacy point context mapping");});
            Run("unknown context cannot mutate storage",style,[&]{Fixture f(style);auto* c=[[NSWritingToolsCoordinatorContext alloc]initWithAttributedString:[[NSAttributedString alloc]initWithString:@"foreign"] range:NSMakeRange(0,7)];Require(!f.Replace(c,NSMakeRange(0,1),@"x") && f.replacements==0,"foreign context");});
            Run("invalid ranges complete without mutation",style,[&]{Fixture f(style);Require(!f.Replace(f.Context(),NSMakeRange(NSNotFound,1),@"x") && !f.replacements,"invalid range");});
            Run("selection maps all provided ranges",style,[&]{Fixture f(style);auto* c=f.Context();__block int done=0;[f.delegate writingToolsCoordinator:f.coordinator selectRanges:@[[NSValue valueWithRange:NSMakeRange(2,2)]] inContext:c completion:^{++done;}];Require(done==1 && [f.ranges isEqual:@[@[@8,@2]]],"selection mapping");});
            Run("geometry returns separate actual segments",style,[&]{Fixture f(style);auto* c=f.Context();__block NSArray* paths=nil;[f.delegate writingToolsCoordinator:f.coordinator requestsBoundingBezierPathsForRange:NSMakeRange(2,2) inContext:c completion:^(NSArray* p){paths=p;}];CGFloat scale=f.view.window.backingScaleFactor;Require(paths.count==2 && NSEqualRects([paths.firstObject bounds],NSMakeRect(20/scale,40/scale,60/scale,20/scale)),"segment geometry");});
            Run("malformed geometry is rejected",style,[&]{Fixture f(style);f.geometryInvalid=true;auto* c=f.Context();__block NSArray* paths=nil;[f.delegate writingToolsCoordinator:f.coordinator requestsBoundingBezierPathsForRange:NSMakeRange(2,2) inContext:c completion:^(NSArray* p){paths=p;}];Require(paths.count==0,"malformed geometry");});
            Run("transparent preview retains image and candidate rectangles",style,[&]{Fixture f(style);auto* c=f.Context();__block NSArray* previews=nil;[f.delegate writingToolsCoordinator:f.coordinator requestsPreviewForTextAnimation:NSWritingToolsCoordinatorTextAnimationRemove ofRange:NSMakeRange(2,2) inContext:c completion:^(NSArray* p){previews=p;}];auto* p=(NSTextPreview*)previews.firstObject;Require(previews.count==1 && CGImageGetWidth(p.previewImage)==2 && p.candidateRects.count==1,"preview data");});
            Run("shortened replacement still finishes original animation",style,[&]{Fixture f(style);auto* c=f.Context();__block int done=0;NSRange range=NSMakeRange(0,c.attributedString.length);[f.delegate writingToolsCoordinator:f.coordinator prepareForTextAnimation:NSWritingToolsCoordinatorTextAnimationRemove forRange:range inContext:c completion:^{++done;}];f.Replace(c,range,@"x");[f.delegate writingToolsCoordinator:f.coordinator finishTextAnimation:NSWritingToolsCoordinatorTextAnimationRemove forRange:range inContext:c completion:^{++done;}];Require(done==2 && f.prepared==1 && f.finished==1,"animation cleanup after shrink");});
            Run("editor round trip retires old contexts",style,[&]{Fixture f(style);auto* c=f.Context();jalium_window_update_ime_context(f.window,0,nullptr,0,0,0,0,0,0);f.editor=@"other";f.Publish();f.editor=@"first-editor";f.Publish();Require(!f.Replace(c,NSMakeRange(2,2),@"x") && f.replacements==0,"retired context");});
            Run("hide detaches coordinator and clears presentation",style,[&]{Fixture f(style);f.Context();jalium_window_hide(f.window);Require(!f.view.writingToolsCoordinator && f.stopped==1,"hidden service cleanup");});
            Run("stop callback's new Show supersedes the old Hide",style,[&]{Fixture f(style);f.Context();
                f.after=[&](int kind){if(kind==8)jalium_apple_window_show(f.window,0);};
                jalium_window_hide(f.window);Require(f.view.window.visible,"old hide overrode callback show");});
            Run("stop callback's Enable retains the current responder",style,[&]{Fixture f(style);
                Require([f.view.window makeFirstResponder:f.view],"prepare input responder");f.Publish();
                f.coordinator=f.view.writingToolsCoordinator;f.delegate=f.coordinator.delegate;f.Context();
                f.after=[&](int kind){if(kind==8)jalium_window_set_enabled(f.window,1);};
                jalium_window_set_enabled(f.window,0);Require(f.view.window.firstResponder==f.view,"old disable cleared the newly enabled responder");});
            Run("capability callback opt-out cannot attach a coordinator",style,[&]{Fixture f(style,false);
                auto enable=reinterpret_cast<int32_t(*)(JaliumPlatformWindow*,int32_t)>(dlsym(RTLD_DEFAULT,"jalium_apple_window_enable_text_services"));Require(enable,"opt-in entry point");
                f.after=[&](int kind){if(kind==0)enable(f.window,0);};enable(f.window,1);
                Require(!f.view.writingToolsCoordinator,"old capability reply revived opted-out services");});
            Run("destroy during context reply completes with no context",style,[&]{Fixture f(style);f.after=[&](int kind){if(kind==1){auto* w=f.window;f.window=nullptr;jalium_window_destroy(w);}};Require(!f.Context(),"stale context returned after callback destruction");});
        }
        jalium_platform_shutdown();
        std::printf("macOS Writing Tools SDK scenarios: %d/%d passed\n",passed,passed+failed);
        std::printf("OBSERVATION active=%d foreground=%d process=%d\n",NSApp.active,NSWorkspace.sharedWorkspace.frontmostApplication.processIdentifier,NSProcessInfo.processInfo.processIdentifier);
        return failed?1:0;
    }
}


#else
int main(){std::puts("SKIP Writing Tools requires the macOS 15.2 SDK");return 77;}
#endif

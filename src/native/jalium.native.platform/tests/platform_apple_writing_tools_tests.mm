#import <AppKit/AppKit.h>
#include "jalium_platform.h"
#include <dlfcn.h>
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
    NSString* styles;
    bool wrongSession = false;
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
            f.proposal=args[@"proposal"];
            ++f.replacements;f.lastRange=NSMakeRange(r.start,r.length);
            NSString* value=Text16([[NSData alloc]initWithBase64EncodedString:args[@"text16"] options:0]);
            if(f.uppercase)value=value.uppercaseString;
            f.text=[f.text stringByReplacingCharactersInRange:f.lastRange withString:value];f.Publish();
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

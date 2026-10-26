#include "jalium_platform.h"
#import <AppKit/AppKit.h>
#include <cmath>
#include <cstdio>
#include <cstring>
#include <functional>
#include <limits>
#include <memory>
#include <stdexcept>
#include <string>
#include <vector>

@interface JaliumScalarRequestObserver : NSObject {
@public std::function<void()> action; bool prior; unsigned count;
}
@end
@implementation JaliumScalarRequestObserver
- (void)observeValueForKeyPath:(NSString*)key ofObject:(id)object change:(NSDictionary*)change context:(void*)context {
    if ([change[NSKeyValueChangeNotificationIsPriorKey] boolValue] == prior) {
        ++count; auto callback = action; if (callback) callback();
    }
}
@end

namespace {
void Require(bool ok, const char* message) { if (!ok) throw std::runtime_error(message); }
struct Window {
    JaliumPlatformWindow* value = nullptr;
    NSWindow* native = nil;
    unsigned destroyed = 0;
    explicit Window(bool custom) {
        JaliumWindowParams p{}; p.title = reinterpret_cast<const JaliumUtf16Char*>(u"Scalar initial 中文🙂");
        p.width = 360; p.height = 220; p.x = p.y = 100;
        p.style = JALIUM_WINDOW_STYLE_RESIZABLE | JALIUM_WINDOW_STYLE_CLOSABLE |
            (custom ? JALIUM_WINDOW_STYLE_BORDERLESS : JALIUM_WINDOW_STYLE_TITLEBAR);
        value = jalium_window_create(&p); Require(value != nullptr, "create hidden window");
        native = ((__bridge NSView*)(void*)jalium_window_get_native_handle(value)).window;
        Require(native && !native.visible, "real hidden native window");
        jalium_window_set_event_callback(value, [](const JaliumPlatformEvent* e, void* state) {
            if (e->type == JALIUM_EVENT_DESTROYED) ++static_cast<Window*>(state)->destroyed;
        }, this);
    }
    void Close() { if (value) { auto* old = value; value = nullptr; jalium_window_destroy(old); } }
    ~Window() { Close(); }
};
struct Case { bool custom, prior; int kind; const char* mode; };
std::vector<Case> Cases() {
    std::vector<Case> cases;
    for (bool custom : {false,true}) for (bool prior : {false,true}) for (int kind = 0; kind < 4; ++kind)
        for (const char* mode : {"control","latest","latest-twice","close","replacement"}) cases.push_back({custom,prior,kind,mode});
    for (bool custom : {false,true}) for (bool prior : {false,true}) {
        cases.push_back({custom,prior,0,"empty-title"}); cases.push_back({custom,prior,1,"reject-nan"});
        for (int kind : {2,3}) { cases.push_back({custom,prior,kind,"unbounded"}); cases.push_back({custom,prior,kind,"coerced"}); }
    }
    return cases;
}
NSString* Key(int kind) { return kind == 0 ? @"title" : kind == 1 ? @"alphaValue" : kind == 2 ? @"contentMinSize" : @"contentMaxSize"; }
int Apply(Window& window, int kind, int request) {
    static const char16_t* titles[] = {u"Outer Miii 中文🙂",u"Latest Miii 中文🙂",u"Newest 中文🙂 Miii",nullptr};
    if (kind == 0) { jalium_window_set_title(window.value, reinterpret_cast<const JaliumUtf16Char*>(titles[request])); return JALIUM_OK; }
    if (kind == 1) { double values[] = {.7,.3,.45}; return jalium_window_set_opacity(window.value, values[request]); }
    static const int bounds[][4] = {{100,100,600,600},{180,160,420,400},{210,190,360,340},{0,0,0,0},{180,160,100,80}};
    const auto& b = bounds[request]; return jalium_window_set_min_max_size(window.value,b[0],b[1],b[2],b[3]);
}
bool Matches(Window& window, int kind, int request) {
    if (kind == 0) {
        NSString* titles[] = {@"Outer Miii 中文🙂",@"Latest Miii 中文🙂",@"Newest 中文🙂 Miii",@""};
        return [window.native.title isEqualToString:titles[request]];
    }
    if (kind == 1) { double values[] = {.7,.3,.45}; return std::abs(window.native.alphaValue-values[request]) < 1e-8; }
    double scale = jalium_window_get_dpi_scale(window.value);
    if (request == 3) return NSEqualSizes(window.native.contentMinSize,NSZeroSize) &&
        window.native.contentMaxSize.width >= 1e30 && window.native.contentMaxSize.height >= 1e30;
    static const int bounds[][4] = {{100,100,600,600},{180,160,420,400},{210,190,360,340},{0,0,0,0},{180,160,180,160}};
    const auto& b = bounds[request]; return NSEqualSizes(window.native.contentMinSize,NSMakeSize(b[0]/scale,b[1]/scale)) &&
        NSEqualSizes(window.native.contentMaxSize,NSMakeSize(b[2]/scale,b[3]/scale));
}
void Run(const Case& c) {
    Window window(c.custom); std::unique_ptr<Window> replacement;
    auto* observer = [JaliumScalarRequestObserver new]; observer->prior = c.prior;
    bool entered = false, immediate = true; int nestedStatus = JALIUM_OK, expected = 0;
    observer->action = [&] {
        if (entered || std::strcmp(c.mode,"control") == 0) return;
        entered = true;
        if (std::strcmp(c.mode,"close") == 0 || std::strcmp(c.mode,"replacement") == 0) {
            window.Close();
            if (std::strcmp(c.mode,"replacement") == 0) { replacement = std::make_unique<Window>(c.custom); nestedStatus = Apply(*replacement,c.kind,2); }
            return;
        }
        if (std::strcmp(c.mode,"reject-nan") == 0) {
            nestedStatus = jalium_window_set_opacity(window.value,std::numeric_limits<double>::quiet_NaN()); return;
        }
        expected = std::strcmp(c.mode,"empty-title") == 0 ? 3 : std::strcmp(c.mode,"unbounded") == 0 ? 3 : std::strcmp(c.mode,"coerced") == 0 ? 4 : 1;
        nestedStatus = Apply(window,c.kind,expected); immediate = Matches(window,c.kind,expected);
        if (std::strcmp(c.mode,"latest-twice") == 0) { expected = 2; nestedStatus = Apply(window,c.kind,expected); immediate = immediate && Matches(window,c.kind,expected); }
    };
    [window.native addObserver:observer forKeyPath:Key(c.kind) options:NSKeyValueObservingOptionPrior context:nullptr];
    int outerStatus = Apply(window,c.kind,0);
    observer->action = {}; [window.native removeObserver:observer forKeyPath:Key(c.kind)];
    Require(observer->count != 0, "real AppKit notification for the selected phase");
    Require(entered == (std::strcmp(c.mode,"control") != 0), "callback entered inside the outer SDK setter");
    if (!window.value) {
        Require(window.destroyed == 1 && !window.native.visible && window.native.delegate == nil,"closed original is detached once");
        Require(outerStatus == JALIUM_OK || outerStatus == JALIUM_ERROR_INVALID_STATE,"retired request returns a lifetime outcome");
        if (replacement) Require(nestedStatus == JALIUM_OK && Matches(*replacement,c.kind,2) && !replacement->native.visible,"old request does not change replacement");
    } else {
        Require(outerStatus == JALIUM_OK,"surviving outer request succeeds");
        Require(nestedStatus == (std::strcmp(c.mode,"reject-nan") == 0 ? JALIUM_ERROR_INVALID_ARGUMENT : JALIUM_OK),"nested request validation");
        Require(immediate,"nested setters remain synchronous"); Require(Matches(window,c.kind,expected),"last requested native value survives the outer setter");
        Require(window.destroyed == 0 && !window.native.visible,"surviving window remains hidden");
        // Normal subsequent calls must still work after a repaired request.
        Require(Apply(window,c.kind,0) == JALIUM_OK && Matches(window,c.kind,0),"subsequent independent request");
    }
}
}
int main(int argc,char** argv) { @autoreleasepool {
    setvbuf(stdout,nullptr,_IONBF,0); auto cases = Cases();
    if (argc == 2 && std::strcmp(argv[1],"--list") == 0) { for (size_t i=0;i<cases.size();++i) { const auto& c=cases[i];std::printf("%zu %s %s %d %s\n",i,c.custom?"Custom":"Native",c.prior?"prior":"after",c.kind,c.mode); } return 0; }
    Require(jalium_platform_init()==JALIUM_OK,"platform init");[NSApp setActivationPolicy:NSApplicationActivationPolicyAccessory];if(!NSApp.running)[NSApp finishLaunching];
    int selected=argc==2&&std::strncmp(argv[1],"--case=",7)==0?std::stoi(argv[1]+7):-1;unsigned total=0,passed=0;
    for(size_t i=0;i<cases.size();++i) { if(selected>=0&&int(i)!=selected)continue;const auto& c=cases[i];++total;
        try { Run(c);++passed;std::printf("PASS scalar %zu %s/%s/%d/%s\n",i,c.custom?"Custom":"Native",c.prior?"prior":"after",c.kind,c.mode); }
        catch(const std::exception& e) {std::fprintf(stderr,"FAIL scalar %zu %s/%s/%d/%s: %s\n",i,c.custom?"Custom":"Native",c.prior?"prior":"after",c.kind,c.mode,e.what());}
    }
    jalium_platform_shutdown();std::printf("macOS scalar request checks: %u/%u; hidden owned windows, no input or system writes\n",passed,total);return total&&passed==total?0:1;
}}

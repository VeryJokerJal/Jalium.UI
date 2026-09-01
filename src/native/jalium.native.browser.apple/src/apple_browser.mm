#include "jalium_browser_api.h"

#include <algorithm>
#include <cstdlib>
#include <cstring>
#include <memory>
#include <string>

#import <TargetConditionals.h>
#import <Foundation/Foundation.h>
#import <QuartzCore/QuartzCore.h>
#if !TARGET_OS_TV
#import <WebKit/WebKit.h>
#endif
#if TARGET_OS_OSX
#import <AppKit/AppKit.h>
#else
#import <UIKit/UIKit.h>
#endif

namespace {
constexpr int kOk=0;
constexpr int kNotImplemented=static_cast<int>(0x80004001u);
constexpr int kInvalidArgument=static_cast<int>(0x80070057u);
constexpr int kFailure=static_cast<int>(0x80004005u);

NSString* FromManagedUtf16(const wchar_t* value)
{
    if(!value)return @"";const uint16_t* chars=reinterpret_cast<const uint16_t*>(value);
    size_t length=0;while(chars[length]&&length<(1u<<24))++length;
    return [[NSString alloc]initWithCharacters:(const unichar*)chars length:length];
}
wchar_t* CopyManagedUtf16(NSString* value)
{
    if(!value)value=@"";NSUInteger length=value.length;
    auto* result=(uint16_t*)malloc((length+1)*sizeof(uint16_t));if(!result)return nullptr;
    [value getCharacters:(unichar*)result range:NSMakeRange(0,length)];result[length]=0;
    return reinterpret_cast<wchar_t*>(result);
}
}

struct JaliumWebView2EnvironmentHandle { int marker=1; };

#if TARGET_OS_TV
struct JaliumWebView2ControllerHandle {
    int marker=1;
    double zoom=1.0;
    uint32_t background=0xffffffffu;
};
#else
@class JaliumWKDelegate;
struct JaliumWebView2ControllerHandle {
    WKWebView* view=nil;
    JaliumWKDelegate* delegate=nil;
    CGRect bounds{};
    uint32_t background=0xffffffffu;
    double zoom=1.0;
    void* userData=nullptr;
    jalium_webview2_navigation_starting_callback navigationStarting=nullptr;
    jalium_webview2_navigation_completed_callback navigationCompleted=nullptr;
    jalium_webview2_source_changed_callback sourceChanged=nullptr;
    jalium_webview2_content_loading_callback contentLoading=nullptr;
    jalium_webview2_document_title_changed_callback titleChanged=nullptr;
    jalium_webview2_web_message_received_callback messageReceived=nullptr;
    jalium_webview2_new_window_requested_callback newWindowRequested=nullptr;
    jalium_webview2_process_failed_callback processFailed=nullptr;
    jalium_webview2_zoom_factor_changed_callback zoomChanged=nullptr;
    jalium_webview2_cursor_changed_callback cursorChanged=nullptr;
    void* cursorUserData=nullptr;
};

@interface JaliumWKDelegate : NSObject <WKNavigationDelegate,WKUIDelegate,WKScriptMessageHandler>
@property(nonatomic,assign) JaliumWebView2ControllerHandle* owner;
@end
@implementation JaliumWKDelegate
- (void)webView:(WKWebView*)webView decidePolicyForNavigationAction:(WKNavigationAction*)action decisionHandler:(void(^)(WKNavigationActionPolicy))handler {
    int cancel=0;auto* o=_owner;if(o&&o->navigationStarting){wchar_t* uri=CopyManagedUtf16(action.request.URL.absoluteString);o->navigationStarting(o->userData,uri,action.navigationType==WKNavigationTypeOther,&cancel);free(uri);}handler(cancel?WKNavigationActionPolicyCancel:WKNavigationActionPolicyAllow);
}
- (void)webView:(WKWebView*)webView didStartProvisionalNavigation:(WKNavigation*)navigation {if(_owner&&_owner->contentLoading)_owner->contentLoading(_owner->userData,0);}
- (void)webView:(WKWebView*)webView didFinishNavigation:(WKNavigation*)navigation {auto* o=_owner;if(!o)return;if(o->navigationCompleted)o->navigationCompleted(o->userData,1,200);if(o->sourceChanged)o->sourceChanged(o->userData,1);if(o->titleChanged){wchar_t* title=CopyManagedUtf16(webView.title);o->titleChanged(o->userData,title);free(title);}}
- (void)webView:(WKWebView*)webView didFailNavigation:(WKNavigation*)navigation withError:(NSError*)error {if(_owner&&_owner->navigationCompleted)_owner->navigationCompleted(_owner->userData,0,(int)error.code);}
- (void)webViewWebContentProcessDidTerminate:(WKWebView*)webView {if(_owner&&_owner->processFailed)_owner->processFailed(_owner->userData,1);}
- (void)userContentController:(WKUserContentController*)controller didReceiveScriptMessage:(WKScriptMessage*)message {if(!_owner||!_owner->messageReceived)return;NSString* text=[message.body isKindOfClass:[NSString class]]?message.body:[message.body description];wchar_t* value=CopyManagedUtf16(text);_owner->messageReceived(_owner->userData,value);free(value);}
- (WKWebView*)webView:(WKWebView*)webView createWebViewWithConfiguration:(WKWebViewConfiguration*)configuration forNavigationAction:(WKNavigationAction*)action windowFeatures:(WKWindowFeatures*)features {if(_owner&&_owner->newWindowRequested){int handled=0;wchar_t* uri=CopyManagedUtf16(action.request.URL.absoluteString);_owner->newWindowRequested(_owner->userData,uri,1,&handled);free(uri);if(handled)return nil;}return nil;}
@end
#endif

extern "C" {
JALIUM_BROWSER_API int jalium_webview2_initialize(void){return TARGET_OS_TV?kNotImplemented:kOk;}
JALIUM_BROWSER_API void jalium_webview2_shutdown(void){}
JALIUM_BROWSER_API int jalium_webview2_get_available_browser_version_string(const wchar_t*,wchar_t** out){if(!out)return kInvalidArgument;*out=CopyManagedUtf16(TARGET_OS_TV?@"":@"WKWebView (system WebKit)");return TARGET_OS_TV?kNotImplemented:(*out?kOk:kFailure);}
JALIUM_BROWSER_API void jalium_webview2_free_string(wchar_t* value){free(value);}
JALIUM_BROWSER_API int jalium_webview2_create_environment(const wchar_t*,const wchar_t*,JaliumWebView2EnvironmentHandle** out){if(!out)return kInvalidArgument;*out=TARGET_OS_TV?nullptr:new JaliumWebView2EnvironmentHandle();return TARGET_OS_TV?kNotImplemented:kOk;}
JALIUM_BROWSER_API void jalium_webview2_destroy_environment(JaliumWebView2EnvironmentHandle* e){delete e;}
JALIUM_BROWSER_API int jalium_webview2_create_controller(JaliumWebView2EnvironmentHandle* env,intptr_t parent,int,JaliumWebView2ControllerHandle** out){if(!env||!parent||!out)return kInvalidArgument;*out=nullptr;
#if TARGET_OS_TV
    return kNotImplemented;
#else
    id parentView=(__bridge id)(void*)parent;
#if TARGET_OS_OSX
    if(![parentView isKindOfClass:[NSView class]])return kInvalidArgument;
#else
    if(![parentView isKindOfClass:[UIView class]])return kInvalidArgument;
#endif
    auto c=std::make_unique<JaliumWebView2ControllerHandle>();WKWebViewConfiguration* config=[WKWebViewConfiguration new];[config.userContentController addScriptMessageHandler:[JaliumWKDelegate new] name:@"jalium"];c->view=[[WKWebView alloc]initWithFrame:[parentView bounds] configuration:config];c->delegate=[JaliumWKDelegate new];c->delegate.owner=c.get();c->view.navigationDelegate=c->delegate;c->view.UIDelegate=c->delegate;[config.userContentController removeScriptMessageHandlerForName:@"jalium"];[config.userContentController addScriptMessageHandler:c->delegate name:@"jalium"];
#if TARGET_OS_OSX
    [parentView addSubview:c->view];
#else
    c->view.autoresizingMask=UIViewAutoresizingFlexibleWidth|UIViewAutoresizingFlexibleHeight;[parentView addSubview:c->view];
#endif
    c->bounds=c->view.frame;*out=c.release();return kOk;
#endif
}
JALIUM_BROWSER_API void jalium_webview2_destroy_controller(JaliumWebView2ControllerHandle* c){if(!c)return;
#if !TARGET_OS_TV
    [c->view.configuration.userContentController removeScriptMessageHandlerForName:@"jalium"];c->delegate.owner=nullptr;[c->view removeFromSuperview];
#endif
    delete c;}
JALIUM_BROWSER_API int jalium_webview2_set_callbacks(JaliumWebView2ControllerHandle* c,jalium_webview2_navigation_starting_callback a,jalium_webview2_navigation_completed_callback b,jalium_webview2_source_changed_callback d,jalium_webview2_content_loading_callback e,jalium_webview2_document_title_changed_callback f,jalium_webview2_web_message_received_callback g,jalium_webview2_new_window_requested_callback h,jalium_webview2_process_failed_callback i,jalium_webview2_zoom_factor_changed_callback j,void* u){if(!c)return kInvalidArgument;
#if !TARGET_OS_TV
    c->navigationStarting=a;c->navigationCompleted=b;c->sourceChanged=d;c->contentLoading=e;c->titleChanged=f;c->messageReceived=g;c->newWindowRequested=h;c->processFailed=i;c->zoomChanged=j;c->userData=u;return kOk;
#else
    return kNotImplemented;
#endif
}
JALIUM_BROWSER_API int jalium_webview2_navigate(JaliumWebView2ControllerHandle* c,const wchar_t* uri){if(!c||!uri)return kInvalidArgument;
#if TARGET_OS_TV
    return kNotImplemented;
#else
    NSURL* url=[NSURL URLWithString:FromManagedUtf16(uri)];if(!url)return kInvalidArgument;[c->view loadRequest:[NSURLRequest requestWithURL:url]];return kOk;
#endif
}
JALIUM_BROWSER_API int jalium_webview2_navigate_to_string(JaliumWebView2ControllerHandle* c,const wchar_t* html){if(!c||!html)return kInvalidArgument;
#if TARGET_OS_TV
    return kNotImplemented;
#else
    [c->view loadHTMLString:FromManagedUtf16(html) baseURL:nil];return kOk;
#endif
}
JALIUM_BROWSER_API int jalium_webview2_navigate_with_web_resource_request(JaliumWebView2ControllerHandle* c,const wchar_t* uri,const wchar_t* method,const uint8_t* data,int length,const wchar_t* headers){if(!c||!uri)return kInvalidArgument;
#if TARGET_OS_TV
    return kNotImplemented;
#else
    NSMutableURLRequest* request=[NSMutableURLRequest requestWithURL:[NSURL URLWithString:FromManagedUtf16(uri)]];request.HTTPMethod=FromManagedUtf16(method);if(data&&length>0)request.HTTPBody=[NSData dataWithBytes:data length:length];for(NSString* line in [FromManagedUtf16(headers) componentsSeparatedByString:@"\n"]){NSRange r=[line rangeOfString:@":"];if(r.location!=NSNotFound)[request setValue:[line substringFromIndex:r.location+1] forHTTPHeaderField:[line substringToIndex:r.location]];}[c->view loadRequest:request];return kOk;
#endif
}
JALIUM_BROWSER_API int jalium_webview2_add_host_object_to_script(JaliumWebView2ControllerHandle*,const wchar_t*,intptr_t){return kNotImplemented;}
JALIUM_BROWSER_API int jalium_webview2_remove_host_object_from_script(JaliumWebView2ControllerHandle*,const wchar_t*){return kNotImplemented;}
JALIUM_BROWSER_API int jalium_webview2_reload(JaliumWebView2ControllerHandle* c){if(!c)return kInvalidArgument;
#if !TARGET_OS_TV
    [c->view reload];return kOk;
#else
    return kNotImplemented;
#endif
}
JALIUM_BROWSER_API int jalium_webview2_stop(JaliumWebView2ControllerHandle* c){if(!c)return kInvalidArgument;
#if !TARGET_OS_TV
    [c->view stopLoading];return kOk;
#else
    return kNotImplemented;
#endif
}
JALIUM_BROWSER_API int jalium_webview2_go_back(JaliumWebView2ControllerHandle* c){if(!c)return kInvalidArgument;
#if !TARGET_OS_TV
    [c->view goBack];return kOk;
#else
    return kNotImplemented;
#endif
}
JALIUM_BROWSER_API int jalium_webview2_go_forward(JaliumWebView2ControllerHandle* c){if(!c)return kInvalidArgument;
#if !TARGET_OS_TV
    [c->view goForward];return kOk;
#else
    return kNotImplemented;
#endif
}
JALIUM_BROWSER_API int jalium_webview2_get_can_go_back(JaliumWebView2ControllerHandle* c,int* out){if(!c||!out)return kInvalidArgument;
#if !TARGET_OS_TV
    *out=c->view.canGoBack;return kOk;
#else
    *out=0;return kNotImplemented;
#endif
}
JALIUM_BROWSER_API int jalium_webview2_get_can_go_forward(JaliumWebView2ControllerHandle* c,int* out){if(!c||!out)return kInvalidArgument;
#if !TARGET_OS_TV
    *out=c->view.canGoForward;return kOk;
#else
    *out=0;return kNotImplemented;
#endif
}
JALIUM_BROWSER_API int jalium_webview2_execute_script_async(JaliumWebView2ControllerHandle* c,const wchar_t* script,jalium_webview2_script_completed_callback cb,void* user){if(!c||!script||!cb)return kInvalidArgument;
#if TARGET_OS_TV
    return kNotImplemented;
#else
    [c->view evaluateJavaScript:FromManagedUtf16(script) completionHandler:^(id result,NSError* error){NSString* text=@"null";if(result){if([NSJSONSerialization isValidJSONObject:result]){NSData* data=[NSJSONSerialization dataWithJSONObject:result options:0 error:nil];text=[[NSString alloc]initWithData:data encoding:NSUTF8StringEncoding];}else text=[result description];}wchar_t* value=CopyManagedUtf16(text);cb(user,error?kFailure:kOk,value);free(value);}];return kOk;
#endif
}
JALIUM_BROWSER_API int jalium_webview2_post_web_message_as_string(JaliumWebView2ControllerHandle* c,const wchar_t* message){if(!c||!message)return kInvalidArgument;
#if TARGET_OS_TV
    return kNotImplemented;
#else
    NSData* data=[NSJSONSerialization dataWithJSONObject:@[FromManagedUtf16(message)] options:0 error:nil];NSString* json=[[NSString alloc]initWithData:data encoding:NSUTF8StringEncoding];NSString* script=[NSString stringWithFormat:@"window.dispatchEvent(new MessageEvent('message',{data:%@.at(0)}));",json];[c->view evaluateJavaScript:script completionHandler:nil];return kOk;
#endif
}
JALIUM_BROWSER_API int jalium_webview2_post_web_message_as_json(JaliumWebView2ControllerHandle* c,const wchar_t* json){return jalium_webview2_post_web_message_as_string(c,json);}
JALIUM_BROWSER_API int jalium_webview2_get_source(JaliumWebView2ControllerHandle* c,wchar_t** out){if(!c||!out)return kInvalidArgument;
#if !TARGET_OS_TV
    *out=CopyManagedUtf16(c->view.URL.absoluteString);return *out?kOk:kFailure;
#else
    *out=nullptr;return kNotImplemented;
#endif
}
JALIUM_BROWSER_API int jalium_webview2_get_document_title(JaliumWebView2ControllerHandle* c,wchar_t** out){if(!c||!out)return kInvalidArgument;
#if !TARGET_OS_TV
    *out=CopyManagedUtf16(c->view.title);return *out?kOk:kFailure;
#else
    *out=nullptr;return kNotImplemented;
#endif
}
JALIUM_BROWSER_API int jalium_webview2_set_bounds(JaliumWebView2ControllerHandle* c,int x,int y,int w,int h){if(!c||w<0||h<0)return kInvalidArgument;
#if !TARGET_OS_TV
    c->bounds=CGRectMake(x,y,w,h);c->view.frame=c->bounds;return kOk;
#else
    return kNotImplemented;
#endif
}
JALIUM_BROWSER_API int jalium_webview2_get_bounds(JaliumWebView2ControllerHandle* c,int* x,int* y,int* w,int* h){if(!c||!x||!y||!w||!h)return kInvalidArgument;
#if !TARGET_OS_TV
    *x=c->bounds.origin.x;*y=c->bounds.origin.y;*w=c->bounds.size.width;*h=c->bounds.size.height;return kOk;
#else
    *x=*y=*w=*h=0;return kNotImplemented;
#endif
}
JALIUM_BROWSER_API int jalium_webview2_set_is_visible(JaliumWebView2ControllerHandle* c,int visible){if(!c)return kInvalidArgument;
#if TARGET_OS_OSX
    c->view.hidden=!visible;return kOk;
#elif !TARGET_OS_TV
    c->view.hidden=!visible;return kOk;
#else
    return kNotImplemented;
#endif
}
JALIUM_BROWSER_API int jalium_webview2_notify_parent_window_position_changed(JaliumWebView2ControllerHandle*){return kOk;}
JALIUM_BROWSER_API int jalium_webview2_close(JaliumWebView2ControllerHandle* c){if(!c)return kInvalidArgument;
#if !TARGET_OS_TV
    [c->view stopLoading];[c->view removeFromSuperview];return kOk;
#else
    return kNotImplemented;
#endif
}
JALIUM_BROWSER_API int jalium_webview2_set_zoom_factor(JaliumWebView2ControllerHandle* c,double zoom){if(!c||zoom<=0)return kInvalidArgument;
#if TARGET_OS_OSX
    c->view.magnification=zoom;
#elif !TARGET_OS_TV
    c->view.scrollView.zoomScale=zoom;
#else
    return kNotImplemented;
#endif
    c->zoom=zoom;if(c->zoomChanged)c->zoomChanged(c->userData,zoom);return kOk;}
JALIUM_BROWSER_API int jalium_webview2_get_zoom_factor(JaliumWebView2ControllerHandle* c,double* out){if(!c||!out)return kInvalidArgument;*out=c->zoom;return TARGET_OS_TV?kNotImplemented:kOk;}
JALIUM_BROWSER_API int jalium_webview2_set_default_background_color(JaliumWebView2ControllerHandle* c,uint32_t argb){if(!c)return kInvalidArgument;c->background=argb;
#if TARGET_OS_OSX
    c->view.underPageBackgroundColor=[NSColor colorWithRed:((argb>>16)&255)/255.0 green:((argb>>8)&255)/255.0 blue:(argb&255)/255.0 alpha:((argb>>24)&255)/255.0];
#elif !TARGET_OS_TV
    c->view.opaque=((argb>>24)&255)==255;c->view.backgroundColor=[UIColor colorWithRed:((argb>>16)&255)/255.0 green:((argb>>8)&255)/255.0 blue:(argb&255)/255.0 alpha:((argb>>24)&255)/255.0];
#endif
    return TARGET_OS_TV?kNotImplemented:kOk;}
JALIUM_BROWSER_API int jalium_webview2_get_default_background_color(JaliumWebView2ControllerHandle* c,uint32_t* out){if(!c||!out)return kInvalidArgument;*out=c->background;return TARGET_OS_TV?kNotImplemented:kOk;}
JALIUM_BROWSER_API int jalium_webview2_set_root_visual_target(JaliumWebView2ControllerHandle* c,intptr_t target){if(!c||!target)return kInvalidArgument;
#if !TARGET_OS_TV
    CALayer* layer=(__bridge CALayer*)(void*)target;[layer addSublayer:c->view.layer];return kOk;
#else
    return kNotImplemented;
#endif
}
JALIUM_BROWSER_API int jalium_webview2_send_mouse_input(JaliumWebView2ControllerHandle*,int,int,uint32_t,int,int){return kNotImplemented;}
JALIUM_BROWSER_API int jalium_webview2_open_devtools_window(JaliumWebView2ControllerHandle*){return kNotImplemented;}
JALIUM_BROWSER_API int jalium_webview2_set_cursor_changed_callback(JaliumWebView2ControllerHandle* c,jalium_webview2_cursor_changed_callback cb,void* user){if(!c)return kInvalidArgument;
#if !TARGET_OS_TV
    c->cursorChanged=cb;c->cursorUserData=user;return kOk;
#else
    return kNotImplemented;
#endif
}
JALIUM_BROWSER_API int jalium_webview2_get_cursor(JaliumWebView2ControllerHandle*,intptr_t* out){if(!out)return kInvalidArgument;*out=0;return kNotImplemented;}
}

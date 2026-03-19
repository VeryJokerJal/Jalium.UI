#pragma once

#include <stdint.h>

#ifdef _WIN32
    #if defined(JALIUM_STATIC)
        #define JALIUM_BROWSER_API
    #elif defined(JALIUM_BROWSER_EXPORTS)
        #define JALIUM_BROWSER_API __declspec(dllexport)
    #else
        #define JALIUM_BROWSER_API __declspec(dllimport)
    #endif
#else
    #define JALIUM_BROWSER_API
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct JaliumWebView2EnvironmentHandle JaliumWebView2EnvironmentHandle;
typedef struct JaliumWebView2ControllerHandle JaliumWebView2ControllerHandle;

typedef void(__stdcall* jalium_webview2_navigation_starting_callback)(void* user_data, const wchar_t* uri, int is_redirected, int* cancel);
typedef void(__stdcall* jalium_webview2_navigation_completed_callback)(void* user_data, int is_success, int http_status_code);
typedef void(__stdcall* jalium_webview2_source_changed_callback)(void* user_data, int is_new_document);
typedef void(__stdcall* jalium_webview2_content_loading_callback)(void* user_data, int is_error_page);
typedef void(__stdcall* jalium_webview2_document_title_changed_callback)(void* user_data, const wchar_t* title);
typedef void(__stdcall* jalium_webview2_web_message_received_callback)(void* user_data, const wchar_t* message);
typedef void(__stdcall* jalium_webview2_new_window_requested_callback)(void* user_data, const wchar_t* uri, int is_user_initiated, int* handled);
typedef void(__stdcall* jalium_webview2_process_failed_callback)(void* user_data, int process_failed_kind);
typedef void(__stdcall* jalium_webview2_zoom_factor_changed_callback)(void* user_data, double zoom_factor);

typedef void(__stdcall* jalium_webview2_cursor_changed_callback)(void* user_data, intptr_t cursor_handle);

typedef void(__stdcall* jalium_webview2_script_completed_callback)(void* user_data, int result, const wchar_t* result_json);

JALIUM_BROWSER_API int jalium_webview2_initialize(void);
JALIUM_BROWSER_API void jalium_webview2_shutdown(void);

JALIUM_BROWSER_API int jalium_webview2_get_available_browser_version_string(
    const wchar_t* browser_executable_folder,
    wchar_t** version_out);

JALIUM_BROWSER_API void jalium_webview2_free_string(wchar_t* value);

JALIUM_BROWSER_API int jalium_webview2_create_environment(
    const wchar_t* browser_executable_folder,
    const wchar_t* user_data_folder,
    JaliumWebView2EnvironmentHandle** environment_out);

JALIUM_BROWSER_API void jalium_webview2_destroy_environment(JaliumWebView2EnvironmentHandle* environment);

JALIUM_BROWSER_API int jalium_webview2_create_controller(
    JaliumWebView2EnvironmentHandle* environment,
    intptr_t parent_window,
    int use_composition_controller,
    JaliumWebView2ControllerHandle** controller_out);

JALIUM_BROWSER_API void jalium_webview2_destroy_controller(JaliumWebView2ControllerHandle* controller);

JALIUM_BROWSER_API int jalium_webview2_set_callbacks(
    JaliumWebView2ControllerHandle* controller,
    jalium_webview2_navigation_starting_callback navigation_starting,
    jalium_webview2_navigation_completed_callback navigation_completed,
    jalium_webview2_source_changed_callback source_changed,
    jalium_webview2_content_loading_callback content_loading,
    jalium_webview2_document_title_changed_callback document_title_changed,
    jalium_webview2_web_message_received_callback web_message_received,
    jalium_webview2_new_window_requested_callback new_window_requested,
    jalium_webview2_process_failed_callback process_failed,
    jalium_webview2_zoom_factor_changed_callback zoom_factor_changed,
    void* user_data);

JALIUM_BROWSER_API int jalium_webview2_navigate(JaliumWebView2ControllerHandle* controller, const wchar_t* uri);
JALIUM_BROWSER_API int jalium_webview2_navigate_to_string(JaliumWebView2ControllerHandle* controller, const wchar_t* html);
JALIUM_BROWSER_API int jalium_webview2_reload(JaliumWebView2ControllerHandle* controller);
JALIUM_BROWSER_API int jalium_webview2_stop(JaliumWebView2ControllerHandle* controller);
JALIUM_BROWSER_API int jalium_webview2_go_back(JaliumWebView2ControllerHandle* controller);
JALIUM_BROWSER_API int jalium_webview2_go_forward(JaliumWebView2ControllerHandle* controller);
JALIUM_BROWSER_API int jalium_webview2_get_can_go_back(JaliumWebView2ControllerHandle* controller, int* can_go_back);
JALIUM_BROWSER_API int jalium_webview2_get_can_go_forward(JaliumWebView2ControllerHandle* controller, int* can_go_forward);
JALIUM_BROWSER_API int jalium_webview2_execute_script_async(
    JaliumWebView2ControllerHandle* controller,
    const wchar_t* script,
    jalium_webview2_script_completed_callback callback,
    void* user_data);
JALIUM_BROWSER_API int jalium_webview2_post_web_message_as_string(JaliumWebView2ControllerHandle* controller, const wchar_t* message);
JALIUM_BROWSER_API int jalium_webview2_post_web_message_as_json(JaliumWebView2ControllerHandle* controller, const wchar_t* json_message);

JALIUM_BROWSER_API int jalium_webview2_get_source(JaliumWebView2ControllerHandle* controller, wchar_t** source_out);
JALIUM_BROWSER_API int jalium_webview2_get_document_title(JaliumWebView2ControllerHandle* controller, wchar_t** title_out);

JALIUM_BROWSER_API int jalium_webview2_set_bounds(JaliumWebView2ControllerHandle* controller, int x, int y, int width, int height);
JALIUM_BROWSER_API int jalium_webview2_get_bounds(JaliumWebView2ControllerHandle* controller, int* x, int* y, int* width, int* height);

JALIUM_BROWSER_API int jalium_webview2_set_is_visible(JaliumWebView2ControllerHandle* controller, int is_visible);
JALIUM_BROWSER_API int jalium_webview2_notify_parent_window_position_changed(JaliumWebView2ControllerHandle* controller);
JALIUM_BROWSER_API int jalium_webview2_close(JaliumWebView2ControllerHandle* controller);

JALIUM_BROWSER_API int jalium_webview2_set_zoom_factor(JaliumWebView2ControllerHandle* controller, double zoom_factor);
JALIUM_BROWSER_API int jalium_webview2_get_zoom_factor(JaliumWebView2ControllerHandle* controller, double* zoom_factor);

JALIUM_BROWSER_API int jalium_webview2_set_default_background_color(JaliumWebView2ControllerHandle* controller, uint32_t argb);
JALIUM_BROWSER_API int jalium_webview2_get_default_background_color(JaliumWebView2ControllerHandle* controller, uint32_t* argb);

JALIUM_BROWSER_API int jalium_webview2_set_root_visual_target(JaliumWebView2ControllerHandle* controller, intptr_t visual_target);
JALIUM_BROWSER_API int jalium_webview2_send_mouse_input(
    JaliumWebView2ControllerHandle* controller,
    int event_kind,
    int virtual_keys,
    uint32_t mouse_data,
    int x,
    int y);

JALIUM_BROWSER_API int jalium_webview2_open_devtools_window(JaliumWebView2ControllerHandle* controller);

JALIUM_BROWSER_API int jalium_webview2_set_cursor_changed_callback(
    JaliumWebView2ControllerHandle* controller,
    jalium_webview2_cursor_changed_callback callback,
    void* user_data);
JALIUM_BROWSER_API int jalium_webview2_get_cursor(JaliumWebView2ControllerHandle* controller, intptr_t* cursor_out);

#ifdef __cplusplus
}
#endif

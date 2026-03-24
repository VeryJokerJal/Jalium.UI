#include "jalium_browser_api.h"

#include <Windows.h>

#include <atomic>
#include <memory>
#include <new>

#include <wrl.h>
#include <wrl/event.h>

#include "WebView2.h"

using Microsoft::WRL::Callback;
using Microsoft::WRL::ComPtr;

#ifndef JALIUM_STATIC
// DLL 模式：运行时动态加载 WebView2Loader.dll
using CreateCoreWebView2EnvironmentWithOptionsFn = HRESULT(STDAPICALLTYPE*)(
    PCWSTR, PCWSTR, ICoreWebView2EnvironmentOptions*,
    ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler*);
using GetAvailableCoreWebView2BrowserVersionStringFn = HRESULT(STDAPICALLTYPE*)(
    PCWSTR, LPWSTR*);

HMODULE g_webview2_loader = nullptr;
CreateCoreWebView2EnvironmentWithOptionsFn g_create_environment = nullptr;
GetAvailableCoreWebView2BrowserVersionStringFn g_get_browser_version = nullptr;

HRESULT EnsureWebView2Loader() {
    if (g_webview2_loader && g_create_environment && g_get_browser_version) return S_OK;
    g_webview2_loader = LoadLibraryW(L"WebView2Loader.dll");
    if (!g_webview2_loader) return HRESULT_FROM_WIN32(GetLastError());
    g_create_environment = reinterpret_cast<CreateCoreWebView2EnvironmentWithOptionsFn>(
        GetProcAddress(g_webview2_loader, "CreateCoreWebView2EnvironmentWithOptions"));
    g_get_browser_version = reinterpret_cast<GetAvailableCoreWebView2BrowserVersionStringFn>(
        GetProcAddress(g_webview2_loader, "GetAvailableCoreWebView2BrowserVersionString"));
    return (g_create_environment && g_get_browser_version) ? S_OK : E_NOINTERFACE;
}
#endif

std::atomic<int> g_com_initialized_count = 0;

HRESULT WaitForEventWithMessagePump(HANDLE event_handle) {
    if (event_handle == nullptr) {
        return E_POINTER;
    }

    while (true) {
        DWORD wait_result = MsgWaitForMultipleObjects(1, &event_handle, FALSE, INFINITE, QS_ALLINPUT);
        if (wait_result == WAIT_OBJECT_0) {
            return S_OK;
        }

        if (wait_result == WAIT_OBJECT_0 + 1) {
            MSG msg;
            while (PeekMessageW(&msg, nullptr, 0, 0, PM_REMOVE)) {
                TranslateMessage(&msg);
                DispatchMessageW(&msg);
            }
            continue;
        }

        return HRESULT_FROM_WIN32(GetLastError());
    }
}

void CoTaskMemFreeIfNotNull(void* ptr) {
    if (ptr != nullptr) {
        CoTaskMemFree(ptr);
    }
}

struct JaliumWebView2EnvironmentHandle {
    ComPtr<ICoreWebView2Environment> environment;
};

struct JaliumWebView2ControllerHandle {
    ComPtr<ICoreWebView2Controller> controller;
    ComPtr<ICoreWebView2Controller2> controller2;
    ComPtr<ICoreWebView2CompositionController> composition_controller;
    ComPtr<ICoreWebView2> core;

    void* user_data = nullptr;
    jalium_webview2_navigation_starting_callback navigation_starting = nullptr;
    jalium_webview2_navigation_completed_callback navigation_completed = nullptr;
    jalium_webview2_source_changed_callback source_changed = nullptr;
    jalium_webview2_content_loading_callback content_loading = nullptr;
    jalium_webview2_document_title_changed_callback document_title_changed = nullptr;
    jalium_webview2_web_message_received_callback web_message_received = nullptr;
    jalium_webview2_new_window_requested_callback new_window_requested = nullptr;
    jalium_webview2_process_failed_callback process_failed = nullptr;
    jalium_webview2_zoom_factor_changed_callback zoom_factor_changed = nullptr;

    EventRegistrationToken navigation_starting_token{};
    EventRegistrationToken navigation_completed_token{};
    EventRegistrationToken source_changed_token{};
    EventRegistrationToken content_loading_token{};
    EventRegistrationToken document_title_changed_token{};
    EventRegistrationToken web_message_received_token{};
    EventRegistrationToken new_window_requested_token{};
    EventRegistrationToken process_failed_token{};
    EventRegistrationToken zoom_factor_changed_token{};
    EventRegistrationToken cursor_changed_token{};
    jalium_webview2_cursor_changed_callback cursor_changed = nullptr;
    void* cursor_changed_user_data = nullptr;
};

void RemoveCallbacks(JaliumWebView2ControllerHandle* controller_handle) {
    if (!controller_handle) {
        return;
    }

    if (controller_handle->core) {
        if (controller_handle->navigation_starting_token.value != 0) {
            controller_handle->core->remove_NavigationStarting(controller_handle->navigation_starting_token);
            controller_handle->navigation_starting_token.value = 0;
        }

        if (controller_handle->navigation_completed_token.value != 0) {
            controller_handle->core->remove_NavigationCompleted(controller_handle->navigation_completed_token);
            controller_handle->navigation_completed_token.value = 0;
        }

        if (controller_handle->source_changed_token.value != 0) {
            controller_handle->core->remove_SourceChanged(controller_handle->source_changed_token);
            controller_handle->source_changed_token.value = 0;
        }

        if (controller_handle->content_loading_token.value != 0) {
            controller_handle->core->remove_ContentLoading(controller_handle->content_loading_token);
            controller_handle->content_loading_token.value = 0;
        }

        if (controller_handle->document_title_changed_token.value != 0) {
            controller_handle->core->remove_DocumentTitleChanged(controller_handle->document_title_changed_token);
            controller_handle->document_title_changed_token.value = 0;
        }

        if (controller_handle->web_message_received_token.value != 0) {
            controller_handle->core->remove_WebMessageReceived(controller_handle->web_message_received_token);
            controller_handle->web_message_received_token.value = 0;
        }

        if (controller_handle->new_window_requested_token.value != 0) {
            controller_handle->core->remove_NewWindowRequested(controller_handle->new_window_requested_token);
            controller_handle->new_window_requested_token.value = 0;
        }

        if (controller_handle->process_failed_token.value != 0) {
            controller_handle->core->remove_ProcessFailed(controller_handle->process_failed_token);
            controller_handle->process_failed_token.value = 0;
        }
    }

    if (controller_handle->controller && controller_handle->zoom_factor_changed_token.value != 0) {
        controller_handle->controller->remove_ZoomFactorChanged(controller_handle->zoom_factor_changed_token);
        controller_handle->zoom_factor_changed_token.value = 0;
    }

    if (controller_handle->composition_controller && controller_handle->cursor_changed_token.value != 0) {
        controller_handle->composition_controller->remove_CursorChanged(controller_handle->cursor_changed_token);
        controller_handle->cursor_changed_token.value = 0;
    }
}

HRESULT AddCallbacks(JaliumWebView2ControllerHandle* controller_handle) {
    if (!controller_handle || !controller_handle->core || !controller_handle->controller) {
        return E_POINTER;
    }

    // Helper: on any add_* failure, remove all previously added callbacks
    // to avoid partial registration that could leak or cause inconsistency.
    auto rollback = [controller_handle](HRESULT failHr) {
        RemoveCallbacks(controller_handle);
        return failHr;
    };

    HRESULT hr = S_OK;

    hr = controller_handle->core->add_NavigationStarting(
        Callback<ICoreWebView2NavigationStartingEventHandler>(
            [controller_handle](ICoreWebView2* /*sender*/, ICoreWebView2NavigationStartingEventArgs* args) -> HRESULT {
                if (!controller_handle->navigation_starting || !args) {
                    return S_OK;
                }

                LPWSTR uri = nullptr;
                BOOL is_redirected = FALSE;
                args->get_Uri(&uri);
                args->get_IsRedirected(&is_redirected);

                int cancel = 0;
                controller_handle->navigation_starting(
                    controller_handle->user_data,
                    uri ? uri : L"",
                    is_redirected ? 1 : 0,
                    &cancel);

                args->put_Cancel(cancel ? TRUE : FALSE);
                CoTaskMemFreeIfNotNull(uri);
                return S_OK;
            })
            .Get(),
        &controller_handle->navigation_starting_token);
    if (FAILED(hr)) {
        return rollback(hr);
    }

    hr = controller_handle->core->add_NavigationCompleted(
        Callback<ICoreWebView2NavigationCompletedEventHandler>(
            [controller_handle](ICoreWebView2* /*sender*/, ICoreWebView2NavigationCompletedEventArgs* args) -> HRESULT {
                if (!controller_handle->navigation_completed || !args) {
                    return S_OK;
                }

                BOOL is_success = FALSE;
                int http_status_code = 0;
                args->get_IsSuccess(&is_success);

                ComPtr<ICoreWebView2NavigationCompletedEventArgs2> args2;
                if (SUCCEEDED(args->QueryInterface(IID_PPV_ARGS(&args2))) && args2) {
                    int code = 0;
                    if (SUCCEEDED(args2->get_HttpStatusCode(&code))) {
                        http_status_code = code;
                    }
                }

                controller_handle->navigation_completed(
                    controller_handle->user_data,
                    is_success ? 1 : 0,
                    http_status_code);

                return S_OK;
            })
            .Get(),
        &controller_handle->navigation_completed_token);
    if (FAILED(hr)) {
        return rollback(hr);
    }

    hr = controller_handle->core->add_SourceChanged(
        Callback<ICoreWebView2SourceChangedEventHandler>(
            [controller_handle](ICoreWebView2* /*sender*/, ICoreWebView2SourceChangedEventArgs* args) -> HRESULT {
                if (!controller_handle->source_changed || !args) {
                    return S_OK;
                }

                BOOL is_new_document = FALSE;
                args->get_IsNewDocument(&is_new_document);
                controller_handle->source_changed(controller_handle->user_data, is_new_document ? 1 : 0);
                return S_OK;
            })
            .Get(),
        &controller_handle->source_changed_token);
    if (FAILED(hr)) {
        return rollback(hr);
    }

    hr = controller_handle->core->add_ContentLoading(
        Callback<ICoreWebView2ContentLoadingEventHandler>(
            [controller_handle](ICoreWebView2* /*sender*/, ICoreWebView2ContentLoadingEventArgs* args) -> HRESULT {
                if (!controller_handle->content_loading || !args) {
                    return S_OK;
                }

                BOOL is_error_page = FALSE;
                args->get_IsErrorPage(&is_error_page);
                controller_handle->content_loading(controller_handle->user_data, is_error_page ? 1 : 0);
                return S_OK;
            })
            .Get(),
        &controller_handle->content_loading_token);
    if (FAILED(hr)) {
        return rollback(hr);
    }

    hr = controller_handle->core->add_DocumentTitleChanged(
        Callback<ICoreWebView2DocumentTitleChangedEventHandler>(
            [controller_handle](ICoreWebView2* sender, IUnknown* /*args*/) -> HRESULT {
                if (!controller_handle->document_title_changed || !sender) {
                    return S_OK;
                }

                LPWSTR title = nullptr;
                sender->get_DocumentTitle(&title);
                controller_handle->document_title_changed(controller_handle->user_data, title ? title : L"");
                CoTaskMemFreeIfNotNull(title);
                return S_OK;
            })
            .Get(),
        &controller_handle->document_title_changed_token);
    if (FAILED(hr)) {
        return rollback(hr);
    }

    hr = controller_handle->core->add_WebMessageReceived(
        Callback<ICoreWebView2WebMessageReceivedEventHandler>(
            [controller_handle](ICoreWebView2* /*sender*/, ICoreWebView2WebMessageReceivedEventArgs* args) -> HRESULT {
                if (!controller_handle->web_message_received || !args) {
                    return S_OK;
                }

                LPWSTR message = nullptr;
                HRESULT message_hr = args->TryGetWebMessageAsString(&message);
                if (FAILED(message_hr)) {
                    controller_handle->web_message_received(controller_handle->user_data, L"");
                } else {
                    controller_handle->web_message_received(controller_handle->user_data, message ? message : L"");
                }

                CoTaskMemFreeIfNotNull(message);
                return S_OK;
            })
            .Get(),
        &controller_handle->web_message_received_token);
    if (FAILED(hr)) {
        return rollback(hr);
    }

    hr = controller_handle->core->add_NewWindowRequested(
        Callback<ICoreWebView2NewWindowRequestedEventHandler>(
            [controller_handle](ICoreWebView2* /*sender*/, ICoreWebView2NewWindowRequestedEventArgs* args) -> HRESULT {
                if (!controller_handle->new_window_requested || !args) {
                    return S_OK;
                }

                LPWSTR uri = nullptr;
                BOOL is_user_initiated = FALSE;
                args->get_Uri(&uri);
                args->get_IsUserInitiated(&is_user_initiated);

                int handled = 0;
                controller_handle->new_window_requested(
                    controller_handle->user_data,
                    uri ? uri : L"",
                    is_user_initiated ? 1 : 0,
                    &handled);
                args->put_Handled(handled ? TRUE : FALSE);

                CoTaskMemFreeIfNotNull(uri);
                return S_OK;
            })
            .Get(),
        &controller_handle->new_window_requested_token);
    if (FAILED(hr)) {
        return rollback(hr);
    }

    hr = controller_handle->core->add_ProcessFailed(
        Callback<ICoreWebView2ProcessFailedEventHandler>(
            [controller_handle](ICoreWebView2* /*sender*/, ICoreWebView2ProcessFailedEventArgs* args) -> HRESULT {
                if (!controller_handle->process_failed || !args) {
                    return S_OK;
                }

                COREWEBVIEW2_PROCESS_FAILED_KIND kind = COREWEBVIEW2_PROCESS_FAILED_KIND_BROWSER_PROCESS_EXITED;
                args->get_ProcessFailedKind(&kind);
                controller_handle->process_failed(controller_handle->user_data, static_cast<int>(kind));
                return S_OK;
            })
            .Get(),
        &controller_handle->process_failed_token);
    if (FAILED(hr)) {
        return rollback(hr);
    }

    hr = controller_handle->controller->add_ZoomFactorChanged(
        Callback<ICoreWebView2ZoomFactorChangedEventHandler>(
            [controller_handle](ICoreWebView2Controller* sender, IUnknown* /*args*/) -> HRESULT {
                if (!controller_handle->zoom_factor_changed || !sender) {
                    return S_OK;
                }

                double zoom_factor = 1.0;
                sender->get_ZoomFactor(&zoom_factor);
                controller_handle->zoom_factor_changed(controller_handle->user_data, zoom_factor);
                return S_OK;
            })
            .Get(),
        &controller_handle->zoom_factor_changed_token);

    if (FAILED(hr)) {
        return rollback(hr);
    }

    return hr;
}

HRESULT ValidateController(JaliumWebView2ControllerHandle* controller_handle) {
    if (!controller_handle || !controller_handle->controller || !controller_handle->core) {
        return E_POINTER;
    }

    return S_OK;
}

int jalium_webview2_initialize(void) {
#ifndef JALIUM_STATIC
    HRESULT loader_hr = EnsureWebView2Loader();
    if (FAILED(loader_hr)) return loader_hr;
#endif
    HRESULT hr = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    if (SUCCEEDED(hr) || hr == S_FALSE) {
        g_com_initialized_count.fetch_add(1);
        return S_OK;
    }

    if (hr == RPC_E_CHANGED_MODE) {
        return S_OK;
    }

    return hr;
}

void jalium_webview2_shutdown(void) {
    int remaining = g_com_initialized_count.load();
    if (remaining > 0) {
        CoUninitialize();
        g_com_initialized_count.fetch_sub(1);
    }
}

int jalium_webview2_get_available_browser_version_string(
    const wchar_t* browser_executable_folder,
    wchar_t** version_out) {
    if (!version_out) {
        return E_POINTER;
    }

    *version_out = nullptr;

#ifdef JALIUM_STATIC
    return GetAvailableCoreWebView2BrowserVersionString(browser_executable_folder, version_out);
#else
    HRESULT hr = EnsureWebView2Loader();
    if (FAILED(hr)) return hr;
    return g_get_browser_version(browser_executable_folder, version_out);
#endif
}

void jalium_webview2_free_string(wchar_t* value) {
    CoTaskMemFreeIfNotNull(value);
}

int jalium_webview2_create_environment(
    const wchar_t* browser_executable_folder,
    const wchar_t* user_data_folder,
    JaliumWebView2EnvironmentHandle** environment_out) {
    if (!environment_out) {
        return E_POINTER;
    }

    *environment_out = nullptr;

    HANDLE completed = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (!completed) {
        return HRESULT_FROM_WIN32(GetLastError());
    }

    HRESULT callback_result = E_FAIL;
    ComPtr<ICoreWebView2Environment> environment;

    auto callback = Callback<ICoreWebView2CreateCoreWebView2EnvironmentCompletedHandler>(
        [&callback_result, &environment, completed](HRESULT result, ICoreWebView2Environment* created_environment) -> HRESULT {
            callback_result = result;
            environment = created_environment;
            SetEvent(completed);
            return S_OK;
        });

#ifdef JALIUM_STATIC
    HRESULT hr = CreateCoreWebView2EnvironmentWithOptions(browser_executable_folder, user_data_folder, nullptr, callback.Get());
#else
    HRESULT loader_hr = EnsureWebView2Loader();
    if (FAILED(loader_hr)) { CloseHandle(completed); return loader_hr; }
    HRESULT hr = g_create_environment(browser_executable_folder, user_data_folder, nullptr, callback.Get());
#endif
    if (SUCCEEDED(hr)) {
        hr = WaitForEventWithMessagePump(completed);
    }

    if (SUCCEEDED(hr)) {
        hr = callback_result;
    }

    CloseHandle(completed);

    if (FAILED(hr) || !environment) {
        return FAILED(hr) ? hr : E_FAIL;
    }

    auto* handle = new (std::nothrow) JaliumWebView2EnvironmentHandle();
    if (!handle) {
        return E_OUTOFMEMORY;
    }

    handle->environment = environment;
    *environment_out = handle;
    return S_OK;
}

void jalium_webview2_destroy_environment(JaliumWebView2EnvironmentHandle* environment) {
    delete environment;
}

int jalium_webview2_create_controller(
    JaliumWebView2EnvironmentHandle* environment,
    intptr_t parent_window,
    int use_composition_controller,
    JaliumWebView2ControllerHandle** controller_out) {
    if (!environment || !environment->environment || !controller_out) {
        return E_POINTER;
    }

    *controller_out = nullptr;

    HANDLE completed = CreateEventW(nullptr, TRUE, FALSE, nullptr);
    if (!completed) {
        return HRESULT_FROM_WIN32(GetLastError());
    }

    HRESULT callback_result = E_FAIL;
    ComPtr<ICoreWebView2Controller> controller;
    ComPtr<ICoreWebView2CompositionController> composition_controller;

    HRESULT hr = S_OK;
    if (use_composition_controller) {
        ComPtr<ICoreWebView2Environment3> environment3;
        hr = environment->environment.As(&environment3);
        if (FAILED(hr) || !environment3) {
            CloseHandle(completed);
            return FAILED(hr) ? hr : E_NOINTERFACE;
        }

        auto callback = Callback<ICoreWebView2CreateCoreWebView2CompositionControllerCompletedHandler>(
            [&callback_result, &composition_controller, completed](HRESULT result, ICoreWebView2CompositionController* created_controller) -> HRESULT {
                callback_result = result;
                composition_controller = created_controller;
                SetEvent(completed);
                return S_OK;
            });

        hr = environment3->CreateCoreWebView2CompositionController(
            reinterpret_cast<HWND>(parent_window),
            callback.Get());
    } else {
        auto callback = Callback<ICoreWebView2CreateCoreWebView2ControllerCompletedHandler>(
            [&callback_result, &controller, completed](HRESULT result, ICoreWebView2Controller* created_controller) -> HRESULT {
                callback_result = result;
                controller = created_controller;
                SetEvent(completed);
                return S_OK;
            });

        hr = environment->environment->CreateCoreWebView2Controller(
            reinterpret_cast<HWND>(parent_window),
            callback.Get());
    }

    if (SUCCEEDED(hr)) {
        hr = WaitForEventWithMessagePump(completed);
    }

    if (SUCCEEDED(hr)) {
        hr = callback_result;
    }

    CloseHandle(completed);

    if (FAILED(hr)) {
        return hr;
    }

    if (use_composition_controller) {
        if (!composition_controller) {
            return E_FAIL;
        }

        hr = composition_controller.As(&controller);
        if (FAILED(hr)) {
            return hr;
        }
    }

    if (!controller) {
        return E_FAIL;
    }

    ComPtr<ICoreWebView2> core;
    hr = controller->get_CoreWebView2(&core);
    if (FAILED(hr) || !core) {
        return FAILED(hr) ? hr : E_FAIL;
    }

    auto* handle = new (std::nothrow) JaliumWebView2ControllerHandle();
    if (!handle) {
        return E_OUTOFMEMORY;
    }

    handle->controller = controller;
    handle->core = core;
    handle->composition_controller = composition_controller;
    handle->controller.As(&handle->controller2);

    *controller_out = handle;
    return S_OK;
}

void jalium_webview2_destroy_controller(JaliumWebView2ControllerHandle* controller) {
    if (!controller) {
        return;
    }

    RemoveCallbacks(controller);

    if (controller->controller) {
        controller->controller->Close();
    }

    delete controller;
}

int jalium_webview2_set_callbacks(
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
    void* user_data) {
    HRESULT hr = ValidateController(controller);
    if (FAILED(hr)) {
        return hr;
    }

    RemoveCallbacks(controller);

    controller->navigation_starting = navigation_starting;
    controller->navigation_completed = navigation_completed;
    controller->source_changed = source_changed;
    controller->content_loading = content_loading;
    controller->document_title_changed = document_title_changed;
    controller->web_message_received = web_message_received;
    controller->new_window_requested = new_window_requested;
    controller->process_failed = process_failed;
    controller->zoom_factor_changed = zoom_factor_changed;
    controller->user_data = user_data;

    return AddCallbacks(controller);
}

int jalium_webview2_navigate(JaliumWebView2ControllerHandle* controller, const wchar_t* uri) {
    HRESULT hr = ValidateController(controller);
    if (FAILED(hr)) {
        return hr;
    }

    if (!uri) {
        return E_INVALIDARG;
    }

    return controller->core->Navigate(uri);
}

int jalium_webview2_navigate_to_string(JaliumWebView2ControllerHandle* controller, const wchar_t* html) {
    HRESULT hr = ValidateController(controller);
    if (FAILED(hr)) {
        return hr;
    }

    if (!html) {
        return E_INVALIDARG;
    }

    return controller->core->NavigateToString(html);
}

int jalium_webview2_reload(JaliumWebView2ControllerHandle* controller) {
    HRESULT hr = ValidateController(controller);
    if (FAILED(hr)) {
        return hr;
    }

    return controller->core->Reload();
}

int jalium_webview2_stop(JaliumWebView2ControllerHandle* controller) {
    HRESULT hr = ValidateController(controller);
    if (FAILED(hr)) {
        return hr;
    }

    return controller->core->Stop();
}

int jalium_webview2_go_back(JaliumWebView2ControllerHandle* controller) {
    HRESULT hr = ValidateController(controller);
    if (FAILED(hr)) {
        return hr;
    }

    return controller->core->GoBack();
}

int jalium_webview2_go_forward(JaliumWebView2ControllerHandle* controller) {
    HRESULT hr = ValidateController(controller);
    if (FAILED(hr)) {
        return hr;
    }

    return controller->core->GoForward();
}

int jalium_webview2_get_can_go_back(JaliumWebView2ControllerHandle* controller, int* can_go_back) {
    HRESULT hr = ValidateController(controller);
    if (FAILED(hr) || !can_go_back) {
        return FAILED(hr) ? hr : E_POINTER;
    }

    BOOL can_navigate = FALSE;
    hr = controller->core->get_CanGoBack(&can_navigate);
    if (SUCCEEDED(hr)) {
        *can_go_back = can_navigate ? 1 : 0;
    }

    return hr;
}

int jalium_webview2_get_can_go_forward(JaliumWebView2ControllerHandle* controller, int* can_go_forward) {
    HRESULT hr = ValidateController(controller);
    if (FAILED(hr) || !can_go_forward) {
        return FAILED(hr) ? hr : E_POINTER;
    }

    BOOL can_navigate = FALSE;
    hr = controller->core->get_CanGoForward(&can_navigate);
    if (SUCCEEDED(hr)) {
        *can_go_forward = can_navigate ? 1 : 0;
    }

    return hr;
}

int jalium_webview2_execute_script_async(
    JaliumWebView2ControllerHandle* controller,
    const wchar_t* script,
    jalium_webview2_script_completed_callback callback,
    void* user_data) {
    HRESULT hr = ValidateController(controller);
    if (FAILED(hr)) {
        return hr;
    }

    if (!script || !callback) {
        return E_INVALIDARG;
    }

    hr = controller->core->ExecuteScript(
        script,
        Callback<ICoreWebView2ExecuteScriptCompletedHandler>(
            [callback, user_data](HRESULT result, LPCWSTR result_object_as_json) -> HRESULT {
                callback(user_data, result, result_object_as_json ? result_object_as_json : L"");
                return S_OK;
            })
            .Get());

    return hr;
}

int jalium_webview2_post_web_message_as_string(JaliumWebView2ControllerHandle* controller, const wchar_t* message) {
    HRESULT hr = ValidateController(controller);
    if (FAILED(hr)) {
        return hr;
    }

    if (!message) {
        return E_INVALIDARG;
    }

    return controller->core->PostWebMessageAsString(message);
}

int jalium_webview2_post_web_message_as_json(JaliumWebView2ControllerHandle* controller, const wchar_t* json_message) {
    HRESULT hr = ValidateController(controller);
    if (FAILED(hr)) {
        return hr;
    }

    if (!json_message) {
        return E_INVALIDARG;
    }

    return controller->core->PostWebMessageAsJson(json_message);
}

int jalium_webview2_get_source(JaliumWebView2ControllerHandle* controller, wchar_t** source_out) {
    HRESULT hr = ValidateController(controller);
    if (FAILED(hr) || !source_out) {
        return FAILED(hr) ? hr : E_POINTER;
    }

    *source_out = nullptr;
    return controller->core->get_Source(source_out);
}

int jalium_webview2_get_document_title(JaliumWebView2ControllerHandle* controller, wchar_t** title_out) {
    HRESULT hr = ValidateController(controller);
    if (FAILED(hr) || !title_out) {
        return FAILED(hr) ? hr : E_POINTER;
    }

    *title_out = nullptr;
    return controller->core->get_DocumentTitle(title_out);
}

int jalium_webview2_set_bounds(JaliumWebView2ControllerHandle* controller, int x, int y, int width, int height) {
    HRESULT hr = ValidateController(controller);
    if (FAILED(hr)) {
        return hr;
    }

    RECT bounds{ x, y, x + width, y + height };
    return controller->controller->put_Bounds(bounds);
}

int jalium_webview2_get_bounds(JaliumWebView2ControllerHandle* controller, int* x, int* y, int* width, int* height) {
    HRESULT hr = ValidateController(controller);
    if (FAILED(hr) || !x || !y || !width || !height) {
        return FAILED(hr) ? hr : E_POINTER;
    }

    RECT bounds{};
    hr = controller->controller->get_Bounds(&bounds);
    if (SUCCEEDED(hr)) {
        *x = bounds.left;
        *y = bounds.top;
        *width = bounds.right - bounds.left;
        *height = bounds.bottom - bounds.top;
    }

    return hr;
}

int jalium_webview2_set_is_visible(JaliumWebView2ControllerHandle* controller, int is_visible) {
    HRESULT hr = ValidateController(controller);
    if (FAILED(hr)) {
        return hr;
    }

    return controller->controller->put_IsVisible(is_visible ? TRUE : FALSE);
}

int jalium_webview2_notify_parent_window_position_changed(JaliumWebView2ControllerHandle* controller) {
    HRESULT hr = ValidateController(controller);
    if (FAILED(hr)) {
        return hr;
    }

    return controller->controller->NotifyParentWindowPositionChanged();
}

int jalium_webview2_close(JaliumWebView2ControllerHandle* controller) {
    HRESULT hr = ValidateController(controller);
    if (FAILED(hr)) {
        return hr;
    }

    RemoveCallbacks(controller);
    return controller->controller->Close();
}

int jalium_webview2_set_zoom_factor(JaliumWebView2ControllerHandle* controller, double zoom_factor) {
    HRESULT hr = ValidateController(controller);
    if (FAILED(hr)) {
        return hr;
    }

    return controller->controller->put_ZoomFactor(zoom_factor);
}

int jalium_webview2_get_zoom_factor(JaliumWebView2ControllerHandle* controller, double* zoom_factor) {
    HRESULT hr = ValidateController(controller);
    if (FAILED(hr) || !zoom_factor) {
        return FAILED(hr) ? hr : E_POINTER;
    }

    return controller->controller->get_ZoomFactor(zoom_factor);
}

int jalium_webview2_set_default_background_color(JaliumWebView2ControllerHandle* controller, uint32_t argb) {
    HRESULT hr = ValidateController(controller);
    if (FAILED(hr)) {
        return hr;
    }

    if (!controller->controller2) {
        return E_NOINTERFACE;
    }

    COREWEBVIEW2_COLOR color{};
    color.A = static_cast<BYTE>((argb >> 24) & 0xFF);
    color.R = static_cast<BYTE>((argb >> 16) & 0xFF);
    color.G = static_cast<BYTE>((argb >> 8) & 0xFF);
    color.B = static_cast<BYTE>(argb & 0xFF);

    return controller->controller2->put_DefaultBackgroundColor(color);
}

int jalium_webview2_get_default_background_color(JaliumWebView2ControllerHandle* controller, uint32_t* argb) {
    HRESULT hr = ValidateController(controller);
    if (FAILED(hr) || !argb) {
        return FAILED(hr) ? hr : E_POINTER;
    }

    if (!controller->controller2) {
        return E_NOINTERFACE;
    }

    COREWEBVIEW2_COLOR color{};
    hr = controller->controller2->get_DefaultBackgroundColor(&color);
    if (SUCCEEDED(hr)) {
        *argb =
            (static_cast<uint32_t>(color.A) << 24) |
            (static_cast<uint32_t>(color.R) << 16) |
            (static_cast<uint32_t>(color.G) << 8) |
            static_cast<uint32_t>(color.B);
    }

    return hr;
}

int jalium_webview2_set_root_visual_target(JaliumWebView2ControllerHandle* controller, intptr_t visual_target) {
    HRESULT hr = ValidateController(controller);
    if (FAILED(hr)) {
        return hr;
    }

    if (!controller->composition_controller) {
        return E_NOINTERFACE;
    }

    auto* target = reinterpret_cast<IUnknown*>(visual_target);
    return controller->composition_controller->put_RootVisualTarget(target);
}

int jalium_webview2_send_mouse_input(
    JaliumWebView2ControllerHandle* controller,
    int event_kind,
    int virtual_keys,
    uint32_t mouse_data,
    int x,
    int y) {
    HRESULT hr = ValidateController(controller);
    if (FAILED(hr)) {
        return hr;
    }

    if (!controller->composition_controller) {
        return E_NOINTERFACE;
    }

    POINT point{ x, y };
    return controller->composition_controller->SendMouseInput(
        static_cast<COREWEBVIEW2_MOUSE_EVENT_KIND>(event_kind),
        static_cast<COREWEBVIEW2_MOUSE_EVENT_VIRTUAL_KEYS>(virtual_keys),
        mouse_data,
        point);
}

int jalium_webview2_open_devtools_window(JaliumWebView2ControllerHandle* controller) {
    HRESULT hr = ValidateController(controller);
    if (FAILED(hr)) {
        return hr;
    }

    return controller->core->OpenDevToolsWindow();
}

int jalium_webview2_set_cursor_changed_callback(
    JaliumWebView2ControllerHandle* controller,
    jalium_webview2_cursor_changed_callback callback,
    void* user_data) {
    HRESULT hr = ValidateController(controller);
    if (FAILED(hr)) {
        return hr;
    }

    if (!controller->composition_controller) {
        return E_NOINTERFACE;
    }

    // Remove previous subscription
    if (controller->cursor_changed_token.value != 0) {
        controller->composition_controller->remove_CursorChanged(controller->cursor_changed_token);
        controller->cursor_changed_token.value = 0;
    }

    controller->cursor_changed = callback;
    controller->cursor_changed_user_data = user_data;

    if (!callback) {
        return S_OK;
    }

    hr = controller->composition_controller->add_CursorChanged(
        Callback<ICoreWebView2CursorChangedEventHandler>(
            [controller](ICoreWebView2CompositionController* /*sender*/, IUnknown* /*args*/) -> HRESULT {
                if (!controller->cursor_changed || !controller->composition_controller) {
                    return S_OK;
                }

                HCURSOR cursor = nullptr;
                controller->composition_controller->get_Cursor(&cursor);
                controller->cursor_changed(
                    controller->cursor_changed_user_data,
                    reinterpret_cast<intptr_t>(cursor));
                return S_OK;
            })
            .Get(),
        &controller->cursor_changed_token);

    return hr;
}

int jalium_webview2_get_cursor(JaliumWebView2ControllerHandle* controller, intptr_t* cursor_out) {
    if (!cursor_out) {
        return E_POINTER;
    }

    *cursor_out = 0;

    HRESULT hr = ValidateController(controller);
    if (FAILED(hr)) {
        return hr;
    }

    if (!controller->composition_controller) {
        return E_NOINTERFACE;
    }

    HCURSOR cursor = nullptr;
    hr = controller->composition_controller->get_Cursor(&cursor);
    if (SUCCEEDED(hr)) {
        *cursor_out = reinterpret_cast<intptr_t>(cursor);
    }

    return hr;
}

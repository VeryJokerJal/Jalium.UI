#include "jalium_platform.h"
#include <chrono>
#include <cstdio>
#include <cstring>
#include <string>
#include <thread>

namespace {
int checks = 0, failures = 0;
void Check(bool condition, const char* name)
{
    ++checks;
    if (!condition) { ++failures; std::fprintf(stderr, "FAILED: %s\n", name); }
}
void Pump(int milliseconds)
{
    const auto end = std::chrono::steady_clock::now() + std::chrono::milliseconds(milliseconds);
    do { jalium_platform_poll_events(); std::this_thread::sleep_for(std::chrono::milliseconds(2)); }
    while (std::chrono::steady_clock::now() < end);
}
JaliumPlatformWindow* Create()
{
    JaliumWindowParams p{};
    p.title = reinterpret_cast<const JaliumUtf16Char*>(u"输入回调生命周期");
    p.width = 420; p.height = 280;
    p.style = JALIUM_WINDOW_STYLE_BORDERLESS | JALIUM_WINDOW_STYLE_RESIZABLE;
    return jalium_window_create(&p);
}
struct State {
    std::string mode;
    JaliumPlatformWindow* other = nullptr;
    bool armed = false, acted = false, closed = false;
    int starts = 0, updates = 0, ends = 0, chars = 0, cancels = 0, ups = 0;
    int replacementId = 0;
    std::string update;
};
void Callback(const JaliumPlatformEvent* event, void* data)
{
    auto& state = *static_cast<State*>(data);
    if (event->type == JALIUM_EVENT_COMPOSITION_START) ++state.starts;
    if (event->type == JALIUM_EVENT_COMPOSITION_UPDATE) { ++state.updates; state.update = event->composition.utf8Text; }
    if (event->type == JALIUM_EVENT_COMPOSITION_END) ++state.ends;
    if (event->type == JALIUM_EVENT_CHAR_INPUT) ++state.chars;
    if (event->type == JALIUM_EVENT_POINTER_UP) ++state.ups;
    if (event->type == JALIUM_EVENT_POINTER_CANCEL) ++state.cancels;
    if (!state.armed || state.acted) return;
    if ((event->type == JALIUM_EVENT_COMPOSITION_END && (state.mode == "commit-reenter" || state.mode == "leave-reenter")) ||
        (event->type == JALIUM_EVENT_DELETE_SURROUNDING_TEXT && state.mode == "v3-delete-close")) {
        state.acted = true;
        if (state.mode == "v3-delete-close") { state.closed = true; jalium_window_destroy(event->window); }
        jalium_test_wayland_inject_keyboard(state.other, JALIUM_EVENT_FOCUS_GAINED, 0, 0);
        if (!state.closed) jalium_test_wayland_inject_preedit(state.other, "新窗口");
    } else if ((event->type == JALIUM_EVENT_COMPOSITION_START && state.mode == "preedit-reenter-same") ||
        (event->type == JALIUM_EVENT_DELETE_SURROUNDING_TEXT && state.mode == "v3-delete-nested")) {
        state.acted = true;
        jalium_test_wayland_inject_preedit(event->window, "嵌套");
    } else if (event->type == JALIUM_EVENT_COMPOSITION_START) {
        state.acted = true;
        if (state.mode == "preedit-close") {
            state.closed = true;
            jalium_window_destroy(event->window);
        } else if (state.mode == "preedit-hide") jalium_window_hide(event->window);
        jalium_test_wayland_inject_keyboard(state.other, JALIUM_EVENT_FOCUS_GAINED, 0, 0);
    } else if (event->type == JALIUM_EVENT_POINTER_CANCEL) {
        state.acted = true;
        if (state.mode == "touch-destroy-reenter" || state.mode == "touch-hide-close") {
            state.closed = true;
            jalium_window_destroy(event->window);
        } else if (state.mode == "touch-cancel-replace") {
            state.replacementId = static_cast<int>(event->pointer.pointerId & 0x0fffffffu);
            Check(jalium_test_wayland_inject_touch(state.other, JALIUM_EVENT_POINTER_DOWN,
                state.replacementId, 15, 25) == JALIUM_OK, "cancel callback can start a contact with a reused id");
        }
    }
}
}

int main(int argc, char** argv)
{
    const std::string mode = argc > 1 ? argv[1] : "preedit-switch";
    if (jalium_platform_init() != JALIUM_OK) return 2;
    Pump(150);
    Check((jalium_test_wayland_get_seat_capabilities() & 3u) == 3u,
        "a real Wayland seat supplies keyboard and pointer");
    State first, second;
    first.mode = mode;
    auto* a = Create();
    auto* b = Create();
    Check(a && b, "create callback lifetime windows");
    if (!a || !b) return 2;
    first.other = b;
    jalium_window_set_event_callback(a, Callback, &first);
    jalium_window_set_event_callback(b, Callback, &second);
    jalium_window_show(a);
    jalium_window_show(b);
    Pump(300);
    if (mode == "commit-reenter" || mode == "leave-reenter") {
        jalium_test_wayland_inject_keyboard(a, JALIUM_EVENT_FOCUS_GAINED, 0, 0);
        jalium_test_wayland_inject_preedit(a, "旧窗口");
        first.armed = true;
        Check((mode == "commit-reenter" ? jalium_test_wayland_inject_commit(a, "提交") :
            jalium_test_wayland_inject_text_input_leave(a)) == JALIUM_OK, "deliver composition ending through protocol listener");
        Check(first.ends == 1 && second.starts == 1 && second.updates == 1,
            "ending callback can start a new target composition without duplicate termination");
        jalium_test_wayland_inject_keyboard(b, JALIUM_EVENT_KEY_DOWN, 30, 0);
        jalium_test_wayland_inject_keyboard(b, JALIUM_EVENT_KEY_UP, 30, 0);
        Check(second.chars == 0, "outer ending callback does not cancel a nested composition");
        jalium_test_wayland_inject_preedit(b, "");
        jalium_test_wayland_inject_keyboard(b, JALIUM_EVENT_KEY_DOWN, 30, 0);
        jalium_test_wayland_inject_keyboard(b, JALIUM_EVENT_KEY_UP, 30, 0);
        Check(second.chars == 1, "ordinary text resumes after the nested composition ends");
    } else if (mode == "preedit-reenter-same") {
        jalium_test_wayland_inject_keyboard(a, JALIUM_EVENT_FOCUS_GAINED, 0, 0);
        first.armed = true;
        jalium_test_wayland_inject_preedit(a, "过期");
        Check(first.starts == 1 && first.updates == 1 && first.update == "嵌套",
            "nested preedit on the same target replaces the retired outer transaction");
    } else if (mode.find("v3-") == 0) {
        jalium_test_wayland_inject_keyboard(a, JALIUM_EVENT_FOCUS_GAINED, 0, 0);
        first.armed = true;
        if (mode == "v3-pending-focus") {
            Check(jalium_test_wayland_inject_text_input_batch(a, 0, 0, nullptr, "过期", 0) == JALIUM_OK,
                "stage a real v3 input transaction");
            jalium_test_wayland_inject_keyboard(b, JALIUM_EVENT_FOCUS_GAINED, 0, 0);
            jalium_test_wayland_inject_text_input_batch(b, 0, 0, nullptr, nullptr, 1);
            Check(first.updates == 0 && second.updates == 0, "staged old-focus transaction cannot reach replacement focus");
        } else {
            Check(jalium_test_wayland_inject_text_input_batch(a, 1, 0, "过期提交", "过期预编辑", 1) == JALIUM_OK,
                "deliver v3 delete commit and preedit in one transaction");
            if (mode == "v3-delete-close")
                Check(first.closed && first.ends == 0 && second.updates == 0,
                    "delete callback teardown retires the remainder of its transaction");
            else Check(first.updates == 1 && first.update == "嵌套" && first.ends == 0,
                "nested v3 transaction survives outer delete callback continuation");
        }
    } else if (mode.find("preedit-") == 0) {
        Check(jalium_test_wayland_inject_keyboard(a, JALIUM_EVENT_FOCUS_GAINED, 0, 0) == JALIUM_OK,
            "focus the original composition target");
        first.armed = true;
        Check(jalium_test_wayland_inject_preedit(a, "旧窗口预编辑") == JALIUM_OK,
            "deliver protocol preedit through the installed input method listener");
        Check(first.acted, "composition start invokes the requested lifetime transition");
        Check(first.updates == 0 && second.updates == 0,
            "retired preedit does not continue into the old or replacement target");
        jalium_test_wayland_inject_keyboard(b, JALIUM_EVENT_KEY_DOWN, 30, 0);
        jalium_test_wayland_inject_keyboard(b, JALIUM_EVENT_KEY_UP, 30, 0);
        Check(second.chars == 1, "new focus accepts ordinary text after the old composition is retired");
    } else {
        Check(jalium_test_wayland_inject_touch(a, JALIUM_EVENT_POINTER_DOWN, 101, 20, 30) == JALIUM_OK &&
            jalium_test_wayland_inject_touch(a, JALIUM_EVENT_POINTER_DOWN, 102, 30, 40) == JALIUM_OK,
            "start two contacts on the retiring window");
        Check(jalium_test_wayland_inject_touch(b, JALIUM_EVENT_POINTER_DOWN, 103, 20, 30) == JALIUM_OK,
            "keep an unrelated window contact active");
        first.armed = true;
        if (mode == "touch-hide-close") jalium_window_hide(a);
        else { jalium_window_destroy(a); first.closed = true; }
        Check(first.acted, "retirement publishes touch cancellation before returning");
        Check(first.cancels == (mode == "touch-hide-close" ? 1 : 2),
            "contacts cancel once and callbacks stop when their target has been destroyed");
        Check(jalium_test_wayland_inject_touch(b, JALIUM_EVENT_POINTER_UP, 103, 20, 30) == JALIUM_OK &&
            second.ups == 1, "unrelated contact survives another window's cancellation");
        if (mode == "touch-cancel-replace")
            Check(jalium_test_wayland_inject_touch(b, JALIUM_EVENT_POINTER_UP, first.replacementId, 15, 25) == JALIUM_OK &&
                second.ups == 2, "new contact reusing a canceled id survives the old cancellation loop");
    }
    first.armed = false;
    if (!first.closed) jalium_window_destroy(a);
    jalium_window_destroy(b);
    jalium_platform_shutdown();
    std::printf("Input lifetime %s: %d passed, %d failed\n", mode.c_str(), checks - failures, failures);
    return failures == 0 ? 0 : 1;
}

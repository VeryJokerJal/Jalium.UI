// Controlled real wl_seat globals on libweston 8. The private backend entry
// points below are exported by that ABI; CMake restricts this fixture to it.
#include <libweston/libweston.h>
#include <wayland-server-core.h>
#include <sys/socket.h>
#include <sys/un.h>
#include <unistd.h>
#include <linux/input-event-codes.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

void weston_seat_init(struct weston_seat*, struct weston_compositor*, const char*);
int weston_seat_init_pointer(struct weston_seat*);
int weston_seat_init_keyboard(struct weston_seat*, struct xkb_keymap*);
int weston_seat_init_touch(struct weston_seat*);
void weston_seat_release(struct weston_seat*);

static struct weston_compositor* compositor;
static struct weston_seat seats[2];
static bool active[2];

static bool filter_global(const struct wl_client* client,
                          const struct wl_global* global, void* data)
{
    (void)client;
    (void)data;
    return wl_global_get_interface(global) != &wl_seat_interface ||
           wl_global_get_user_data(global) == &seats[0] ||
           wl_global_get_user_data(global) == &seats[1];
}

static void add_seat(int index)
{
    if (active[index]) return;
    char name[32];
    snprintf(name, sizeof name, "jalium-test-seat-%d", index);
    memset(&seats[index], 0, sizeof seats[index]);
    weston_seat_init(&seats[index], compositor, name);
    weston_seat_init_pointer(&seats[index]);
    weston_seat_init_keyboard(&seats[index], NULL);
    weston_seat_init_touch(&seats[index]);
    active[index] = true;
}

static struct weston_view* find_view(pid_t pid)
{
    struct weston_view* view;
    wl_list_for_each(view, &compositor->view_list, link) {
        if (!view->surface->resource || view->surface->width <= 0) continue;
        pid_t client_pid;
        wl_client_get_credentials(wl_resource_get_client(view->surface->resource),
                                  &client_pid, NULL, NULL);
        if (client_pid == pid) return view;
    }
    return NULL;
}

static void position_pointer(struct weston_seat* seat, struct weston_view* view, int x, int y)
{
    struct weston_pointer* pointer = weston_seat_get_pointer(seat);
    float global_x, global_y;
    weston_view_to_global_float(view, x, y, &global_x, &global_y);
    struct weston_pointer_motion_event motion = {
        .mask = WESTON_POINTER_MOTION_ABS, .x = global_x, .y = global_y
    };
    clock_gettime(CLOCK_MONOTONIC, &motion.time);
    weston_pointer_move(pointer, &motion);
    weston_pointer_set_focus(pointer, view, wl_fixed_from_int(x), wl_fixed_from_int(y));
    weston_pointer_send_motion(pointer, &motion.time, &motion);
}

static int command_ready(int fd, uint32_t mask, void* data)
{
    (void)mask;
    (void)data;
    char command[96];
    ssize_t count;
    while ((count = recv(fd, command, sizeof command - 1, MSG_DONTWAIT)) > 0) {
        command[count] = 0;
        int index, pid, x, y;
        unsigned key, state, modifiers;
        if (sscanf(command, "remove%d", &index) == 1 && index >= 0 && index < 2) {
            if (active[index]) {
                active[index] = false;
                weston_seat_release(&seats[index]);
            }
        } else if (sscanf(command, "add%d", &index) == 1 && index >= 0 && index < 2) {
            add_seat(index);
        } else if (sscanf(command, "key %u %u", &key, &state) == 2) {
            struct weston_seat* seat = active[0] ? &seats[0] : active[1] ? &seats[1] : NULL;
            if (seat) {
                struct timespec time;
                clock_gettime(CLOCK_MONOTONIC, &time);
                weston_keyboard_send_key(weston_seat_get_keyboard(seat), &time, key, state);
            }
        } else if (sscanf(command, "modifiers %u", &modifiers) == 1) {
            struct weston_seat* seat = active[0] ? &seats[0] : active[1] ? &seats[1] : NULL;
            if (seat) weston_keyboard_send_modifiers(weston_seat_get_keyboard(seat),
                wl_display_next_serial(compositor->wl_display), modifiers, 0, 0, 0);
        } else if (sscanf(command, "pointer %d %d %d %u", &pid, &x, &y, &state) == 4) {
            struct weston_view* view = find_view(pid);
            struct weston_seat* seat = active[0] ? &seats[0] : active[1] ? &seats[1] : NULL;
            if (view && seat) {
                struct timespec time;
                clock_gettime(CLOCK_MONOTONIC, &time);
                struct weston_pointer* pointer = weston_seat_get_pointer(seat);
                position_pointer(seat, view, x, y);
                weston_pointer_send_button(pointer, &time, BTN_LEFT, state);
                weston_pointer_send_frame(pointer);
            }
        } else if (sscanf(command, "hold %d", &pid) == 1 ||
                   sscanf(command, "focus %d", &pid) == 1) {
            struct weston_view* view = find_view(pid);
            struct weston_seat* seat = active[0] ? &seats[0] : active[1] ? &seats[1] : NULL;
            if (view && seat) {
                struct weston_keyboard* keyboard = weston_seat_get_keyboard(seat);
                struct weston_pointer* pointer = weston_seat_get_pointer(seat);
                weston_keyboard_set_focus(keyboard, view->surface);
                position_pointer(seat, view, 20, 30);
                if (strncmp(command, "hold", 4) == 0) {
                    struct timespec time;
                    clock_gettime(CLOCK_MONOTONIC, &time);
                    weston_keyboard_send_modifiers(keyboard,
                        wl_display_next_serial(compositor->wl_display),
                        1u << keyboard->xkb_info->ctrl_mod, 0, 0, 0);
                    weston_keyboard_send_key(keyboard, &time, KEY_LEFTCTRL, WL_KEYBOARD_KEY_STATE_PRESSED);
                    weston_keyboard_send_key(keyboard, &time, KEY_A, WL_KEYBOARD_KEY_STATE_PRESSED);
                    weston_pointer_send_button(pointer, &time, BTN_LEFT, WL_POINTER_BUTTON_STATE_PRESSED);
                    weston_pointer_send_frame(pointer);
                    struct weston_touch* touch = weston_seat_get_touch(seat);
                    weston_touch_set_focus(touch, view);
                    weston_touch_send_down(touch, &time, 1, wl_fixed_from_int(40), wl_fixed_from_int(40));
                    weston_touch_send_down(touch, &time, 2, wl_fixed_from_int(60), wl_fixed_from_int(40));
                    weston_touch_send_frame(touch);
                }
            } else {
                fprintf(stderr, "seat fixture: missing mapped view or seat for %s\n", command);
            }
        }
        fprintf(stderr, "seat fixture: %s active=%d,%d\n", command, active[0], active[1]);
        wl_display_flush_clients(compositor->wl_display);
    }
    return 0;
}

WL_EXPORT int wet_module_init(struct weston_compositor* instance, int* argc, char* argv[])
{
    (void)argc;
    (void)argv;
    const char* path = getenv("JALIUM_TEST_SEAT_CONTROL");
    if (!path || strlen(path) >= sizeof(((struct sockaddr_un*)0)->sun_path)) return -1;
    compositor = instance;
    wl_display_set_global_filter(instance->wl_display, filter_global, NULL);
    add_seat(0);
    int fd = socket(AF_UNIX, SOCK_DGRAM | SOCK_NONBLOCK | SOCK_CLOEXEC, 0);
    struct sockaddr_un address = { .sun_family = AF_UNIX };
    strcpy(address.sun_path, path);
    if (fd < 0 || bind(fd, (struct sockaddr*)&address, sizeof address) < 0) return -1;
    return wl_event_loop_add_fd(wl_display_get_event_loop(instance->wl_display),
                               fd, WL_EVENT_READABLE, command_ready, NULL) ? 0 : -1;
}

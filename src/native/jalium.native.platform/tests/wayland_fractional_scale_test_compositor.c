// Weston test module: supplies a controlled fractional preference while Weston
// performs real xdg-shell, viewport, buffer and presentation validation.
#include <libweston/libweston.h>
#include <wayland-server-core.h>
#include "fractional-scale-v1-server-protocol.h"
#include <stdlib.h>

static uint32_t preferred_scale = 150;

static void destroy_resource(struct wl_client* client, struct wl_resource* resource)
{
    (void)client;
    wl_resource_destroy(resource);
}

static const struct wp_fractional_scale_v1_interface scale_implementation = { destroy_resource };

static void get_scale(struct wl_client* client, struct wl_resource* manager,
                      uint32_t id, struct wl_resource* surface)
{
    (void)manager;
    (void)surface;
    struct wl_resource* resource = wl_resource_create(client, &wp_fractional_scale_v1_interface, 1, id);
    if (!resource) { wl_client_post_no_memory(client); return; }
    wl_resource_set_implementation(resource, &scale_implementation, NULL, NULL);
    wp_fractional_scale_v1_send_preferred_scale(resource, preferred_scale);
}

static const struct wp_fractional_scale_manager_v1_interface manager_implementation = {
    destroy_resource, get_scale
};

static void bind_manager(struct wl_client* client, void* data, uint32_t version, uint32_t id)
{
    (void)data;
    (void)version;
    struct wl_resource* resource = wl_resource_create(client, &wp_fractional_scale_manager_v1_interface, 1, id);
    if (!resource) { wl_client_post_no_memory(client); return; }
    wl_resource_set_implementation(resource, &manager_implementation, NULL, NULL);
}

WL_EXPORT int wet_module_init(struct weston_compositor* compositor, int* argc, char* argv[])
{
    (void)argc;
    (void)argv;
    const char* value = getenv("JALIUM_TEST_FRACTIONAL_SCALE");
    if (value) preferred_scale = (uint32_t)strtoul(value, NULL, 10);
    if (preferred_scale < 120 || preferred_scale > 960) return -1;
    return wl_global_create(compositor->wl_display, &wp_fractional_scale_manager_v1_interface,
                            1, NULL, bind_manager) ? 0 : -1;
}

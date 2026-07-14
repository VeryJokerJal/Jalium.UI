#define JALIUM_GSTREAMER_LOADER_IMPLEMENTATION
#include "gstreamer_loader.h"

#if JALIUM_HAS_GSTREAMER

#include <array>
#include <cstdlib>
#include <cstring>
#include <dlfcn.h>
#include <mutex>

namespace jalium::media::gst_runtime {

#define JALIUM_DEFINE_GSTREAMER_FUNCTION(name) \
    decltype(&::name) name##_ptr = nullptr;
JALIUM_GSTREAMER_FUNCTIONS(JALIUM_DEFINE_GSTREAMER_FUNCTION)
#undef JALIUM_DEFINE_GSTREAMER_FUNCTION

GType* gst_fraction_type_ptr = nullptr;

namespace {

struct Library {
    const char* soname;
    void* handle = nullptr;
};

// Load the leaf libraries explicitly as well as GLib/GObject. dlsym(handle,
// name) searches a library's dependency scope, but keeping every handle makes
// the required runtime capability deterministic and permits symmetric unload.
std::array<Library, 9> g_libraries{{
    {"libglib-2.0.so.0"},
    {"libgobject-2.0.so.0"},
    {"libgstreamer-1.0.so.0"},
    {"libgstbase-1.0.so.0"},
    {"libgstapp-1.0.so.0"},
    {"libgstaudio-1.0.so.0"},
    {"libgstvideo-1.0.so.0"},
    {"libgstpbutils-1.0.so.0"},
    {"libgstallocators-1.0.so.0"},
}};

std::mutex g_mutex;
bool g_loaded = false;

void* FindSymbol(const char* name) noexcept
{
    for (const Library& library : g_libraries) {
        if (!library.handle) continue;
        if (void* symbol = dlsym(library.handle, name)) return symbol;
    }
    return nullptr;
}

void ResetSymbols() noexcept
{
#define JALIUM_RESET_GSTREAMER_FUNCTION(name) name##_ptr = nullptr;
    JALIUM_GSTREAMER_FUNCTIONS(JALIUM_RESET_GSTREAMER_FUNCTION)
#undef JALIUM_RESET_GSTREAMER_FUNCTION
    gst_fraction_type_ptr = nullptr;
}

void CloseLibraries() noexcept
{
    for (auto it = g_libraries.rbegin(); it != g_libraries.rend(); ++it) {
        if (it->handle) dlclose(it->handle);
        it->handle = nullptr;
    }
}

bool IsDisabledByEnvironment() noexcept
{
    const char* value = std::getenv("JALIUM_MEDIA_DISABLE_GSTREAMER");
    return value && *value && std::strcmp(value, "0") != 0;
}

} // namespace

bool Load() noexcept
{
    std::lock_guard<std::mutex> lock(g_mutex);
    if (g_loaded) return true;
    if (IsDisabledByEnvironment()) return false;

    for (Library& library : g_libraries) {
        library.handle = dlopen(library.soname, RTLD_NOW | RTLD_LOCAL);
        if (!library.handle) {
            CloseLibraries();
            ResetSymbols();
            return false;
        }
    }

#define JALIUM_RESOLVE_GSTREAMER_FUNCTION(name) \
    name##_ptr = reinterpret_cast<decltype(name##_ptr)>(FindSymbol(#name)); \
    if (!name##_ptr) { \
        CloseLibraries(); \
        ResetSymbols(); \
        return false; \
    }
    JALIUM_GSTREAMER_FUNCTIONS(JALIUM_RESOLVE_GSTREAMER_FUNCTION)
#undef JALIUM_RESOLVE_GSTREAMER_FUNCTION

    gst_fraction_type_ptr = static_cast<GType*>(FindSymbol("_gst_fraction_type"));
    if (!gst_fraction_type_ptr) {
        CloseLibraries();
        ResetSymbols();
        return false;
    }

    g_loaded = true;
    return true;
}

void Unload() noexcept
{
    std::lock_guard<std::mutex> lock(g_mutex);
    if (!g_loaded) return;
    CloseLibraries();
    ResetSymbols();
    g_loaded = false;
}

bool IsLoaded() noexcept
{
    std::lock_guard<std::mutex> lock(g_mutex);
    return g_loaded;
}

} // namespace jalium::media::gst_runtime

#endif // JALIUM_HAS_GSTREAMER

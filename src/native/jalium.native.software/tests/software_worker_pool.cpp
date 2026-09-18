#include "jalium_api.h"

#include <cstdlib>
#include <iostream>
#include <memory>
#include <string>

extern "C" void jalium_software_init();

namespace {

bool SetEnvironmentValue(const char* name, const char* value)
{
#ifdef _WIN32
    return _putenv_s(name, value ? value : "") == 0;
#else
    return value ? setenv(name, value, 1) == 0 : unsetenv(name) == 0;
#endif
}

class ScopedEnvironmentVariable {
public:
    ScopedEnvironmentVariable(const char* name, const char* value)
        : name_(name)
    {
        const char* previous = std::getenv(name);
        if (previous) {
            hadPrevious_ = true;
            previous_ = previous;
        }
        valid_ = SetEnvironmentValue(name, value);
    }

    ~ScopedEnvironmentVariable()
    {
        if (hadPrevious_)
            (void)SetEnvironmentValue(name_.c_str(), previous_.c_str());
        else
            (void)SetEnvironmentValue(name_.c_str(), nullptr);
    }

    bool IsValid() const { return valid_; }

private:
    std::string name_;
    std::string previous_;
    bool hadPrevious_ = false;
    bool valid_ = false;
};

struct ContextDeleter {
    void operator()(JaliumContext* context) const
    {
        jalium_context_destroy(context);
    }
};

struct RenderTargetDeleter {
    void operator()(JaliumRenderTarget* target) const
    {
        jalium_render_target_destroy(target);
    }
};

using ContextPtr = std::unique_ptr<JaliumContext, ContextDeleter>;
using RenderTargetPtr = std::unique_ptr<JaliumRenderTarget, RenderTargetDeleter>;

bool DrawClear(JaliumRenderTarget* target, float value)
{
    if (!target) {
        std::cerr << "  DrawClear received a null target\n";
        return false;
    }
    const JaliumResult beginResult = jalium_render_target_begin_draw(target);
    if (beginResult != JALIUM_OK) {
        std::cerr << "  BeginDraw failed: " << static_cast<int>(beginResult) << '\n';
        return false;
    }
    jalium_render_target_clear(
        target, value, value * 0.75f, value * 0.5f, 1.0f);
    const JaliumResult endResult = jalium_render_target_end_draw(target);
    if (endResult != JALIUM_OK) {
        std::cerr << "  EndDraw failed: " << static_cast<int>(endResult) << '\n';
        return false;
    }
    return true;
}

bool QueryStats(JaliumRenderTarget* target, JaliumGpuStats& stats)
{
    stats = {};
    return target &&
        jalium_render_target_query_gpu_stats(target, &stats) == JALIUM_OK;
}

bool ExpectStats(
    const char* stage,
    JaliumRenderTarget* target,
    int32_t expectedWorkerCount,
    bool expectParallelWork)
{
    JaliumGpuStats stats{};
    const bool querySucceeded = QueryStats(target, stats);
    const bool parallelMatches = expectParallelWork
        ? stats.softwareParallelNs > 0
        : stats.softwareParallelNs == 0;
    if (querySucceeded &&
        stats.softwareWorkerCount == expectedWorkerCount &&
        parallelMatches) {
        return true;
    }

    std::cerr << "  stage=" << stage
              << " query=" << (querySucceeded ? "ok" : "failed")
              << " workers=" << stats.softwareWorkerCount
              << " expected-workers=" << expectedWorkerCount
              << " parallel-ns=" << stats.softwareParallelNs
              << " expected-parallel=" << (expectParallelWork ? "yes" : "no")
              << '\n';
    return false;
}

bool TestWorkerThreadsStartOnDemand()
{
    ScopedEnvironmentVariable threadOverride("JALIUM_SOFTWARE_THREADS", "8");
    ScopedEnvironmentVariable backendOverride(
        "JALIUM_RENDER_BACKEND", "software");
    if (!threadOverride.IsValid() || !backendOverride.IsValid()) {
        std::cerr << "  failed to set test environment variables\n";
        return false;
    }

    jalium_software_init();
    ContextPtr context(jalium_context_create(JALIUM_BACKEND_SOFTWARE));
    if (!context) {
        std::cerr << "  failed to create Software context\n";
        return false;
    }
    const JaliumBackend backend = jalium_context_get_backend(context.get());
    if (backend != JALIUM_BACKEND_SOFTWARE) {
        std::cerr << "  context selected unexpected backend: "
                  << static_cast<int>(backend) << '\n';
        return false;
    }
    JaliumSurfaceDescriptor offscreenSurface{};
    offscreenSurface.platform = JALIUM_PLATFORM_UNKNOWN;
    offscreenSurface.kind = JALIUM_SURFACE_KIND_NATIVE_WINDOW;
    // The public API requires a non-zero native handle. Software ignores an
    // unknown-platform handle, which gives this cross-platform test a real
    // render target without coupling it to a window-system connection.
    offscreenSurface.handle0 = 1;
    RenderTargetPtr target(jalium_render_target_create_for_surface(
        context.get(), &offscreenSurface, 64, 64));
    if (!target) {
        std::cerr << "  failed to create Software render target\n";
        return false;
    }

    if (!ExpectStats("constructed", target.get(), 0, false))
        return false;

    // The default visible empty-window size must stay on the render thread. At
    // 800x600 this is the only full-surface operation an untouched window needs.
    if (jalium_render_target_resize(target.get(), 800, 600) != JALIUM_OK ||
        !DrawClear(target.get(), 0.1f) ||
        !ExpectStats("default-empty-window", target.get(), 0, false)) {
        return false;
    }

    // 32 rows at grain 16 produce two chunks. The width reaches the simple-clear
    // parallel threshold, and the submitting thread consumes one chunk, so this
    // job needs exactly one background worker.
    if (jalium_render_target_resize(target.get(), 65536, 32) != JALIUM_OK ||
        !DrawClear(target.get(), 0.2f) ||
        !ExpectStats("two-chunk", target.get(), 1, true)) {
        return false;
    }

    // A larger job grows the pool to the configured cap instead of eagerly
    // creating every worker for the first qualifying task.
    if (jalium_render_target_resize(target.get(), 4096, 1024) != JALIUM_OK ||
        !DrawClear(target.get(), 0.3f) ||
        !ExpectStats("large", target.get(), 8, true)) {
        return false;
    }

    // Once workers exist, later small work still executes directly. Destroying
    // target and backend at function exit exercises worker wakeup and join.
    if (jalium_render_target_resize(target.get(), 800, 600) != JALIUM_OK ||
        !DrawClear(target.get(), 0.4f) ||
        !ExpectStats("default-after-growth", target.get(), 8, false)) {
        return false;
    }

    return true;
}

} // namespace

int main()
{
    if (!TestWorkerThreadsStartOnDemand()) {
        std::cerr << "FAIL: software worker pool was not created lazily per workload\n";
        return 1;
    }

    std::cout << "PASS: software worker pool starts on demand and shuts down cleanly\n";
    return 0;
}

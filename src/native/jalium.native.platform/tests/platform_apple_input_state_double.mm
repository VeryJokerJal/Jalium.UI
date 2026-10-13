#import <ApplicationServices/ApplicationServices.h>
#import <Carbon/Carbon.h>
#include <array>
#include <atomic>
#include <cstring>
#include <dlfcn.h>
#include <mutex>
#include <pthread.h>
#include <vector>

// This library is linked only by the input-state regression. Interposition
// supplies snapshot data to the real ABI; it never posts a system event.
static std::array<std::atomic<bool>, 128> keys{};
static std::atomic<unsigned> queries{0};
static std::atomic<bool> wrongSource{false};
static std::atomic<int> layoutASCII{1};
static std::atomic<unsigned> offMainLayoutQueries{0};
struct LayoutObserver {
    CFNotificationCenterRef center;
    const void* observer;
    CFNotificationCallback callback;
};
static std::mutex observerMutex;
static std::vector<LayoutObserver> layoutObservers;

extern "C" void JaliumTestSetLayoutASCII(int value) { layoutASCII.store(value, std::memory_order_relaxed); }
static void* TestInputSourceProperty(TISInputSourceRef source, CFStringRef property)
{
    if (!pthread_main_np()) offMainLayoutQueries.fetch_add(1, std::memory_order_relaxed);
    int mode = layoutASCII.load(std::memory_order_relaxed);
    if (mode != 3 && CFEqual(property, kTISPropertyInputSourceIsASCIICapable)) {
        if (mode == -1) return nullptr;
        if (mode == 2) return const_cast<void*>(reinterpret_cast<const void*>(CFSTR("invalid metadata")));
        return const_cast<void*>(reinterpret_cast<const void*>(mode ? kCFBooleanTrue : kCFBooleanFalse));
    }
    // References within the interposition library retain the original target.
    return TISGetInputSourceProperty(source, property);
}
static TISInputSourceRef TestCurrentLayout()
{
    if (!pthread_main_np()) offMainLayoutQueries.fetch_add(1, std::memory_order_relaxed);
    return TISCopyCurrentKeyboardLayoutInputSource();
}
static void TestAddObserver(CFNotificationCenterRef center, const void* observer, CFNotificationCallback callback,
    CFStringRef name, const void* object, CFNotificationSuspensionBehavior behavior)
{
    Dl_info image{};
    if (name && CFEqual(name, kTISNotifySelectedKeyboardInputSourceChanged) &&
        dladdr(reinterpret_cast<const void*>(callback), &image) && image.dli_fname &&
        std::strstr(image.dli_fname, "libjalium.native.platform.dylib")) {
        std::lock_guard<std::mutex> lock(observerMutex);
        layoutObservers.push_back({center, observer, callback});
    }
    CFNotificationCenterAddObserver(center, observer, callback, name, object, behavior);
}
extern "C" void JaliumTestKeyboardLayoutChanged()
{
    // Invoke only callbacks belonging to the loaded Platform, in this test
    // process. This never posts a distributed notification or changes the
    // system's selected input source.
    std::vector<LayoutObserver> observers;
    { std::lock_guard<std::mutex> lock(observerMutex); observers = layoutObservers; }
    for (auto observer : observers)
        observer.callback(observer.center, const_cast<void*>(observer.observer),
            kTISNotifySelectedKeyboardInputSourceChanged, nullptr, nullptr);
}
extern "C" unsigned JaliumTestOffMainLayoutQueries() { return offMainLayoutQueries.load(std::memory_order_relaxed); }

extern "C" void JaliumTestSetPolledKey(unsigned code, bool down)
{
    if (code < keys.size()) keys[code].store(down, std::memory_order_relaxed);
}
extern "C" void JaliumTestResetPolledKeys()
{
    for (auto& key : keys) key.store(false, std::memory_order_relaxed);
    queries.store(0, std::memory_order_relaxed); wrongSource.store(false, std::memory_order_relaxed);
}
extern "C" unsigned JaliumTestKeyQueryCount() { return queries.load(std::memory_order_relaxed); }
extern "C" bool JaliumTestWrongKeySource() { return wrongSource.load(std::memory_order_relaxed); }
static bool TestKeyState(CGEventSourceStateID source, CGKeyCode key)
{
    queries.fetch_add(1, std::memory_order_relaxed);
    if (source != kCGEventSourceStateCombinedSessionState) wrongSource.store(true, std::memory_order_relaxed);
    return key < keys.size() && keys[key].load(std::memory_order_relaxed);
}
__attribute__((used)) static const struct { const void* replacement; const void* original; }
    interpose[] __attribute__((section("__DATA,__interpose"))) = {
        {reinterpret_cast<const void*>(TestKeyState), reinterpret_cast<const void*>(CGEventSourceKeyState)},
        {reinterpret_cast<const void*>(TestInputSourceProperty), reinterpret_cast<const void*>(TISGetInputSourceProperty)},
        {reinterpret_cast<const void*>(TestCurrentLayout), reinterpret_cast<const void*>(TISCopyCurrentKeyboardLayoutInputSource)},
        {reinterpret_cast<const void*>(TestAddObserver), reinterpret_cast<const void*>(CFNotificationCenterAddObserver)}};

#import <ApplicationServices/ApplicationServices.h>
#include <array>
#include <atomic>

// This library is linked only by the input-state regression. Interposition
// supplies snapshot data to the real ABI; it never posts a system event.
static std::array<std::atomic<bool>, 128> keys{};
static std::atomic<unsigned> queries{0};
static std::atomic<bool> wrongSource{false};

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
    interpose __attribute__((section("__DATA,__interpose"))) = {
        reinterpret_cast<const void*>(TestKeyState), reinterpret_cast<const void*>(CGEventSourceKeyState)};

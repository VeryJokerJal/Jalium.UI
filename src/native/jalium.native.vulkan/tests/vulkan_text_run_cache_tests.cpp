#include "vulkan_text_run_cache.h"
#include <iostream>
using namespace jalium::vulkan_text_cache;
int main()
{
    int checks = 0, failures = 0;
    auto check = [&](bool value, const char* name) { ++checks; if (!value) { ++failures; std::cerr << name << "\n"; } };
    SelfHostedTextRunCache cache;
    SelfHostedTextRunKey first{}; first.textLength = 3; first.textHash = 7; first.visibility = 1; first.glyphRanges = {0, 1};
    SelfHostedTextRunEntry entry{}; entry.pixels = std::make_shared<std::vector<uint8_t>>(64, 8);
    auto pixels = entry.pixels;
    auto* stored = cache.Insert(first, L"one", std::move(entry));
    check(stored && stored->pixels == pixels, "small entries retain their immutable bitmap");
    check(cache.Find(first, L"two") == nullptr, "hash collisions do not return another source string");
    auto second = first; second.glyphRanges = {1, 1};
    SelfHostedTextRunEntry next{}; next.pixels = std::make_shared<std::vector<uint8_t>>(32, 9);
    cache.Insert(second, L"one", std::move(next));
    check(cache.Find(first, L"one")->pixels == pixels && cache.Find(second, L"one")->pixels != pixels,
        "different source glyph ranges cannot share a bitmap");
    SelfHostedTextRunEntry oversized{};
    oversized.pixels = std::make_shared<std::vector<uint8_t>>(SelfHostedTextRunCache::kMaxBytes + 1, 0);
    auto large = oversized.pixels;
    check(cache.Insert(first, L"one", std::move(oversized)) == nullptr, "oversized bitmaps are not inserted then immediately evicted");
    check(oversized.pixels == large && oversized.text.empty(), "rejection leaves the caller's fallback draw intact");
    check(cache.Find(first, L"one") && cache.Find(first, L"one")->pixels == pixels, "rejected updates do not replace a valid cached value");
    auto huge = first; huge.textLength = SelfHostedTextRunCache::kMaxBytes / sizeof(wchar_t) + 1;
    std::wstring source(huge.textLength, L'x'); SelfHostedTextRunEntry sourceOnly{};
    check(cache.Insert(huge, source.data(), std::move(sourceOnly)) == nullptr && sourceOnly.text.empty(), "source strings also count toward the byte budget before copying");
    for (uint32_t i = 0; i < SelfHostedTextRunCache::kMaxEntries + 2; ++i) {
        auto key = first; key.formatInstanceId = i + 1; SelfHostedTextRunEntry small{}; cache.Insert(key, L"one", std::move(small));
    }
    auto oldest = first; oldest.formatInstanceId = 1;
    auto newest = first; newest.formatInstanceId = SelfHostedTextRunCache::kMaxEntries + 2;
    check(!cache.Find(oldest, L"one") && cache.Find(newest, L"one"), "LRU trimming preserves the newest entry");
    cache.Clear(); check(!cache.Find(newest, L"one"), "clear releases retained source layouts");
    std::cout << "{\"count\":" << checks << ",\"failed\":" << failures << "}\n";
    return failures ? 1 : 0;
}

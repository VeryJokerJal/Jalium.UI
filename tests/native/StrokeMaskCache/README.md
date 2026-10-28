# Analytic stroke mask cache checks

This standalone suite needs a C++20 compiler and CMake; it does not require a GPU.
Run it from the repository root:

```sh
cmake -S tests/native/StrokeMaskCache -B build/stroke-mask-cache
cmake --build build/stroke-mask-cache --config Debug
ctest --test-dir build/stroke-mask-cache -C Debug --output-on-failure
cmake --build build/stroke-mask-cache --config Release
ctest --test-dir build/stroke-mask-cache -C Release --output-on-failure
```

The 13 cases cover independent LRU lanes, count and allocated-capacity budgets,
multiple evictions, oversized replacements, disabled lanes, and recycling. A
borrower models both pending draws and the primary cache: its mask must remain
immutable after secondary eviction. Recycled masks must have cleared logical
state and retain vector capacity. Once warm, replacement churn must allocate no
mask, list, or map storage.

The production engine keeps its existing 512-entry primary cache and enables
only the protected lane: masks with at least 128 rectangles, up to 1,024 entries
and 32 MiB of rectangle capacity. One uniquely owned allocation of up to 1 MiB
can be retained for recycling. These limits exclude container overhead, primary
cache storage, and masks still owned by pending draws.

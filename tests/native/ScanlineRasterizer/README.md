# Native stroke and coverage checks

This standalone CMake project tests analytic fill coverage, winding, degenerate
strokes, and reusable contour-buffer lifetimes. It compares pooled and vector
stroke output for 1,500 deterministic paths, including caps, joins, closed paths,
repeated points, and self-intersections.
The separate engine-selection test checks entry thresholds, cheap cached paths,
hysteresis, and unavailable GPU pipelines without depending on machine timing.
The simple-stroke check compares 10,000 transformed segments against the general
flattening and widening pipeline, including repeated endpoints and reflected or
sheared transforms. It also checks exact analytic coverage for 1,000 of them.

```powershell
cmake -S tests/native/ScanlineRasterizer -B out/native-scanline
cmake --build out/native-scanline --config Release
ctest --test-dir out/native-scanline -C Release --output-on-failure
```

The executable also prints two CPU microbenchmarks as JSON lines. Allocation
counts are collected separately from timing, after warm-up. Timings are
observations rather than pass/fail thresholds.

For a comparison with an earlier implementation, configure with
`-DREFERENCE_INCLUDE_DIR=<absolute directory>` containing that implementation's
`jalium_impeller_stroke.h` and `jalium_scanline_rasterizer.h`. This adds
`scanline_reference`; its output digest must match `scanline_tests` before using
the timing comparison. The reference target deliberately uses the vector output
API and needs no copy of the earlier headers in the repository.

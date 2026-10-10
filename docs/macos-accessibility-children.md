# macOS accessibility child snapshots

Fix for PR #175 review discussion_r4238211356, initially based on c9a203bee275b5580b33af8609caf1149ca0fa90, then rebased onto d8a1a5882687b6ba1b06a5e39d40d8e65be3d032 without overlapping changes.

`accessibilityChildren` now begins a request-scoped snapshot, copies its IDs in one callback, and releases it in an Objective-C `@finally`. It does not query Info or issue one Child callback per element. The three appended operations preserve the existing operation numbers and 136-byte request layout. For ReadChildren only, the existing text pointer carries aligned uint64 IDs and textCapacity counts IDs rather than UTF-16 units.

The managed snapshot calls the existing visibility/realization/deduplication traversal once and stores only IDs. Independent tokens permit nested queries without overwriting another request. Existing node IDs and native object lookup are reused. Each new request enumerates current state; mutations cannot resize an outstanding snapshot. Missing, detached, hidden, and destroyed objects continue to fail ordinary live-node queries. Release remains valid after the parent becomes hidden or detached. Native cleanup checks the captured callback/context before calling it, and bridge disposal clears snapshots before freeing the GCHandle. Native allocation failure also releases the snapshot.

Realized item parents use their owning ItemsControl so the bulk child relationship and parent lookup agree even when a template host's raw ancestry omits the list. Weak ID registry pruning is spaced according to the live registry size rather than every 512 registrations, avoiding repeated growing full-registry sweeps during initial large-tree registration.

## Regression coverage

- 128, 1024, and 4096 children: identical ordered IDs between the existing indexed query and snapshot query, stable repeat queries, parent IDs, allocations and elapsed time.
- Nested containers, removal/addition, collapsed elements, retained IDs after visibility changes, rejected detached-node queries, and a consistent outstanding snapshot across mutation.
- Independent tokens, wrong-owner requests, short buffers, duplicate releases, parent detachment, and queries after disposal.
- A 500-item virtualized ListBox: unrealized items remain absent; scrolling realizes item 400, exposes its actual container ID and correct list parent.
- Existing custom-element regressions now retrieve children through snapshots, retaining their CSS hidden/display-none and promoted-descendant cases.
- AppKit fixture protocols updated in accessibility, Window accessibility, and styled text/font suites. The native accessibility fixture adds a 4096-child request with one enumeration and one release. Existing reentrant hide, destroy, detach and replacement cases now exercise begin/read phases.

## Measurement and validation

Linux x64, .NET SDK 10.0.300 / runtime 10.0.8, Debug build. The comparison warms peer caches and IDs, then measures the managed callback workloads on the same live tree. It includes the managed output array, but excludes native vectors/NSArray allocation. Elapsed times are single-run observations, not statistically controlled benchmarks. Tests assert ordered results and reduced allocation, not timing thresholds.

The old native request requires N+1 full managed Children traversals; the new protocol requires one traversal and three callbacks (begin/read/release). Traversal counts follow the verified dispatch paths; the native regression asserts one begin callback for the complete large request. For fixed-depth trees, this reduces the repeated child traversal from O(N²) to O(N); ancestry and visibility checks still depend on depth.

Measured values and final test results are recorded below. This environment has no Apple SDK or macOS host, so AppKit Objective-C++ compilation, native tests and actual VoiceOver interaction remain unverified. The Linux C++ ABI check confirms the request size and old/new operation numbers; it does not compile AppKit code.

| Children | Full traversals, old → new | Callbacks, old → new | Managed time ms, old → new | Allocated bytes, old → new |
| ---: | ---: | ---: | ---: | ---: |
| 128 | 129 → 1 | 129 → 3 | 16.160 → 0.140 | 1,236,280 → 11,800 |
| 1024 | 1025 → 1 | 1025 → 3 | 1,112.435 → 1.164 | 92,004,976 → 106,320 |
| 4096 | 4097 → 1 | 4097 → 3 | 17,491.724 → 5.870 | 1,591,452,680 → 454,136 |

Managed compilation succeeded with existing warnings. The final focused run passed 24/33: all 6 new snapshot cases and all 14 visibility cases passed, together with 4 existing custom-element cases. The other 9 custom-element cases fail identically on the untouched c9a203be baseline on this Linux host, where macOS fallback peers are unavailable. Their failure identities were compared before/after; no new failure was introduced in this focused run. This is not a complete repository test run.

Reproduce:

```sh
dotnet test tests/Jalium.UI.MacOS.Tests/Jalium.UI.MacOS.Tests.csproj --no-restore -p:RunAnalyzers=false \
  --filter 'FullyQualifiedName~MacOSChildrenSnapshotTests|FullyQualifiedName~MacOSAutomationVisibilityTests|FullyQualifiedName~MacOSCustomElementAccessibilityTests' \
  --logger 'console;verbosity=detailed'
```

On macOS, run the existing platform_apple_accessibility_tests, platform_apple_window_accessibility_tests and platform_apple_text_font_tests CTest targets, then navigate a large realized list and nested/virtualized content with VoiceOver, including hide/show, scrolling and window destruction. Those native/VoiceOver acceptance steps are pending.

After rebasing onto d8a1a588, the snapshot and visibility suites were rebuilt and rerun: **20/20 passed**. The baseline comparison above refers to c9a203be.

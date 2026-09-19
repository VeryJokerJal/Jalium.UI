# Native XAML CSS conformance work

The target is the XAML-applicable functionality in the [22 June 2026 CSS Snapshot](https://www.w3.org/TR/2026/NOTE-css-2026-20260622/), together with the concrete gaps documented in `css-styling.md`. This work does not introduce an HTML or JavaScript runtime. Native local values and bindings continue to outrank CSS.

This is an implementation tracker, **not a claim of complete CSS conformance**. A feature is complete only after its applicable parsing, computed-value, layout, rendering, interaction, and platform tests pass. The initial isolated Windows CSS/Flex-related baseline had 351 passing test cases. Subsequent runs use the stricter class-name filter below to exclude unrelated test methods whose names happen to contain `css`.

| Area | Current implementation | Remaining acceptance work |
| --- | --- | --- |
| Shared XAML nodes | Visual elements, TextBlock inlines, and FrameworkContentElement/Run/Paragraph/FlowDocument share cascade and matching; markup and bindings tested | Generated content and full document layout coverage |
| Variables | Case-sensitive custom properties, escaped identifiers/functions/keywords, inheritance of computed tokens, complete cyclic components including fallback edges, pending shorthand substitution | Full conformance corpus |
| Registered properties | @property descriptors, layer/source-order registration per native document, syntax alternatives/lists, typed computation and inheritance, invalid-value recovery, font-size/face/line-height/color cycles, URL bases, conditional activation, viewport initial values and shared-sheet lifecycle regressions | Registered-property interpolation/keyframes, full font-feature/variation dependency coverage, modern color/image syntax, 3D transforms and full WPT/backend coverage |
| Numeric values | Values 4 arithmetic/comparison/stepped/trigonometric/exponential/sign functions and constants, dimensional products/division, symbolic percentages, special-value propagation, deferred scalar computation through existing converters, integer math rounding, all font-relative and viewport unit names, native font rulers, explicit host viewport variants and six container-relative units | Complete target-range metadata including zero-sized CSS typography, full font variation/stretch and vertical metrics, CSS writing-mode integration, additional value-module functions and the full WPT/backend corpus |
| Global keywords/layers | Initial/inherit/unset/all, author revert, named/nested layers, important-order reversal and revert-layer | Full initial/computed-value metadata and full conformance corpus |
| Selectors | Attribute, sibling, structural and functional selectors, :root/:scope, expanded-name of-type counting, namespace/default rules, escapes/comments and dynamic invalidation | Additional host pseudo states, generated pseudo-elements and full selector corpus |
| Namespaces | @namespace with per-sheet/import isolation, qualified/wildcard/null namespaces, namespace-aware supports/nesting/scope, runtime/generated XAML names and live qualified attributes, weak metadata/subscriptions | Full WPT/device coverage and remaining selector-module integrations |
| Nesting and scope | Shared parent-selector references, specificity, relative selectors, interleaved declarations, nested groups, explicit/implicit @scope roots and limits, proximity, scoped declarations, and weak tracking of external ancestor/sibling dependencies | Pseudo-element integration and full WPT/backend corpus |
| Conditional rules | Token-aware @media/@supports boolean grammar, geometric ranges and unknown-value handling, selector/at-rule support probes, scoped/nested groups, container rules and @property activation | Remaining media features, font-tech/font-format probes and full condition corpus |
| Container queries | Named/nested size queries, boolean/range conditions, content-box metrics, horizontal size containment, per-axis units, unregistered/registered custom-property style queries and dynamic invalidation; native bindings and templates preserved | Scroll-state queries, style ranges/ordinary-property queries, intrinsic-size overrides, orthogonal writing modes and complete backend conformance |
| Resources | Conditional/layered @import, support-gated fetching, live media conditions, independent anonymous layers, path-specific cycle recovery, redirect/base-URI preservation, cancellation/disposal and weak shared-sheet subscriptions | @font-face registration, stylesheet encodings/content types and full resource corpus |
| Layout values | Percent/math sizes and spacing, horizontal auto margins, relative translations | Full formatting contexts, remaining position modes, independent overflow axes |
| Ordinary flow | Block/flow-root layout over native children, adjoining and nested/empty/negative margin collapse, atomic inline controls, anonymous line runs, wrapping, inline-block sizing, baseline/top/bottom/length alignment and directional text alignment | Splittable text/inline fragments, full bidi/writing modes, remaining vertical-align values, contents/list/table/multicol models and full conformance |
| Floats/clear | Left/right and horizontal logical sides, shrink-to-fit float boxes, shared exclusions through normal blocks, independent-context isolation, inline reflow around floats, clearance/margin interactions, dynamic updates and native paint/hit ordering | Text-fragment wrapping, shape-outside, orthogonal writing modes, remaining clearance corner cases and full stacking/compositing conformance |
| Display and line-height | Separate outer inline role for inline-flex/inline-grid, two-keyword display syntax, independent display/visibility combination, typed number-versus-length line-height inheritance and length expressions | Full visibility descendant override semantics, remaining display models and full text formatting |
| Flex | Percent/math/font-relative basis, flex-flow, and display:flex on existing Panel-derived controls without reparenting | Baseline alignment, remaining sizing edge cases and other display models |
| CSS Grid | Existing panels support fixed/percent/math/font-relative/fr tracks, minmax/fit-content, integer/auto-fill/auto-fit repetition, named lines/areas, negative lines, spans, row/column/dense placement, grid/template shorthands and item alignment | Full intrinsic text sizing and spanning edge cases, baseline alignment, writing modes, inline/grid baseline details, fragmentation and complete backend conformance |
| Subgrid | Shared rows/columns, descendant intrinsic contributions, inherited/local/repeated line names, clipped parent named areas, bounded placement, gap differences, margin/padding edges, native flow-direction reversal and CSS constraint handling with native-local-value protection | Orthogonal writing modes, full cross-axis intrinsic interactions, absolute-positioning/fragmentation integration and the full WPT/backend corpus |
| CSS Grid box edges | CSS padding on existing panels, percent padding/margins and content-box sizing; native padding/border properties remain authoritative where present | Remaining formatting contexts and independent CSS border painting |
| Layout invalidation | Versioned native inputs, bounded grid/flow caches, nested layout regressions, headless/managed layout and query convergence, weak query subscriptions, and independent-sibling resize checks | Broader incremental-layout and resource-lifetime corpus |
| Grid/Flex gaps | Shared symbolic length handling, per-axis percentage bases, relative lengths and proper shorthand cascade | Percentage gaps in native non-CSS layouts and all indefinite-size/cyclic combinations |
| Color and filters | Inherited typography/color on generic visual containers, currentColor, shared color-matrix filters | Modern color spaces, full gradient syntax, backend pixel verification |
| Painting | Existing brushes, borders, shadows and effects retained | Layered backgrounds, independent borders, elliptical percentage radii, masks, blending and 3D |
| Text | Existing native text pipeline receives CSS document values | Spacing, whitespace, casing/index maps, writing modes and complete typography |
| Animation | Per-property durations/delays, exact CSS Bézier/steps curves, existing frame clocks, immediate local/binding takeover of CSS transitions, native animation precedence and live visual Storyboard base-value restoration | Symbolic layout-value and nonvisual-node transitions, keyframes and timelines |
| Documents/printing | CSS styles apply to existing XAML document nodes | Fragmentation, @page, page margin boxes and printing parity |

## WPF coexistence requirements

The default layout path remains native. CSS formatting boxes reference the original
controls and never replace user Children collections, namescopes, templates or bindings.
Explicit display declarations select a formatting algorithm through Measure/Arrange;
removing them restores the native algorithm. Native local values and bindings outrank
CSS, including CSS animations; native Storyboards retain native animation precedence.
Existing CssMappings registrations and converters remain authoritative over built-ins.
Every extension requires native-behavior regressions alongside its CSS tests.

CSS transition clocks record their origin. Assigning a native local value or binding
stops that clock immediately, even when the new value equals the CSS destination or
automatic animations have since been disabled. Native BeginAnimation/Storyboard clocks
retain control until stopped. Removing a visual Storyboard reveals the current native
or CSS base layer without replaying an old snapshot into the local property; active and
HoldEnd clocks, live bindings and absence of accidental local values are covered.

Within a CSS flow container, native children without an explicit display declaration
participate as opaque block host boxes. Explicit inline/inline-block/inline-flex/
inline-grid declarations select atomic inline participation. This preserves native
control internals; it does not claim the still-pending splittable Run/text inline model.

## Verification

### Generated CSS C#

The CSS build compiler reuses the runtime parser and emits factories for parsed
syntax graphs. It does not precompute all property values or eliminate runtime
condition/value processing; see [the precompilation boundary](css-styling.md#build-time-css-to-c).

The Windows generation validation passed 1196 CSS tests (including 18 code-generation
cases) with the `FullyQualifiedName~Jalium.UI.Tests.Css` filter. The generated-code
tests compile and execute emitted C#, checking syntax graph round trips, shared
nesting/scope nodes, namespace attributes, registrations, dynamic state, native local
values, anonymous-layer identity, imports, resolver overrides, Razor markup and
dynamic-text fallback. Evidence is in
`artifacts/css-codegen-validation/test-results/css-regressions-final.trx`.

`Jalium.UI.Css.CodeGenSmoke` passed as a managed application and as a published
`win-x64` NativeAOT executable, printing `CSS generated C# smoke passed`. The AOT
publish log is `artifacts/css-codegen-aot/publish-final.log`, and the executable's
output is in `artifacts/css-codegen-aot/run-final.log`. Eight build integration
checks also passed: initial generation, unchanged output/timestamp, CSS edits,
additions, removal, removal of all inputs, linked resource metadata and the opt-out.
Their results are recorded in
`artifacts/css-codegen-validation/incremental-fixture-final/verification.json`.
Existing framework
trimming/AOT warnings remain; this smoke does not establish warning-free publication,
other-platform code-generation qualification or native-rendering coverage.

### Existing platform verification

Use a dedicated build root; do not replace existing build outputs or unrelated working changes:

```powershell
dotnet test tests/Jalium.UI.Tests/Jalium.UI.Tests.csproj `
  -p:JaliumBuildRoot=<workspace>/artifacts/css-validation `
  --filter 'FullyQualifiedName~Jalium.UI.Tests.Css|FullyQualifiedName~Jalium.UI.Tests.FlexPanel'
```

| Platform | Evidence | Pending |
| --- | --- | --- |
| Windows managed | 1506 CSS/relative-unit/math/namespace/import/condition/nesting/scope/registered-property/container/float/flow/Grid/subgrid/Flex and native XAML/panel/layout/binding/template/precedence/animation/Storyboard/text/hit-test regressions passed (`css-relative-unit-wpf-regression.trx`), including twenty-nine WPT ports | Full conformance corpus and remaining functionality |
| Linux source build | Isolated SDK 10.0.300; 1296 CSS/relative-unit/math/namespace/import/condition/nesting/scope/registered-property/container/float/flow/Grid/subgrid/Flex and native animation/Storyboard cases passed from Linux sources, including all twenty-nine WPT ports (`css-relative-unit-linux.trx`) | Full Vulkan/software platform visual corpus |
| Windows effects | 24 existing software/Vulkan effect parsing and render-target cases passed | CSS-specific visual corpus and D3D12 coverage |
| Gallery | 21 CSS page tests passed, including ch/lh sizing, independently updated viewport variants, mathematical width/opacity updates driven by a native FontSize binding, generated qualified element names and Tag bindings, embedded conditional imports, scoped nesting, typed variables, containers, floats/flow/Grid/subgrid and existing converters (`css-relative-unit-gallery.trx`) | Examples for remaining functionality |
| Native font interface | Windows D3D12/Vulkan/software and Linux core/text/software/Vulkan libraries built in isolated directories; Windows and Linux `jalium.native.font-units` passed, covering the C ABI, legacy-interface fallback, native face/glyph rulers, zero-glyph layout agreement and line-spacing independence | Broader typeface/fallback/variation and GPU/device corpus |
| Android | Shared managed code compiled with 0 errors (existing FocusVisualAdorner and CollectionView warnings). Core/text/software/Vulkan arm64 libraries compiled with NDK 27.2.12479018 for API 24; existing Vulkan switch warning remains | Emulator/device execution and Vulkan/software visual validation |
| NativeAOT | win-x64 publish succeeded; the executable loaded the newly built core/software libraries and printed `CSS NativeAOT smoke passed`, including real native glyph rulers reaching CSS layout and explicit viewport variants. Earlier math, native-local takeover, namespace, import, scope, registration, container and layout cases remain covered | Broader AOT app coverage and warning audit |
| Metal | Existing TextFormat vtable preserved; non-Apple Metal implementation compiled as an isolated C++ object target after removing an existing duplicate CacheIdentity declaration | Apple SDK/device validation and native Metal font-unit implementation; no new Apple host |

Retain original WPT identifiers and specification references when adding equivalent XAML cases. Record unavailable platform checks as unverified; do not count them as passes or hide missing functionality behind permissive pixel tolerances.

The font-unit native checks use `artifacts/css-native/windows` and
`artifacts/css-native/linux`, with binaries under `artifacts/css-native/bin/<rid>/Release`.
`ctest -R jalium.native.font-units --output-on-failure` runs the C ABI and native
metric checks. The win-x64 AOT validation deploys this build's core/software backend
libraries into its isolated publish directory before running the executable; an older
payload is not evidence for the new native interface. Android arm64 was cross-compiled
with the installed NDK for API 24. The D3D12/Vulkan shared/static project-item validator
also passed. Device execution, full visual comparisons and release-package verification
remain required for the overall completion gate.

The first subgrid WPT box-layout/paint ports are listed in [css-wpt-cases.md](css-wpt-cases.md).
Their Canvas references are independent of the CSS formatter. Their decorative glyphs
are replaced by fixed line boxes, so they do not claim text-raster conformance.

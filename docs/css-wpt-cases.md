# CSS tests adapted to native XAML

The implementation baseline is CSS Snapshot 2026 and the linked CSS Grid 2
[2025-03-26 edition](https://www.w3.org/TR/2025/CRD-css-grid-2-20250326/).
The following source/reference pairs were inspected on 2026-09-11. Their original
WPT paths are retained as test traits in `CssSubgridWptTests.cs`; the expected
rectangles are fixed in the local tests, rather than fetched at test execution.

| WPT identifier | Source and reference | Local verification |
| --- | --- | --- |
| `css/css-grid/subgrid/grid-gap-normal-001.html` | [Source](https://github.com/web-platform-tests/wpt/blob/master/css/css-grid/subgrid/grid-gap-normal-001.html), [reference](https://github.com/web-platform-tests/wpt/blob/master/css/css-grid/subgrid/grid-gap-normal-001-ref.html) | 530 × 320 layout and exact managed-software box pixels; shared gaps and 20px padding |
| `css/css-grid/subgrid/grid-gap-smaller-001.html` | [Source](https://github.com/web-platform-tests/wpt/blob/master/css/css-grid/subgrid/grid-gap-smaller-001.html), [reference](https://github.com/web-platform-tests/wpt/blob/master/css/css-grid/subgrid/grid-gap-smaller-001-ref.html) | 560 × 228 layout and exact managed-software box pixels; zero subgrid gaps versus 20px parent gaps and intrinsic row contributions |

Both upstream cases are by Mats Palmgren and carry a public-domain dedication.
These ports cover their layout and colored-box painting assertions. The original
decorative single-letter labels use a 16px line-height; transparent 16px line boxes
preserve their block-size contributions without introducing platform font raster
differences. The fixed column sizes are unaffected by this substitution. Glyph
painting is not claimed as covered by these cases. The reference scenes use native
Canvas rectangles, independently of the CSS Grid/subgrid formatter.

Broader WPT conversion, unified-font text comparisons, and native D3D12/Vulkan/
Android device pixel comparisons remain pending. Passing these ports is not a
claim of complete Grid or CSS conformance.

## Ordinary flow

`CssFlowWptTests.cs` adds these original identifiers (sources inspected 2026-09-11):

| WPT identifier | Source | Local verification |
| --- | --- | --- |
| `css/CSS2/margin-padding-clear/margin-collapse-002.xht` | [Microsoft WPT case](https://github.com/web-platform-tests/wpt/blob/master/css/CSS2/margin-padding-clear/margin-collapse-002.xht) | 20px-high green boxes at y=0 and y=60; preserves the original em lengths and 20px font-size, replaces the diagnostic background image with an independent Canvas reference |
| `css/CSS2/margin-padding-clear/margin-collapse-003.xht` | [Microsoft WPT case](https://github.com/web-platform-tests/wpt/blob/master/css/CSS2/margin-padding-clear/margin-collapse-003.xht) | The positive/negative 2in margins cancel, leaving adjacent 50 × 20 blue/orange boxes |
| `css/CSS2/margin-padding-clear/margin-auto-on-block-box.html` | [Oriol Brufau WPT case](https://github.com/web-platform-tests/wpt/blob/master/css/CSS2/margin-padding-clear/margin-auto-on-block-box.html) | All 46 original auto-margin/over-constraint combinations, compared to independent Canvas rectangles |

These ports exclude the surrounding instruction text and compare the subject box
pixels at 96 DPI. They do not require an HTML runtime or font glyph substitution.

## Floats

`CssFloatWptTests.cs` retains these identifiers, inspected 2026-09-11:

| WPT identifier | Source | Local verification |
| --- | --- | --- |
| `css/CSS2/floats/floats-placement-001.html` | [Source](https://github.com/web-platform-tests/wpt/blob/master/css/CSS2/floats/floats-placement-001.html) | Zero-height float, right clearance, atomic inline and absolute-child placement combine into the 100px green reference square |
| `css/CSS2/floats/floats-placement-002.html` | [Source](https://github.com/web-platform-tests/wpt/blob/master/css/CSS2/floats/floats-placement-002.html) | The same reference square with an intervening normal block and later floats |
| `css/CSS2/floats/floats-placement-005.html` | [Source](https://github.com/web-platform-tests/wpt/blob/master/css/CSS2/floats/floats-placement-005.html), [reference](https://github.com/web-platform-tests/wpt/blob/master/css/CSS2/floats/floats-placement-005-ref.html) | 150 × 190 float/source-order layout and exact green/cyan box pixels against independent Canvas rectangles |

Instruction text is excluded. The zero-offset positioned root in the first two
tests is represented by the native panel's existing absolute-child containing
block; these ports do not claim relative-position offsets. No glyph substitution
is needed. Logical float/clear direction follows CSS Logical Properties and Values
Level 1, [2025-12-04 edition](https://www.w3.org/TR/2025/WD-css-logical-1-20251204/#float-clear).

## Container queries

`CssContainerWptTests.cs` adds these identifiers, inspected 2026-09-11, using
[CSS Conditional Rules 5, 2025-10-30](https://www.w3.org/TR/2025/WD-css-conditional-5-20251030/):

| WPT identifier | Source | Local verification |
| --- | --- | --- |
| `css/css-conditional/container-queries/container-units-small-viewport-fallback.html` | [Source](https://github.com/web-platform-tests/wpt/blob/master/css/css-conditional/container-queries/container-units-small-viewport-fallback.html) | Original six unit results before/after a 200 × 40 to 400 × 80 viewport resize, with a 70 × 30 inline-size container; exact software bar pixels against Canvas references |
| `css/css-conditional/container-queries/query-container-name-dynamic.html` | [Source](https://github.com/web-platform-tests/wpt/blob/master/css/css-conditional/container-queries/query-container-name-dynamic.html) | All three name-only query states and original custom-property expectations across class changes; size containment remains disabled |

The unit case transfers the original computed offset/margin values to six bar
widths for pixel comparison. It tests the shared length resolver and container
selection, rather than asserting additional offset/margin layout behavior. The
native root represents the upstream iframe viewport. Neither case executes
JavaScript or introduces a browser tree. Full style-query, writing-mode, scroll
state and GPU/device coverage remains pending.

## Registered custom properties

`CssRegisteredPropertyWptTests.cs` retains these identifiers, inspected 2026-09-11,
with [Properties and Values API 1, 2024-03-26](https://www.w3.org/TR/2024/WD-css-properties-values-api-1-20240326/) as the pinned module:

| WPT identifier | Source | Local verification |
| --- | --- | --- |
| `css/css-properties-values-api/registered-property-change-style-001.html` | [Source](https://github.com/web-platform-tests/wpt/blob/master/css/css-properties-values-api/registered-property-change-style-001.html) | Both original color-update sequences through three native nodes, including a registration added after ordinary styles |
| `css/css-properties-values-api/registered-property-change-style-002.html` | [Source](https://github.com/web-platform-tests/wpt/blob/master/css/css-properties-values-api/registered-property-change-style-002.html), [reference](https://github.com/web-platform-tests/wpt/blob/master/css/css-properties-values-api/registered-property-change-style-002-ref.html) | Initial registration values change visibility/display; verifies native state, hit testing and exact software pixels against an empty Canvas |
| `css/css-properties-values-api/registered-property-computation.html` | [Source](https://github.com/web-platform-tests/wpt/blob/master/css/css-properties-values-api/registered-property-computation.html) | Thirteen supported numeric, length/list, color and transform computations at the original 10px font size and blue foreground; fixed 400 × 300 viewport |

Declarative `@property` activation replaces upstream JavaScript registration. The
first case supplies the browser's initial black foreground as an explicit native
root value. The visibility case replaces diagnostic glyphs with pink rectangles,
retaining their disappearance requirement. Modern color functions, additional
font units and other subcases of the computation source are explicitly excluded
from this port and remain pending; no full-file conformance claim is made.

## Nesting and scope

`CssNestingScopeWptTests.cs` adds these identifiers, inspected 2026-09-11, using
[Nesting 1, 2026-01-22](https://www.w3.org/TR/2026/WD-css-nesting-1-20260122/) and
[Cascade 6, 2024-09-06](https://www.w3.org/TR/2024/WD-css-cascade-6-20240906/), including
the newer nesting rules for scoped declarations and zero-specificity `&` in scope:

| WPT identifier | Source | Local verification |
| --- | --- | --- |
| `css/css-nesting/nesting-basic.html` | [Source](https://github.com/web-platform-tests/wpt/blob/master/css/css-nesting/nesting-basic.html), [reference](https://github.com/web-platform-tests/wpt/blob/master/css/css-nesting/nesting-basic-ref.html) | Twelve supported 30px green squares, compared exactly to independent Canvas pixels; includes parent lists, repeated/functional nesting, reversed relationships, declaration order, importance and type-plus-nesting selectors |
| `css/css-cascade/scope-proximity.html` | [Source](https://github.com/web-platform-tests/wpt/blob/master/css/css-cascade/scope-proximity.html) | All five original proximity/specificity/order comparison cases and their border-color results |
| `css/css-cascade/scope-declarations.html` | [Source](https://github.com/web-platform-tests/wpt/blob/master/css/css-cascade/scope-declarations.html) | Direct declarations, explicit/implicit roots and zero-specificity order; native ZIndex assertions |

The nesting port excludes the pending `::before` case and instruction text.
HTML divs become StackPanels; section/span/b identities use distinct CSS classes
on native Borders, preserving the tested relationships. Native spacing gives
the reference squares 8px gaps. Scope tests use native controls and Name values;
the browser's default `z-index:auto` corresponds to the unchanged native default.
CSSOM object-count/mutation APIs are outside these XAML ports. No browser runtime
or generated user-tree nodes are introduced.

## Conditional imports and layer registration

`CssImportWptTests.cs` adds these identifiers, inspected 2026-09-11, with
[Cascade 5, 2022-01-13](https://www.w3.org/TR/2022/CR-css-cascade-5-20220113/) as the import/layer baseline:

| WPT identifier | Source | Local verification |
| --- | --- | --- |
| `css/css-cascade/layer-import.html` | [Source](https://github.com/web-platform-tests/wpt/blob/master/css/css-cascade/layer-import.html) | All 24 original green winners, including anonymous/named/nested imports, statement ordering and failed loads; exact software pixels against a native green Border |
| `css/css-cascade/import-conditions.html` | [Source](https://github.com/web-platform-tests/wpt/blob/master/css/css-cascade/import-conditions.html) | 25 supported cases, including boolean declarations, !important, media conditions, selector/at-rule probes, invalid conditions and no-fetch assertions for false support conditions |
| `css/css-cascade/layer-property-override.html` | [Source](https://github.com/web-platform-tests/wpt/blob/master/css/css-cascade/layer-property-override.html) | All four original registration winners, including sheets appended after initial computation |

The upstream in-memory/data-URL resources are supplied through an isolated
ICssResourceResolver. The `target` type selector becomes a `.target` class uniformly;
its foreground is projected through currentColor onto a 40px native Border for the
pixel comparison. Font-format and font-tech cases that require pending native font
loading capabilities are excluded from the condition port. Browser CSSOM and fetch
policy APIs are not claimed by these XAML equivalents.

## Namespaces

`CssNamespaceWptTests.cs` retains these identifiers, inspected 2026-09-11. The
namespace module is pinned to [Namespaces 3, 2014-03-20](https://www.w3.org/TR/2014/REC-css-namespaces-3-20140320/).

| WPT identifier | Source | Local verification |
| --- | --- | --- |
| `css/css-namespaces/prefix-001.xml` | [Source](https://github.com/web-platform-tests/wpt/blob/master/css/css-namespaces/prefix-001.xml) | Case-sensitive prefix selection, undeclared prefix recovery, exact lime box pixels |
| `css/css-namespaces/prefix-002.xml` | [Source](https://github.com/web-platform-tests/wpt/blob/master/css/css-namespaces/prefix-002.xml) | Empty namespace prefix and exact lime box pixels |
| `css/css-namespaces/scope-001.xml` | [Source](https://github.com/web-platform-tests/wpt/blob/master/css/css-namespaces/scope-001.xml) | Prefix isolation between two sheets and exact lime box pixels |
| `css/css-conditional/at-supports-namespace-002.html` | [Source](https://github.com/web-platform-tests/wpt/blob/master/css/css-conditional/at-supports-namespace-002.html) | Valid/misplaced namespace declarations, selector support and negation; the original 100px green square |
| `css/selectors/nth-of-type-namespace.html` | [Source](https://github.com/web-platform-tests/wpt/blob/master/css/selectors/nth-of-type-namespace.html) | All three original 100-element namespace groups and their green final elements |

Native Borders carry the original expanded names through the same builder API
used by generated XAML. Diagnostic sentence backgrounds in the XML cases become
40 × 20 boxes, so glyph rasterization is excluded. Pixel references use native
solid-color Borders independent of CSS. Runtime XAML aliases, compiled qualified
Tag bindings, import isolation, metadata refresh and release have separate tests.

## Mathematical functions

`CssMathWptTests.cs` retains two more identifiers, inspected 2026-09-11, using
[Values 4, 2024-03-12](https://www.w3.org/TR/2024/WD-css-values-4-20240312/) as its function baseline:

| WPT identifier | Source | Local verification |
| --- | --- | --- |
| `css/css-values/round-mod-rem-computed.html` | [Source](https://github.com/web-platform-tests/wpt/blob/master/css/css-values/round-mod-rem-computed.html) | Twelve mixed-percentage and zero-percentage cases, with the original 75px basis, layout widths and exact managed software pixels |
| `css/css-values/sin-cos-tan-computed.html` | [Source](https://github.com/web-platform-tests/wpt/blob/master/css/css-values/sin-cos-tan-computed.html) | Thirteen number/angle/constant/nesting cases, projected to widths using `100px + result * 10px`, with exact reference pixels |

References use native Borders with fixed widths and no CSS. These are selected
numeric cases, not whole-file conformance: remaining font units, newer sibling
functions and browser CSSOM serialization are excluded. The independent math suite
also checks inverse trigonometry, powers/logarithms/hypot, signed zero and infinities,
type failures, parser limits, registered values, dynamic Grid fractions and font
contexts, transition-duration ranges, and retained native bindings.

## Font-relative and viewport units

`CssRelativeUnitWptTests.cs` adds three identifiers from the same Values 4 baseline,
inspected 2026-09-11:

| WPT identifier | Source | Local verification |
| --- | --- | --- |
| `css/css-values/lh-unit-001.html` | [Source](https://github.com/web-platform-tests/wpt/blob/master/css/css-values/lh-unit-001.html) | Parent-relative line-height produces the original 100px green square with exact managed software pixels |
| `css/css-values/viewport-units-compute.html` | [Source](https://github.com/web-platform-tests/wpt/blob/master/css/css-values/viewport-units-compute.html) | All 34 original numeric cases with the 200×100 viewport, layout heights and exact native-box reference pixels |
| `css/css-values/viewport-units-invalidation.html` | [Source](https://github.com/web-platform-tests/wpt/blob/master/css/css-values/viewport-units-invalidation.html) | All 24 original unit variants update when the viewport changes from 200×100 to 400×300 |

The single NBSP line in the lh case becomes an atomic native line box. Iframes
become isolated XAML roots with the same viewport allocation; no HTML runtime is
introduced. Separate managed cases cover root metrics, ch/cap/ex/ic, bindings,
font/line-height cycles, explicit small/large/dynamic sizes, host logical axes,
zero-sized viewports and dependency removal. The native C API tests verify real
font rulers on Windows and Linux, but these ports do not claim full font loading,
vertical text or GPU glyph-raster conformance.

# CSS tests adapted to native XAML

## CSS Values 5 first-valid()

`CssFirstValidTests.cs` exercises the [CSS Values 5 first-valid() definition](https://drafts.csswg.org/css-values-5/#funcdef-first-valid)
against native CSS declarations. It checks first supported candidate selection,
whole-value placement, nested and brace-wrapped candidates, shorthand expansion,
custom-property grammar and dependency cycles, substitution only in the selected
candidate, priority and `revert-rule` behavior, and invalid-at-computed-value
fallback. These are spec-based native tests; no browser WPT case is claimed.

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

`CssContainerQueryTests.cs` also ports numeric style-range cases from
[WPT `query-evaluation-style.html`](https://github.com/web-platform-tests/wpt/blob/master/css/css-conditional/container-queries/query-evaluation-style.html),
including custom-property substitution, `var()`, math, unit conversion, type
mismatch, zero-length comparison and chained ranges. The native tests additionally
cover frequency values, escaped custom-property names, live updates and registered
initial values. Numeric `attr()` cases use the selected native container's
attributes, with typed values, unit syntax, raw strings and fallbacks.
Custom-property equality queries also test `attr()` expectations, including
registered typed values and live attribute changes. Registered property
declarations now compute `attr()` and preserve its origin through inheritance,
but the matching `query-evaluation-style.html` registered-declaration case remains
unported. The two `env(safe-area-inset-*)` numeric style-query cases are covered
with the native window's safe-area values.
Separate native tests cover ordinary `style()` features for 25 directly stored
longhands, three shorthands, CSS initial and inherited values, native local
property changes, aliases, and unsupported-feature handling. These cases follow
the computed-value and shorthand rules in CSS Conditional Rules 5; they are not
claimed as browser-runtime WPT ports.
Additional native cases cover twelve preferred, minimum and maximum size longhands,
absolute-unit conversion, retained percentages, linear `calc()` equivalence,
CSS initial values, `auto` versus `0`, explicit inheritance, and live CSS/native size changes.
These are spec-based tests rather than WPT ports.
Native layout-style cases additionally cover `box-sizing` keywords and
`aspect-ratio` number pairs, combined `auto` syntax, degenerate ratios,
CSS-wide values, inheritance and live state-rule changes.
Separate native `position` style-query cases cover all five computed keywords,
including distinct `fixed` and `sticky` values while their layout falls back
to static, and changes from inheritance or state rules.
Separate native `display` style-query cases cover initial/boolean and CSS-wide
values, one- and two-keyword equivalence, root/float/position/flex/grid item
blockification, inheritance, native visibility independence and live parent or
state-rule changes.
Separate native `visibility` style-query cases cover inherited visible/hidden/
collapse values, CSS-wide and boolean comparisons, native local and ancestor
precedence, independent `display:none`, and live state-rule changes.
Separate native `color` and `background-color` style-query cases cover initial
and boolean values, equivalent absolute colors, symbolic `currentColor` and
dependent mixes, explicit and implicit inheritance, native brush precedence,
mutable brush changes, and live state-rule changes.
Separate native `font-weight` style-query cases cover initial and boolean
values, equivalent numeric/keyword weights, parent-relative `bolder`/`lighter`,
explicit and implicit inheritance, and native weight precedence on parent and
container.
Separate native `font-size` style-query cases cover the 14px initial size,
equivalent keywords and absolute units, parent-relative `em`/percentages and
`larger`/`smaller`, root-relative `rem`, CSS-wide values, and native size
precedence on parent and container.
Separate native `font-stretch`/`font-width` style-query cases cover the normal
initial value, equivalent keywords and percentages, percentage math, retained
continuous values through implicit and explicit inheritance, alias cascade order,
shorthand keyword and CSS-wide reset/inheritance, state changes, and native
precedence even when a native width class equals the CSS rendering projection.
Separate native `font-style` style-query cases cover initial and boolean values,
bare versus explicit oblique angles, equivalent angle units, computed math bounds,
implicit and explicit inheritance, shorthand reset, state changes, and native
precedence even when the native oblique class equals the CSS projection.
Separate native `font-family` style-query cases cover the initial generic,
quoted versus generic names, ordered fallback lists, syntax rejection, explicit
and implicit inheritance, shorthand reset, state changes, and native precedence
even when a CSS generic maps to the same native family name.
`CssFontFaceTests` also loads a quoted family containing a comma and verifies its
private native font and `ch` metric resolve from the same ordered CSS list.
Separate native inset style-query cases cover eight physical and logical
longhands and three shorthands, initial and CSS-wide values, percentages on
static containers, linear `calc()` comparison, right-to-left mapping, native
Canvas precedence, explicit inheritance and live state-rule changes.
Separate native margin/padding style-query cases cover sixteen physical and
logical longhands and six shorthands, `auto`, retained percentages and math,
zero-percent versus zero-pixel comparison, native `Thickness` precedence,
explicit inheritance through a Panel without a Padding property, and dynamic
state changes. Negative literal padding is rejected and negative numeric
functions are clamped when computable before layout.
Separate native overflow style-query cases cover four physical/logical axes and
the shorthand, cross-axis `visible`/`clip` promotion, the `overlay` alias,
boolean and CSS-wide values, inheritance, native clipping independence and
dynamic state changes.

The unit case transfers the original computed offset/margin values to six bar
widths for pixel comparison. It tests the shared length resolver and container
selection, rather than asserting additional offset/margin layout behavior. The
native root represents the upstream iframe viewport. Neither case executes
JavaScript or introduces a browser tree. Full style-query, writing-mode, scroll
state and GPU/device coverage remains pending.

## Variable fallback keywords

`CssCustomPropertySyntaxTests.cs` adapts the custom-property inheritance and
initial-value cases from
[WPT `wide-keyword-fallback-002.html`](https://github.com/web-platform-tests/wpt/blob/master/css/css-variables/wide-keyword-fallback-002.html),
inspected 2026-09-27. Native dimensions replace browser computed-style reads.
The local cases also change the inherited value after the first evaluation and
check `@property` declarations whose `inherits` descriptor is false. They cover
`var()` producing `initial`, `inherit` or `unset`; they do not port the full
browser-runtime test file.
Additional cases from
[WPT `variable-css-wide-keywords-after-substitution.html`](https://github.com/web-platform-tests/wpt/blob/master/css/css-variables/variable-css-wide-keywords-after-substitution.html)
check an empty custom property before `initial`, `inherit`, `unset`, `revert`
and layered `revert-layer`. The sibling-background subcase now checks that an
empty value restored by `revert-layer` leaves the following `green` token usable.

`CssVariableNameSubstitutionTests.cs` adapts cases from
[WPT `variable-reference-name-substitution.html`](https://github.com/web-platform-tests/wpt/blob/master/css/css-variables/variable-reference-name-substitution.html),
inspected 2026-09-27. Nested variable names, braced names, missing and invalid
names, registered and universal properties, and self and indirect cycles are
checked against native widths; local cases also cover `attr()` provided names
and `Css.Supports`. The `if()` and `random-item()` name cases are covered by
`CssIfSubstitutionTests.cs` and `CssRandomItemTests.cs`.

## Conditional values

`CssIfSubstitutionTests.cs` adapts selected cases from
[WPT `if-conditionals.html`](https://github.com/web-platform-tests/wpt/blob/master/css/css-values/if-conditionals.html),
[WPT `if-cycle.html`](https://github.com/web-platform-tests/wpt/blob/master/css/css-values/if-cycle.html),
[WPT `if-function-revert-rule.html`](https://github.com/web-platform-tests/wpt/blob/master/css/css-values/if-function-revert-rule.html),
[WPT `attr-security-if.html`](https://github.com/web-platform-tests/wpt/blob/master/css/css-values/attr-security-if.html),
and the remaining `if()` name argument in
[WPT `variable-reference-name-substitution.html`](https://github.com/web-platform-tests/wpt/blob/master/css/css-variables/variable-reference-name-substitution.html),
inspected 2026-09-27. Native widths and backgrounds replace browser computed
style reads. The 42 local cases check ordered and nested branches, boolean
conditions, media/supports probes, registered custom-property comparisons,
live style changes, malformed branch parsing, cycles in selected versus unused
values, `revert-rule`, and attribute taint in URL content. The local self-query
expectation follows the current CSS Values 5 editor's draft, which says a
`style()` query of the active custom property is false; some `if-cycle.html`
assertions still expect the older invalid result. `CssSpreadSubstitutionTests.cs`
adds 14 draft-based cases for early `var()`, `attr()`, `env()` and `if()` substitution,
argument delimiters, literal dots, invalid replacement, and lazy nested
conditions. These are local spec cases rather than WPT ports. The full upstream
files and browser serialization checks remain unported.

## Random item selection

`CssRandomItemTests.cs` adapts selected parse-time and computed-value cases
from [WPT `random-item-valid.html`](https://github.com/web-platform-tests/wpt/blob/master/css/css-values/random-item-valid.html),
[WPT `random-item-invalid.html`](https://github.com/web-platform-tests/wpt/blob/master/css/css-values/random-item-invalid.html),
[WPT `random-item-computed.html`](https://github.com/web-platform-tests/wpt/blob/master/css/css-values/random-item-computed.html)
and [WPT `random-item-nested.html`](https://github.com/web-platform-tests/wpt/blob/master/css/css-values/random-item-nested.html),
inspected 2026-09-27. The local cases cover fixed-index selection, a
shared named key, brace-wrapped shorthand values, lazy and nested substitution,
variable names and cycles, spread syntax, registered-property inheritance,
keyframe values/timing functions, and
parse-time argument checks. Additional local cases cover numeric `random()`
ranges, steps, units, shared named keys and custom-property timing. The full
random cache and computed serialization corpus remains unported. Local cases
also check that `if()` condition random values use separate `ua-early-*` keys
and indexes while selected branches use normal keys, including within an
unregistered custom property. Additional local cases cover random values in
early spread arguments, selected versus unselected options, and index
isolation for unused registered `var()` fallbacks.

## Tree counting functions

`CssTreeCountingTests.cs` checks `sibling-index()` and `sibling-count()` against
[CSS Values 5 tree counting](https://drafts.csswg.org/css-values-5/#tree-counting)
and selected cases from [WPT parsing](https://github.com/web-platform-tests/wpt/blob/master/css/css-values/tree-counting/calc-sibling-function-parsing.html)
and [WPT computed values](https://github.com/web-platform-tests/wpt/blob/master/css/css-values/tree-counting/calc-sibling-function.html),
with the [WPT container-query invalidation case](https://github.com/web-platform-tests/wpt/blob/master/css/css-values/tree-counting/sibling-function-container-query-invalidation.html)
and [WPT gradient color channel case](https://github.com/web-platform-tests/wpt/blob/master/css/css-values/tree-counting/sibling-index-linear-gradient-gcs.html),
inspected 2026-09-27. The eight local cases cover numeric type and empty-argument
syntax, the root as its only sibling, live sibling addition/removal, a
registered `<number>` custom property, size-container query invalidation, and
gradient color channels and stop positions with live sibling changes, plus a
dynamic keyframe length that retains its animation runner after tree changes.
Browser pseudo-elements, shadow-tree
scoping, computed serialization, other image functions, and the remaining
dynamic WPT cases are not covered.

## Interpolation progress math

`CssProgressFunctionTests.cs` adapts selected cases from
[WPT computed values](https://github.com/web-platform-tests/wpt/blob/master/css/css-values/progress-computed.html)
and [WPT invalid syntax](https://github.com/web-platform-tests/wpt/blob/master/css/css-values/progress-invalid.html),
inspected 2026-09-27. Sixteen local cases cover compatible numeric dimensions,
invalid argument counts and compound types, default clamping, `no-clamp`, equal
endpoints, nested math, percentages against a length basis, and font-relative
recalculation. Browser computed serialization and the full WPT corpus remain
unported.

## Weighted numeric mixing

`CssCalcMixTests.cs` adapts selected cases from
[WPT computed values](https://github.com/web-platform-tests/wpt/blob/master/css/css-values/calc-mix-computed.tentative.html)
and [WPT invalid values](https://github.com/web-platform-tests/wpt/blob/master/css/css-values/calc-mix-invalid.tentative.html),
inspected 2026-09-27. Local cases cover explicit and omitted weights, sums above
and below 100%, typed zero, computed clamping of `calc()` weights, direct
out-of-range rejection, dynamic tree-counting weights and zero-weight values,
nested math, percentage-basis isolation, font-relative lengths and layout
percentages. [WPT serialization](https://github.com/web-platform-tests/wpt/blob/master/css/css-values/calc-mix-serialize.tentative.html)
and the remaining browser-specific computed-value cases remain unported.

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
pixel comparison. This earlier import-condition port excludes font queries;
the separate font-support port below covers those query forms against the
native font capabilities. Browser fetch-policy APIs are not claimed by these
XAML equivalents.

## Environment variables

`CssEnvironmentVariableTests.cs` adapts the eight valid/invalid `width` probes
from [WPT `env-parsing.html`](https://github.com/web-platform-tests/wpt/blob/master/css/css-env/env-parsing.html),
the index grammar from [WPT `indexed-env.tentative.html`](https://github.com/web-platform-tests/wpt/blob/master/css/css-env/indexed-env.tentative.html),
the fallback token checks from [WPT `env-fallback-substitution.html`](https://github.com/web-platform-tests/wpt/blob/master/css/css-env/env-fallback-substitution.html),
and invalid custom-property inheritance from [WPT `env-in-custom-properties.tentative.html`](https://github.com/web-platform-tests/wpt/blob/master/css/css-env/env-in-custom-properties.tentative.html),
inspected 2026-09-27. Native tests also cover nested and comma-containing
fallbacks, scalar index mismatch, invalid-at-computed-value behavior, custom
property cycles, token boundaries and live host-window safe-area updates.
CSS Conditional 5 `env(<ident>)` support queries are checked through the native
support API, conditional rules and conditional imports. Container style queries
cover the two safe-area comparisons from
[WPT `query-evaluation-style.html`](https://github.com/web-platform-tests/wpt/blob/master/css/css-conditional/container-queries/query-evaluation-style.html).
These cases verify native computed values and support probes, not browser DOM
serialization or device-specific cutout pixels.

## Rule rollback

`CssRevertRuleTests.cs` adapts the basic, important, layer,
revert-layer interaction and custom-property cases from
[WPT CSS Cascade](https://github.com/web-platform-tests/wpt/tree/master/css/css-cascade),
plus the [nested-rule](https://github.com/web-platform-tests/wpt/blob/master/css/css-nesting/nesting-revert-rule.html),
[environment fallback](https://github.com/web-platform-tests/wpt/blob/master/css/css-env/env-revert-rule.html)
and [attribute fallback](https://github.com/web-platform-tests/wpt/blob/master/css/css-values/attr-revert-rule.html)
cases, inspected 2026-09-27. Native checks use dimensions and custom-property
values in place of browser CSSOM serialization, and also exercise logical
aliases, the `all` shorthand and live attribute changes. Additional native
cases exercise ordinary-property `var()` fallbacks yielding `revert`,
`revert-layer` and `revert-rule`, live custom-property changes and recomputation
of registered font-relative values after rule rollback. Native cases also
exercise substituted rollback in registered and unregistered custom properties,
including `var()`, `env()` and `attr()` fallbacks, candidate dependencies,
live changes and direct/indirect variable cycles. The four custom-property,
shorthand and longhand assertions from
[WPT `revert-rule-in-fallback.html`](https://github.com/web-platform-tests/wpt/blob/master/css/css-variables/revert-rule-in-fallback.html)
and [WPT `revert-layer-in-fallback.html`](https://github.com/web-platform-tests/wpt/blob/master/css/css-variables/revert-layer-in-fallback.html),
plus [WPT `revert-rule-to-var.html`](https://github.com/web-platform-tests/wpt/blob/master/css/css-variables/revert-rule-to-var.html),
are adapted to native values. The two cases in tentative
[WPT `revert-rule-cycle.tentative.html`](https://github.com/web-platform-tests/wpt/blob/master/css/css-cascade/revert-rule-cycle.tentative.html)
check unset behavior through inherited color/custom properties and non-inherited
width. The linked CSSWG issue is still open; no broader cycle claim is made.

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

`CssAttributeDeclarationTests.cs` adapts attribute-name expectations from
[CSS Values 5 `attr()`](https://drafts.csswg.org/css-values-5/#attr-notation),
inspected 2026-09-27:

| WPT identifier | Source | Local verification |
| --- | --- | --- |
| `css/css-values/attr-namespace-valid.xhtml` | [Source](https://github.com/web-platform-tests/wpt/blob/master/css/css-values/attr-namespace-valid.xhtml) | Qualified attribute supplies a computed background color and updates after a live attribute change |
| `css/css-values/attr-null-namespace.xhtml` | [Source](https://github.com/web-platform-tests/wpt/blob/master/css/css-values/attr-null-namespace.xhtml) | Unqualified and explicit null names ignore the stylesheet's default namespace; a declared prefix reads the qualified attribute |
| `css/css-values/attr-all-types.html` | [Source](https://github.com/web-platform-tests/wpt/blob/master/css/css-values/attr-all-types.html) | `type(<string>)` and `type(<string>+)` compute registered string properties; malformed values fall back to their registered initial values |

Additional native cases check unknown, case-mismatched and wildcard prefixes,
namespace separator whitespace, import and generated-sheet isolation, XAML XML
metadata, custom-property inheritance, keyframes and container style queries.
These cases assert native property values; they do not claim the original XHTML
rendering or pixel references.

## CSS Color 4 absolute colors

`CssColor4Tests.cs` retains these WPT values and reference colors, inspected
2026-09-23. The XAML target is a native `Border`; original instruction text and
12em HTML box dimensions are omitted. The parser test checks 8-bit channels
within one code value where the upstream decimal input was rounded.

| WPT identifier | Source | Preserved color/reference |
| --- | --- | --- |
| `css/css-color/hwb-001.html` | [Source](https://github.com/web-platform-tests/wpt/blob/master/css/css-color/hwb-001.html) | `hwb(120 0% 49.8039%)` → `#008000` |
| `css/css-color/lab-001.html` | [Source](https://github.com/web-platform-tests/wpt/blob/master/css/css-color/lab-001.html) | `lab(46.2775% -47.5621 48.5837)` → `#008000` |
| `css/css-color/lch-001.html` | [Source](https://github.com/web-platform-tests/wpt/blob/master/css/css-color/lch-001.html) | `lch(46.2775% 67.9892 134.3912)` → `#008000` |
| `css/css-color/oklab-001.html` | [Source](https://github.com/web-platform-tests/wpt/blob/master/css/css-color/oklab-001.html) | `oklab(51.975% -0.1403 0.10768)` → `#008000` |
| `css/css-color/oklch-001.html` | [Source](https://github.com/web-platform-tests/wpt/blob/master/css/css-color/oklch-001.html) | `oklch(51.975% 0.17686 142.495)` → `#008000` |
| `css/css-color/predefined-001.html` | [Source](https://github.com/web-platform-tests/wpt/blob/master/css/css-color/predefined-001.html) | `color(srgb 0 0.6 0)` → `#009900` |

The first five also check center pixels on Windows software, D3D12 and Vulkan.
These ports do not claim HTML layout, wide-gamut output or whole-frame pixel parity.

## CSS Color 5 color mixing

`CssColorMixTests.cs` retains selected grammar assertions from WPT
[`color-valid-color-mix-function.html`](https://github.com/web-platform-tests/wpt/blob/master/css/css-color/parsing/color-valid-color-mix-function.html)
and [`color-invalid-color-mix-function.html`](https://github.com/web-platform-tests/wpt/blob/master/css/css-color/parsing/color-invalid-color-mix-function.html):
percentages before/after colors, `calc()` percentages, hue-method syntax,
negative/over-100% rejection and missing comma rejection. Native XAML tests
also check sRGB/Oklab reference channels, multi-color order, percentage alpha,
premultiplication, nested wide-gamut and missing-channel carry-forward,
variables, brushes and gradient stops. They evaluate computed native colors;
browser CSSOM serialization and full WPT page rendering remain unverified.

`CssRelativeColorTests.cs` adapts computed-channel examples from
[CSS Color 5 relative colors](https://www.w3.org/TR/css-color-5/#relative-colors)
and selected valid/invalid forms from WPT
[`color-valid-relative-color.html`](https://github.com/web-platform-tests/wpt/blob/master/css/css-color/parsing/color-valid-relative-color.html).
It checks `from` origins, named channels, `calc()`, omitted alpha, all ten
predefined `color()` spaces, missing values, nested colors and invalid legacy
commas. `CssRelativeColorNativeTests.cs` checks used colors and gradient stops
on software, D3D12 and Vulkan. CSSOM serialization and full WPT page images
are not claimed.

`CssBackgroundLayerTests.cs` and `CssBackgroundLayerNativeTests.cs` check the
[CSS Backgrounds 3 painting order](https://www.w3.org/TR/css-backgrounds-3/#layering):
the first translucent image paints above later images and `background-color`,
and transparent image pixels reveal the layers below. Tests also cover `none`
entries, size/repeat-list cycling, shorthand reset and invalid-list recovery.
Managed pixels check a CSS `repeat-x` bitmap over a background color. Native
pixel checks cover gradient image lists, intrinsic bitmap tiling, `no-repeat`,
per-layer `contain`/`cover` bitmap brushes, elliptical corners, the default Button
template, live color changes and whole-frame recording on Windows software,
D3D12 and Vulkan. These cases use the specification's painting rule; they are
native XAML tests, not WPT page ports.

`CssCurrentColorMixTests.cs` adds two source-backed native XAML adaptations:

| WPT identifier | Source | Preserved assertion |
| --- | --- | --- |
| `css/css-color/color-mix-currentcolor-001.html` | [Source](https://github.com/web-platform-tests/wpt/blob/master/css/css-color/color-mix-currentcolor-001.html) | A child inheriting a mixed background resolves `currentColor` using its own green foreground, then responds to parent background changes |
| `css/css-color/color-mix-currentcolor-nested-for-color-property.html` | [Source](https://github.com/web-platform-tests/wpt/blob/master/css/css-color/color-mix-currentcolor-nested-for-color-property.html) | The nested `color` mix uses the blue inherited foreground before producing the light blue-green result |

Additional cases check native foreground precedence, escaped identifiers,
border/outline inheritance, direct `currentColor`, and linear/radial/conic
gradient stops. Native XAML regressions also change a mutable foreground brush
in place, replace it, and verify that obsolete subscriptions are released.
These ports preserve expressions and selected computed colors,
without claiming HTML layout or browser CSSOM serialization.

`CssShadowColorTests.cs` adds native XAML assertions for the CSS Backgrounds 3
and Filter Effects 1 shadow grammars: omitted and explicit `currentColor`,
mixed shadow alpha, mutable foreground changes, native effect priority,
`blur()` defaults and invalid forms. They are specification-based regressions,
not ports of a particular WPT page.

`CssOuterShadowNativeTests.cs` adds specification-based native pixel checks
for positive and negative outer spread and rounded corners on software,
D3D12 and Vulkan. These are not a port of a specific WPT page.

The same test now verifies that a transparent box does not reveal its own
outer shadow inside the original border contour. Straight edges, elliptical
corners, offsets and a rounded ancestor clip are checked on software, D3D12,
Vulkan and forced Vulkan CPU capture. These are specification assertions, not
whole-image WPT comparisons.

The same native test also compares a 12px `box-shadow` blur radius with a 6px
`filter: drop-shadow()` standard deviation at the outside edge. The underlying
Gaussian relation comes from CSS Backgrounds 3 and Filter Effects 1; this is
still a native specification assertion rather than a WPT page port.

`CssOuterShadowNativeTests.cs` also checks `filter: blur(6px)` on all three
Windows backends against that 12px shadow reference, including pixels beyond
the box and rounded corner and a half-alpha source. The Vulkan CPU-capture
variant is exercised separately. These remain specification-based assertions,
not full WPT bitmap comparisons.

`CssEffectLengthTests.cs` checks specification-based native XAML behavior for
font, viewport, container and math lengths in shadows and filters, plus
unitless-zero, percentage and negative calculated-blur rules. Generated CSS
and an `em`-spread native pixel test cover the same path. These are not ports
of a particular WPT page.

`CssFilterChainNativeTests.cs` checks the Filter Effects 1 function order on
software, D3D12 and Vulkan: reversing brightness and invert changes pixels,
and repeated opacity multiplies rather than overpainting the source. These
tests also distinguish per-stage clipping from one fused matrix when
`brightness(2)` temporarily exceeds the channel range. Generated CSS and
effect cloning retain the stages; 20/64-stage chains and a 65-stage GPU
fallback are checked. These are native specification assertions,
not a WPT page port; mixed blur/shadow chains remain to be compared.

`CssFilterDropShadowNativeTests.cs` adds native specification assertions for
the `drop-shadow()` input-alpha mask, partial source alpha, signed offsets
and Gaussian skirt,
contrasted with the same element's border-box `box-shadow`. Software, D3D12,
Vulkan and an explicit Vulkan CPU capture pass these checks. Generated CSS
retains the alpha-mask semantics. This is not a WPT page port.

`CssShadowFilterCompositionNativeTests.cs` checks the Filter Effects 1 group
input when `box-shadow` and `filter` coexist. Brightness, grayscale, a
two-stage color-matrix chain, blur and a second drop-shadow all consume the
painted box-shadow on software, D3D12, Vulkan and forced Vulkan CPU. The
recorded whole-frame path is checked too. These remain specification-based
native pixel assertions rather than WPT page ports.

`CssFilterOutlineNativeTests.cs` checks that CSS `brightness(0)` transforms an
outline, that `blur(4px)` reaches beyond a rounded outline, and that an outline
and box shadow both enter the same filter. Software, D3D12, Vulkan and forced
Vulkan CPU pass these specification-based native pixels; no WPT page image is
ported.

`CssBoxShadowOrderNativeTests.cs` checks the CSS Backgrounds 3 front-to-back
list order with three completely overlapping outer shadows. Software, D3D12,
Vulkan and an explicit Vulkan CPU run paint the first authored color on top;
the native XAML assertion keeps child order visible separately. It is not a
WPT page port.

`CssMixedShadowNativeTests.cs` checks the separate CSS painting positions of
outer and inset shadows with both authored orders, then checks that two inset
shadows both remain visible and the first authored inset paints on top.
Software, D3D12, Vulkan and a forced Vulkan CPU capture pass these native
pixel assertions. The same cases verify that an opaque child of a `Border`
covers a pure CSS inset shadow, including when the parent background is
transparent or clips its own children, while an uncovered inset and the outer
shadow remain visible.
They are specification-based checks, not WPT page ports.

`CssPanelShadowContentNativeTests.cs` applies the same opaque-child assertion
to seven built-in panels whose `OnRender` only paints the background. It also
checks the uncovered inset and outer shadow on software, D3D12, Vulkan and
forced Vulkan CPU. These are native pixel checks rather than WPT ports.

## CSS gradient color interpolation

`CssGradientInterpolationTests.cs` and `CssGradientInterpolationNativeTests.cs`
adapt two WPT gradient cases inspected 2026-09-23. Their source gradient expressions
and 480 × 50 reference bitmap size are retained on a native `Border`.

| WPT identifier | Source and reference | Interior RGB at x=120, 240, 360 |
| --- | --- | --- |
| `css/css-images/gradient/legacy-color-gradient.html` | [Source](https://github.com/web-platform-tests/wpt/blob/master/css/css-images/gradient/legacy-color-gradient.html), [sRGB PNG](https://github.com/web-platform-tests/wpt/blob/master/css/css-images/gradient/resources/srgb-gradient.png) | `(191,64,0)`, `(127,128,0)`, `(63,192,0)` |
| `css/css-images/gradient/css-color-4-colors-default-to-oklab-gradient.html` | [Source](https://github.com/web-platform-tests/wpt/blob/master/css/css-images/gradient/css-color-4-colors-default-to-oklab-gradient.html), [Oklab PNG](https://github.com/web-platform-tests/wpt/blob/master/css/css-images/gradient/resources/oklab-gradient.png) | `(237,116,0)`, `(208,169,0)`, `(160,214,0)` |

Reference PNG SHA-256 values are respectively
`5b1bd849f53a8684f635627c166a7803507bde52755cd5399b3be8326ad333a4`
and `d1aeabe6f2b49554527825daf57d77c975f09c05edf21313507aac6d0470a86e`.
The three Windows native backends match these six samples within four 8-bit
channel values. These checks preserve the gradient color expressions and sampled
reference colors, without claiming an HTML layout port or whole-image equality.

Two further WPT gradient behaviors are retained in managed tests:

| WPT identifier | Source | Local verification |
| --- | --- | --- |
| `css/css-images/gradient/gradient-hue-direction.html` | [Source](https://github.com/web-platform-tests/wpt/blob/master/css/css-images/gradient/gradient-hue-direction.html) | HSL and LCH `increasing hue` match `shorter hue`; `decreasing hue` match `longer hue` for the source red/orange pair. Three interior points also match on Windows software/D3D12/Vulkan. |
| `css/css-images/gradient/gradient-none-interpolation.html` | [Source](https://github.com/web-platform-tests/wpt/blob/master/css/css-images/gradient/gradient-none-interpolation.html) | The source sRGB middle stop with a missing red channel splits into two effective stops at 50%; all-missing sRGB red remains zero, and an OkLCh missing hue takes each adjacent hue. |

The broader explicit-space matrix covers all fifteen CSS Color 4 interpolation
spaces with same-space midpoints, plus shorter/longer OkLCh arcs. It is a native
XAML behavior test, not a claim that the full HTML WPT rendering page is ported.

`CssGradientInterpolationTests.cs` also retains the percentage-hint grammar
assertions from [WPT `color-stops-parsing.html`](https://github.com/web-platform-tests/wpt/blob/master/css/css-images/gradient/color-stops-parsing.html):
`red, 25%, blue`, `red 0%, 25%, blue 100%` and unitless zero are valid; leading, trailing and
adjacent hints are invalid. The native test uses the
[CSS Images 4 25% hint example](https://www.w3.org/TR/css-images-4/#coloring-gradient-line)
at 480 × 50 and checks red/blue values at x=120, 240 and 360 on all three
Windows backends. `calc(25%)`, boundary hints and ordered-position fixup have
additional native XAML cases.

[WPT `gradient-single-stop-001.html`](https://github.com/web-platform-tests/wpt/blob/master/css/css-images/gradient/gradient-single-stop-001.html)
supplies the `linear-gradient(green)` solid-color assertion. The XAML checks
also exercise a specified position, an explicit OkLCh hue method and a radial
single stop. The native pixel test uses its existing 480 × 50 target rather
than the original 100 × 100 HTML box.

`CssGradientLengthTests.cs` and `CssGradientLengthNativeTests.cs` add two
used-size ports, inspected 2026-09-23:

| WPT identifier | Source | Preserved assertions |
| --- | --- | --- |
| `css/css-images/linear-gradient-calc-em-units.html` | [Source](https://github.com/web-platform-tests/wpt/blob/master/css/css-images/linear-gradient-calc-em-units.html) | Two 100 × 100 backgrounds use `linear-gradient(blue calc(2em), yellow)` with 10px and 30px native font sizes; the blue stop resolves to 20px and 60px. A pixel at y=40 is blended for the first and blue for the second. |
| `css/css-images/linear-gradient-non-square.html` | [Source](https://github.com/web-platform-tests/wpt/blob/master/css/css-images/linear-gradient-non-square.html) | The `to right bottom` black/lightgray 50% hard boundary crosses the two neighboring corners of the original 200 × 100 box; pixels on either side match on Windows software/D3D12/Vulkan. The other three source directions are not counted as ports. |

Additional native XAML checks keep a 100px absolute stop fixed when the same
Border grows from 100px to 200px, resolve `calc(50% + 10px)` against each used
line length, and verify local Background precedence. The native tests use
isolated rebuilt libraries and test only interior colors away from the hard
transition's antialiasing band. Full WPT page layout and whole-image parity
remain unclaimed.

`CssRadialGeometryTests.cs` ports the center/size relationship from
[WPT `radial-gradient-container-relative-units-001.html`](https://github.com/web-platform-tests/wpt/blob/master/css/css-images/radial-gradient-container-relative-units-001.html):
the source `50px 50px ellipse at 50cqw 50px` on a 100 × 100 size container
has a 50px radius and a centered X coordinate; enlarging the container to
200px moves that center to 100px. The native XAML test retains the authored
gradient expression and box dimensions, and checks computed geometry rather
than claiming the entire HTML reference page. Separate Windows backend pixel
checks use the [CSS Images closest-side example](https://www.w3.org/TR/css-images-3/#radial-gradients)
on a 200 × 100 box with a center at 20px,30px, and verify that a non-square
CSS `circle` keeps equal physical radii.

## Font support feature queries

`CssFontSupportQueryTests.cs` ports the nine font-query assertions in
[WPT `CSS-supports-L5.html`](https://github.com/web-platform-tests/wpt/blob/master/css/css-conditional/js/CSS-supports-L5.html),
using the [CSS Conditional Rules 5 definition](https://www.w3.org/TR/css-conditional-5/#at-supports-ext).
The original tests call browser `CSS.supports(conditionText)`; the native XAML port
calls the C# `Css.Supports(condition)` overload, which uses the same evaluator as
`@supports` and conditional `@import`. It retains the known OpenType format and feature-tech
true cases, and the unknown-name, list and quoted-string false cases. Additional
native checks cover the supported font formats, deliberately unadvertised
technologies, boolean composition, style application, generated CSS and source
selection. Browser CSSOM and HTML rendering are outside this port.

## Repeating gradients

The [CSS Images 3 repeating-gradient rules](https://www.w3.org/TR/css-images-3/#repeating-gradients)
define the repeat period by the first and last fixed-up source stops. Native
`repeating-linear-gradient()` and `repeating-radial-gradient()` expand finite
stops over their actual paint bounds; radial ending shapes reach the farthest
painted corner. A zero-length or subpixel cycle becomes the specified
premultiplied sRGBA average instead of overflowing the native stop table.
`CssRepeatingGradientTests.cs` checks phase, used width, radial coverage, average
colors and invalid grammar. `CssRepeatingGradientNativeTests.cs` checks linear/radial
repeat and zero-period average pixels on Windows software, D3D12 and Vulkan. These are native XAML
cases based on the specification, not full HTML WPT page ports.

## Conic gradients

`CssConicGradientTests.cs` retains these CSS Images 4 source expressions and
the original box dimensions, inspected 2026-09-23:

| WPT identifier | Source | Preserved native checks |
| --- | --- | --- |
| `css/css-images/conic-gradient-angle.html` | [Source](https://github.com/web-platform-tests/wpt/blob/master/css/css-images/conic-gradient-angle.html) | Original 200 × 200 `from 90deg` four-stop gradient; blue, black, green and red interior quadrants |
| `css/css-images/conic-gradient-angle-negative.html` | [Source](https://github.com/web-platform-tests/wpt/blob/master/css/css-images/conic-gradient-angle-negative.html) | Original 200 × 200 `from -90deg` value and the same reference quadrants |
| `css/css-images/conic-gradient-center.html` | [Source](https://github.com/web-platform-tests/wpt/blob/master/css/css-images/conic-gradient-center.html) | Original 200 × 200 `at 25% 25%` value and four interior colors around the offset center |
| `css/css-images/gradient/conic-gradient-001.html` | [Source](https://github.com/web-platform-tests/wpt/blob/master/css/css-images/gradient/conic-gradient-001.html) | The first `lime 0 999999999%` solid-color box only; remaining special-value cases are not ported |

These are selected expression and interior-pixel ports onto a native Border,
not whole-page HTML reference comparisons. Independent tests cover repeating
angular periods, resize and raster limits, invalid grammar, managed software
rendering, cross-thread retained recorder replay and Windows software/D3D12/Vulkan pixels
with rounded clipping.

## WOFF 2 font data

`CssWoff2WptTests.cs` consumes 41 original font files from `css/WOFF2/`, with
their original `.xht` identifiers, assertions and SHA-256 hashes retained in
`tests/Jalium.UI.Tests/TestAssets/Fonts/Woff2Wpt/manifest.json`.
The [WOFF 2 recommendation of 2024-08-08](https://www.w3.org/TR/2024/REC-WOFF2-20240808/)
is the decoding baseline.

These are font load-result ports: header and length errors, custom known tags,
invalid collection pairing, all eight basic valid fonts, private data, Brotli errors,
glyph bounds, original-size hints, loca reconstruction and hmtx transforms.
The tests assert acceptance/rejection and reconstructed checksums. They do not claim
to reproduce every original browser page or metadata UI. Native tests separately
exercise both TrueType and CFF output, plus an exact pixel comparison against an
uncompressed Ahem font.

Six test-only fontTools fixtures cover additional null transforms, transformed
metrics over untransformed outlines, shared collections, overlap flags, instructions
and scaled/affine composite glyphs. The independent semantic comparison is recorded
in `artifacts/woff2-reference-comparison.json`.

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

`CssLineWidthRoundTests.cs` adds 20 local cases against the current
[CSS Values 4 `round(line-width, …)` definition](https://drafts.csswg.org/css-values-4/#round-func),
inspected 2026-09-27. They cover omitted and explicit step values, positive and
negative snapping, zero avoidance, length type rejection, property support, and
recalculation of a nested element when root DPI changes. These are local
specification cases; no dedicated WPT file was ported.

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

## Elliptical border radii

`CssBorderRadiusWptTests.cs` ports two original shorthand/longhand reference pairs,
using [Backgrounds and Borders 3, 2024-03-11](https://www.w3.org/TR/2024/CRD-css-backgrounds-3-20240311/#border-radius):

| WPT identifier | Source | Preserved checks |
| --- | --- | --- |
| `css/css-backgrounds/border-radius-005.xht` | [Source](https://github.com/web-platform-tests/wpt/blob/master/css/css-backgrounds/border-radius-005.xht) | Original three horizontal/two vertical radii and four paired longhands; identical computed radii and exact managed software pixels |
| `css/css-backgrounds/border-radius-007.xht` | [Source](https://github.com/web-platform-tests/wpt/blob/master/css/css-backgrounds/border-radius-007.xht) | Original mixed px/mm/pc/in/pt/percentage values, all-corner overlap factor, identical longhand reference pixels |

HTML boxes become native Borders with explicit `box-sizing:content-box`, preserving
the original 200 × 100 content area, 2px border and 204 × 104 border box. Explanatory
text and inter-box spacing are omitted. Separate tests compare a 50% background
exactly to an independent native ellipse, exercise software child clipping, thick
opposite borders, generated CSS, binding precedence and live size changes. Native
software/D3D12/Vulkan cases compare exact interior/edge colours away from the
antialiased contour; they do not claim full native pixel parity or elliptical clipping.

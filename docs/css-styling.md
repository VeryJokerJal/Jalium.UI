# CSS Styling

Jalium.UI adds CSS styling to its existing WPF-compatible XAML controls. Supported CSS
properties use standard names; native properties without a CSS counterpart map through an
automatic kebab-case form (`is-tab-stop` → `IsTabStop`, `horizontal-content-alignment` →
`HorizontalContentAlignment`).

See [the conformance tracker](css-conformance.md) for implementation boundaries and platform
verification. This is a native XAML styling engine; complete CSS conformance remains in progress.

## WPF coexistence

Existing XAML, dependency properties, bindings, resources, styles, triggers, templates,
events and commands remain the application model. Controls without CSS keep their
native behavior. CSS values use separate property layers and the existing CssMappings
converters; applying or removing CSS does not rewrite local values or binding objects.

Native local values and bindings outrank every CSS declaration, including `!important`
and CSS animation output. For example, this Border stays 100 units wide:

```xml
<Border Width="100" Css.Style="width:200px !important; background-color:red" />
```

With `Width="{Binding CardWidth}"`, the binding keeps controlling the width while CSS
can supply the background. Native animations and property coercion retain their native
precedence. Setting a local value or binding during a CSS transition stops that CSS
clock immediately, including when the new value equals the transition's destination.

Native Grid, StackPanel and other panel layouts remain the default. An explicit supported
`display` declaration selects a CSS formatter over the original child collection;
removing it restores the native formatter and retained definitions/attached properties.
Page selectors respect template boundaries. CSS never replaces user Children collections,
namescopes or bindings to implement layout or generated visual content.

These are XAML host integration rules. CSS conformance is tracked within that host
contract; it does not imply that browser document behavior or every WPF API is complete.

## Two channels

**Inline declarations** — the `Css.Style` attached property takes a declaration block:

```xml
<Border Css.Style="background-color: rgb(0 120 215); border-radius: 8px;
                   padding: 12px 16px; box-shadow: 0 2px 8px rgba(0,0,0,.35)" />
```

**Style sheets** — parsed once, applied through selectors. Application-wide:

```csharp
Application.Current.StyleSheets.Add(CssStyleSheet.Parse("""
    Button { border-radius: 6px; transition: background-color 0.15s ease-out }
    Button:hover { background-color: #2563eb }
    .card > TextBlock { font-size: 1.1em; color: #e5e7eb }
    """));
// or from generated CSS (with an embedded pack resource as the fallback):
Application.Current.StyleSheets.Add(
    CssStyleSheet.FromUri(new Uri("/MyApp;component/styles/app.css", UriKind.Relative)));
```

Element-scoped (applies to that element's subtree only):

```csharp
Css.GetStyleSheets(panel).Add(CssStyleSheet.Parse("TextBlock { color: gray }"));
```

The same scope from markup — `Css.StyleSheet` takes style-sheet text directly, the markup
equivalent of HTML's `<style>`:

```xml
<StackPanel Css.StyleSheet="
        TextBlock { color: gray }
        Button:hover { background-color: #2563eb }">
    …
</StackPanel>
```

Re-setting the text replaces that sheet only; sheets added to `Css.StyleSheets` in code are
left alone, and both channels feed the same subtree scope in insertion order.

Classes come from the `Css.Class` attached property (space-separated):

```xml
<StackPanel Css.Class="card elevated">…</StackPanel>
```

## Build-time CSS to C#

Projects importing `Jalium.UI.Build` now compile CSS into C# by default, in both
Debug and Release, including NativeAOT publication. The build discovers project
`*.css` files recursively (excluding build outputs and `node_modules`) as
`JaliumCss` items. It also compiles static `Css.Style` and `Css.StyleSheet`
**attributes** from the effective JALXAML inputs after the existing Razor transform.
Bindings and other markup extensions keep their runtime behavior.
Documents containing non-XML Razor syntax use the existing JALXAML parser's
lowering rules for CSS extraction. If it cannot provide a static tree, `JALCSS003`
reports that the document keeps its runtime CSS path.

For example, put this in `styles/app.css`:

```css
.card { padding: 16px; border-radius: 8px; background-color: #1f2937; }
.card:hover { background-color: #374151; }
```

Load it using the assembly-qualified component URI, with the assembly name and
resource path matching the project:

```csharp
using Jalium.UI.Styling;

var sheet = CssStyleSheet.FromUri(
    new Uri("/MyApp;component/styles/app.css", UriKind.Relative));
Css.GetStyleSheets(page).Add(sheet);
// Application.Current.StyleSheets.Add(sheet) applies the same sheet globally.
```

Generated resources do not need an `EmbeddedResource` entry or a deployed `.css`
file. Registration alone does not apply a stylesheet: attach it to the desired
subtree or application as above. Existing `Css.Class`, `Css.Style` and
`Css.StyleSheet` markup continues to work without changing the call sites.

The output is `$(JalxamlIntermediateOutputPath)Css/Jalium.Css.g.cs`, normally under
the configuration/framework-specific `obj` directory. `CssGeneratedOutputPath`
can override it. A module initializer registers lazy factories; those factories
directly construct selector graphs, declaration records, scopes, namespace bindings,
layers, imports and registered-property syntax through `CssStyleSheetBuilder`.
The generated factories do not call `CssStyleSheet.Parse`. Shared nesting nodes,
source order and specificity are retained, and anonymous layers receive fresh
identities each time a factory creates a sheet.

Use `LoadAsync` when the sheet contains `@import`:

```csharp
var sheet = await CssStyleSheet.LoadAsync(
    new Uri("/MyApp;component/styles/app.css", UriKind.Relative));
```

The existing import loader resolves generated resources first, preserving relative
base URIs, conditional imports, layers, cancellation and cycle recovery. `FromUri`
retains its existing single-sheet behavior and does not expand imports. Build-time
compilation performs no network fetching. An explicitly supplied
`ICssResourceResolver` remains authoritative and bypasses the generated-resource
lookup, allowing callers to load changed files or remote styles.

To choose inputs explicitly, disable discovery and declare `JaliumCss` items. For
linked files, `ResourcePath` sets the path after `;component/`; `Link` metadata is
also accepted when `ResourcePath` is absent:

```xml
<PropertyGroup>
  <EnableDefaultCssItems>false</EnableDefaultCssItems>
</PropertyGroup>
<ItemGroup>
  <JaliumCss Include="styles/app.css" />
  <JaliumCss Include="../Shared/theme.css" ResourcePath="styles/theme.css" />
</ItemGroup>
```

`<EnableCssCodeGeneration>false</EnableCssCodeGeneration>` disables this build
step. In that mode, resource-based loading needs the original embedded resources
or a caller-supplied stream/resolver. `CssStyleSheet.Parse` and `FromStream` always
remain available for runtime CSS. Static strings written only in C# and XAML
property-element syntax are not discovered by this build step; they continue
through the runtime paths.

The build tracks the complete input list and content hashes, including the compiler
and parser binaries. Unchanged inputs leave the generated file untouched; edits,
additions and removals update it, including removal of the last CSS file. Generated
files and their manifest/cache are registered with MSBuild for cleaning. Source
checkout builds isolate the portable compiler's outputs from application RID/AOT
outputs so its restore does not overwrite application assets.

**Precompilation boundary:** stylesheet syntax, declaration splitting and selector
parsing move to build time. Property value conversion, custom mapping lookup,
shorthand expansion, condition-query compilation, dynamic values, selector matching
and cascade evaluation still use the existing runtime engine and its caches.
Consequently this does not remove every parser or make every property a C# constant.
It preserves runtime `CssMappings` changes, bindings, transitions and the native
local-value precedence described above. CSS parse-recovery diagnostics appear as
`JALCSS001` build warnings; portable-compiler failures use `JALCSS002` errors.

`tests/Jalium.UI.Css.CodeGenSmoke` demonstrates standalone CSS, imports, generated
JALXAML attributes and dynamic fallback. Its CSS files deliberately are not embedded,
so its resource checks require generated factories. Run it normally or publish it:

```powershell
dotnet run --project tests/Jalium.UI.Css.CodeGenSmoke -c Release
dotnet publish tests/Jalium.UI.Css.CodeGenSmoke -c Release -r win-x64 -p:PublishAot=true
```

## Selectors

Type (`Button`, matches derived types), universal (`*`), class (`.card`), id (`#hero`,
matches `x:Name`), descendant (`A B`) and child (`A > B`) combinators, comma groups,
`!important`, and the dynamic pseudo-classes `:hover`, `:active`, `:focus`,
`:focus-visible`, `:focus-within`, `:enabled`, `:disabled`, `:checked`, `:indeterminate`.
Also supported: sibling combinators (`~`, `+`), attribute selectors (including their
operators and `i`/`s` flags), `:root`, `:scope`, `:empty`, first/last/only child and type
selectors, `:is()`, `:where()`, `:not()`, `:has()`, and `:nth-child()` families including
`of <selector-list>`. Identifiers and strings accept CSS escapes. Namespace-qualified
selectors are supported; generated pseudo-elements remain pending.

`Css.GetAttributes(element)` exposes an observable attribute collection; `Css.SetAttribute`
sets or removes individual entries. Attribute matching also projects explicit native
property values, while `id` and `class` map to `Name` and `Css.Class`.

Template-generated parts are invisible to page-level CSS: they never match selectors and
the ancestor axis skips them, so `Button > TextBlock` reaches a button's content even
though template elements sit between them visually. User content inside a templated
control (and DataTemplate content) is fully matchable.

### XAML namespaces

`@namespace` binds case-sensitive CSS prefixes to literal namespace strings. These
bindings are local to one stylesheet and do not cross imports. They must appear
after imports and before style/group rules; initial layer statements are allowed.
URI spelling is preserved, including empty strings: namespace declarations never
fetch resources or normalize URLs.

```css
@namespace ui "http://schemas.jalium.ui/2024";
@namespace controls "clr-namespace:Jalium.UI.Controls;assembly=Jalium.UI.Controls";
ui|Border { background-color: #2563eb }
controls|Border[controls|Tag="highlight"] { outline: 2px solid gold }
```

`prefix|Type`, `prefix|*`, `*|Type`, `|Type` and their attribute equivalents match
expanded XML names. The runtime parser and generated XAML builders record original
namespace/local names, including aliases that resolve to the same native control.
Qualified selectors use those names exactly. Unqualified type selectors also retain
the existing native assignable-type matching. Code-created framework controls use
the canonical Jalium presentation namespace; other code-created types use their
CLR namespace/assembly URI. XamlBuilder.SetXmlIdentity can supply an explicit identity.

A default namespace constrains elements, including implicit universal selectors,
but not unqualified attributes. `:is()`, `:where()` and `:not()` preserve the
subject-compound exceptions; `*-of-type` counts expanded names independently.
Selector comments do not become descendant combinators or merge identifiers.
Support queries resolve prefixes from their own sheet and reject unsupported
branches even inside otherwise forgiving selector functions.

Use `Css.SetAttribute(element, namespaceUri, localName, value)` and the corresponding
GetAttribute overload for explicit namespaced attributes. `*|` can match any of
several attributes sharing a local name. Markup attributes project live native DP
values, including bindings and attached properties; CSS does not overwrite them.
Metadata and observable attribute subscriptions use weak ownership. Gallery shows
two Border namespaces and a qualified Tag binding. Run's text content is preserved
through the same runtime/generated construction paths.

The namespace baseline is [CSS Namespaces 3, 2014-03-20](https://www.w3.org/TR/2014/REC-css-namespaces-3-20140320/).
The full selector corpus, remaining host states and pseudo-elements are still pending.

### Nesting and scoped rules

Style rules can contain nested selectors and media/supports/container/layer groups.
`&` references the parent selector list, including its maximum specificity. The
engine retains shared selector objects and memoizes matching rather than expanding
all combinations. Relative selectors, repeated `&`, functions containing `&`,
type selectors and declarations interleaved with nested rules are supported.
Declarations retain their source position and the parent rule's individual
selector specificities. Concatenations such as `&-suffix` are invalid CSS.

```css
.card {
    padding: 12px;
    > .title { font-weight: bold }
    &:hover { background-color: #334155 }
    @media (min-width: 600px) { padding: 20px }
}
@scope (.card) to (.nested-widget) {
    border-radius: 8px;
    .action { background-color: #2563eb }
}
```

`@scope` matches explicit roots and excludes the limit elements and their
descendants. An omitted root uses the stylesheet's native host; within a style
rule it uses that rule's nesting context. `:scope` and `&` can target the scope
root, with pseudo-class and zero specificity respectively. Direct declarations
inside `@scope` target that root with zero specificity. Nested scopes constrain
their subjects by all enclosing limits; the innermost root determines proximity.
Root and limit selectors do not add specificity to the body's rules.

Scope only restricts the rule subject. Other selector components can refer to
ancestors or siblings outside the scope, and property inheritance continues
through scope limits. Matching tracks those external states and tree changes
through weak subscriptions. Local values, bindings, templates and native child
collections retain their existing behavior. Gallery demonstrates a scope limit
controlled by a native IsEnabled binding.

The pinned sources are [CSS Nesting 1, 2026-01-22](https://www.w3.org/TR/2026/WD-css-nesting-1-20260122/)
and [Cascade 6, 2024-09-06](https://www.w3.org/TR/2024/WD-css-cascade-6-20240906/), with the newer nesting
rules for `&` and scoped declarations. Pseudo-element integration and
the broader conformance corpus remain pending.

## Precedence

CSS values integrate with the dependency-property system as two layers:

```
local values / bindings  >  parent-template triggers  >  parent-template values
    >  CSS :state winners  >  style triggers  >  template triggers
    >  CSS normal winners  >  style setters  >  native fallback
```

Practically: CSS overrides theme/implicit/explicit style *setters* but never a local value
you set in XAML or code, and a `:hover` rule you write outranks the theme's hover trigger.
Within CSS, winner selection accounts for `!important`, inline declarations, named layers,
native subtree scope, specificity, @scope proximity, and source order. Proximity
breaks equal-specificity ties before source order. `@layer` supports named and nested
layers; important declarations reverse layer order. `revert-layer` rolls back the current
layer and `revert` reveals the native host fallback. `SetCurrentValue` on a
CSS-supplied property writes into the owning CSS layer and is overwritten by the next
re-evaluation, matching style-reapplication semantics.

Setting a `transition` makes pseudo-class flips animate automatically:

```css
Button { transition: background-color 0.2s ease-out; background-color: #1f2937 }
Button:hover { background-color: #374151 }
```

## Property mapping highlights

The mismatches between CSS and the property system are resolved by the engine:

| CSS | Maps to | Notes |
|---|---|---|
| `margin: 1px 2px 3px 4px` | `Margin` (Thickness) | CSS order top/right/bottom/left is converted; `Thickness` stores left/top/right/bottom |
| `background-color`, `color` | `Background`, `Foreground` (Brush) | colors wrap into `SolidColorBrush`; `#RRGGBBAA` uses CSS trailing-alpha semantics |
| `background: linear-gradient(…)` | `LinearGradientBrush` | CSS angles (0deg = up, clockwise) convert to Start/EndPoint |
| `border: 1px solid red` | `BorderThickness` + `BorderBrush` | `none`/`hidden` clears the brush and uses zero border width; dashed/dotted render solid with a diagnostic |
| `border-radius` | `CornerRadius` | corner order matches 1:1 |
| `box-shadow: 3px 4px 5px …` | `DropShadowEffect` | cartesian offsets convert to polar `Direction`/`ShadowDepth`; `inset` → `InnerShadowEffect`; multiple shadows → `EffectGroup` |
| `cursor: pointer` | `Cursors.Hand` | full keyword table (`not-allowed` → `No`, `move` → `SizeAll`, …) |
| `display: none` / `visibility: hidden` | `Visibility` Collapsed / Hidden | Display and visibility combine independently; block/flow-root/flex/grid select layout on existing panels |
| `transform: translate(…) rotate(…)` | `RenderTransform` (TransformGroup) | does not affect layout, like CSS |
| `transition` | per-property CSS transition data plus native configuration mirrors | Individual durations, positive/negative delays, exact CSS Bézier and steps curves; native local target values remain authoritative |
| `z-index`, `left`/`top`, `grid-row`/`grid-column`, `gap` | `Panel.ZIndex`, `Canvas.Left/Top`, `Grid.Row/Column(+Span)`, per-panel spacing | grid lines convert from 1-based to 0-based |
| `font`, `font-size: 1.2em`, `line-height: 1.5` | font properties | `em`/`%` on font-size use the inherited size; unitless line-height multiplies the element's font size |
| `outline: 2px solid red` (+`outline-offset`) | `OutlineBrush/OutlineThickness/OutlineStyle/OutlineOffset` | self-drawn ring outside the bounds, takes no layout space; `outline: none` clears it; pairs naturally with `:focus-visible` |
| `is-tab-stop: true`, `horizontal-alignment: Center`, … | any dependency property | the kebab-case fallback channel; values go through the XAML converter stack |

Lengths support `px` (default), `pt`, `in`, `cm`, `mm`, `q`, `pc`, `em`, `rem`; colors
support hex, all CSS named colors, `rgb()`/`rgba()`/`hsl()`/`hsla()` in legacy and modern
syntax, and `transparent`.

Math functions preserve percentage terms until layout. `vw`,
`vh`, `vmin`, and `vmax` use the visual root's viewport. Translation functions also
resolve percentages against the transformed element and support font-relative lengths.
`currentColor` is supported by background, border, and outline color mappings.

Typography and color inherit through generic visual containers. The same engine styles
`FrameworkContentElement`, `Run`, `Paragraph`, and `FlowDocument`; XAML document collections
preserve the parent links required for selector matching and inheritance.

Real CSS properties the framework has no capability for (`letter-spacing`,
`text-shadow`, `animation`, `backdrop-filter`, …) are registered as explicit
*unsupported* entries: they never fall through to the kebab lookup, and each reports a
one-shot diagnostic explaining the limitation and the nearest alternative.

Diagnostics are one-shot per (property, reason, element type) and can be silenced with
`CssDiagnostics.LogUnresolvedProperties = false`.

## Mathematical functions

The function baseline is [CSS Values 4, 2024-03-12](https://www.w3.org/TR/2024/WD-css-values-4-20240312/).
Supported families include `calc/min/max/clamp`, `round/mod/rem`,
`sin/cos/tan/asin/acos/atan/atan2`, `pow/sqrt/hypot/log/exp`, and `abs/sign`.
`round()` supports all four strategies; `clamp()` accepts `none` bounds.
Calculations accept `e`, `pi`, `infinity`, `-infinity`, and `NaN`.

```css
.meter {
    width: round(8em, 40px);
    height: hypot(30px, 40px);
    opacity: clamp(.25, 1em / 40px, 1);
    transform: rotate(atan2(1em, 20px));
}
```

Intermediate products retain dimensions, allowing `calc(10px * 20px / 5px)`.
The final type must fit the property: a number-valued function does not become a
length. Percentage dependencies survive unit cancellation. Special arithmetic values
remain inside the tree; its outer result converts NaN/signed zero to zero and
infinities to finite double limits before narrower target ranges are applied.
Integer math rounds ties toward positive infinity. Limits are 512 terms and 64
nesting levels, with invalid syntax rejected independently of other declarations.

Dynamic scalar values reuse the existing parsers with the actual font, viewport
and query-container context. This includes transition shorthands, colors, transforms
and Grid fractions. Registered values normalize relative lengths while retaining
nonlinear percentages and special-value propagation. Gallery binds FontSize to a
native slider and demonstrates width and opacity recomputation.

Full range metadata (including zero-sized CSS typography), additional value-module
functions and the full WPT/backend corpus remain pending.

## Font and viewport units

The [Values 4 unit baseline](https://www.w3.org/TR/2024/WD-css-values-4-20240312/#relative-lengths)
now includes `ex/rex`, `cap/rcap`, `ch/rch`, `ic/ric`, `lh/rlh` alongside `em/rem`.
Font properties use parent metrics where required; `lh/rlh` avoid self-reference
inside line-height. Root font declarations use initial metrics for their own root
units. Other properties use the newly computed font and line height.

All 24 viewport variants are recognized: `vw/vh/vi/vb/vmin/vmax` and their
`sv*`, `lv*`, `dv*` forms. Ordinary native windows use one client viewport for all
three sizes. Embedded hosts can supply different sizes without changing their trees:

```csharp
Css.SetViewportMetrics(host, new CssViewportMetrics(
    small: new Size(420, 100), large: new Size(420, 200), dynamic: new Size(420, 150)));
// Replacing this immutable value updates dependent styles; null restores inheritance.
Css.SetViewportMetrics(host, null);
```

`CssViewportMetrics.IsVertical` lets an explicit host provide its logical axes.
This does not implement the still-pending CSS vertical text/layout formatter.
Container-unit fallback uses the small viewport; zero-sized viewports resolve to zero.

`TextMeasurement.GetFontUnitMetrics` queries D3D12/Windows Vulkan through DirectWrite,
Windows software through GDI, and Linux/Android through the self-hosted font parser.
It returns actual face/glyph rulers with availability flags and standard fallbacks.
The optional native side interface preserves the existing TextFormat vtable and C
text-metrics layout. Caches are bounded and keyed by font identity/size/context;
cache changes notify weak CSS dependents, and removed declarations stop observing.

Registered values track font-family/weight/style and line-height cycles as well as
font-size cycles. Native local values and bindings remain authoritative. Gallery
demonstrates a native FontSize binding and an independently resized embedded viewport.
Font loading, full variation/stretch/vertical metrics and device/GPU pixel coverage
remain in the conformance tracker.

## Layout: percentages, aspect-ratio, position, flexbox

Percentage lengths resolve during layout against the containing block (the constraint
the parent hands down): `width/height/min-*/max-*`, all four `margin`/`padding` edges
(basis is the block's *width*, per CSS), and the `left/top/right/bottom` insets. Against
an infinite constraint (a StackPanel's stacking axis, a Canvas) a percentage degrades to
`auto`. A local `Width` set in code still outranks a CSS percentage. `aspect-ratio`
derives the auto axis from the determinate one; `box-sizing: content-box` adds the
padding+border chrome to explicit sizes.

`position: absolute` takes an element out of its panel's flow: it contributes nothing to
the flow or the panel's desired size and is placed by the inset rules (`left`/`top`/
`right`/`bottom`, `%` allowed; both edges set with `width: auto` stretches between them).
All built-in panels participate; a custom panel opts in with three lines
(`if (IsCssAbsolute(child)) continue;` in its loops plus the two `…CssAbsoluteChildren`
helpers). Without `position: absolute`, absolute-pixel `left/top/right/bottom` keep their
historical Canvas attached-property mapping.

**FlexPanel**, and existing panels declaring `display:flex`, share a flexbox layout algorithm
(`Direction`, `Wrap`, `JustifyContent`, `AlignItems`, `AlignContent`, `Row/ColumnSpacing`
plus the attached `Grow`/`Shrink`/`Basis`/`AlignSelf`/`Order`). The whole flex property
family maps onto it: `flex-direction/-wrap`, `justify-content`, `align-items/-content`,
`flex-flow`, `flex` (and its longhands), `align-self`, `order`, `gap`. Flex basis accepts
percentages and length expressions. Horizontal `margin:auto` absorbs available space.
Applying or removing `display:flex` preserves the original child collection and parent links.

`display:grid` also works over an existing Panel-derived control without replacing its
children or native Grid definitions. Grid values use the existing declaration compiler,
variables, aliases, custom converters and CSS value layers. The grid formatter supports
fixed/percentage/math/font-relative tracks, `fr`, `minmax()`, `fit-content()`, integer and
automatic `repeat()`, named lines and areas, negative lines, spans, row/column auto-flow,
dense placement, and `grid`/`grid-template` shorthands. Intrinsic sizing is currently
adapted from native child measurement; full CSS text intrinsic measurements,
baseline alignment, fragmentation and complete inline/grid baseline interactions remain pending.

Grid and Flex resolve percentage/math gaps against the corresponding container axis;
font-relative gaps also use the existing native spacing mappings. Native layouts outside
these CSS formatting contexts still require work for percentage gap semantics. `gap`
now expands into row/column longhands so their cascade and `!important` work independently.

Local widths, bindings, explicit native alignments and Grid placement values retain
priority. CSS item alignment is passed through private arrange context rather than
written into local dependency-property values. Removing `display:grid` restores the
original panel algorithm, native definitions and native alignment behavior.

Nested CSS grids can now declare `grid-template-columns:subgrid` and/or
`grid-template-rows:subgrid`. Their tracks follow the parent span, including named
lines, clipped named areas, local line names and `repeat(auto-fill,[name])`.
Descendant sizes contribute to the corresponding parent tracks. A subgrid cannot
create implicit tracks on a shared axis; out-of-range placement is clamped there.
`gap:normal` follows the parent gutters, while an explicit different gap keeps the
gutter centers aligned. Margin and padding edges participate in the shared sizing.

CSS widths, min/max constraints and alignment on a subgrid are ignored on shared
axes as required by Grid 2. Native local values and bindings still retain the
agreed XAML precedence. With no CSS Grid parent, `subgrid` behaves as `none` and
the container lays out independently. Existing CSS converters and aliases remain
in the same property-registration pipeline.

Native measure-input revisions invalidate cached intrinsic contributions when
content changes. CSS allocation changes trigger layout and paint without being
mistaken for new intrinsic content. The grid keeps a bounded cache for the
provisional/resolved allocations visited during nested layout. General text
intrinsic sizing, orthogonal writing modes and the complete conformance corpus
remain unfinished.

`display:block` and `display:flow-root` now select ordinary flow on an existing panel.
The formatter owns margin placement, including sibling, nested, empty-block and
negative margin collapse, without changing native Margin values. Padding, clipping
and independent formatting roots stop parent/child margin collapse. Percentage
values retain their containing-block basis when a smaller border box is arranged.

In this XAML host profile, native children retain opaque block participation by
default. Explicit `inline`, `inline-block`, `inline-flex` and `inline-grid` select
atomic inline participation; native control internals and templates stay intact.
The formatter builds anonymous line runs around intervening blocks, wraps atomic
boxes, and supports native baseline offsets plus baseline/top/bottom/text-top/
text-bottom/length vertical alignment. Two-keyword forms such as `inline grid`
are accepted. Splittable text/Run fragments, inline decorations across fragments,
full bidi and the remaining vertical-align modes still need implementation.

CSS ordinary flow defaults to wrapping; an explicitly set native TextWrapping
value or a CSS white-space declaration participates through the property system.
Line-height now retains unitless numbers through inheritance and resolves length
or percentage values before inheritance. Display:none stays collapsed even beside
visibility:visible. Native local Visibility and other local/bound values continue
to outrank CSS, and removing the CSS display declaration restores native layout.

Headless `UpdateLayout` now propagates pending descendant measure/arrange invalidation
before starting at the root, matching the ancestor work normally done by a layout
manager. This is also covered for ordinary native panels without CSS.

CSS flow containers now support `float:left/right` and `clear:left/right/both`.
Logical `inline-start/inline-end` map through the containing block's native flow
direction. A floating native box uses shrink-to-fit sizing when its width is auto,
and establishes an independent formatting context. Normal block descendants share
float exclusions, translated through their margins and padding; `flow-root`, clipped
contexts and other independent boxes isolate their contents. Floats are inert in
native layouts and as Grid/Flex items.

Atomic inline runs are shortened beside floats and regain the full width below
them. Floats encountered after inline content either reposition that line or move
down when there is insufficient space. Clearances account for the requested float
sides and adjoining margins. Formatting roots include their floating descendants
in automatic height. Native paint/hit traversal places float groups above normal
block backgrounds while retaining native ZIndex precedence and the original
Children collection. This does not yet constitute full CSS stacking-context or
text-fragment conformance.
On panels using their native layout, container
properties go through the **interception protocol**: a panel overrides
`TryApplyCssPropertyCore(name, rawValue, setter)` to map CSS concepts onto its own
properties (StackPanel maps `flex-direction: row|column` to `Orientation`); the same
override is the last resort for any unknown property name, so custom panels can consume
arbitrary custom keys. Values written through the setter live in the CSS layers and are
automatically cleared when the rule stops matching.

## Variables, conditional rules, and resources

Custom properties preserve identifier case. Their computed token streams inherit through
the XAML tree; cycles invalidate the participating properties, and `var()` fallbacks are
applied at computed-value time. A shorthand containing variables still participates in
the cascade through its individual longhands. `initial`, `inherit`, `unset`, and `all`
use the registered property metadata.

```css
@layer base, accents;
:scope { --space: 16px; --brand: #2563eb }
@layer base { .card { width: calc(100% - var(--space) * 2); margin: 0 auto } }
@layer accents { .card { background-color: var(--brand) } }
@media (min-width: 600px) {
    @supports (width: calc(100% - 1px)) { .card { padding: 20px } }
}
```

Media query coverage currently includes width, height, aspect ratio, orientation,
boolean features, reversed/chained ranges and boolean composition. Unknown features
remain unknown under negation. `Css.Supports(property, value)` checks registered value
grammars; support queries also accept `selector()` and implemented `at-rule()` names.
Remaining media features remain pending.

### Registered custom properties

`@property` adds syntax, inheritance and initial values to custom properties. Rules
share a registration map within each native document root, even when their
stylesheet is attached to a descendant. Different roots remain independent;
ordinary selector scoping and template isolation retain their existing behavior.
Registrations follow normal layer priority, then source order. Media/supports conditions can
activate registrations; container conditions do not constrain name-defining rules.

```css
@property --card-width {
    syntax: "<length>";
    inherits: false;
    initial-value: 160px;
}
@property --card-color {
    syntax: "<color>";
    inherits: true;
    initial-value: #2563eb;
}
.card { width: var(--card-width); background-color: var(--card-color) }
.large { --card-width: 2in }
.invalid { --card-width: red } /* Uses the registered 160px initial value. */
```

The existing converters receive serialized computed values: a registered `2em`
length computed at 20px becomes `40px` before another variable inherits or uses it.
Type checks happen after the cascade, so an invalid winning declaration cannot
revive an older declaration. Inheritance, global keywords, variable cycles,
font/color dependencies and typed fallbacks follow the registration. Unknown
custom properties still use ordinary token substitution. `Css.Supports` checks
their permissive declaration grammar independently of registered types.

Syntax strings support alternatives, literal identifiers and `+`/`#` lists, using
the existing numeric, length, color, URL/image and 2D transform parsers. Mixed
length/percentage math remains symbolic. Typed URLs keep their declaration or
registration resource base. Parsing and computing a registration perform no I/O.
Viewport-relative initial values use the host's allocated viewport, independently
of a styled root's own Width. Shared stylesheet subscriptions do not retain dead
native roots.

The implementation is pinned to [Properties and Values API 1, 2024-03-26](https://www.w3.org/TR/2024/WD-css-properties-values-api-1-20240326/).
Registered-property interpolation/keyframes, remaining font units, modern color
and image syntax, 3D transforms and the full WPT/device corpus remain pending.
Gallery's editable declarations example preserves its native TextBox/Css.Style
binding while demonstrating valid values and invalid-value recovery.

### Container queries

`container-name`, `container-type:normal/inline-size/size`, and the `container`
shorthand use the existing CSS property layers. Named, nested `@container` rules
support width/height, horizontal inline/block size, aspect ratio, orientation,
boolean combinations, reversed/chained ranges, and comma-separated conditions.
Measurements use the selected ancestor's content box; relative query thresholds
use that container's font and ancestor context.

```css
.card { container: card / inline-size; --density: comfortable }
.tiles { display: grid; grid-template-columns: 1fr; gap: 12px }
@container card (width >= 360px) and style(--density: comfortable) {
    .tiles { grid-template-columns: repeat(2, minmax(0, 1fr)) }
}
.indicator { width: 10cqi; height: 6px }
```

`cqw/cqh/cqi/cqb/cqmin/cqmax` work through the existing length converters, including
math, Grid tracks, typography and transforms. Each axis selects its own eligible
ancestor; the native host viewport supplies the fallback. Unregistered custom
property `style()` queries support computed-token equality and boolean existence,
including on nonvisual document nodes.
Registered properties compare typed computed values; their boolean queries compare
against the registered initial value.

Explicit size containment excludes child intrinsic contributions on the queried
axes and creates an independent flow context. Local XAML dimensions and bindings
still win. Query changes settle with layout, inherited variable updates propagate,
and weak subscriptions allow detached controls to be collected. Removing CSS
restores native layout and values. The Gallery slider demonstrates a native Width
binding with responsive CSS columns.

This implementation is pinned to [CSS Conditional Rules 5, 2025-10-30](https://www.w3.org/TR/2025/WD-css-conditional-5-20251030/).
Scroll-state queries, style ranges/ordinary-property queries,
orthogonal writing modes, intrinsic-size overrides, dynamic viewport
variants and full native backend verification remain pending.

### Conditional and layered imports

`CssStyleSheet.Parse` performs no I/O. `await CssStyleSheet.LoadAsync(uri)` resolves
imports with strings or `url()`, named/anonymous layers, `supports()` and media
conditions, including nested import graphs.

```css
@layer base, responsive;
@import "base.css" layer(base);
@import "wide.css" layer(responsive)
    supports(display: grid) screen and (min-width: 600px);
```

A false `supports()` condition prevents the resource request. Media-dependent
resources are loaded eagerly and their rules switch with the native viewport
without another fetch. Register converter/alias capabilities before loading;
reload the sheet if those capabilities change after a support-gated import was
skipped. Rules, layers, scopes and registered properties retain their import
conditions and resource bases.

Repeated imports share fetched/parsed bytes but expand independently, including
their anonymous layers and ancestor-specific cycle recovery. Failed imports still
declare their requested layer when its conditions match. Layer identifiers preserve
case and normalize escapes; an escaped dot remains part of a single identifier.
Imports remain consecutive after initial layer statements; a layer statement after
an import prevents further imports. Redirect cycles, cancellation and stream disposal
are covered by regressions.

An `ICssResourceResolver` overload supports application loaders. It returns a
`CssResource` with the final URI and a stream owned and disposed by the loader.
The default resolver supports application resources, files and HTTP(S). Gallery's
embedded CSS files demonstrate conditional layer imports with a native width binding.
The import baseline is [Cascade 5, 2022-01-13](https://www.w3.org/TR/2022/CR-css-cascade-5-20220113/).
Remaining font/media capabilities and stylesheet encoding/content-type coverage
stay on the conformance checklist.

Color-matrix filters now include brightness, contrast, grayscale, hue rotation, invert,
opacity, saturate, and sepia, in addition to blur and drop-shadow. The implementation uses
the shared native effect pipeline. Keyframes, transitions of layout-only symbolic values,
and nonvisual document-node transitions still require additional work.

## Extending the mapping table

`CssMappings` (namespace `Jalium.UI.Styling`) is the public registration surface — keys
and values are both customizable. Value conversion is interface-shaped, modeled on
`IValueConverter` but narrowed for CSS: input is always the declaration text, conversion
is one-way, and the culture is always `InvariantCulture`:

```csharp
public interface ICssValueConverter
{
    object? Convert(string rawValue, Type targetType, object? parameter, CultureInfo culture);
}

public interface ICssMultiPropertyConverter   // one raw value in, many assignments out
{
    IReadOnlyList<CssPropertyAssignment>? Convert(
        string rawValue, FrameworkElement element, object? parameter, CultureInfo culture);
}
```

```csharp
// A design-token alias for a built-in property:
CssMappings.RegisterAlias("brand-radius", "border-radius");

// A custom property with a custom value converter:
sealed class GlowLevelConverter : ICssValueConverter
{
    public object? Convert(string rawValue, Type targetType, object? parameter, CultureInfo culture)
        => double.TryParse(rawValue, NumberStyles.Float, culture, out var v)
            ? Math.Clamp(v / 10, 0, 1)
            : null;                       // null drops the declaration, per the CSS error model
}
CssMappings.RegisterProperty("glow-level", UIElement.OpacityProperty, new GlowLevelConverter());

// Resolved by DP name per element type ("Padding"/"Background" style), with a
// registration parameter — the same converter class can serve several css names:
CssMappings.RegisterProperty("surface-tint", "Background",
    new TintBrushConverter(), converterParameter: 0.35);

// Element-aware, several assignments from one declaration:
CssMappings.RegisterProperty("card-size", new CardSizeConverter());   // ICssMultiPropertyConverter
```

Later registrations win — including over built-ins and the unsupported placeholders
(registering `letter-spacing` yourself replaces the placeholder). A converter returning
null drops that declaration; for a multi-property converter an empty list is a successful
no-op. Exceptions are treated as null and never escape the engine.

`cacheByValue` (default true) converts once per distinct declaration text and shares the
result across matched elements — return frozen Freezables; `false` converts on every
application. `targetType` is the target property's `PropertyType` whenever the property is
known at conversion time: always for a fixed-`DependencyProperty` registration, and for a
by-name registration when `cacheByValue` is `false`. A by-name registration with
`cacheByValue: true` converts at compile time, before any element exists — `targetType` is
then `typeof(object)`, because a single shared result cannot depend on which owner type
the name later resolves against.

The `culture` argument is always `CultureInfo.InvariantCulture` — deliberately different
from the binding engine's `CurrentCulture` default: style-sheet text is culture-invariant
source code, and cached conversions are shared process-wide. Parse numbers with it
(`double.TryParse(rawValue, NumberStyles.Float, culture, out var v)`) rather than the
bare overload. `CssValueParsing` exposes the engine's color/length parsing for custom
converters. There is no unregister; already-parsed style sheets pick up new registrations
automatically.

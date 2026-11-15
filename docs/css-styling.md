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

To keep those rules in an application project CSS file, use `Css.StyleSheetSource`
with its project-relative path:

```xml
<StackPanel Css.StyleSheetSource="styles/app.css">
    …
</StackPanel>
```

This relative path resolves through `Application.ResourceAssembly`. For a CSS file
in another assembly, use an explicit URI such as
`/MyLibrary;component/styles/app.css`. The build discovers project CSS files
automatically as described below. The source
property loads one style sheet synchronously and can be changed or cleared at
runtime; it leaves sheets added through `Css.StyleSheet` or `Css.StyleSheets`
alone. It also accepts a `file://` URI for a deployed CSS file. If the file uses
`@import`, load it with `CssStyleSheet.LoadAsync` in code and add the result to
the appropriate sheet collection.

For a CSS file on disk, use `CssStyleSheet.FromFile(path)` to load just that file,
or `await CssStyleSheet.LoadFileAsync(path)` to expand relative `@import` rules.
Relative paths passed to these C# methods use the process's current working
directory. `CssStyleSheet.FromUri` also accepts a `file://` URI.

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
`:focus-visible`, `:focus-within`, `:enabled`, `:disabled`, `:checked`, `:unchecked`, `:indeterminate`.
Also supported: sibling combinators (`~`, `+`), attribute selectors (including their
operators and `i`/`s` flags), `:root`, `:scope`, `:empty`, first/last/only child and type
selectors, `:is()`, `:where()`, `:not()`, `:has()`, `:lang()`, `:dir()`, `:placeholder-shown`,
`:read-only`, `:read-write`, `:valid`, `:invalid`, `:required`, `:optional`,
`:open`, `:default`, and `:nth-child()` families including
`of <selector-list>`. Identifiers and strings accept CSS escapes. Namespace-qualified
selectors are supported; generated pseudo-elements remain pending.

The [Selectors 4 `:lang()` pseudo-class](https://www.w3.org/TR/selectors-4/#lang-pseudo)
accepts comma-separated CSS identifiers or strings and matches the effective
inherited XAML `Language` property on visual and document elements. Its language ranges
use [RFC 4647 extended filtering](https://www.rfc-editor.org/rfc/rfc4647.html#section-3.3.2),
including quoted or escaped wildcards; an empty string
matches only `XmlLanguage.Empty`. The default XAML language is `en-US`, and changes to
an ancestor's `Language` update descendant styles. IANA language-tag canonicalization
and extlang conversion use a checked-in static table generated from the
[IANA Language Subtag Registry](https://www.iana.org/assignments/language-subtag-registry/language-subtag-registry)
snapshot dated 2026-09-17. The regeneration script pins its SHA-256 and the
matcher does not fetch registry data at runtime.

The [Selectors 4 `:dir()` pseudo-class](https://www.w3.org/TR/selectors-4/#dir-pseudo)
accepts one CSS identifier. `:dir(ltr)` and `:dir(rtl)` follow native XAML
`FlowDirection` values and parent inheritance on visual and document elements;
the default direction is LTR. CSS `direction` affects layout but does not change
which `:dir()` selector matches. Other identifier arguments are valid selectors
but never match. Native direction changes update descendant styles even when a
CSS value masks the changed native property layer.

The [Selectors 4 `:placeholder-shown` pseudo-class](https://www.w3.org/TR/selectors-4/#placeholder-shown-pseudo)
tracks the placeholder actually displayed by `TextBox`, `PasswordBox`,
`AutoCompleteBox`, `NumberBox`, `DatePicker`, `TimePicker`, `ComboBox`, and
`TreeSelector`. Text, password, selection, search, and placeholder changes
update matching. Empty placeholder text does not match; date and time pickers
use their built-in placeholder when `PlaceholderText` is null.

The [Selectors 4 mutability pseudo-classes](https://www.w3.org/TR/selectors-4/#rw-pseudos)
map `:read-write` to enabled native text editors (`TextBoxBase` descendants,
`PasswordBox`, `EditControl`, `HexEditor`, `Terminal`), editable `ComboBox` and
`JsonTreeViewer`, and editable `DataGrid`/`DataGridCell`. Their effective
`IsReadOnly` or `IsEditable` properties and inherited enabled state update
matching. `:read-only` matches every other XAML element, including disabled
editors and non-editable display controls. Selection-only controls are not
treated as text editors.

The [Selectors 4 input-value pseudo-classes](https://www.w3.org/TR/selectors-4/#checked)
use native check or selection state. `:checked`, `:unchecked` and
`:indeterminate` are mutually exclusive. Toggle buttons, checkable menus,
task-list markers and tree checkboxes use `IsChecked`; selectable item controls
use `IsSelected`. `TreeSelectorItem` uses its checkbox state when both are
present. A `ProgressBar` matches only `:indeterminate` while its
`IsIndeterminate` value is true. Plain elements, non-checkable menus, and
ordinary list markers match none of these selectors.

The [Selectors 4 `:open` pseudo-class](https://www.w3.org/TR/selectors-4/#open-state)
follows a control's native boolean `IsOpen`, `IsDropDownOpen`,
`IsSubmenuOpen`, `IsPaneOpen`, or `IsExpanded` dependency property. This covers
popups, menus, pickers, expandable items, and panes. Changes to those properties
update matching; an open control still matches when its `Visibility` is hidden
or collapsed. Controls without a native open state never match.

The [Selectors 4 `:default` pseudo-class](https://www.w3.org/TR/selectors-4/#default-pseudo)
matches a native `Button` whose `IsDefault` property is true. It follows the
configured default action even when the button is disabled or cannot currently
receive the Enter key; the focus-dependent `IsDefaulted` state does not define
`:default`. XAML selection controls do not expose a persistent initial-default
option state.

The [Selectors 4 validity pseudo-classes](https://www.w3.org/TR/selectors-4/#validity-pseudos)
follow native `Validation.HasError` on value controls: text inputs, selection and
toggle controls, sliders, date/time and color pickers, data-grid containers, and
editable data views. These controls match `:valid` until native validation reports
an error, then match `:invalid`; binding validation and `Validation.MarkInvalid`/
`ClearInvalid` update styles immediately. Other elements match neither selector
unless native validation has attached errors to them. An element remains a
validation participant after its errors are cleared.

The [Selectors 4 optionality pseudo-classes](https://www.w3.org/TR/selectors-4/#opt-pseudos)
use `AutomationProperties.IsRequiredForForm` or a `RequiredValidationRule` on a
`Binding`, `MultiBinding`, or the active child of a `PriorityBinding` attached
to a native input control's value property,
including text, selection, toggle, date/time and color controls, sliders and
editable data-grid cells. Such an input matches exactly one of `:required` and
`:optional`, regardless of its enabled or validation state. Native metadata
changes update its own styles and selectors involving siblings or ancestors.
Display containers and data-grid rows match neither selector. Binding attachment,
replacement and removal update matching, including direct and generated CSS.
Adding, removing, replacing, or clearing rules in an attached binding's
`ValidationRules` collection invalidates those selectors for the next CSS
evaluation.

The [Selectors 4 range pseudo-classes](https://www.w3.org/TR/selectors-4/#range-pseudos)
match native range inputs and value bindings with a `RangeValidationRule` that
defines at least one limit. `NumberBox` compares its current edit text with
`Minimum` and `Maximum`; `Slider` and `RangeSlider` compare their current values;
`DatePicker` participates when `DisplayDateStart` or `DisplayDateEnd` is set.
Inputs without range limits match neither `:in-range` nor `:out-of-range`.
Non-numeric edit text does not count as underflow or overflow. Value and limit
changes, binding changes, validation-rule collection edits, and changes to a
`RangeValidationRule`'s limits refresh dependent selectors.

The [Selectors 4 presentation pseudo-classes](https://www.w3.org/TR/selectors-4/#modal-state)
map `:modal` to a `Window` opened with `ShowDialog()` and to a currently shown
`ContentDialog`. A `Window` with `WindowState.FullScreen` matches both `:fullscreen`
and `:modal`; these selectors apply to the window itself, while descendant
selectors can use it as an ancestor. Entering or leaving these states refreshes
direct and dependent styles. The existing `(display-mode: fullscreen)` media
query continues to describe the host window's presentation mode.

The [Selectors 4 media state pseudo-classes](https://www.w3.org/TR/selectors-4/#resource-pseudos)
map `MediaElement:playing`, `:paused`, `:buffering`, and `:muted` to the live
media control state. A buffering element also matches `:playing`; playback and
sound changes refresh selectors on the element and selectors that depend on it.

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
| `margin-inline`, `margin-block`, `padding-inline`, `padding-block` and logical side longhands | `Margin`/`Padding` (Thickness) | one or two values select start/end; logical and physical edges cascade together; inline-start/end swap left/right under the element's computed RTL direction, including inherited logical values |
| `inline-size`/`block-size` and `min-`/`max-` counterparts | `Width`/`Height` and their bounds | logical and physical declarations cascade together; inline maps to width and block maps to height in horizontal writing mode, including RTL; percentages and math use the CSS layout state |
| `background-color`, `color` | `Background`, `Foreground` (Brush) | colors wrap into `SolidColorBrush`; `#RRGGBBAA` uses CSS trailing-alpha semantics. Run/Span `background-color` paints TextBlock line fragments beneath selection and text, with outer Span backgrounds beneath nested backgrounds |
| `background: linear-gradient(…)` | `LinearGradientBrush` | CSS angles (0deg = up, clockwise) convert to Start/EndPoint |
| `background-color` + comma-separated `background-image` list | `Background` (layered Brush) | the first image paints above later images and the color; `none` occupies a list position; all visible layers share the native shape contour |
| `background-size: 50% auto, 24px 16px` | CSS bitmap placement | `auto`, `cover`, `contain`, lengths, percentages and math dimensions match image layers; one `auto` axis preserves the natural ratio; shorter lists cycle and excess values are ignored |
| `background-position: right 10px bottom 5px, center` | CSS bitmap placement | one- to four-value positions match image layers; percentages and math resolve against the area remaining after image sizing; shorter lists cycle, including across `none` layers |
| `background-repeat: space no-repeat, round` | CSS bitmap tiling | repeat modes match image layers; `repeat` is the default; `repeat-x`, `repeat-y`, `no-repeat`, `space`, `round` and paired axis keywords are supported; `space` distributes whole tiles and `round` adjusts tile size |
| `background-origin: content-box, padding-box` | CSS bitmap placement on supported background owners | each image uses its selected border, padding or content box; the default is `padding-box`, and shorter lists cycle |
| `background-clip: padding-box, content-box` | CSS layer clipping on supported background owners | each image clips to its selected box; `background-color` uses the bottom-most layer's clip; the default is `border-box` |
| `object-fit: cover; object-position: right 10px bottom 5px` | XAML `Image` source and `MediaElement` video content | `fill`, `contain`, `cover`, `none` and `scale-down` size the image or video inside its content box; one- to four-value positions, percentages and math place it in the remaining space; CSS padding and clipping apply; with `transition-behavior:allow-discrete`, fit modes switch the rendered size at 50% eased progress; local native `Stretch` keeps priority |
| `image-rendering: pixelated` | inherited bitmap sampling on `Image`, `MediaElement` bitmap frames, CSS background images and native local `ImageBrush` backgrounds | `auto`, `smooth`, `high-quality`, `crisp-edges` and `pixelated` are supported; `optimizeSpeed` and `optimizeQuality` are accepted as legacy aliases; `pixelated` uses nearest-neighbor scaling to the closest integer multiple followed by smooth scaling; with `transition-behavior:allow-discrete`, inherited sampling modes switch at 50% eased progress; explicit native scaling modes on image/video controls and brushes keep priority |
| `background-image: conic-gradient(…)` | used-size `ImageBrush` | angular/percentage stops, optional center/rotation and repeating form; raster capped at 2048 pixels per axis |
| `border: 1px solid red` and physical/logical side shorthands | native border properties where present; CSS used border widths and side paint on built-in panels, `TextBlock`, template-bound `Border` and supported direct controls | omitted components reset to medium width, `none` style and `currentColor`; `border-inline`/`border-block` set both logical sides; `none`/`hidden` zero only their sides |
| `border-inline-width`, `border-block-width` and their side longhands | native `BorderThickness` or CSS used border widths on panels and `TextBlock` | one or two values set start/end; logical and physical widths cascade together, with RTL swapping inline sides; an omitted style defaults to `none` and makes the used width zero |
| `border-color`, four physical side colors, `border-inline-color`/`border-block-color` and logical side colors | `BorderBrush` where present; CSS side paint on `Border`, template-bound `Border`, built-in panels, `TextBlock` and supported direct controls | one to four colors expand in top/right/bottom/left order; logical colors share the physical cascade in LTR/RTL; distinct solid sides with equal or different alpha paint square and rounded corner joins in one pass; a native local `BorderBrush` wins |
| `border-style`, four physical side styles, `border-inline-style`/`border-block-style` and logical side styles | CSS side style and used border widths on `Border`, template-bound `Border`, built-in panels, `TextBlock` and supported direct controls | all ten line styles paint per side, including rounded corners; omitted widths on visible sides use the CSS initial `medium` (3 px); `none`/`hidden` sides use zero width; `groove`/`ridge`/`inset`/`outset` use light and dark shades derived from each side's color; translucent solid-color combinations of `solid`/`dashed`/`dotted`/`double`/relief composite shared corner joins once |
| `border-radius` | `CornerRadius` where available and a CSS contour on visual elements | horizontal/vertical percentages, slash syntax, paired longhands, overlap reduction; Border, Panel and TextBlock shape their backgrounds; native local values still win |
| `border-start-start-radius`, `border-start-end-radius`, `border-end-start-radius`, `border-end-end-radius` | `CornerRadius` and CSS contour | logical corners compete with physical corners; RTL swaps inline sides while preserving ellipse axes and inherited percentages |
| `overflow`, `overflow-x`, `overflow-y`, `overflow-inline`, `overflow-block` | `ClipToBounds` and `ClipToBoundsEdges` | independent clipping axes, two-value shorthand and CSS cross-axis computation; logical axes follow horizontal writing mode, including RTL; `auto`/`scroll` clip without creating a scroll container |
| `overflow-clip-margin: content-box 4px` | CSS overflow clip geometry | nonnegative lengths and content/padding/border boxes; adjusts rounded clip corners and pointer clipping; applies only to a computed `clip` axis |
| `scrollbar-width: auto` / `thin` / `none` | `ScrollViewer` and controls with a template-hosted `ScrollViewer` (including `ListBox`) | non-inherited; a template-hosted viewer uses its owner's value unless it has its own declaration; a root `Window` CSS declaration styles direct `ScrollViewer` content or the designated template viewer of direct `ListBox`, `DataGrid`, `TreeDataGrid`, `TreeView`, `Markdown`, or `Terminal` content as its viewport. The grids' header viewers keep their own value. `thin` reduces desktop bar strips from 12 to 10 DIPs, while the already compact overlay indicator keeps its platform size; `none` hides both bars without changing scrollability; `transition-behavior:allow-discrete` switches width keywords at 50% eased progress; `unset`/`initial` use `auto`, and removing a declaration restores the applicable owner or default value. Other viewport arrangements and scroll containers without `ScrollViewer` are pending |
| `scrollbar-gutter: auto` / `stable` / `stable both-edges` | `ScrollViewer` and template-hosted viewers | non-inherited; classic `auto` bars consume space when shown, including when one bar causes overflow on the other axis. An authored `stable` value reserves vertical inline space even when hidden or unnecessary, while `both-edges` mirrors it; a visible horizontal bar consumes block-end space. Root `Window` CSS declarations apply to direct `ScrollViewer` content or the designated template viewer of direct `ListBox`, `DataGrid`, `TreeDataGrid`, `TreeView`, `Markdown`, or `Terminal` content, while grid headers and nested viewers retain their own value. RTL and `scrollbar-width:thin` adjust the gutter. The content viewport, paint clip and pointer hits exclude the reserved area. Overlay bars and `scrollbar-width:none` do not reserve space. With `transition-behavior:allow-discrete`, gutter modes switch and reflow at 50% eased progress. Other viewport arrangements and scroll containers remain pending |
| `scrollbar-color: auto` / `<thumb-color> <track-color>` | `ScrollViewer` scroll bar brushes, including template-hosted viewers | inherited; the first color paints the thumb and the second paints the track on both axes, including `currentColor` and contextual color functions; root `Window` CSS declarations style direct `ScrollViewer` content or the designated template viewer of direct `ListBox`, `DataGrid`, `TreeDataGrid`, `TreeView`, `Markdown`, or `Terminal` content and update with the root color. Two-color transitions animate thumb and track together, using sRGB for legacy color pairs and Oklab otherwise with premultiplied alpha. With `transition-behavior:allow-discrete`, a color pair switches to or from `auto` at 50% eased progress. The themed thumb keeps the authored color while hovered, dragged or disabled; `unset` inherits, `initial`/`auto` restore the theme, and explicit native local brushes and templates keep priority. Other viewport arrangements and scroll containers remain pending |
| `clip-path: inset(10% round 8px)`, `xywh()`, `rect()`, `circle()`, `ellipse()`, `polygon()`, `path()`, `shape()`, geometry boxes, `url('masks.svg#clip')` | CSS contour clip | clips own paint, effects, border, outline and descendants; intersects native `Clip` and overflow; shapes use border/padding/content/margin reference boxes. `polygon()` supports percentages, winding rules and rounded vertices; `path()` accepts quoted SVG path data with fixed CSS-pixel coordinates, optional fill rule and implicit closure. `shape()` accepts CSS positions, relative commands, lines, quadratic/cubic curves, smooth curves, arcs, math lengths and optional fill rule; percentages track the reference box when its size changes. External URL references resolve against the owning stylesheet and use its resource resolver; the default resolver accepts file, HTTP(S) and percent/base64 `data:` sources. SVG/SVGZ `<clipPath>` supports basic shapes, text glyph outlines, nested `<tspan>` x/y/dx/dy/rotate lists, transforms, font/visibility overrides, horizontal `textLength` adjustment and `<textPath>` positioning with local or inline paths, groups, local `<use>`, `clip-rule`, `userSpaceOnUse` and `objectBoundingBox`; textPath stretch/automatic spacing, vertical/bidirectional path text, full bidi/wrapped `textLength` behavior and Apple text outlines remain pending. SVG images also resolve local `url(#id)` on shapes, groups and `<use>`, with nested clip intersections. These clips paint and hit test on managed and native software/D3D12/Vulkan. Ordinary XAML trees have no SVG definition nodes for local `url(#id)` |
| `pointer-events: none` / `auto` | CSS hit-test filter | inherited targeting rule; an `auto` descendant can receive input through a `none` ancestor; native local `IsHitTestVisible` still gates or enables the native subtree; with `transition-behavior:allow-discrete`, the hit target switches at 50% eased progress |
| `user-select: auto` / `text` / `none` / `contain` / `all` | TextBlock and direct/template Label text selection | non-inherited computed keyword; `auto` takes `all` or `none` from the parent's used value and otherwise uses `text`. Authored CSS enables native mouse selection in TextBlock/Label; `none` blocks new selection without clearing an existing range, `all` selects the complete text owner with one click, and `contain` uses the owner's native bounded range. Local native `IsTextSelectionEnabled` wins; without a CSS declaration, native selection defaults remain. `allow-discrete` changes selection at 50% eased progress. Cross-owner ranges, document inlines and editable controls remain open |
| `box-shadow: 3px 4px 5px …` | `DropShadowEffect` | omitted/direct/mixed `currentColor` follows the element's foreground; offsets, blur and spread accept supported font/viewport/container/math lengths and update with their context; outer and inset shadows retain independent X/Y corners; positive/negative spread changes the perimeter; an outer shadow is clipped out of the original border box even when its background is transparent; multiple shadows → `EffectGroup`, with outer shadows below the background, inset shadows above it, and the first authored shadow on top within each layer; for pure CSS shadows on `Border` and standard background-only panels, children paint above the inset |
| `backdrop-filter: blur(4px)`, `grayscale(1)` or `opacity(50%)` | `UIElement.BackdropEffect` | `none` clears the CSS value; one blur, brightness, contrast, saturate, grayscale, sepia, invert, hue-rotate or opacity function paints behind a visual element before its background and content. Blur accepts supported relative/math lengths; color amounts accept numbers or percentages. Ordered lists are pending |
| `cursor: pointer` | `Cursors.Hand` | full keyword table (`not-allowed` → `No`, `move` → `SizeAll`, …); keyword changes switch at 50% eased progress with `transition-behavior:allow-discrete` |
| `caret-color: red` | insertion caret paint in TextBox, AutoCompleteBox, NumberBox, RichTextBox, PasswordBox, EditControl and HexEditor | inherited `auto` or CSS color; `currentColor` and color mixes update with the foreground; `auto` uses the native caret choice, and a local native `CaretBrush` wins |
| `caret-animation: manual` | native insertion-caret animation in the same editors | inherited `manual` keeps the caret visible and stops native blinking or fading; `auto` restores the native behavior |
| `caret-shape: block` | insertion caret geometry in the same editors | inherited `auto`, `bar`, `block` or `underscore`; block and underscore span the next grapheme or an approximate `1ch` at line end; `auto` retains each native editor's shape |
| `caret: red manual block` | the three caret longhands | accepts color, animation and shape in any order; omitted parts reset to `auto` |
| `display: none` / `visibility: hidden` | native `Visibility.Collapsed` for display; CSS box visibility for hidden/collapse | Display and visibility combine independently; hidden boxes retain layout and allow explicitly visible descendants; native local Visibility still takes precedence; block/flow-root/flex/grid select layout on existing panels. With `transition-behavior:allow-discrete`, CSS `visibility` switches paint and input at 50% eased progress while retaining layout. `display` changes between non-`none` modes switch layout at 50% eased progress; transitions to `none` preserve the box and paint until the endpoint while blocking new input immediately and queuing focus revalidation, and transitions from `none` render from the start. Native local Visibility keeps priority |
| `transform: translate(…) rotate(…)`, `scale(150%)` | `RenderTransform` (TransformGroup) | does not affect layout; 2D scale percentages map to numeric factors, while nonzero translate lengths and rotate/skew angles require units |
| `transform-origin: 25px 50%`, `calc(50% + 10px) top` | `RenderTransformOrigin` | planar lengths and percentages use the selected `transform-box`; font, size and content-inset changes recompute the pivot. The Z coordinate remains pending |
| `transform-box: content-box`, `fill-box`, `border-box`, `stroke-box`, `view-box` | CSS transform reference rectangle | on CSS layout boxes, `fill-box` uses content-box and `stroke-box`/`view-box` use border-box; the rectangle controls percentage translation, transform keyframes and planar origin. SVG reference geometry without a CSS layout box remains pending; transform-box keyframes and allow-discrete transitions update dependent percentage translations and planar origins at the sampled midpoint |
| `transition` and `@starting-style` | per-property CSS transition data plus native configuration mirrors | Individual durations, positive/negative delays, exact CSS Bézier and steps curves; `transition-behavior:normal | allow-discrete` accepts comma-separated lists, and the `transition` shorthand resets omitted behavior to `normal`. Supported discrete scrollbar, visibility, pointer-events, cursor, image-rendering, object-fit and transform-box values switch at 50% eased progress; `display:none` uses entry/exit endpoints while other display modes switch at 50%. `transition:initial` and other CSS-wide resets include behavior. A winning `@starting-style` rule supplies the starting value when an unrendered element first receives CSS or returns from CSS `display:none`; top-level/nested rules, conditions, layers, imports, generated CSS and custom properties share ordinary cascade precedence. Initial changes without a winning starting value are immediate. Native local target values remain authoritative; native attach/detach lifecycle parity and complete platform coverage remain open |
| `inset`, `inset-inline`, `inset-block` and physical/logical inset longhands | absolute-position layout state; static pixel values use `Canvas.Left/Top/Right/Bottom` | physical and logical edges cascade together under computed LTR/RTL direction; percentages and math resolve against the containing block; `auto` clears an edge; native local Canvas offsets take priority |
| `z-index`, `grid-row`/`grid-column`, `gap` | `Panel.ZIndex`, `Grid.Row/Column(+Span)`, per-panel spacing | grid lines convert from 1-based to 0-based |
| `font`, `font-size: 1.2em`, `line-height: 1.5` | font properties | `em`/`%` on font-size use the inherited size; unitless line-height multiplies the element's font size. `font-weight:bolder/lighter` in the longhand or shorthand computes from the parent's winning weight and updates with it. TextBlock measures and draws Run/Span font family, size, weight, style and stretch; mixed sizes affect wrapping, line height, baseline, selection and hit testing. Shaping across Run boundaries remains limited |
| `font-stretch: 82%`, `font-width: condensed` | inherited `FontStretch` and retained CSS width percentage | keywords map to the CSS percentage table; nonnegative percentage math is retained through inheritance and queries. The `font` shorthand accepts width keywords. Native rendering uses the nearest of nine width classes; continuous variation-axis rendering remains open |
| `font-style: oblique 25deg` | inherited `FontStyle` and retained CSS oblique angle | degrees, radians, gradians, turns and angle math resolve to degrees; `font` shorthand, inheritance and style queries preserve the explicit angle. Native face selection still uses its default oblique slant; directional `left`/`right` values remain open |
| `font-family: Arial, sans-serif` | inherited `FontFamily` and retained ordered CSS family list | quoted names remain distinct from generic keywords; common generics map to native fonts after computation. Inheritance, `font` shorthand and style queries preserve list order and item type. Quoted names containing commas reach `@font-face` loading and font-relative metrics. Installed comma-bearing family fallback and script-specific generics remain open |
| `font-size: math`, `math-depth: add(1)`, `math-style: compact` | inherited font size and CSS math depth/style properties | Per [MathML Core](https://www.w3.org/TR/mathml-core/#the-math-depth-property), `auto-add` increments depth when the inherited math style is compact; `add(<integer>)` adjusts inherited depth. A math font size uses the selected inherited font's [OpenType MATH constants](https://learn.microsoft.com/en-us/typography/opentype/spec/math) for script levels, with 0.71 scaling for other depth steps or fonts without MATH. Negative steps invert the factor. Apple/Metal table access remains pending |
| `text-align: justify` | `TextBlock.TextAlignment` and justified line placement | wrapped lines distribute free width across interior word gaps and permitted CJK grapheme boundaries; the last line of a paragraph and explicit final lines use `text-align-last`. Run fonts/colors, selection, hit testing and decoration endpoints follow the adjusted positions; complex-script justification remains open |
| `text-justify: auto` / `none` / `inter-word` / `inter-character` / `distribute` | inherited TextBlock justification method | `none` suppresses expansion; `inter-word` uses word separators; `inter-character` and its `distribute` alias use safe grapheme boundaries. `auto` combines word and CJK opportunities. Lines without opportunities follow `text-align-last`, with `justify` falling back to center. Selection, hit testing and decorations share the drawn positions. Full language-sensitive shaping and justification in other text controls remain open |
| `text-indent: <length-percentage> [hanging] [each-line]` | inherited TextBlock and ordinary atomic-inline line-box indentation | positive/negative lengths, percentages and supported math lengths; optional keywords can appear in either order. The first formatted line is indented by default, `each-line` also selects lines after forced breaks, and `hanging` reverses the selection. Indentation changes wrapping, alignment, selection and hit testing in TextBlock, and ordinary flow places inline blocks beside floats using the block container's own content width for percentages. TextBox, RichTextBox and splittable inline fragments remain open |
| `overflow-wrap: normal` / `anywhere` / `break-word`; `word-wrap` alias | inherited TextBlock line-break policy | `normal` preserves unbreakable sequences as overflowing lines; `anywhere` and `break-word` add emergency breaks only when no standard break fits, without splitting extended grapheme clusters. `anywhere` reduces intrinsic min-content width; `break-word` does not. An authored or inherited value uses CSS `white-space: normal` wrapping when no native local wrapping or CSS `white-space` rule overrides it. Native local `TextWrapping` retains priority. Complex-script shaping across an emergency break and other text controls remain open |
| `word-break: normal` / `keep-all` / `break-all` / `break-word` | inherited TextBlock line-break policy | `keep-all` suppresses implicit breaks between CJK and other letter units while retaining spaces and explicit separators; with `overflow-wrap: normal`, it may relax that restriction at used width when no ordinary break fits, but keeps an unbroken intrinsic width. `break-all` adds breaks between letter/digit grapheme units, and deprecated `break-word` behaves like `normal` plus `overflow-wrap: anywhere`. Full Unicode line-break classes, language-tailored breaking and shaping across split words remain open |
| `line-break: auto` / `loose` / `normal` / `strict` / `anywhere` | inherited TextBlock line-break strictness | `auto` keeps the existing default break policy; `loose` permits selected small-kana, iteration-mark, hyphen, inseparable and centered-punctuation breaks using Unicode 18 ID/CJ/IN data, and Chinese/Japanese PO/PR wide punctuation breaks. `normal` and `strict` restrict them, with Chinese/Japanese writing-system exceptions for U+301C/U+30A0. `anywhere` takes the last fitting extended grapheme without preferring spaces or obeying punctuation, word-joiner or `word-break` restrictions, and reduces min-content width. Language tags and script subtags select the CJK exceptions; CSS `white-space: nowrap` and native local `TextWrapping.NoWrap` still prevent wrapping. The complete Unicode line-break algorithm, complex-script shaping and other text controls remain open |
| `white-space` shorthand | TextBlock whitespace processing and native wrapping mirror | legacy `normal`/`nowrap`/`pre`/`pre-wrap`/`pre-line` keywords and combinations of `white-space-collapse`, `text-wrap-mode` and `white-space-trim` expand into independently cascading longhands; omitted components reset to `collapse`, `wrap` and `none`. `break-spaces` preserves spaces with a break after each. Explicit inline `LineBreak` stays forced. Native local wrapping retains priority |
| `white-space-collapse: collapse` / `discard` / `preserve` / `preserve-breaks` / `preserve-spaces` / `break-spaces` | inherited TextBlock source whitespace processing | `collapse` combines CSS spaces, tabs and segment breaks; `discard` removes them; `preserve` keeps spaces and breaks; `preserve-breaks` keeps breaks but collapses adjacent spaces; `preserve-spaces` converts tabs and segment breaks to spaces, including one space for CRLF; `break-spaces` adds a wrap opportunity after each preserved space. Measurement, drawing, selection and hit testing retain source offsets. Per-Run overrides, CJK segment-break tailoring and whitespace processing in other text controls remain open |
| `tab-size: <number [0,∞]> \| <length [0,∞]>` | inherited TextBlock tab stops with Run/Span overrides | initial value `8`; only tabs preserved by `white-space-collapse: preserve` or `break-spaces` advance to the next stop. Numeric sizes use the TextBlock space advance plus its word and letter spacing; lengths resolve to pixels, and stops closer than half a `0` glyph width skip to the following stop. Wrapping, text indentation, drawing, selection and hit testing share the measured advance. Centered/right-aligned line painting and interaction keep stops relative to the block content edge, including padding. Exact aligned width choice/wrapping, transformed paint-wrap, bidi/vertical text and other text controls remain open |
| `hyphens: none` / `manual` / `auto` | inherited TextBlock conditional hyphenation with Run/Span overrides | initial value `manual`; U+00AD stays invisible and retains its source offset when no break occurs. `manual` and `auto` allow a break at U+00AD, counting the inserted string in line width, then draw it with the source Run's typography and color. `none` suppresses that conditional break, while visible hyphens remain ordinary break points. `auto` currently uses explicit U+00AD opportunities because no language-specific hyphenation resource is bundled. Automatic language-specific glyph choice, shaping across the break, other text controls and full WPT pixels remain open |
| `hyphenate-character: auto` / `<string>` | inherited TextBlock hyphenation marker with Run/Span overrides | custom CSS strings, including multiple characters and the empty string, replace the marker drawn at a soft-hyphen break; CSS escapes decode before use. Marker width participates in break selection, line measurement, drawing and justification. `auto` currently uses `-` as a fallback for supported horizontal text. Language-specific placement and automatic marker choice remain open |
| `hyphenate-limit-chars: [auto \| <integer [0,∞]>]{1,3}` | inherited TextBlock minimum word/before/after counts with Run/Span overrides | missing second value uses `auto`, and a missing third repeats the second; `auto` currently resolves to 5 characters for the word and 2 on each side. U+00AD breaks are suppressed when any limit fails, excluding nonspacing marks and intra-word punctuation from counts. The current word scan covers letter/digit runs with combining marks and common intra-word punctuation; full Unicode word segmentation and automatic dictionary hyphenation remain open |
| `hyphenate-limit-lines: no-limit` / `<integer [0,∞]>` | inherited TextBlock consecutive hyphenated line limit | initial value `no-limit`; a nonnegative integer caps successive lines ending at an accepted U+00AD break. An intervening line without a soft-hyphen break resets the count. `0` suppresses these breaks; an unbreakable word may overflow when the limit is reached. Emergency wrapping can still split the word without a hyphen marker. Other emergency-breaking interactions and non-TextBlock containers remain open |
| `hyphenate-limit-zone: <length-percentage>` | inherited TextBlock unfilled-line threshold | initial value `0`; a soft-hyphen break is deferred when a preceding ordinary break would leave no more than this amount of unused line width. Percentages use the line box width before text indentation. If no ordinary break is available, the soft hyphen can still prevent word overflow. Negative used thresholds behave as `0`. Automatic dictionary hyphenation and non-TextBlock containers remain open |
| `hyphenate-limit-last: none` / `always` / `column` / `page` / `spread` | inherited TextBlock end-of-element hyphenation guard | initial value `none`; `always` avoids a soft-hyphen break on the last full line before the element's final line, including after reflow and independently of earlier forced-break groups. `column`, `page`, and `spread` parse and cascade, but have no effect until corresponding fragmentation boundaries exist. Automatic dictionary hyphenation and non-TextBlock containers remain open |
| `white-space-trim: none` / `discard-before` / `discard-after` / `discard-inner` | non-inherited TextBlock and Run/Span whitespace trimming | `discard-inner` on TextBlock removes outer source whitespace through its first/last segment break while keeping interior indentation; on Run/Span it removes whitespace inside the inline edges. `discard-before` and `discard-after` on Run/Span remove adjacent whitespace outside those edges; the three flags can combine. Source text, selection offsets and explicit inline `LineBreak` remain intact. Outside-edge trimming of an atomic TextBlock and whitespace trimming in other text controls remain open |
| `text-wrap-mode: wrap` / `nowrap` | inherited soft-wrap control | cascades independently of `white-space-collapse` on TextBlock and mirrors native `TextWrapping` where available; explicit native local `TextWrapping` wins |
| `text-wrap-style: auto` / `balance` / `stable` / `pretty` / `avoid-short-last-line` | inherited TextBlock line-choice policy | `auto` uses greedy breaks; `stable` keeps those same breaks for static TextBlock content; `balance` probes narrower break widths to reduce variation in occupied line widths while retaining the greedy line count, independently within forced-break groups, and falls back to `auto` above ten lines. `pretty` also penalizes short final lines; `avoid-short-last-line` changes breaks only when its final line is short and preceding lines remain usable. Multiline search is bounded to 24 lines and 4096 source code units per forced-break group; beyond that, line choice falls back to `auto`. Native local no-wrap suppresses rebalancing. Other text controls, floats and full bidi/writing-mode metrics remain open |
| `text-wrap: wrap balance` | shorthand for `text-wrap-mode` and `text-wrap-style` | accepts either order, and resets an omitted mode to `wrap` or style to `auto`; it leaves `white-space-collapse` and `white-space-trim` unchanged |
| `word-spacing: normal` / `<length-percentage>` | inherited TextBlock word-separator spacing | positive, negative and calculated lengths and percentages adjust recognized word separators across Run/Span content; percentages use each TextBlock or Run's used font size while absolute length components remain computed before inheritance. Measurement, wrapping, drawing, selection, justification, numeric tab stops and ellipsis use the adjusted advance. Language-specific separators and other text controls remain open |
| `word-space-transform: none` / `space` / `ideographic-space`, optionally with `auto-phrase` | inherited TextBlock and AccessText expansion of U+200B separators | transforms source zero-width spaces to ordinary or ideographic spaces after whitespace collapsing and before `text-transform`, preserving source selection/copy offsets. Run/Span overrides, wrapping, word spacing and `full-width` transformation use the displayed separator. A separator immediately beside a forced break remains zero-width. `auto-phrase` parses and cascades, but no automatic phrase boundaries are added because this renderer has no language-specific phrase detector; XAML has no `<wbr>` counterpart. Other text controls remain open |
| `letter-spacing: normal` / `<length>` | inherited TextBlock tracking | positive, negative and calculated lengths place gaps between visible grapheme units, including units introduced by text transformation inside one source character. Run/Span values average across boundaries; width, wrapping, drawing, selection, hit testing and ellipsis share the spacing. Bidi placement, complex-script shaping and other text controls remain open |
| `text-autospace: normal` / `auto` / `no-autospace` / `ideograph-alpha` / `ideograph-numeric` / `punctuation`, optionally with `insert` or `replace` | inherited TextBlock interscript and French punctuation spacing | `normal` and `auto` insert one eighth of the containing font's ideographic advance between directly adjacent CJK ideographs and non-wide letters or non-fullwidth decimal digits, using Unicode 18 Han Script_Extensions and East_Asian_Width classes. The two ideograph keywords select either class; `no-autospace` suppresses both. In French content, `punctuation` inserts U+202F before `;!?` and U+00A0 before `:` and `»` and after `«`; existing Unicode space separators suppress insertion. `replace` substitutes a qualifying source U+0020 with the chosen spacing, including after CSS whitespace collapse, while selection and copy keep the source space. The advance combines with word and letter spacing and vanishes at wrapped line edges. Run/Span common ancestors control inline boundaries. Bidi-after-reordering placement, regional French typography differences and other text controls remain open |
| `hanging-punctuation: none` / `first` / `last` / `force-end` / `allow-end` | inherited TextBlock line-edge glyph placement | the flags combine except that `force-end` and `allow-end` are mutually exclusive. In horizontal text, opening punctuation on the first formatted line and closing punctuation on the last can hang, including at a Run text boundary with an inherited Span value on an earlier or later line of the TextBlock. `force-end` hangs a stop or comma at any line end; `allow-end` hangs only its overflowing portion. The specified Unicode stop/comma set is recognized. Hanging advance is excluded from fitting, applicable intrinsic size, alignment and justification while drawing, selection, hit testing and decoration retain the glyph. Bidi visual-edge placement, full inline box geometry and border/padding inhibition, vertical text, other text controls and full WPT/backend pixels remain open |
| `text-transform: none` / `[capitalize \| uppercase \| lowercase] \|\| full-width \|\| full-size-kana` | inherited TextBlock and AccessText display-text transformation, including Run overrides | Unicode 18 unconditional full case mappings cover expansions such as `ß` to `SS`; Unicode context properties handle Lithuanian dot insertion/removal, Turkish/Azeri Latin I, and Greek final sigma across intervening marks and Run boundaries. Unicode 18 wide/narrow mappings and CSS Text 3 small-kana mappings apply in spec order. Full-width changes only preserved U+0020 spaces. Text, selection and copied source remain unchanged; transformed characters affect measurement, wrapping, hit testing, tracking and ellipsis. `line-break:anywhere`, emergency wrapping and `word-break:break-all` can break between painted graphemes introduced by one source character while preserving source selection. Word and character justification on split or unwrapped expansions keeps painting, selection and decorations aligned. AccessText mnemonic underlines follow transformed and wrapped paint while preserving the source access key. CLDR-specific casing, language-sensitive shaping and justification, and text transformation in TextBox, RichTextBox and other controls remain open |
| `text-align: start` / `end` | logical text placement and native `TextAlignment` where authored | resolves against the element's computed `direction`, including same-rule declarations, inherited alignment across opposite directions and later direction changes. Direct TextBlock, TextBox, AccessText and TextEffectPresenter drawing follows the logical side; explicit native local alignment retains precedence |
| `text-align-all`, `text-align-last`, `text-align: match-parent` / `justify-all` | symbolic line alignment plus the native `TextAlignment` mirror for all lines | the shorthand resets last-line alignment to `auto`, except `justify-all` and `match-parent`; `auto` leaves the final line at logical start when all lines are justified. TextBlock positions wrapped and forced final lines, and can justify their word/CJK gaps; ordinary flow positions its last atomic-inline line; TextBox positions unwrapped logical lines. `match-parent` resolves a parent's logical side using the parent's direction; native local alignment retains precedence. Per-row last-line alignment in wrapped TextBox content and justification of atomic inline controls remain open |
| `text-group-align: none` / `start` / `end` / `left` / `right` / `center` | non-inherited line-box group alignment | TextBlock aligns its own wrapped or forced lines within a shared content area before applying each line's `text-align`; justification, text indentation, selection and hit testing use the same area. CSS flow containers group atomic inline line boxes across separate runs and descendant block panels in the same block formatting context, including float exclusion bands; independent formatting contexts are skipped, and `text-align-last` selects each run's actual final line. Vertical writing and other text controls remain open |
| `line-padding: <length>` | inherited line-start and line-end spacing | TextBlock reserves the resolved lengths at both edges of each line for wrapping, alignment, tabs, justification, trimming, selection and hit testing. The innermost Run at each line edge supplies its value, including values inherited from Span; breaks across Run boundaries and transformed paint use the edge-specific lengths. Run/Span background colors extend across their line-edge space, including wrapped and ellipsized lines. Ordinary flow reserves the same edges around atomic inline runs, including lines beside floats. Percentages are invalid; `0`, relative and calculated lengths are supported. Inline background images, borders, full bidi inline painting, vertical writing and other text controls remain open |
| `text-overflow: ellipsis` / `clip` | `TextBlock.TextTrimming` | with `white-space: nowrap` and a constrained width, TextBlock draws a grapheme-safe ellipsis or clips the original line; Run color and typography style the ellipsis, and the source text stays intact. `TextTrimming.WordEllipsis` also trims at word boundaries |
| `text-decoration` and `text-decoration-line`/`-style`/`-color`/`-thickness` | native `TextDecorations` compatibility value plus CSS line paint | underline, overline and line-through paint solid, double, dotted, dashed or wavy lines on TextBlock, TextBox, AccessText and RichTextBox text; exclusive spelling and grammar error lines use red and green wavy markers with user-agent styling; `from-font` thickness uses the selected font's OpenType `post` value when available and otherwise uses automatic thickness; Run/Span/Paragraph sources cover their child text across runs and wrapped lines where supported; native local `TextDecorations` wins on the same node. TextBlock paints inline foreground colors within their text ranges |
| `text-underline-offset: auto` / `<length>` / `<percentage>` | inherited CSS decoration offset | signed offsets move CSS underlines from their zero position (the alphabetic baseline by default); percentages track the decorating element's font size after inheritance. The `text-decoration` shorthand leaves this value intact. Native local `TextDecorations` retain their own placement |
| `text-underline-position: auto` / `from-font` / `under` / `left` / `right` | inherited CSS underline origin | `under` positions the underline below the decorated text and changes the zero point for `text-underline-offset`; `left` and `right` behave as `auto` in horizontal text, including when combined with `under`. `from-font` uses the selected font's OpenType `post` underline position when available and otherwise uses `auto`. The `text-decoration` shorthand does not reset this property |
| `text-decoration-skip-inset: none` / `auto` | inherited CSS decoration endpoint inset | `auto` moves each CSS line-decoration endpoint inward by half the stroke thickness, capped to a quarter of short spans; `none` keeps full endpoints. This applies to direct and inline text targets in horizontal writing. The `text-decoration` shorthand does not reset this property; native local `TextDecorations` retain their own endpoints |
| `text-decoration-inset: auto` / `<length-percentage>{1,2}` | non-inherited CSS decoration endpoint adjustment | Signed values trim or extend the logical start and end of CSS underline, overline and line-through strokes; one value applies to both ends. With default `box-decoration-break: slice`, percentages use the decorated text's total inline size across lines, the adjustment starts at the outer fragments, and a positive inset that exceeds one fragment carries into subsequent fragments. With `clone`, each fragment gets its own start and end adjustments and percentage basis. `auto` chooses a stroke-width gap at the applicable endpoints. TextBlock, TextBox, AccessText, RichTextBox and their supported Run/Span/Paragraph decoration sources paint these endpoints in horizontal writing; `text-decoration` leaves the value intact, and native local `TextDecorations` keep their own endpoints. Vertical writing and author control of spelling/grammar marker endpoints remain open |
| `box-decoration-break: slice` / `clone` | non-inherited text decoration fragment mode | selects joined or independent CSS line-decoration endpoints for wrapped TextBlock, TextBox, AccessText and RichTextBox content, including supported Run/Span/Paragraph sources; `all: initial` restores `slice`. Solid Run/Span backgrounds paint TextBlock line fragments, but this property does not yet change their painting. Inline background images, borders, padding, margins, masks, box shadows and paged/multicolumn boxes remain open |
| `text-shadow: 2px 3px 4px currentColor` | inherited text and decoration shadow | Comma-separated outer shadows paint from the glyph and decoration alpha in author order (first on top) on TextBlock, TextBox, AccessText, and RichTextBox through software, D3D12, and Vulkan rendering, including recorded drawing. Text and decoration composite once above all shadows. Run and Span overrides in TextBlock and RichTextBox are painted per inline fragment, including `none` suppression; adjacent text stays above the shadows. The optional color defaults to `currentColor`; Level 4 spread and `inset` remain unsupported |
| `outline: 2px solid red` (+`outline-offset`) | `OutlineBrush/OutlineThickness/OutlineStyle/OutlineOffset` | self-drawn ring outside the bounds, takes no layout space; shorthand and longhand widths accept relative lengths and math, and standalone longhands fill omitted CSS initial values; `solid`/`dashed`/`dotted`/`double`/`groove`/`ridge`/`inset`/`outset` paint; `auto` style uses a medium solid ring and ignores authored width; `auto` color uses the accent brush with auto style and currentColor otherwise; `double` divides the width into two strokes and a gap; relief styles shade each side and groove/ridge divide its width in half; large negative offsets clamp the outside shape to at least twice the outline width; `outline: none` hides the ring while resetting omitted components to their initial values; pairs naturally with `:focus-visible` |
| `is-tab-stop: true`, `horizontal-alignment: Center`, … | any dependency property | the kebab-case fallback channel; values go through the XAML converter stack |

For RichTextBox, `text-shadow` on a Paragraph or FlowDocument also overrides the
control's inherited shadow. Explicit `none` suppresses it, and clearing that
declaration restores inheritance.

Lengths support `px` (default), `pt`, `in`, `cm`, `mm`, `q`, `pc`, `em`, `rem`; colors
support hex, all CSS named colors, `rgb()`/`rgba()`/`hsl()`/`hsla()` in legacy and modern
syntax, and `transparent`.

Math functions preserve percentage terms until layout. `vw`,
`vh`, `vmin`, and `vmax` use the visual root's viewport. Translation functions also
resolve percentages against the transformed element and support font-relative lengths.
`currentColor` is supported by background, border, outline and shadow color mappings.
Absolute CSS Color 4 `hwb()`, `lab()`, `lch()`, `oklab()`, `oklch()` and the predefined
`color()` spaces are accepted by these mappings and by custom CSS color converters.
The current rendering surfaces are sRGB. Standalone wide-gamut colors are mapped
into sRGB before painting. Absolute `color-mix()` expressions use Oklab by default
or an explicit CSS Color 4 interpolation space. They accept one or more colors,
optional percentages on either side, constant calculated percentages, hue paths and
premultiplied alpha; nested mixes keep source channels until the final sRGB
conversion. A mix containing `currentColor` is evaluated for the styled element:
the `color` property uses its parent's color, while background, border and
outline colors use its own winning color. Explicit inheritance keeps that
expression live for a child with a different foreground.
CSS Color 5 relative functions accept `from <color>` in modern RGB, HSL, HWB,
Lab/LCH, Oklab/OkLCh and predefined `color()` spaces, plus `alpha()`. Named
source channels and `calc()` expressions are evaluated in the target space;
omitted alpha follows the source color. `currentColor` origins remain live for
the styled element and inherited color expressions. Source channels and
missing-component markers survive into color mixes and gradient stops before
the final sRGB painting conversion.
Replacing the foreground brush or changing a mutable `SolidColorBrush.Color`
recomputes dependent mixes and gradient stops; subscriptions are released when
the expression stops using that brush.
CSS gradients retain source channels until interpolation:
pure legacy-color gradients use sRGB, gradients containing modern colors use Oklab,
and all fifteen CSS Color 4 interpolation spaces can be selected explicitly with
`in`. Polar spaces accept the four hue-direction options. The result is sampled
into native gradient stops for software, D3D12 and Vulkan. Source preservation
through custom-property computation or transitions still needs work.
CSS percentage transition hints (including `calc()` percentages and unitless zero) move the
halfway color without creating another authored color. Ordered stop/hint fixup,
double-position stops and single-color solid gradients are supported. Length
and mixed math positions now resolve using the element's CSS length context and
the actual paint box. Linear-gradient corner directions and end points track
the box's aspect ratio. Nondegenerate radial circles and ellipses use the
requested side, corner or explicit radius at the current box and position;
`repeating-linear-gradient()` and `repeating-radial-gradient()` repeat the
fixed-up stop interval across the painted box. Repeating radial brushes extend
their ending shape to the farthest painted corner. Zero-length and subpixel
periods use their premultiplied average color. Zero-radius ellipse rules and
the remaining position grammar still need work.
`conic-gradient()` and `repeating-conic-gradient()` sample around the center,
starting at the top and moving clockwise. They resolve `from`/`at`, color-space
methods and angular stops at paint time, default to Oklab interpolation, then
rasterize into an ImageBrush so
software, D3D12 and Vulkan use the same shape clipping. The raster scales with
the paint box up to 2048 pixels per axis; large or high-DPI output and the full
conic WPT image corpus remain unverified.
Linear, radial and conic color stops also accept `currentColor` and nested mixes
that reference it. Foreground changes rebuild their used-size gradient.
Modern `rgb()`/`hsl()` values also accept `none`, and modern HSL accepts number
components for saturation and lightness. Legacy comma-separated RGB keeps one
component unit kind across all three channels.

### Percentage and elliptical corners

CSS retains horizontal and vertical radius lengths independently. Horizontal
percentages use the element's border-box width and vertical percentages use its height;
`border-radius:50%` therefore produces an ellipse on a non-square box. The shorthand
accepts one to four values on each side of `/`, and each corner longhand accepts
one or two values. Nonzero literal lengths require a unit. Negative literals and
malformed axis lists reject the whole declaration; math results clamp to zero.

```xml
<Border Width="200" Height="80"
        Css.Style="border-radius:50%; background-color:#0f766e" />
<Border Width="160" Height="80"
        Css.Style="border-radius:50px 15px 40px / 20px 25px; border:3px solid blue" />
<TextBlock Width="160" Height="80"
           Css.Style="border-radius:50%; background-color:#0f766e" />
```

Percentages and mixed math follow live size changes. All eight used radii share
one overlap-reduction factor. The inner curve subtracts each corresponding border
width after that reduction. Explicit inheritance copies computed lengths, retaining
percentages for the child's own box. A native `CornerRadius` value, binding or
animation takes precedence; removing CSS restores the native layer. Circular and
elliptical CSS radii share the same normalized contour and axis-specific border
insets; elements without CSS continue using the native drawing path.

The extended `Border` path supports background/border painting, rounded input and
managed/native clipping. With CSS overflow clipping, the background paints to the
outer edge and children clip at the padding edge. Software, D3D12 and Vulkan retain
both axes through the native clip interface. Rectangle, bitmap, text and path draws
use the intersection of all active contours, including captured transforms, open
edges and inner curves that cross an opposite border. Older native libraries without
the optional interface retain their previous fallback and emit a diagnostic.

Panel and TextBlock also paint CSS backgrounds and native local backgrounds to the
rounded contour without requiring a native `CornerRadius` property. Their CSS
background clips use inset curves. Built-in panels and `TextBlock` paint CSS
borders with the same side colors, styles and rounded joins as `Border`.
`TextBlock` reserves the used border widths in text measurement and positioning.
CSS Grid, Flex and flow layout reserve the panel's used border widths and padding;
native panel layouts retain their own child placement. Direct `ScrollViewer`, `ScrollBar`,
`RichTextBox`, `TabControl`, untemplated `TextBox`, `NumberBox`, `AutoCompleteBox` and `Label`, `PasswordBox`, `DatePicker`, `TimePicker`, `ColorPicker`, `Calendar`,
`ProgressBar`, `Slider`, `RangeSlider`, `Thumb`, `CommandBar`, direct `ToolBar`, `ToolBarTray`, `MenuBar`, `Menu`, `MenuBarItem`, `MenuFlyoutItem`, `MenuItem`, `MenuFlyoutSeparator`, built-in `Separator`, `AppBarSeparator`, `StatusBar`, and `StatusBarItem` paint CSS side colors and styles. A `Border` bound to its control's
`BorderBrush` and `BorderThickness` inside a template receives the same side
paint, including the built-in `Label`, `TextBox`, and `Button` templates. Native
source or template values keep precedence. Custom direct renderers still need
review. `TabControl` reserves its border and padding around the tab strip and
selected content for all four strip placements, including after CSS changes or
native border-thickness overrides. An untemplated `StatusBar` likewise measures
and arranges its items host inside the border and padding content box.
Each `StatusBarItem` also reserves its border and padding around text or child
content.
`ScrollViewer` uses that content box as its viewport: ordinary and `IScrollInfo`
content, overlay scroll bars, and scroll metrics use the inset size. Its child
clip, when enabled, keeps scrolled content out of the border and padding while
its own background remains visible, including with CSS rounded corners.
An explicitly styled `ScrollBar` reserves its CSS or native border and padding
for desktop buttons and track, or for the overlay track and indicator hit strip.
Its intrinsic size includes those edges. The built-in 2-DIP padding retains
the original part geometry until a box edge is explicitly set. CSS removal and
native values equal to the default also reflow the parts.
`ScrollBar` paints CSS background color and image layers from their chosen
border, padding or content box, including rounded corners. `background:none`
removes its built-in track fill; a local `Background` retains priority.
`CommandBar` and `MenuBar` paint CSS background layers across the complete bar,
including rounded corners, and paint CSS border sides above their children.
Their item panels use the border and padding content box; the `CommandBar`
overflow button stays at its trailing edge. Removing CSS edges reflows those
children. `background:none` removes the `CommandBar` fallback fill, and native
local backgrounds and border brushes retain priority. Its built-in bottom
separator remains when no border thickness is authored.
`MenuBarItem` and `MenuFlyoutItem`, including submenu and toggle items, paint
CSS background layers, elliptical corners and border sides. Their titles,
icons, accelerator text, submenu arrows and checkmarks follow the border and
padding content box. CSS `color`, including an inherited color, paints their
text and glyphs; native local
foreground, background and border brushes retain priority. The built-in hover
fill remains for theme-only backgrounds, while a CSS background controls the
item surface directly. Removing CSS edges restores the original item layout.
The direct `Menu` paints CSS background layers and border sides while placing
its items host in the content box. Its built-in bottom line remains when no
border thickness is authored. A `MenuItem` paints those layers and sides around
its whole box; the header, icon, gesture text, checkmark and submenu arrow use
the content box. An effective `background:none` suppresses its selected
fallback fill. `MenuFlyoutSeparator`, built-in `Separator`, and
`AppBarSeparator` paint CSS box backgrounds and borders, include CSS edges in
intrinsic size and keep the line inside the content box. The built-in
`Separator` style draws through this path, including its theme stroke. CSS
`color` can paint the line when no native stroke brush takes precedence.
Direct `ToolBar` and `ToolBarTray` also paint CSS background layers, rounded
contours and border sides. `ToolBar` uses its content box for items and
overflow decisions; `ToolBarTray` places its bands there and includes the
edges in intrinsic size. Their theme backgrounds paint on direct paths, and
removing CSS restores the theme fill and layout. Custom `ToolBar` templates
still control their own surface and child layout.
`ToolBarPanel` and `ToolBarOverflowPanel` place their children inside CSS
border and padding edges. The main panel uses the stacking-axis gap, while the
overflow panel uses both row and column gaps for wrapping. The owning
`ToolBar` accounts for those edges and gaps when selecting overflow items.
Percentage gaps use the panel's content width; a native local `Spacing`
Both panels expose `Padding`; percentage padding resolves against the
containing width, and a native local value keeps priority.
Direct `StatusBar` and `Calendar` backgrounds use those layer boxes and rounded
corners as well. Native local backgrounds and an explicit `CalendarItemStyle`
background retain their existing priority.
Direct `PasswordBox` also uses the layer boxes and rounded contour, while its
border and padding continue to inset password text, selection and caret. A
native local `Background` retains priority.
`StatusBarItem` paints CSS background layers within the selected box and rounded
contour. Native local `Background` and `BorderBrush` retain priority.
`Page` uses CSS border widths and padding to size and position its content or
template root. Its background layers use border, padding and content boxes,
including CSS rounded corners, and its CSS border paints each side. A local
`Background` remains authoritative.
An untemplated `NumberBox` paints CSS background layers over its whole box,
including the header area. CSS border and padding inset the header, value text
and spin buttons together. Its template path paints CSS sides through the
bound `OuterBorder`; native local background and border brushes keep priority.
`DatePicker` paints CSS background layers over its whole box, including the
header, and paints CSS border sides over the input content. CSS border and
padding inset the header, date text and calendar button. Its popup calendar
has its own box and style; native local background and border brushes keep
priority on the picker.
An untemplated `AutoCompleteBox` paints CSS background layers and border sides
around its input box. CSS edges move the input text and IME caret while the
inline suggestion list begins below that box. Its built-in template paints
the input through the bound `OuterBorder`; native local background and border
brushes keep priority.

`TimePicker` paints CSS background layers over the complete box, including
the header, and CSS border sides over its content. CSS edges inset the header,
time text and clock button. Its popup has its own box and style.
`ColorPicker` paints CSS background layers and border sides around its spectrum,
sliders and preview. CSS edges move those regions together with their pointer
targets. Native local background and border brushes keep priority.
`ProgressBar` paints CSS background layers and border sides on both its theme
template and direct rendering path. Its filled and indeterminate indicators use
the content box after border and padding, including when those edges change.
The theme track keeps its capsule shape; large native corner radii are reduced
to the box size before CSS background clipping. Native local background and
border brushes keep priority; `background:none` removes the direct fallback
track fill.
`Slider` and `RangeSlider` use `Background` for the whole control box and
`TrackBrush` for the narrow track. Their theme templates bind an outer `Border`
to CSS background, border, padding and radius properties. Their direct paths
paint the same background layers and border sides; native local background and
border brushes keep priority. Both paths position the track, selected range
and thumbs inside the content box and map pointer input over that same range;
direct tick painting uses the inset track too. The theme track supports both
orientations, including segmented mode. A 32-DIP minimum height preserves the
default size and lets CSS border and padding increase the outer height.
An untemplated `Thumb` paints CSS background color and image layers within the
chosen border, padding and content boxes, including elliptical corners. It
paints CSS border side colors and styles over the grip, while its existing
native brush and pen behavior remains when CSS does not own the box.
`background:none` removes the built-in fill without hiding the grip, and a
local `Background` retains priority. CSS border and padding move its direct grip
within the content box. The implicit `Thumb` template binds its
`Padding` to `ThumbBorder`, so the grip uses the CSS content box. Custom thumb
templates and the slider track and thumb parts still need separate styling
contracts.

`overflow:hidden`
clips Panel children to the rounded box, and pointer hits on either element follow
its curve. A custom element that draws its own background must use the CSS contour
in its own rendering code.

`overflow:clip visible` clips the horizontal axis only; `overflow:visible clip`
clips the vertical axis only. A scrollable value (`hidden`, `auto`, or `scroll`)
on one axis changes `visible` to `auto` and `clip` to `hidden` on the other, so
both axes clip. A single clipped axis keeps its corners square. Native local
`ClipToBounds` and `ClipToBoundsEdges` values retain dependency-property
precedence. `overflow-inline` selects the horizontal axis and `overflow-block`
the vertical axis in the current horizontal writing mode; physical and logical
declarations on the same axis follow normal CSS cascade priority and declaration
order. RTL direction does not swap axes. Vertical writing modes are not yet
supported. `overflow-clip-margin` follows [CSS Overflow 3](https://www.w3.org/TR/css-overflow-3/#overflow-clip-margin):
its default edge is the padding box, and an optional nonnegative length expands
that edge. It accepts `content-box`, `padding-box`, or `border-box` in either order
with the length. It changes the clip for painting descendants and pointer input,
including the corresponding corner radius, when an axis computes to `clip`.
`hidden`, `auto`, and `scroll` do not use the margin. CSS scrolling is not implemented.

`pointer-events` follows [CSS UI 4](https://www.w3.org/TR/css-ui-4/#pointer-events):
`none` excludes the element's own box from pointer targeting, and `auto` on a
descendant restores that descendant's targeting. This CSS rule is separate
from native `IsHitTestVisible`, whose local value retains precedence and whose
`false` value still blocks its full subtree. CSS targeting changes invalidate
the window's same-point hit-test cache.

A native XAML `ImageBrush` assigned to `Background` follows the same contour without
being replaced by CSS. Bitmap, animated, DrawingImage and SvgImage sources retain
viewport/viewbox units, stretch, alignment, tile modes, brush transforms and opacity.
Mutable pixels, deferred decode upgrades, animation frames and nested vector sources
invalidate dependency-aware raster caches. Arbitrary geometry fills use the real path
mask rather than its bounding rectangle; transparent texels are bilinearly sampled in
premultiplied space. That native-brush increment preceded CSS `background-image`
support. CSS image lists paint the first image on top and `background-color`
below all images. CSS bitmap layers support intrinsic and explicit sizes,
per-layer positions and all four axis repetition modes. `space` ignores the
specified position when multiple whole tiles fit; `round` can change the other
dimension to preserve the natural ratio when it is `auto`. On `Border`, a
template-bound `Border`, `TextBlock`, directly painted `TextBox` and `Label`, CSS Grid/Flex/flow
`Panel`, `ScrollViewer`, `RichTextBox` and `TabControl`, each background layer's
origin and clip follow current border and padding values. Panel padding comes
from the same CSS layout metrics used to position its children. CSS Flex
measures and arranges items inside that padded content area, including native
`FlexPanel` when no `display` declaration is present.
CSS gradients have no natural dimensions or ratio: `auto` fills the positioning
box, explicit dimensions and positions define gradient tiles, and repetition
can paint beyond the positioning box up to the clipping edge. Elliptical CSS
corners are supported on `Border`; direct controls retain their existing corner
shape. Other direct-drawing owners and special native shape tiling remain open.

Outlines use the same used two-axis contour. Each corner grows independently by the
outline offset plus half its stroke width, including percentage/slash radii after a
size change. Solid, dashed, dotted, double and relief outlines therefore follow an ellipse instead of
the public `CornerRadius` horizontal projection. A native local/current/animated
radius remains authoritative and returns the outline to the native circular path.

Outer `box-shadow` effects receive the same eight used radii through direct rendering
and retained command replay. Software blurs the captured alpha; D3D12 and Vulkan use a
per-corner elliptical SDF for their analytic Gaussian shadow. Older native payloads
fall back to the horizontal projection with a diagnostic. Inset shadows use the padding
edge after native border widths are removed. Positive spread contracts their perimeter;
negative spread expands it, with the CSS small-radius adjustment applied on each axis.
Their color and blur do not depend on background alpha. Older native payloads retain
the scalar effect with a diagnostic.

`TemplateBinding CornerRadius` carries both axes to the original template part.
This covers typed bindings, runtime/generated markup bindings and the public
`TemplateBindingExtension`. Percentages resolve against the source control's border
box before transfer; the part normalizes those lengths to its own available box.
The binding object, user content, local values and namescope remain intact. A native
trigger, local value, animation, current-value override or converter result takes
precedence over the CSS contour. Changes to only the vertical radius or source height
still invalidate the template's paint.

Controls use their outer contour for overflow and their own input area. A bound
Border uses the padding-edge contour for children, following the source overflow
setting when its own clip properties have no explicit value. Native clip settings
on a template part remain authoritative. Replacing a template releases its old owner
and binding subscriptions; reusing a part binds it to the new owner.

Custom controls that draw their own chrome without a bound Border, ordinary value
bindings/converters that intentionally produce circular `CornerRadius` values,
symbolic radius transitions and native LiquidGlass,
SuperEllipse, custom-shader and backdrop combinations need further integration. This is partial
rounded-corner support, not full module conformance.

Typography and color inherit through generic visual containers. The same engine styles
`FrameworkContentElement`, `Run`, `Paragraph`, and `FlowDocument`; XAML document collections
preserve the parent links required for selector matching and inheritance.

Real CSS properties the framework has no capability for (`content`, …) are registered as explicit
*unsupported* entries: they never fall through to the kebab lookup, and each reports a
one-shot diagnostic explaining the limitation and the nearest alternative.

`@keyframes` definitions are parsed with case-sensitive names, valid frame offsets,
source-order blocks and the keyframe declaration restrictions. They survive conditional
groups, `@import` and compiled CSS resources. Internal frame evaluation resolves values
against the target element, then forms separate tracks for each property with computed
0%/100% endpoints when omitted. `animation` and its eight longhands start those
tracks on the central render clock. Lists repeat their shorter component lists;
the first named animation wins for a property shared with a later one. Sampling
supports frame-local easing, delays, iteration count, direction, fill, and pause.
Normal CSS declarations yield to keyframes; important declarations, native local
values, and explicit native animations retain priority. Removing the animation
restores the underlying value. Animation events, composition modes, timelines,
3D transforms, independent priority within a composed native property, and full
platform visual parity remain open. Supported 2D `transform` keyframes and
transitions interpolate matching translate, scale, rotate and skew functions;
different function-list suffixes use decomposed matrices, falling back to a
discrete switch for non-invertible matrices. Decomposed matrix reflection retains
its selected scale axis; distinct skew function names take that matrix path.
Cubic Bézier output beyond 0–1 extrapolates numeric keyframes and 2D transforms.
Running tracks refresh after style and size changes without restarting their clocks.

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

## Downloadable fonts

`@font-face` now has a native resource path, using the
[CSS Fonts family/source model](https://www.w3.org/TR/2024/WD-css-fonts-4-20240201/#font-resources).
The parser retains sources, family, weight/style/width descriptors, Unicode ranges,
display policy, layers, conditions and resource bases. Generated CSS C# preserves
these definitions as well.

```css
@font-face {
    font-family: AppText;
    src: local("Arial"), url("fonts/app.woff") format("woff");
    font-display: swap;
}
.text { font-family: AppText, Arial; }
```

Fonts are requested when CSS-selected families reach text measurement, drawing or
font-relative units. Unused declarations do not fetch resources. `Parse` performs
no I/O. `LoadAsync` retains its resolver for later font requests; the four-argument
`Parse(text, label, baseUri, resolver)` overload supports embedded/custom resources.
Source failures try the next supported source. Removing a rule cancels its request
and releases its registration when existing native formats no longer need it.

```csharp
await Css.WaitForFontsAsync(root); // Waits for requests already started by layout.
root.UpdateLayout();
var errors = Css.GetFontLoadErrors(root);
```

SFNT/TrueType/OpenType, WOFF 1 and WOFF 2 data are assigned private family names. Windows
uses process-private GDI resources and DirectWrite collections; Linux/Android share
the bytes with the existing native font parser. Text and native FontFamily values
remain unchanged, and local/bound native families keep their native interpretation.
Loaded families invalidate retained text layouts without replacing bindings.

This is not complete Fonts 4 conformance. Non-default collection fragments,
full local face/PostScript-name matching, composite Unicode-range fallback, width
and variation matching, feature/metric/size-adjust descriptors, complete inline
document coverage and the full font-display timing corpus remain pending. The
initial display policies use block/swap deadlines and preserve fallback measurement;
their complete cross-platform visual behavior still needs the remaining tests.

WOFF 2 uses the [8 August 2024 recommendation](https://www.w3.org/TR/2024/REC-WOFF2-20240808/).
The managed decoder handles Brotli data, compact directories, glyph/loca transforms,
hmtx reconstruction, empty/simple/composite glyphs, instructions and overlap flags.
  It also accepts null transforms and shared-table collections. TTC and WOFF 2
  `@font-face` URLs may select one face by its exact PostScript name, such as
  `url("fonts/collection.woff2#Ahem-Second")`. An omitted fragment selects the first
  face. Invalid fragments are skipped before fetching; a valid but missing name
  continues to the next `src` item.
Original-size hints do not dictate reconstructed glyph sizes. Invalid transforms,
stream bounds, variable integers and required bounding boxes are checked before use.

Validation includes 41 original WPT font files and six independently encoded fixtures.
FontTools compared 1,668 glyph records against their uncompressed inputs: coordinates,
endpoints, flags, bounds, instructions and metrics matched. Native pixels matched the
uncompressed font. FontTools and its Brotli package are test-only dependencies; runtime
decompression uses .NET's existing Brotli support and the native font pipeline.

## Layout: percentages, aspect-ratio, position, flexbox

Percentage lengths resolve during layout against the containing block (the constraint
the parent hands down): `width/height/min-*/max-*`, all four `margin`/`padding` edges
(basis is the block's *width*, per CSS), and the physical/logical insets. Against
an infinite constraint (a StackPanel's stacking axis, a Canvas) a percentage degrades to
`auto`. A local `Width` set in code still outranks a CSS percentage. `aspect-ratio`
derives the auto axis from the determinate one; `box-sizing: content-box` adds the
padding+border chrome to explicit sizes.

[CSS Logical 1 §4.1](https://www.w3.org/TR/css-logical-1/#box)
maps `inline-size`/`block-size` and their min/max counterparts to physical size
properties. The current horizontal writing mode maps inline to width and block
to height, independently of RTL direction. Logical and physical declarations
share the computed axis, including declaration order and `revert-layer` behavior.
An inherited percentage remains a percentage against the child's containing
block. Vertical writing modes are not yet implemented.

[CSS Logical 1 §4](https://www.w3.org/TR/css-logical-1/#box) also maps
`margin-inline`/`margin-block`, `padding-inline`/`padding-block`, and
`inset-inline`/`inset-block` to physical edges. Inline start/end swap sides
under RTL direction; block start/end stay top/bottom in horizontal writing
mode. Each logical edge competes with its physical counterpart by cascade
priority and declaration order, including layers. Explicit inheritance reads
the parent's logical side and preserves percentage/math values for the child.

`position: absolute` takes an element out of its panel's flow: it contributes nothing to
the flow or the panel's desired size and is placed by the inset rules (`left`/`top`/
`right`/`bottom` or their logical forms, `%` allowed; both edges set with
`width: auto` stretches between them). With a definite width and both inline
edges set, the containing block's RTL direction favors the right edge; LTR
favors the left edge.
All built-in panels participate; a custom panel opts in with three lines
(`if (IsCssAbsolute(child)) continue;` in its loops plus the two `…CssAbsoluteChildren`
helpers). With `position: static`, absolute-pixel `left/top/right/bottom` keep their
historical Canvas attached-property mapping. `fixed` and `sticky` still fall
back to static positioning.

`position: relative` keeps the element's normal flow slot and translates its
visual and hit-test box after layout. Physical and logical insets accept pixels,
percentages and math expressions; horizontal percentages resolve against the
containing block's content width and vertical percentages against its content
height. Left moves right, right moves left, top moves down and bottom moves up.
When both horizontal edges are specified, the containing block's direction
chooses left in LTR or right in RTL; top wins over bottom. Native local Canvas
edges retain priority over the corresponding CSS offset.

**FlexPanel**, and existing panels declaring `display:flex`, share a flexbox layout algorithm
(`Direction`, `Wrap`, `JustifyContent`, `AlignItems`, `AlignContent`, `Row/ColumnSpacing`
plus the attached `Grow`/`Shrink`/`Basis`/`AlignSelf`/`Order`). The whole flex property
family maps onto it: `flex-direction/-wrap`, `justify-content`, `align-items/-content`,
`flex-flow`, `flex` (and its longhands), `align-self`, `order`, `gap`. Flex basis accepts
percentages and length expressions. Flex-item `margin:auto` absorbs positive
free space on either axis before justification or alignment; two auto sides
share the space. `direction:rtl` maps to inherited `FlowDirection`, so row
main-start and column cross-start follow RTL, including reverse and
`wrap-reverse` cases. The `all` shorthand leaves `direction` unchanged.
Applying or removing `display:flex` preserves the original child collection and parent links.
Nested flex containers export their first and last content baselines to outer
baseline alignment. Row lines use their shared baseline when items participate;
otherwise the startmost/endmost item's baseline is used. Column flex containers
include main-axis justification in the exported offset. Ordinary flow boxes
propagate first/last baselines through nested blocks; an inline-block uses its
last line, while an inline-flex uses its first flex baseline. Changing a native
`BaselineOffset` remeasures ancestor baseline groups through the layout tree.
Orthogonal writing modes and last-baseline item alignment remain pending.

`display:grid` also works over an existing Panel-derived control without replacing its
children or native Grid definitions. Grid values use the existing declaration compiler,
variables, aliases, custom converters and CSS value layers. The grid formatter supports
fixed/percentage/math/font-relative tracks, `fr`, `minmax()`, `fit-content()`, integer and
automatic `repeat()`, named lines and areas, negative lines, spans, row/column auto-flow,
dense placement, and `grid`/`grid-template` shorthands. Intrinsic sizing is currently
adapted from native child measurement. First-baseline grid items share a baseline
within each row; the needed offsets contribute to non-subgrid intrinsic row
sizing. Grid containers export first/last row baselines, and inline-grid aligns
its first baseline with the surrounding flow line. Block-axis auto margins
absorb positive free space before self-alignment. Full CSS text intrinsic
measurements, last-baseline and inline-axis alignment, cross-level subgrid
baseline groups, cyclic intrinsic-size fallback and fragmentation remain pending.

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
text-bottom/middle/sub/super/length/percentage vertical alignment, plus CSS
Inline 3 `center`. Two-keyword forms such as `inline grid` are accepted.
Splittable text/Run fragments,
inline decorations across fragments,
full bidi, table-cell vertical alignment and CSS Inline 3 alignment longhands
still need implementation.

CSS ordinary flow defaults to wrapping; an explicitly set native TextWrapping
value or a CSS white-space declaration participates through the property system.
Line-height now retains unitless numbers through inheritance and resolves length
or percentage values before inheritance. Display:none stays collapsed even beside
visibility:visible. Native local Visibility and other local/bound values continue
to outrank CSS, and removing the CSS display declaration restores native layout.
CSS `visibility:hidden` and ordinary `visibility:collapse` keep their boxes in
layout. A descendant with `visibility:visible` can paint and receive input;
native `Visibility.Hidden` and `display:none` still suppress the whole subtree.

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
and display resolution (`dppx`/`x`, `dpi`, `dpcm`, including calculated values).
Resolution uses the root's vertical device-pixel ratio and re-evaluates when its
DPI changes. Boolean features, reversed/chained ranges and boolean composition
are supported; unknown features remain unknown under negation.
The native eight-bit sRGB output reports `color:8`, `color-gamut:srgb`,
`monochrome:0` and `color-index:0`. `scripting:none` matches because there is no
document script runtime. Wider gamuts and script-enabled modes do not match.
The same native presentation path reports `dynamic-range:standard`,
`environment-blending:opaque` and bitmap `grid:0`. The eight-bit output does
not qualify for `dynamic-range:high`. Video surfaces use the same graphics
target, so `video-color-gamut:srgb` and `video-dynamic-range:standard` match;
wide-gamut and high-dynamic-range video output do not.
`prefers-reduced-motion` follows the native client-area animation and UI-effects
settings; system-setting changes re-evaluate CSS conditions. The boolean form
matches only when reduced motion is requested.
`prefers-color-scheme` follows the desktop's light/dark preference independently
of the application's chosen theme. The boolean form always matches; when no
preference is expressed, `light` matches. Preference changes re-evaluate CSS.
`prefers-contrast: more` follows Windows high-contrast settings and Linux
portal or high-contrast theme settings. Otherwise `no-preference` matches;
the boolean form then does not match. `less` and `custom` are valid but have
no current platform input, and system changes re-evaluate CSS conditions.
`display-mode` reports `standalone` for a native window and `fullscreen` when
its `WindowState` is `FullScreen`; the condition updates when that state changes.
`Css.Supports(property, value)` checks registered value
grammars; the `Css.Supports(condition)` overload evaluates complete support conditions.
Support queries also accept `selector()`, implemented `at-rule()` names,
`font-format()` and `font-tech()`. Font queries report the same format and technology
capabilities used when selecting `@font-face src` entries. The native font path accepts
TrueType/OpenType, WOFF 1, WOFF 2 and collections. OpenType GSUB/GPOS is the only
technology currently advertised; variation selection, palette control, individual
color-font technologies and incremental transfer remain unadvertised.
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

`container-name`, `container-type:normal/inline-size/size/scroll-state`, and the `container`
shorthand use the existing CSS property layers. Named, nested `@container` rules
support width/height, horizontal inline/block size, aspect ratio, orientation,
boolean combinations, reversed/chained ranges, and comma-separated conditions.
Measurements use the selected ancestor's content box; relative query thresholds
use that container's font and ancestor context.

`scroll-state` can be combined with `size` or `inline-size`. On native
`ScrollViewer` containers, `scroll-state(scrollable: …)` selects a reachable
scroll axis and tests the available direction at the current offset.
`scroll-state(scrolled: …)` tests the last relative scroll direction on each
axis independently. Line/page commands, wheel input, content panning and
scrollbar line/page clicks update it after movement commits; absolute offset
changes, scrollbar thumb positioning and Home/End leave it unchanged. Both
features accept physical and logical directions, `x`/`y`, `block`/`inline`,
boolean form and `none`. Direction, extent and CSS overflow changes refresh
queries. A `var()` value uses each candidate container's computed custom
properties before axis selection, so changing a variable can select a
different ancestor; fallbacks and registered properties work as well. A
disabled or CSS-hidden axis is skipped when selecting a query container.
`stuck` and `snapped` still require implementation.

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
Custom-property equality queries also substitute `attr()` in the expected value
using the selected container's attributes. Typed values, raw strings and lazy
fallbacks work for registered and unregistered properties; attribute changes
refresh the query.
Ordinary-property `style()` queries now compare the container's computed storage
value for `opacity`, `direction`, `float`, `clear`, `container-type`,
`container-name`, `user-select`, `image-rendering`, `object-fit`, and the
supported direct-storage text, caret and box-decoration longhands. The
`container`, `text-wrap` and `white-space` shorthands compare every longhand.
Size style queries also cover the physical and logical `width`, `height`,
`min-width`, `min-height`, `max-width` and `max-height` longhands. They compare
computed lengths and retained percentages, including linear `calc()` values;
they do not substitute the used pixel size for a percentage. An effective native
size value takes precedence over a CSS percentage declaration. Minimum-size
queries preserve the computed `auto` keyword separately from the native `0`
sentinel, including when explicitly inherited.
The eight physical and logical inset longhands, plus `inset`, `inset-inline`
and `inset-block`, compare computed `auto`, lengths, percentages and linear
`calc()` values. Logical edges follow the selected container's direction, and
native Canvas offsets override CSS values in these queries. Static-position
percentages remain available for computed-value queries although the native
static layout still drops them with a diagnostic.
`margin` and `padding` style queries cover their eight physical and logical
longhands and the physical, inline and block shorthands. They retain `auto`
for margins, percentages and linear math instead of comparing used `Thickness`
pixels. Native local spacing values take precedence, and `0%` remains distinct
from `0px`. Negative padding literals are invalid; negative numeric functions
clamp when their computed value can be resolved.
`overflow-x`, `overflow-y`, `overflow-inline`, `overflow-block` and `overflow`
style queries compare the computed two-axis value. A scrollable opposite axis
promotes queried `visible` to `auto` and `clip` to `hidden`; the shorthand compares
both axes after that promotion. Logical names share the physical axis in the
currently supported horizontal writing mode, and `overlay` computes as `auto`.
Native `ClipToBounds` controls clipping independently of this CSS value.
`box-sizing` queries distinguish the CSS initial `content-box` from an explicit
`border-box`; native-only controls still use the framework's border-box layout
default. `aspect-ratio` queries retain the specified number pair and optional
`auto` keyword, even when two pairs produce the same layout ratio. Replaced-element
layout behavior for combined `auto` and ratio values remains to be validated.
`position` queries retain `static`, `relative`, `absolute`, `fixed` and `sticky`
as computed keywords. The native layout still treats `fixed` and `sticky` as
`static` and reports a lossy-conversion diagnostic.
`display` style queries compare the supported inner and outer display types,
including equivalent one- and two-keyword forms. The queried value is blockified
on the CSS root, floated or absolutely positioned boxes, and children of CSS
flex/grid containers. Explicit inheritance takes the parent's computed display
type; changes to the parent's type or float value refresh descendants. Native
`Visibility` does not change the CSS `display` value. Other display models and
box fixups remain outside this query implementation.
`visibility` style queries compare `visible`, `hidden` and `collapse` after
inheritance. Native local Visibility values take precedence on their element;
an explicit visible CSS descendant can still have a visible computed value
under a hidden ancestor. CSS `display:none` does not change the computed
`visibility` value, including while the display animation mirrors its native
Visibility state. Ancestor style and native changes refresh dependent queries.
`color` and `background-color` style queries compare parsed absolute colors and
retain `currentColor` and currentColor-dependent color functions as symbolic
computed values. `color` inherits, while `background-color:inherit` retains a
parent's symbolic value and resolves it against the child's color. Native local
foreground/background brushes take precedence; changing a mutable solid brush
refreshes dependent queries. Boolean queries use the framework's CSS initial
black and transparent-black values. Absolute-color comparison currently uses
the engine's 8-bit color representation; wider color-space precision and full
system-color behavior remain open.
`font-weight` style queries compare the inherited integer weight after
`normal`, `bold`, `bolder` or `lighter` resolves against the parent. Numeric
weights and equivalent keywords match; native font-weight values take precedence,
and parent changes refresh relative comparisons. Fractional variable-font
weights still use the engine's integer precision.
`font-size` style queries compare the inherited computed pixel size, including
all eight absolute keywords through `xxx-large` (42px with the default 14px
`medium`), lengths, `em`/percentages against the parent, `rem` against
the root, `larger`/`smaller`, and `math` against computed math depth. Native local sizes take precedence; parent and
root size changes refresh dependent queries. Zero lengths and percentages are
valid font sizes, and out-of-range font-size math clamps to zero; that computed
size is inherited and used for `em` and style queries. TextBlock, TextBox,
AccessText, RichTextBox document, TextEffectPresenter, AutoCompleteBox, NumberBox,
PasswordBox, DiffViewer, HexEditor, Terminal, EditControl, MarkdownTextPresenter,
MarkdownCodeTextPresenter, ChartLegend,
ChartTooltip, DockItem, template-less DataGridColumnHeader, direct Label/HyperlinkButton,
DatePicker/TimePicker, Calendar and its native
buttons, ColorPicker's hex label, direct InfoBar/ToastNotificationItem,
MenuItem/MenuBarItem/MenuFlyoutItem, TabItem/StatusBarItem, and FontIcon suppress
glyphs at zero size. The input controls also avoid native
zero-height text layouts; AutoCompleteBox suggestions and NumberBox headers
follow the same value. DiffViewer and HexEditor retain 4px and 2px row spacing
respectively at zero size. Terminal's cells and EditControl's text metrics have
zero width and height; EditControl also drops its text scroll extent. Markdown
prose/code presenters keep inline images or padding without text glyphs;
BulletDecorator aligns its bullet against a zero-height first text line. ChartLegend
and ChartTooltip keep their color markers and tooltip chrome. Calendar weekday
labels keep their 11px default and scale with the computed font size; menu icon and accelerator glyphs scale from
their native defaults. Other self-rendered controls still apply
their own positive-size label fallback.
`font-stretch` and its `font-width` alias compare inherited computed percentages.
The nine width keywords map to their CSS percentages; nonnegative percentages
and percentage math retain their exact computed value for queries and inheritance.
Native rendering projects that value to the nearest of its nine width classes,
while native local stretch values take precedence. Continuous variable-font
width shaping remains open. The `font` shorthand accepts width keywords but not
width percentages. See [CSS Fonts 4](https://drafts.csswg.org/css-fonts/).
`font-style` style queries compare `normal`, `italic`, bare `oblique` and
explicit oblique angles as computed degrees. Inheritance preserves an authored
angle even when the native `FontStyle` has only an oblique class. Native local
styles take precedence; angle-specific face selection and directional
`left`/`right` styles remain open.
`font-family` style queries compare ordered computed lists, preserving named-font
strings separately from generic family keywords even when both render with the
same native font. Quoted and unquoted spellings of one named family compute
to the same string; CSS inheritance and native local values keep their priority.
Boolean queries compare against CSS initial values; `initial`, `inherit` and
`unset` are resolved on the selected container. Expected values can use `var()`
or `attr()`, and changes to native properties refresh matching descendants.
Numeric `style()` ranges accept `<`, `<=`, `=`, `>=` and `>` comparisons, including
two-sided ranges such as `style(8px < --gap <= 16px)`. A custom-property name is
substituted from the candidate container's computed value; `var()` fallbacks and
math functions work in range operands. Numbers, percentages, lengths, angles,
times, frequencies and resolutions compare only with the same type, except that
unitless zero can compare as a zero length. Changes to custom properties and the
container font refresh affected queries.

In numeric range operands, `attr()` reads attributes of the selected container.
`type(<length>)` and other supported numeric types, the `number` keyword,
explicit units such as `px` and `%`, and fallback values are evaluated before
comparison. Unspecified or `raw-string` attribute types produce strings and do
not become numeric values. Attribute changes refresh dependent queries.

Explicit size containment excludes child intrinsic contributions on the queried
axes and creates an independent flow context. Local XAML dimensions and bindings
still win. Query changes settle with layout, inherited variable updates propagate,
and weak subscriptions allow detached controls to be collected. Removing CSS
restores native layout and values. The Gallery slider demonstrates a native Width
binding with responsive CSS columns.

The container-query implementation follows the applicable parts of
[CSS Conditional Rules 5, 2026-06-30](https://www.w3.org/TR/2026/WD-css-conditional-5-20260630/).
`stuck`/`snapped` scroll-state queries, remaining ordinary-property style queries
including other font, layout and effects properties,
`env()` in style ranges, full Values 5 `attr()` substitution across declarations,
orthogonal writing modes, intrinsic-size overrides and full native backend
verification remain pending.

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

Repeated imports share fetched bytes and cache parsing per inherited encoding,
but expand independently, including
their anonymous layers and ancestor-specific cycle recovery. Failed imports still
declare their requested layer when its conditions match. Layer identifiers preserve
case and normalize escapes; an escaped dot remains part of a single identifier.
Imports remain consecutive after initial layer statements; a layer statement after
an import prevents further imports. Redirect cycles, cancellation and stream disposal
are covered by regressions.

An `ICssResourceResolver` overload supports application loaders. It returns a
`CssResource` with the final URI, an optional `ContentType` and a stream owned
and disposed by the loader. Explicit non-`text/css` content types are rejected;
byte decoding uses BOM, charset metadata, exact initial `@charset` and the
importing stylesheet's encoding before falling back to UTF-8.
The default resolver supports application resources, files and HTTP(S). Gallery's
embedded CSS files demonstrate conditional layer imports with a native width binding.
The import baseline is [Cascade 5, 2022-01-13](https://www.w3.org/TR/2022/CR-css-cascade-5-20220113/).
The loader recognizes the Encoding Standard's 228 labels and uses its indexes
for 28 legacy single-byte encodings, including ISO-8859-10, ISO-8859-14 and
ISO-8859-16. Seven legacy multibyte encodings use pinned standard indexes and
state machines, including the shared GBK/gb18030 decoder and Big5's combining
character mappings. The full malformed-byte corpus and host environment encodings
for document formats beyond XAML stay on the conformance checklist.

Color-matrix filters now include brightness, contrast, grayscale, hue rotation, invert,
opacity, saturate, and sepia, in addition to blur and drop-shadow. The implementation uses
the shared native effect pipeline. `drop-shadow()` uses the styled element's color when
its color is omitted and accepts direct or mixed `currentColor`. It rejects spread and
inset; `blur()` defaults to zero. The filter's third shadow length is a Gaussian
standard deviation and is converted for each native shadow path; the standard
deviation of `box-shadow` is half its blur radius. Software uses a
three-pass box approximation. `drop-shadow()` follows the captured input's
alpha silhouette on software, D3D12 and Vulkan; `box-shadow` follows the
border box even when the element paints only a smaller shape. CSS `blur()` interprets
its length as Gaussian sigma and paints its full captured apron, including
outside rounded corners. Native `BlurEffect` retains its existing radius
behavior. `blur()` and `drop-shadow()` accept supported relative and calculated
lengths; percentages and nonzero unitless lengths are invalid, and a negative
calculated blur clamps to zero. A chain containing only color-matrix functions
runs in declaration order and clamps RGBA after each stage on software, D3D12
and Vulkan (up to 64 stages). Chains mixing blur or drop-shadow still need
serial compositing. When `box-shadow` and `filter` are both set on `Border` or
supported background-only panels, the filter consumes the painted shadows,
background, child content, border and outline as one captured group. A CSS
filter by itself also consumes the outline; native XAML effects retain a sharp
outline above their output. Keyframes,
transitions of layout-only symbolic values,
and nonvisual document-node transitions still require additional work.

[`backdrop-filter`](https://drafts.csswg.org/filter-effects-2/#BackdropFilterProperty)
accepts `none` or one `blur()`, `brightness()`, `contrast()`, `saturate()`,
`grayscale()`, `sepia()`, `invert()`, `hue-rotate()` or `opacity()` on visual elements. CSS specifies
the blur length as Gaussian standard deviation; the backdrop material uses
three times that length for its capture extent and keeps the standard deviation
separately. Font, viewport, container and calculated lengths use the same
resolution rules as `filter: blur()`. Color amounts accept numbers and
percentages; grayscale, sepia, invert and opacity clamp amounts above one. The native
software, D3D12 and Vulkan pipelines use the CSS color matrices for saturation,
grayscale and hue rotation. A shared paint step puts the backdrop before
ordinary elements' backgrounds, templates and children; Border keeps its
specialized border-box paint, while CSS on ScrollBar uses the shared border box
instead of its native track-only placement. Elliptical CSS border radii clip direct
and recorded backdrop paint. Unsupported functions and function lists invalidate
the whole declaration because native backdrop materials process their color
parameters in a fixed order. The existing `filter` matrix-chain renderer reads
an element's own offscreen capture. Ordered backdrop lists need a separate
capture of the Backdrop Root Image and serial operations before the border-box
clip, as specified by Filter Effects 2. `drop-shadow()`, URL filters,
backdrop-root isolation, D3D12 fallback rendering and full WPT pixels remain
open. `CssBackdropFilterNativeTests` checks blur, radius clipping, color
functions and opacity on transparent and translucent backdrops across all three backends.

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

Later registrations win — including over built-ins and unsupported placeholders
(for example, registering `content` yourself replaces its placeholder). A converter returning
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

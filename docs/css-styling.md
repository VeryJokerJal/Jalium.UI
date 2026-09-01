# CSS Styling

Jalium.UI supports styling elements with CSS. Property keywords are 100% native CSS;
anything CSS has no keyword for maps to the control's dependency properties through an
automatic kebab-case form (`is-tab-stop` → `IsTabStop`, `horizontal-content-alignment` →
`HorizontalContentAlignment`).

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
// or from a pack resource:
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

## Selectors

Type (`Button`, matches derived types), universal (`*`), class (`.card`), id (`#hero`,
matches `x:Name`), descendant (`A B`) and child (`A > B`) combinators, comma groups,
`!important`, and the dynamic pseudo-classes `:hover`, `:active`, `:focus`,
`:focus-visible`, `:focus-within`, `:enabled`, `:disabled`, `:checked`, `:indeterminate`.
Sibling combinators (`~`, `+`), attribute selectors, pseudo-elements, and functional
pseudo-classes (`:not()` etc.) are not supported; rules using them are dropped with a
diagnostic.

Template-generated parts are invisible to page-level CSS: they never match selectors and
the ancestor axis skips them, so `Button > TextBlock` reaches a button's content even
though template elements sit between them visually. User content inside a templated
control (and DataTemplate content) is fully matchable.

## Precedence

CSS values integrate with the dependency-property system as two layers:

```
local value  >  template triggers  >  CSS :state winners  >  style triggers
             >  template triggers  >  CSS normal winners  >  style setters  >  default
```

Practically: CSS overrides theme/implicit/explicit style *setters* but never a local value
you set in code, and a `:hover` rule you write outranks the theme's hover trigger. Within
CSS the standard cascade applies — scope (application < ancestor < own < inline),
specificity, then document order, with `!important` on top. `SetCurrentValue` on a
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
| `border: 1px solid red` | `BorderThickness` + `BorderBrush` | `border-style: none` clears the brush; dashed/dotted render solid with a diagnostic |
| `border-radius` | `CornerRadius` | corner order matches 1:1 |
| `box-shadow: 3px 4px 5px …` | `DropShadowEffect` | cartesian offsets convert to polar `Direction`/`ShadowDepth`; `inset` → `InnerShadowEffect`; multiple shadows → `EffectGroup` |
| `cursor: pointer` | `Cursors.Hand` | full keyword table (`not-allowed` → `No`, `move` → `SizeAll`, …) |
| `display: none` / `visibility: hidden` | `Visibility` Collapsed / Hidden | other `display` values render visible (layout model unchanged) |
| `transform: translate(…) rotate(…)` | `RenderTransform` (TransformGroup) | does not affect layout, like CSS |
| `transition` | `TransitionProperty/Duration/TimingFunction` | one shared duration/curve; delays are ignored (diagnostic) |
| `z-index`, `left`/`top`, `grid-row`/`grid-column`, `gap` | `Panel.ZIndex`, `Canvas.Left/Top`, `Grid.Row/Column(+Span)`, per-panel spacing | grid lines convert from 1-based to 0-based |
| `font`, `font-size: 1.2em`, `line-height: 1.5` | font properties | `em`/`%` on font-size use the inherited size; unitless line-height multiplies the element's font size |
| `outline: 2px solid red` (+`outline-offset`) | `OutlineBrush/OutlineThickness/OutlineStyle/OutlineOffset` | self-drawn ring outside the bounds, takes no layout space; `outline: none` clears it; pairs naturally with `:focus-visible` |
| `is-tab-stop: true`, `horizontal-alignment: Center`, … | any dependency property | the kebab-case fallback channel; values go through the XAML converter stack |

Lengths support `px` (default), `pt`, `in`, `cm`, `mm`, `q`, `pc`, `em`, `rem`; colors
support hex, all CSS named colors, `rgb()`/`rgba()`/`hsl()`/`hsla()` in legacy and modern
syntax, and `transparent`.

Real CSS properties the framework has no capability for (`letter-spacing`,
`text-shadow`, `animation`, `backdrop-filter`, …) are registered as explicit
*unsupported* entries: they never fall through to the kebab lookup, and each reports a
one-shot diagnostic explaining the limitation and the nearest alternative.

Diagnostics are one-shot per (property, reason, element type) and can be silenced with
`CssDiagnostics.LogUnresolvedProperties = false`.

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

`display: flex` semantics belong to **FlexPanel** — a full flexbox container
(`Direction`, `Wrap`, `JustifyContent`, `AlignItems`, `AlignContent`, `Row/ColumnSpacing`
plus the attached `Grow`/`Shrink`/`Basis`/`AlignSelf`/`Order`). The whole flex property
family maps onto it: `flex-direction/-wrap`, `justify-content`, `align-items/-content`,
`flex` (and its longhands), `align-self`, `order`, `gap`. On other panels, container
properties go through the **interception protocol**: a panel overrides
`TryApplyCssPropertyCore(name, rawValue, setter)` to map CSS concepts onto its own
properties (StackPanel maps `flex-direction: row|column` to `Orientation`); the same
override is the last resort for any unknown property name, so custom panels can consume
arbitrary custom keys. Values written through the setter live in the CSS layers and are
automatically cleared when the rule stops matching.

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

# Native XAML CSS conformance work

The target is the XAML-applicable functionality in the [22 June 2026 CSS Snapshot](https://www.w3.org/TR/2026/NOTE-css-2026-20260622/), together with the concrete gaps documented in `css-styling.md`. This work does not introduce an HTML or JavaScript runtime. Native local values and bindings continue to outrank CSS.

This is an implementation tracker, **not a claim of complete CSS conformance**. A feature is complete only after its applicable parsing, computed-value, layout, rendering, interaction, and platform tests pass. The initial isolated Windows CSS/Flex-related baseline had 351 passing test cases. Subsequent runs use the stricter class-name filter below to exclude unrelated test methods whose names happen to contain `css`.

| Area | Current implementation | Remaining acceptance work |
| --- | --- | --- |
| Shared XAML nodes | Visual elements, TextBlock inlines, and FrameworkContentElement/Run/Paragraph/FlowDocument share cascade and matching; markup, bindings and inherited `direction` on document nodes tested | Generated content and full document layout coverage |
| CSS syntax and recovery | Direct and decoded stylesheets and inline declarations normalize line endings, NUL and unpaired surrogates before tokenization; rule and declaration scanners respect balanced component-value blocks, escaped delimiters, comments and bad strings ending at a newline; declaration priority recognizes terminal `!` and ASCII-insensitive `important` tokens, including escaped identifiers; top-level CDO/CDC tokens are discarded | Complete malformed-token and parser-recovery corpus |
| Variables | Case-sensitive custom properties, escaped identifiers/functions/keywords, `<declaration-value>` rejection of bad strings, bad URLs, unmatched closing tokens and top-level `!`, reserved `--` name rejection, dynamic `var()` first-argument substitution (nested variables, attributes, braces, registered names, `if()` branches and invalid-name fallbacks), EOF function closure, inheritance of computed tokens, static fallback-edge cycles and dynamic cycles during substitution, pending shorthand substitution, computed `initial`/`inherit`/`unset` from `var()`, candidate-specific rollback from substitution in registered and unregistered custom properties, and CSS Values 5 spread syntax for early `var()` argument substitution | Full variables conformance corpus and tentative rollback cycles |
| Conditional values | CSS Values 5 `first-valid()` chooses the first parse-valid whole-value candidate in ordinary declarations, shorthands, and registered or unregistered custom properties; braced arguments retain internal commas; unchosen branches do not substitute variables, attributes or environment values, and a missing supported candidate is invalid at computed-value time. `if()` parses unspread branch structure before the cascade and spread-generated delimiters at computed time, evaluates `style()`, `media()` and `supports()` conditions in order, substitutes only the selected branch, preserves token boundaries, and works in ordinary, shorthand, registered and unregistered custom properties; same-element style queries compare computed custom properties, guard an active property query as false, and carry attribute taint into URL restrictions; selected `revert-rule` branches re-enter the cascade; `...var()`, `...attr()`, `...env()` and nested `...if()` substitute before branch splitting | Complete `first-valid()` WPT and computed serialization coverage, complete `if()` condition and serialization corpus, ordinary-property self-query/invalidation edge cases, other URL-taking functions and full platform coverage |
| Random values | CSS Values 5 `random-item()` selects a lazily substituted list item; numeric `random()` resolves compatible number, dimension and percentage ranges with optional uniform steps. Both share `fixed`, `auto`, named and scoped cache keys. Raw custom properties defer numeric random evaluation until use; typed registrations compute it before inheritance. `if()` tests use separate early random cache keys and indexes; early spread leaves numeric random in unchosen options unevaluated, and unused registered fallbacks do not consume active indexes | Full WPT random-value corpus, exact auto indexing through all nested substitution cases, and computed serialization |
| Environment variables | `env()` syntax checks in declarations and support probes; missing-name, indexed-scalar and nested fallbacks; the four `safe-area-inset-*` lengths read the host `Window.SafeAreaInsets` and update after platform inset changes; `env(<ident>)` capability queries in `Css.Supports`, `@supports` and `@import supports()`; ordinary/shorthand declarations, custom properties, generated CSS, keyframes and container style-query expectations use token-aware substitution | Safe-area maximum values, viewport segments, preferred text scale, at-rule descriptors, media/size-query expressions and full WPT/device coverage |
| Attribute substitution | Computed-time `attr()` in ordinary mapped CSS longhands and shorthands reads the styled element's live attributes, including through `var()`; unqualified/null and prefix-qualified attribute names use the declaration's stylesheet namespace, including imports, generated CSS, keyframes, custom properties and container style queries; number/unit, typed string/string-list, length/color/image, fallback and invalid-at-computed-value handling work; registered and unregistered custom properties resolve attributes on their owning element before inheritance; attribute taint follows typed and untyped custom-property values through variables and inheritance, rejecting use in `url()`/`src()` | Full taint handling in other URL-taking functions, further Values 5 types and the complete WPT/backend corpus |
| Registered properties | @property descriptors, layer/source-order registration per native document, syntax alternatives/lists including `<string>`, typed computation and inheritance, invalid-value recovery, font-size/face/line-height/color cycles, URL bases, conditional activation, viewport initial values and shared-sheet lifecycle regressions | Registered-property interpolation/keyframes, full font-feature/variation dependency coverage, modern color/image syntax, 3D transforms and full WPT/backend coverage |
| Numeric values | Values 4 arithmetic/comparison/stepped/trigonometric/exponential/sign functions and constants, dimensional products/division, symbolic percentages, special-value propagation, deferred scalar computation through existing converters, integer math rounding, `round(line-width, …)` with root-DPI pixel snapping, all font-relative and viewport unit names, native font rulers, explicit host viewport variants and six container-relative units; Values 5 `progress()` with default clamping, `no-clamp`, consistent numeric dimensions and zero-range behavior, plus `calc-mix()` with typed weighted averages, omitted-weight distribution, excess-weight normalization, and typed-zero leftovers | Complete target-range metadata, zero-sized typography in remaining self-rendered controls, full font variation/stretch and vertical metrics, CSS writing-mode integration, additional value-module functions, computed serialization and the full WPT/backend corpus |
| Tree counting values | CSS Values 5 `sibling-index()` and `sibling-count()` parse as numeric math functions with no arguments and resolve against the styled native element's framework siblings; the root resolves to one, and parent tree changes recompute dependent ordinary, registered-property and container-query values as well as gradient color channels and stop positions | Pseudo-element origin rules, shadow-tree scoping, generated content, browser computed serialization, image functions beyond native gradients, and the full tree-counting WPT corpus |
| Global keywords/layers | Initial/inherit/unset/all, author revert, named/nested layers, important-order reversal and revert-layer; `revert-rule` rolls back the current matched rule or inline style, including important declarations, custom properties, logical aliases, `all`, and substituted `var()`/`env()`/`attr()` fallbacks in ordinary and custom properties; the tentative important `revert-rule`/normal `revert-layer` cycle resolves to unset in the two-layer and multi-layer WPT cases | Other rollback cycles and final CSSWG clarification, full initial/computed-value metadata and full conformance corpus |
| Selectors | Attribute, sibling, structural and functional selectors, :root/:scope, `:lang()` over inherited XAML Language with RFC 4647 extended filtering, RFC 5646 canonical/extlang forms from the pinned 2026-09-17 IANA registry and live invalidation, `:dir(ltr)`/`:dir(rtl)` over native XAML FlowDirection and its parent chain independently of CSS `direction`, live `:placeholder-shown` on eight native placeholder controls, `:read-write` on native editors with working editability controls and complementary `:read-only`, live `:valid`/`:invalid` from native validation on value controls and explicitly validated elements, live `:required`/`:optional` from native IsRequiredForForm and bound RequiredValidationRule on value inputs, live `:in-range`/`:out-of-range` from native range limits and bound RangeValidationRule, live `:modal` for native dialogs and fullscreen windows plus `:fullscreen` for native fullscreen windows, mutually exclusive `:checked`/`:unchecked`/`:indeterminate` across native toggles, selected items and progress, live `:open` across native popup, dropdown, submenu, pane and expansion states, `:default` on native buttons marked IsDefault, expanded-name of-type counting, namespace/default rules and escapes/comments | Additional host pseudo states, generated pseudo-elements and full selector corpus |
| Media state selectors | `:playing`/`:paused` follow `MediaElement.IsPlaying`, `:buffering` requires active playback and native `IsBuffering`, and `:muted` follows `MediaElement.IsMuted`; own, ancestor/sibling and generated selectors refresh dynamically | No native seeking, stalled or volume-lock signals yet; full media/WPT corpus |
| Namespaces | @namespace with per-sheet/import isolation, qualified/wildcard/null namespaces, namespace-aware supports/nesting/scope, runtime/generated XAML names and live qualified attributes, weak metadata/subscriptions | Full WPT/device coverage and remaining selector-module integrations |
| Nesting and scope | Shared parent-selector references, specificity, relative selectors, interleaved declarations, nested groups, explicit/implicit @scope roots and limits, proximity, scoped declarations, and weak tracking of external ancestor/sibling dependencies | Pseudo-element integration and full WPT/backend corpus |
| Conditional rules | Token-aware @media/@supports boolean grammar, viewport geometry and device-resolution ranges, native output color-depth/gamut, `dynamic-range`, `video-color-gamut`, `video-dynamic-range`, `environment-blending`, bitmap `grid` and no-script capabilities, native standalone/fullscreen `display-mode`, `pointer`/`hover`/`any-pointer`/`any-hover` from native pointing-device capabilities, plus system-backed `prefers-reduced-motion`, `prefers-color-scheme` and partial `prefers-contrast` with weak change subscriptions and unknown-value handling; selector/at-rule probes, font-format/font-tech probes sharing @font-face source capabilities, `env(<ident>)` probes for four safe-area variables, scoped/nested groups, container rules and @property activation | Apple hardware inventory, Android hot-plug device validation, remaining media features, additional font technologies and full condition corpus |
| Container queries | Named/nested size queries, boolean/range conditions, content-box metrics, horizontal size containment, per-axis units, unregistered/registered custom-property style queries, typed `attr()` expectations and dynamic invalidation; direct-storage ordinary-property style queries for 27 longhands (including computed `math-depth` and inherited `math-style`) and three shorthands, plus computed-value queries for twelve preferred, minimum and maximum size longhands (absolute lengths, retained percentages, linear math and minimum-size auto versus zero), plus computed-keyword `box-sizing` and `position` queries (retaining `fixed`/`sticky` even where layout falls back to static), computed `display` type queries (including root, float, positioned and flex/grid item blockification), inherited `visibility` keyword queries (including native local and ancestor precedence), computed `color` and `background-color` queries (including symbolic `currentColor`, inheritance and native brush changes), computed `font-weight` queries (including parent-relative weights and native precedence), computed `font-size` queries (including parent/root-relative sizes and native precedence), computed `font-stretch`/`font-width` percentage queries (including keyword equivalence, inheritance and native precedence), computed `font-style` queries retaining explicit oblique angles through inheritance and native precedence, computed `font-family` ordered-list queries distinguishing named and generic families, and number-pair `aspect-ratio` queries (including combined `auto` syntax), plus computed-value queries for eight physical/logical inset longhands and three shorthands (auto, retained percentages, linear math, direction and native Canvas precedence), and sixteen physical/logical margin/padding longhands and six shorthands (auto margins, retained percentages and math, native Thickness precedence), plus four physical/logical overflow longhands and the shorthand (cross-axis computed promotion, overlay alias and native clipping independence), with CSS-wide values and native change tracking; `style()` numeric ranges compare numbers, percentages, lengths, angles, times, frequencies and resolutions, including computed custom properties, `var()` fallbacks, math, chained comparisons and typed `attr()` values from the selected native container; `container-type: scroll-state` combines with size types; `scroll-state(scrollable: …)` queries native `ScrollViewer` reachability, and `scroll-state(scrolled: …)` tracks the last committed relative direction independently on each axis across wheel, pan, keyboard, and scrollbar line/page input while absolute scrolling leaves it intact; physical/logical directions, `none`, and live changes supported; `var()` values from unregistered/registered custom properties dynamically select a scrollable axis and ancestor, including fallbacks; native bindings and templates preserved | `stuck`/`snapped` state features, scroll containers outside native `ScrollViewer`, remaining ordinary-property style queries (notably layout and effects), `env()` and full Values 5 `attr()` substitution across CSS declarations, intrinsic-size overrides, orthogonal writing modes and complete backend conformance |
| Resources | Conditional/layered @import, support-gated fetching, live media conditions, independent anonymous layers, path-specific cycle recovery, redirect/base-URI preservation, cancellation/disposal and weak shared-sheet subscriptions; external stylesheets use BOM, transport charset, exact initial @charset and parent import encoding in priority order, recognize all 228 Encoding Standard labels, decode 28 legacy single-byte and seven legacy multibyte encodings from standard indexes and state machines, reject explicit non-CSS MIME types, and cache fetched bytes independently of decoded imports; the default resolver decodes percent-encoded and base64 `data:` URLs for CSS stylesheets and SVG clip sources; lazy @font-face source loading and per-document registrations | Complete malformed-byte conformance corpus, environment encodings from other host document formats and full resource corpus |
| Font faces | Parsed/generated definitions, source fallback, shared format/technology hint checks, family/style/weight selection, Unicode usage filtering, display deadlines, private native SFNT/WOFF 1/WOFF 2 resources, retained-format lifetime and arrival/removal invalidation; PostScript-name URL fragments select TTC/WOFF 2 faces with invalid-fragment prefetch rejection; native values/text bindings preserved | Full local face matching, composite Unicode ranges, width/variation/features/metric overrides, complete inline-document and font-display behavior, full WPT/device corpus |
| Font weights | Absolute `font-weight` and parent-relative `bolder`/`lighter` in the longhand and `font` shorthand; inherited and native parent changes recompute the weight on visual and document nodes. The managed computed value preserves integer weights through 1000 | Fractional variable-font precision; legacy Windows DirectWrite text formats and outlines render CSS 1000 using their maximum accepted weight, 999; full font matching corpus |
| Font widths | `font-stretch`/`font-width` alias, keyword-to-percentage computed values, nonnegative percentages and math in the longhand, width keywords in the `font` shorthand, inherited exact query values, and projection to native width classes | Continuous variable-font width rendering, arbitrary-width face matching and full WPT/backend coverage |
| Font styles | `normal`, `italic` and `oblique` with optional -90deg to 90deg angle in the longhand and `font` shorthand; computed style retains the explicit angle through inheritance and `style()` queries | Angle-specific native face selection and shaping, angle interpolation, directional `left`/`right` styles, and full WPT/backend coverage |
| Font families | Token-aware `font-family` lists preserve quoted and unquoted names, generic keywords, ordering and CSS escapes as computed values through inheritance, `font` shorthand and `style()` queries; common generics map to native fonts, while quoted names containing commas stay intact for `@font-face` loading and font-relative metrics | Complete generic-family rendering and script-specific fallback, installed comma-bearing family fallback and native list selection on all backends, and full WPT/backend coverage |
| Font sizes | Eight CSS Fonts 4 absolute-size keywords through `xxx-large` use the spec's suggested table with 14px `medium`; `larger`/`smaller` use a 1.2 ratio. Zero length/percentage and out-of-range math sizes compute to 0, flow through inheritance and font-relative units, and compare in `style()` queries; zero-sized glyphs are suppressed in TextBlock, TextBox, AccessText, RichTextBox document, TextEffectPresenter, AutoCompleteBox, NumberBox, PasswordBox, DiffViewer, HexEditor, Terminal, EditControl, MarkdownTextPresenter, MarkdownCodeTextPresenter, ChartLegend, ChartTooltip, DockItem, template-less DataGridColumnHeader, direct Label/HyperlinkButton, DatePicker/TimePicker, Calendar and its native buttons, ColorPicker's hex label, direct InfoBar/ToastNotificationItem, MenuItem/MenuBarItem/MenuFlyoutItem, TabItem/StatusBarItem, and FontIcon. DiffViewer and HexEditor retain only their 4px/2px row spacing at zero size; Terminal and EditControl use zero-sized text metrics, Markdown prose/code presenters retain inline images or padding without text glyphs, BulletDecorator aligns against a zero-height first line, and chart legends/tooltips retain their color markers and chrome. Calendar weekday labels and menu icon/accelerator glyphs scale from their native defaults with the computed size. The longhand, `font` shorthand, `@supports` and computed `style()` queries share the parser; `font-size:math` scales with computed versus inherited `math-depth`, using the inherited font's OpenType MATH script percentages where available and the MathML Core 0.71 fallback otherwise; inherited `math-style` and `math-depth:auto-add`/`add(<integer>)` participate | Zero-sized text in remaining self-rendered controls and complete mixed-size formatted-text ranges; Apple/Metal MATH-table runtime validation, broader math-font corpus and full WPT/backend coverage |
| WOFF 2 | Brotli and compact directories; null/glyf/loca/hmtx transforms; simple/composite/empty glyphs, instructions, overlap and short/long offsets; shared-table default and named collection faces (named fixture uses null transforms); malformed-data recovery | Transformed multi-face combinations, complete collection reconstruction as a TTC, broader fuzzing and complete cross-platform font/pixel corpus |
| Layout values | Percent/math sizes and spacing, horizontal auto margins, `position:relative` offsets after normal layout with physical/logical insets, percentages, RTL conflict resolution and native Canvas precedence; logical inline/block sizes and insets cascade with physical counterparts in horizontal writing mode, preserving percent/math layout values and computed inheritance | Vertical writing modes, full formatting contexts, `fixed`/`sticky`, and full positioned containing-block and stacking-context behavior |
| Overflow | One- and two-value `overflow` shorthand; physical `overflow-x`/`overflow-y` and logical `overflow-inline`/`overflow-block` compete by cascade priority and declaration order in horizontal writing mode; independent axis clipping, computed visible/clip promotion beside a scrollable axis, native clip-edge precedence, elliptical corner masking; `overflow-clip-margin` parses three visual boxes and nonnegative lengths, adjusts clip edges/corners and pointer input, and passes direct/recorded Windows software/D3D12/Vulkan pixels | CSS scroll containers, formatting-context effects, vertical writing modes, remaining background owners/custom shapes and full device/WPT corpus |
| Scrollbar width and color | `scrollbar-width: auto`, `thin` and `none` on `ScrollViewer` and controls with a template-hosted viewer, including `ListBox`; the width is non-inherited, but the template-hosted viewer uses its owner's value unless it has its own declaration. Thin narrows desktop bar strips, while none hides both bars without changing scroll extent or scrollability. Inherited `scrollbar-color: auto` or two colors sets thumb then track on both axes, including template-hosted viewers; `currentColor` updates with the source color, and the default theme keeps the authored thumb color through interaction states. A CSS declaration on `Window` applies to direct `ScrollViewer` content or the designated template viewer of direct `ListBox`, `DataGrid`, `TreeDataGrid`, `TreeView`, `Markdown`, or `Terminal` content as the native viewport; the grids' separate header viewers keep their own non-inherited width. Root declaration removal, template detachment and `currentColor` updates refresh the bars. Two-color CSS transitions interpolate thumb and track independently with premultiplied alpha, using sRGB for legacy color pairs and Oklab otherwise. With `transition-behavior:allow-discrete`, width keywords and color pairs to or from `auto` switch at 50% eased progress; `normal` applies those changes immediately. `initial`/`auto` restore theme brushes; native local values retain priority | Scroll containers without `ScrollViewer`, other viewport templates and arrangements, discrete transitions for other CSS properties, user-agent preference overrides, forced-color behavior and full WPT/device coverage |
| [Scrollbar gutter](https://drafts.csswg.org/css-overflow-3/#scrollbar-gutter-property) | Non-inherited `scrollbar-gutter: auto`, `stable` and `stable both-edges` on `ScrollViewer` and template-hosted viewers. Classic `auto` bars consume inline-end or block-end space when shown, including the default declaration and bars on either axis that appear after the other axis loses space. Authored `stable` reserves the inline-end vertical gutter even without overflow or with hidden scrolling; `both-edges` mirrors it on inline-start, with RTL side mapping. A root `Window` declaration applies to direct `ScrollViewer` content or the designated template viewer of direct `ListBox`, `DataGrid`, `TreeDataGrid`, `TreeView`, `Markdown`, or `Terminal` content; grid header viewers and other nested viewers retain their own non-inherited value. Measurement, viewport metrics, bar placement, content paint clipping and pointer hit testing share the reserved content box; thin width, visibility changes and declaration removal reflow it. Platform overlay bars and `scrollbar-width:none` reserve no space. With `transition-behavior:allow-discrete`, gutter modes switch at 50% eased progress and reflow the viewport | Other viewport templates and arrangements, vertical writing modes, other scroll containers and full WPT/device coverage |
| Pointer interaction | `pointer-events:auto/none` uses inherited box targeting, allows an explicit `auto` descendant inside a `none` ancestor, keeps native `IsHitTestVisible` precedence and subtree gating, and invalidates same-point window hit memo on CSS/native source changes; `allow-discrete` transitions switch hit targeting at 50% eased progress | Full input-device and document-node corpus |
| User selection | [`user-select`](https://www.w3.org/TR/css-ui-4/#propdef-user-select) parses `auto`, `text`, `none`, `contain` and `all` as a non-inherited property. TextBlock and direct/template Label text honor the parent-dependent used value of `auto`: `none` blocks new mouse selection without clearing an existing selection, `text` enables selection, `all` selects a whole text owner on one click, and `contain` stays within that owner's native selection range. Ancestor style changes stop active drags; a local native selection setting retains priority; `allow-discrete` changes the behavior at 50% eased progress | Cross-element and inline-document selection ranges, editable controls, selection through mixed media, complete CSS UI/WPT and platform corpus |
| Clip paths | `clip-path:none`, CSS layout geometry boxes, `inset()`, `xywh()` and `rect()` including `round`, plus `circle()` and `ellipse()` with one-, two- and four-value positions clip box painting, descendants, effects, outlines and input; percentages resolve against the used reference box; overflow and native `Clip` intersect; direct/recorded software, D3D12 and Vulkan pixels pass; `polygon()` parses vertices, optional `nonzero`/`evenodd` and `round` radius; `path()` parses quoted SVG path data with optional fill rule and implicit closure; `shape()` parses CSS positions, relative commands, curves, arcs and fill rules and resolves layout-dependent lengths against the used box; external `url(file.svg#id)` and `data:image/svg+xml` URLs load SVG/SVGZ `<clipPath>` through the stylesheet resolver, supporting `userSpaceOnUse`/`objectBoundingBox`, transforms, shapes and text glyph contours, nested `<tspan>` x/y/dx/dy/rotate lists and transforms, inherited fonts and visibility, anchored text chunks, horizontal `textLength` with `spacing`/`spacingAndGlyphs` and nested adjustments, horizontal `<textPath>` with inline/local paths, shape references, offsets, referenced `pathLength` calibration, tangent rotation and anchor-aware closed-path wrapping, child unions and `clip-rule`; local `url(#id)` in SVG images applies these definitions to shapes, groups and `<use>`, including nested clip intersections; all clip managed and native software/D3D12/Vulkan pixels and pointer input against their contours | No SVG definition nodes in ordinary XAML trees for local `url(#id)`; SVG `textPath` stretch/automatic spacing, vertical/bidirectional path text, full `textLength` behavior for bidi/forced wrapping/complex positioned text and complete shaping across positioned characters, Apple text clip silhouettes, fill/stroke/view reference boxes, stacking-context semantics and full WPT/device corpus |
| Ordinary flow | Block/flow-root layout over native children, adjoining and nested/empty/negative margin collapse, atomic inline controls, anonymous line runs, wrapping, inline-block sizing, first/last line baseline propagation through nested block containers, inline-flex baseline placement, CSS 2.2 baseline/top/bottom/text-top/text-bottom/middle/sub/super/length/percentage vertical alignment, CSS Inline 3 `center` alignment, directional text alignment, and inherited `text-indent` on atomic-inline lines beside floats | Splittable text/inline fragments, full bidi/writing modes, CSS Inline 3 alignment longhands/combinations, table-cell vertical alignment, contents/list/table/multicol models and full conformance |
| Floats/clear | Left/right and horizontal logical sides, shrink-to-fit float boxes, shared exclusions through normal blocks, independent-context isolation, inline reflow around floats, clearance/margin interactions, dynamic updates and native paint/hit ordering | Text-fragment wrapping, shape-outside, orthogonal writing modes, remaining clearance corner cases and full stacking/compositing conformance |
| Display, visibility and line-height | Separate outer inline role for inline-flex/inline-grid, two-keyword display syntax, independent display/visibility combination, CSS hidden/collapse boxes retain ordinary layout while explicit visible descendants can paint and receive input, flex-item collapse removes main-axis participation while retaining an original-line cross-size strut, native Visibility and display:none still gate their subtrees, typed number-versus-length line-height inheritance and length expressions; `allow-discrete` visibility transitions switch paint/input at 50% eased progress while preserving layout | Table row/column collapse, inline document-node visibility, remaining display models and full text formatting |
| Flex | Percent/math/font-relative basis, flex-flow, display:flex on existing Panel-derived controls without reparenting, two-pass visibility:collapse with rewrapping and original-line stretch struts, final-box remeasurement and symbolic-basis resolution when arrange differs from measure, row/row-reverse first- and last-baseline groups using TextBlock line metrics, first/last baseline export from nested row/column flex and ordinary flow boxes, main/cross-axis auto margins, and RTL plus wrap-reverse axis mapping | Orthogonal writing modes, column cross-axis baseline sharing, remaining intrinsic sizing and aspect-ratio interactions, other display models and full backend/WPT coverage |
| CSS Grid | Existing panels support fixed/percent/math/font-relative/fr tracks, minmax/fit-content, integer/auto-fill/auto-fit repetition, named lines/areas, negative lines, spans, row/column/dense placement, grid/template shorthands and item alignment; separate first/last block-axis baseline groups, non-subgrid intrinsic row shims, first/last container baseline export, inline-grid baseline placement, and block-axis auto margins | Inline-axis baseline groups, baseline sharing through subgrids, cyclic intrinsic fallback, full intrinsic text sizing and spanning edge cases, writing modes, fragmentation and complete backend conformance |
| Subgrid | Shared rows/columns, descendant intrinsic contributions, inherited/local/repeated line names, clipped parent named areas, bounded placement, gap differences, margin/padding edges, native flow-direction reversal and CSS constraint handling with native-local-value protection | Orthogonal writing modes, full cross-axis intrinsic interactions, absolute-positioning/fragmentation integration and the full WPT/backend corpus |
| CSS formatting box edges | CSS padding on existing Grid, Flex and flow panels, percent padding/margins and content-box sizing; logical margin/padding/inset, border-width/color/style edges and border side shorthands cascade with physical edges under computed LTR/RTL direction, including layers, native values and inheritance; `Border`, template-bound `Border`, built-in panels, `TextBlock`, and direct `ScrollViewer`, `ScrollBar`, `CommandBar`, direct `ToolBar`, `ToolBarTray`, `MenuBar`, `Menu`, `MenuBarItem`, `MenuFlyoutItem`, `MenuItem`, `MenuFlyoutSeparator`, built-in `Separator`, `AppBarSeparator`, `RichTextBox`, `TabControl`, `TextBox`, `PasswordBox`, `NumberBox`, `DatePicker`, `TimePicker`, `ColorPicker`, `ProgressBar`, `Thumb`, `AutoCompleteBox`, `Label`, `Calendar`, `StatusBar`, `StatusBarItem` and `Page` paths paint distinct side colors and line styles; CSS panel/TextBlock layout accounts for used border widths, `TabControl` reserves border and padding for its tab strip and selected content, a direct `StatusBar` reserves them for its items host, `StatusBarItem` reserves them for its content, `Page` reserves them for its content, a direct `NumberBox` reserves authored CSS edges around its header, text and spin buttons, `DatePicker` and `TimePicker` reserve them around their headers, text and picker buttons, `ColorPicker` reserves them around its spectrum, sliders and preview, a direct `AutoCompleteBox` reserves them around its input text while keeping its inline suggestion list outside that box, `ProgressBar` reserves them for its filled or animated indicator, `CommandBar` reserves them for its primary items and overflow button, direct `ToolBar` for its items and overflow, `ToolBarTray` for its bands, `ToolBarPanel` and `ToolBarOverflowPanel` for their children, `MenuBar` and direct `Menu` for their items, `MenuBarItem` for its title, `MenuFlyoutItem` for its icon, text, accelerator and derived glyphs, `MenuItem` for its header, icon, gesture and glyphs, and menu separators for their lines, `ScrollViewer` reserves them for its viewport, content and overlay bars, and `ScrollBar` reserves authored edges for its desktop buttons and track or overlay track and hit strip | Vertical writing modes, border painting on remaining direct-drawing elements, native child placement in some controls, remaining formatting contexts and full CSS border painting coverage |
| Slider control boxes | `Slider` and `RangeSlider` theme templates and direct paths paint CSS background layers and border side styles across the complete control; border and padding inset continuous or segmented tracks, selected range, thumbs and pointer-to-value mapping in both orientations, plus tick painting on direct paths; the themed 32-DIP minimum height expands for CSS edges; CSS removal reflows template parts; local background and border brushes retain priority | Per-part CSS selectors for track and thumbs, nonstandard custom template contracts and full browser range-input parity |
| Layout invalidation | Versioned native inputs, bounded grid/flow caches, nested layout regressions, headless/managed layout and query convergence, weak query subscriptions, and independent-sibling resize checks | Broader incremental-layout and resource-lifetime corpus |
| Grid/Flex gaps | Shared symbolic length handling, per-axis percentage bases, relative lengths and proper shorthand cascade | Percentage gaps in native non-CSS layouts and all indefinite-size/cyclic combinations |
| Color and filters | Inherited typography/color on generic visual containers, currentColor, shared color-matrix filters; CSS Color 4 absolute `hwb()`, `lab()`, `lch()`, `oklab()`, `oklch()` and all ten predefined `color()` spaces convert through linear sRGB with CSS binary-search gamut mapping for current sRGB surfaces; modern RGB/HSL `none` and number syntax plus legacy RGB unit consistency; CSS Color 5 absolute `color-mix()` with ordered multi-color interpolation, percentages, alpha and retained source channels; relative `rgb`/`hsl`/`hwb`/Lab/Oklab/predefined `color()`/`alpha()` forms resolve origin channels, math, missing values and `currentColor`; currentColor-dependent mixes on color/background/border/outline, shadow colors and direct/mixed gradient stops resolve in element context, with symbolic inheritance for supported color properties; CSS gradients retain source channels through sampling, choose legacy sRGB or modern Oklab defaults, and accept all fifteen CSS Color 4 interpolation spaces with four polar hue paths; used-size linear geometry and nondegenerate circle/ellipse radial extents, length/percentage/math stop and hint positions, ordered fixup, single-stop, finite repeating linear/radial gradients and used-size rasterized conic/repeating-conic gradients; selected WPT color and gradient pixels verified on software/D3D12/Vulkan | Full missing-component and out-of-gamut interpolation corpus, typed-color/animation source preservation, custom color-profile relative forms and full relative-color WPT coverage, currentColor in other mapped/typed values, wide-gamut surfaces, degenerate radial shapes and complete position grammar, conic whole-image/high-DPI parity and complete WPT/device pixels |
| Backdrop filters | `backdrop-filter: none` and one `blur()`, `brightness()`, `contrast()`, `saturate()`, `grayscale()`, `sepia()`, `invert()`, `hue-rotate()` or `opacity()` on visual elements; shared paint runs before their own backgrounds and content, with Border's specialized border-box paint; CSS Gaussian sigma and filter color matrices apply on software/D3D12/Vulkan; relative/math blur lengths refresh with element context; color amounts accept numbers and percentages, with CSS clamping; elliptical CSS border radii clip direct and recorded backdrop paint; untinted opacity preserves a transparent backdrop's alpha | `drop-shadow()`, URL filters, ordered lists, backdrop-root isolation, D3D12 fallback rendering, full WPT/backend parity |
| Rounded corners | Border percentage/math radii, independent horizontal/vertical shorthand and physical/logical corner longhands, shared overlap reduction, computed inheritance across opposite directions, size updates, native-value/animation takeover, common circular/elliptical contours, template-binding propagation and lifecycle, pointer contours, managed software clips and native software/D3D12/Vulkan clip stacks for rectangles, bitmap/vector/animated ImageBrush fills, text and arbitrary paths; Panel and TextBlock CSS backgrounds, borders, overflow clips and pointer contours without a native CornerRadius property; solid/dashed/dotted/double/groove/ridge/inset/outset outlines and outer/inset box shadows retain both axes, including positive/negative spread | Vertical writing modes, self-drawn custom controls, custom-effect/backdrop integration, symbolic transitions and full WPT/device conformance |
| Replaced image and video | Authored `object-fit` (`fill`, `contain`, `cover`, `none`, `scale-down`) and `object-position` on `Image` and `MediaElement` size and place image/video content in the CSS content box without changing the element's own size; shared background-position grammar covers edge offsets, percentages and math; content clips to its box even with visible overflow; CSS padding is included; native local `Stretch` retains priority; video decoder target follows the fitted size; `object-fit` transitions switch rendered size at 50% eased progress under `allow-discrete`; direct and recorded software/D3D12/Vulkan pixels pass for image sources and video bitmap fallback frames | Other replaced controls, live video-surface pixels, high-DPI decode variants, full WPT and non-Windows device pixels |
| Image sampling | Inherited `image-rendering` accepts `auto`, `smooth`, `high-quality`, `crisp-edges`, `pixelated` and legacy aliases; `Image`, `MediaElement` bitmap frames, CSS bitmap background layers, native local `ImageBrush` backgrounds on supported owners, and `Shape` image fills and strokes use the selected mode; `pixelated` performs the integer nearest stage and final smooth stage in managed software and native software/D3D12/Vulkan; inherited `image-rendering` transitions switch sampling at 50% eased progress under `allow-discrete`; direct and recorded image/background/shape pixels pass on Windows; managed software solid/dashed path strokes share one image sampling area and coverage mask, including miter/bevel/round joins | Other nonuniform transformed stroke paths, unresolved degenerate joins and very dense subpixel dash patterns, ImageBrush paints in other controls or custom drawing, live video-surface pixels, other image-valued paints, full WPT and non-Windows device coverage |
| Painting | Existing brushes, borders, shadows and effects retained; ImageBrush viewport/viewbox/stretch/alignment/tile modes, transforms, opacity, deferred decode, mutable/animated/vector sources and exact shape masks; solid ImageBrush strokes preserve texture on native software/D3D12/Vulkan through widened path masks, while dashed and partially stroked open/closed paths use rasterized stroke masks with miter/bevel/round joins and miter limits; CSS bitmap background sizes, positions and `repeat`/`no-repeat`/`space`/`round` modes match and cycle per image; bitmap `auto` uses intrinsic dimensions, while explicit lengths/percentages/math preserve natural ratio for an `auto` axis; CSS gradients have no intrinsic dimensions and use per-layer sizing, positioning and repetition; Border, template-bound Border, TextBlock, direct TextBox/PasswordBox/NumberBox/DatePicker/TimePicker/ColorPicker/ProgressBar/Thumb/AutoCompleteBox/Label, CSS Grid/Flex/flow Panel, ScrollViewer, ScrollBar, CommandBar, direct ToolBar, ToolBarTray, MenuBar, Menu, MenuBarItem, MenuFlyoutItem, MenuItem, MenuFlyoutSeparator, built-in Separator, AppBarSeparator, RichTextBox, TabControl, StatusBar, StatusBarItem, Page and Calendar backgrounds use per-layer border/padding/content positioning and clipping boxes | Other nonuniform transformed stroke paths and unresolved degenerate joins, DrawingBrush/VisualBrush, box geometry for remaining background owners, native special-shape gradient tiling, independent borders, masks, blending and 3D |
| Text | Inherited comma-separated `text-shadow` values paint glyph-and-decoration alpha shadows in author order with text composited once above them in direct and recorded TextBlock, TextBox, AccessText and RichTextBox drawing on Windows software/D3D12/Vulkan; omitted color and `currentColor` follow the styled foreground, while `none` clears the effect; Run and Span overrides in TextBlock and RichTextBox paint per inline fragment with adjacent text above all shadows; existing native text pipeline receives CSS document values; decoration line/style/color/thickness longhands and shorthand plus inherited text-underline-offset lengths, percentages, and math, horizontal text-underline-position under and from-font placement, inherited text-decoration-skip-inset endpoint insetting, selected-font decoration thickness, currentColor, and explicit color inheritance paint solid/double/dotted/dashed/wavy lines in direct `TextBlock`, `TextBox`, `AccessText`, RichTextBox Run/Span/Paragraph content, and TextBlock Run/Span ranges; exclusive `spelling-error`/`grammar-error` values paint red/green wavy markers; inline ancestor lines continue across runs and wrapped lines; TextBlock Run/Span foreground colors and font family/size/weight/style/stretch affect measured layout and drawn glyphs; `text-overflow: ellipsis` visibly trims constrained, unwrapped TextBlock lines at grapheme boundaries with Run styling and native local precedence; `text-align: justify` distributes wrapped-line space at word and CJK grapheme boundaries, and inherited `text-justify` selects auto/none/inter-word/inter-character behavior in TextBlock while preserving selection, hit testing and decorations; inherited `text-indent` supports lengths, percentages, `each-line` and `hanging` in TextBlock, including wrapping and logical start alignment; inherited `overflow-wrap`/`word-wrap` controls TextBlock emergency breaks and intrinsic min-content width while preserving native local wrapping; inherited `word-break` controls letter/CJK breaks and its deprecated `break-word` fallback; inherited `line-break` controls selected CJK punctuation strictness and equal-priority grapheme breaks for `anywhere`; inherited `word-spacing` adjusts TextBlock word separators across wrapping, drawing, selection and trimming; inherited `letter-spacing` adjusts TextBlock grapheme spacing across Run boundaries and inside transformed expansions, including wrapping, drawing and interaction; inherited `white-space` processes TextBlock source spacing and segment breaks for normal/nowrap/pre/pre-wrap/pre-line/break-spaces while retaining source offsets and explicit inline LineBreaks; inherited `text-transform` applies case, full-width and full-size-kana combinations in CSS order to TextBlock, AccessText and Run paint while preserving source text, access keys and selection offsets; `text-align: start/end` resolves against computed direction and follows inherited direction changes in direct text controls; direct and recorded Windows software/D3D12/Vulkan pixels cover direct controls, RichTextBox Run, and TextBlock Run; native local values retain precedence | Level 4 spread/inset, complex-script justification, bidi shaping and placement, shaping across Run boundaries and exact color/decoration endpoints for shaped clusters in TextBlock, other document viewers, propagation through visual ancestor box trees, vertical underline position and ink/space/box decoration skipping, `letter-spacing` and `word-spacing` in other text controls, per-Run white-space overrides and whitespace processing in other text controls, CLDR-specific casing and language-sensitive shaping/justification, text transformation in TextBox, RichTextBox and other controls, text-indent/overflow-wrap/word-break/line-break in other text controls, full Unicode line-breaking and language tailoring, writing modes and complete typography |
| Text autospace | Inherited `text-autospace` inserts `1/8ic` between adjacent CJK ideographs and non-wide letters or non-fullwidth decimal digits in TextBlock; `normal` and `auto` enable both classes, `ideograph-alpha` and `ideograph-numeric` select them, and `no-autospace` suppresses them. For French content, `punctuation` inserts narrow no-break spacing before `;!?` and word no-break spacing before `:` and `»` and after `«`. `replace` substitutes qualifying U+0020 spaces without changing source offsets, including after whitespace collapse; wrapping, drawing, selection and hit testing share the advance. Run/Span common ancestors control inline boundaries; Unicode 18 Han Script_Extensions and East_Asian_Width data classify the CJK units | Bidi-after-reordering placement, inline box-edge suppression, vertical writing, regional French typography differences and other text controls |
| Hanging punctuation | Inherited `hanging-punctuation` supports `first`, `last`, `force-end`, `allow-end` and valid combinations in horizontal TextBlock, including Run text boundaries with inherited Span values at line edges; relevant Unicode opening/closing punctuation and the specified stop/comma set can hang. The used advance is excluded from line fitting, applicable intrinsic sizing, alignment and justification while paint, selection, hit testing and decorations retain the glyph | Bidi visual-edge placement, full inline box geometry and border/padding inhibition, vertical writing, other text controls and complete WPT/backend pixels |
| Caret styling | Inherited `caret-color: auto \| <color>`, `caret-animation: auto \| manual`, and `caret-shape: auto \| bar \| block \| underscore` apply to the insertion caret of TextBox, AutoCompleteBox, NumberBox, RichTextBox, PasswordBox, EditControl and HexEditor; `caret` expands all three parts; contextual colors track foreground changes, native local `CaretBrush` wins, and `manual` stops native blink/fade even when inherited by a focused child | platform caret variants, vertical writing modes and complete pixel/backend corpus |
| 2D transforms and origin | `transform` parses 2D translate, scale, rotate, skew and matrix lists, including percentage scale factors and strict nonzero length/angle units. Keyframes and transitions preserve skew function identity, interpolate matching functions and decompose mixed suffix matrices. `transform-origin` accepts planar keywords, percentages, lengths and length-percentage math; used values follow element-size and font changes, and rotated render bounds use the computed pivot. On CSS layout boxes, `transform-box` accepts all five keywords; `fill-box` uses content-box while `stroke-box` and `view-box` use border-box. The selected box controls percentage translation and the planar origin in direct values, transform keyframes and discrete `transform-box` animation | Third transform-origin Z value, SVG fill/stroke/view geometry without a CSS layout box, 3D transforms, stacking/containing-block semantics and complete backend pixels |
| Animation | Per-property durations/delays, exact CSS Bézier/steps curves, `transition-behavior` lists, shorthand and CSS-wide resets, supported discrete scrollbar, visibility, pointer-events, user-select, cursor, image-rendering, object-fit and transform-box transitions at 50% eased progress; [`display` transitions](https://drafts.csswg.org/css-display-4/#display-animation) switch between non-`none` layouts at 50% eased progress, while `display:none` entry renders from the start and exit retains layout and paint until the endpoint. Exit blocks pointer hits and new focus immediately, releases mouse, stylus and touch capture, and queues revalidation of existing focus; the animated display value drives native layout and Visibility mirrors without overriding native local Visibility. `@starting-style` provides a before-change style on the first unrendered application and when a CSS `display:none` element becomes rendered, using the ordinary selector/layer/source-order cascade across nested groups, imports and generated CSS, with custom-property substitution and parent after-change inheritance; initial changes without a winning starting value remain immediate; existing frame clocks, immediate local/binding takeover of CSS transitions, native animation precedence and live visual Storyboard base-value restoration; CSS keyframe shorthand and eight longhands, list cycling, per-property tracks, frame scheduling, pause/resume, delay/iteration/direction/fill, normal-versus-important cascade priority, and recycling cleanup; supported 2D transform-function and matrix interpolation for keyframes and transitions | Transitions for other discrete properties, complete rendered-lifecycle semantics across native tree attachment and removal, symbolic layout-value and nonvisual-node transitions, keyframe events, animation composition/timelines, 3D transforms, transform-origin Z coordinate and SVG reference boxes and full platform/WPT coverage |
| Documents/printing | CSS styles apply to existing XAML document nodes | Fragmentation, @page, page margin boxes and printing parity |

Ordinary CSS declarations now defer `attr()` until computed-value time, following
[CSS Values 5 attribute substitution](https://drafts.csswg.org/css-values-5/#attr-notation).
The selected element supplies the attribute, including after `var()` expansion;
typed values and fallbacks work in longhands and shorthands; unknown unit names
and first arguments that fail after substitution select the fallback.
Values without a usable fallback become `unset`. Live attribute changes
restyle the element. Custom properties
resolve attributes on the element where they are declared, then inherit the
computed value and its attribute-origin bit. Direct `attr()` and tainted values
substituted into `url()` or `src()` are rejected; typed image attributes can
still supply a gradient. Qualified attribute names resolve against the
declaration's stylesheet `@namespace` bindings; unqualified and `|`-prefixed
names use the null namespace even when the sheet declares a default namespace.
`type(<string>)` parses a CSS quoted string and canonicalizes its escapes;
`raw-string` instead preserves the attribute's literal characters as a string.
Unknown or case-mismatched prefixes, wildcard prefixes, and whitespace around
the namespace separator select the fallback. Separate sheets and imports keep
their mappings independent, including generated stylesheets. Focused native
tests adapt the applicable expectations from Values 5 namespace WPT cases.
Other URL-taking functions, further Values 5 types, and the complete WPT/backend
corpus remain open.

`env()` now substitutes before `var()` in supported declaration values, following
[CSS Environment Variables 1](https://drafts.csswg.org/css-env-1/#env-function).
The native window supplies dynamic safe-area inset lengths in DIPs; a detached
tree uses zero. Unknown names and indices on scalar safe-area values select the
fallback, while a missing fallback makes the declaration invalid at computed-
value time. CSS Conditional 5 `env(<ident>)` support queries report the four
implemented safe-area variables in `Css.Supports`, `@supports` and conditional
imports. Substitution skips CSS dimension, hash and at-keyword tokens containing
the letters `env`. The eight upstream `env-parsing.html` support probes and
adapted indexed/fallback/custom-property cases pass on Windows. Other defined
environment variables and descriptor/query contexts remain open.

The [CSS Cascade 5 `revert-rule` keyword](https://drafts.csswg.org/css-cascade-5/#revert-rule-keyword)
now removes the winning style rule's declarations for the affected property
before selecting the next cascade candidate. Rule identity is retained across
ordinary and logical aliases, layers, important declarations, inline styles,
custom properties and the `all` shorthand. Direct `env()` and `attr()` fallback
results can select `revert-rule` at computed-value time; changes to native
attributes cause the rule selection to refresh. For ordinary properties, `var()`
now resolves against this cascade pass's computed custom properties before
selecting a `revert`, `revert-layer` or `revert-rule` winner. If that changes the
selected font declaration, registered font-relative custom properties are
recomputed before ordinary values are applied. Custom-property candidates now
resolve their own substitutions before rollback selection; each change to the
selected candidate recomputes dependent custom and ordinary values. Direct and
indirect variable cycles retain their invalid-at-computed-value behavior. The
adapted basic, important, layer, nesting, custom-property, env and attr WPT
cases pass on Windows. The two scenarios in the tentative
[revert-rule cycle WPT](https://github.com/web-platform-tests/wpt/blob/master/css/css-cascade/revert-rule-cycle.tentative.html)
now resolve to unset for inherited and non-inherited properties. The related
[CSSWG clarification](https://github.com/w3c/csswg-drafts/issues/13916) remains
open, so other rollback cycles remain acceptance work.
The broader default Debug CSS run passed 4,939 of 4,942 cases. Its three
`InstalledMathFontConstantsControlDepthTransitions` backend cases fail at the
pre-style `HasMathTable` assertion; the default test-bin native DLLs predate the
isolated MATH-font native build described below. Excluding that method name
(including its Linux-only case) leaves 4,938/4,938 Debug CSS cases passing.

The [CSS Conditional 5 scrollable feature](https://drafts.csswg.org/css-conditional-5/#scrollable) now uses native `ScrollViewer` extent and offset snapshots. The focused container-query suite passed 125/125 (`css-scroll-state-final-focus.trx`), and the Debug CSS/ScrollViewer/container regression passed 4,461/4,461 (`css-scroll-state-final-regression.trx`).

The [scrolled feature](https://drafts.csswg.org/css-conditional-5/#scrolled) now distinguishes [relative from absolute scrolling](https://drafts.csswg.org/css-scroll-snap-1/#scroll-types) and retains direction independently on the horizontal and vertical axes, as exercised by the [WPT axis-persistence test](https://github.com/web-platform-tests/wpt/blob/master/css/css-conditional/container-queries/scroll-state/scroll-state-scrolled-hv.html). The focused native scroll-state tests passed 44/44, and the Debug CSS/ScrollViewer/container regression passed 4,478/4,478 without skips.

Scroll-state query values now substitute `var()` against each candidate container before deciding which scroll axis that candidate can answer. Missing or invalid values stay unknown, while fallbacks and registered computed values participate in selection; custom-property changes reselect the container. This follows the [CSS Conditional 5 container selection rule](https://drafts.csswg.org/css-conditional-5/#container-rule) and covers the wrapped `scroll-state(scrollable: var(...))` case in [WPT](https://github.com/web-platform-tests/wpt/blob/master/css/css-conditional/container-queries/scroll-state/scroll-state-query-with-var.html). The focused native scroll-state tests passed 54/54, and the Debug CSS/ScrollViewer/container regression passed 4,488/4,488 without skips.

The Release CSS/Flex/scroll-control/Window/ListBox/DataGrid/TreeDataGrid/input
regression passed 4,718/4,718 without skips
(`artifacts/test-results/css-user-select-regression-final.trx`).
It includes classic `auto` cases for two-axis feedback, RTL placement, gutter
release and `IScrollInfo` remeasurement, alongside stable-gutter parsing,
layout, paint, hit testing, templates, empty containers and direct-Window
viewport propagation through `ScrollViewer`, `ListBox`, `DataGrid`,
`TreeDataGrid`, `TreeView`, `Markdown` and `Terminal` content. The 19 viewport
cases include native local precedence, default templates, designated viewer
selection (including grid data versus header), dynamic root declarations and
template detachment. Four scrollbar-color transition cases cover legacy/modern
color interpolation, premultiplied alpha, CSS steps and live bar-brush updates. Seventeen discrete-transition cases cover behavior parsing and list cycling, shorthand reset, default `normal`, initial style suppression, post-layout first declarations, eased width and gutter midpoint changes, scrollability, viewport reflow, color-to-`auto` theme restoration, CSS-wide resets, visibility and flex-collapse layout, pointer-events and cursor midpoint changes. Three image-transition cases verify inherited sampling pixels, rendered object size and default normal behavior.

The `user-select` increment covers parsing, parent-dependent `auto`, CSS-wide
keywords and live `inherit`, native local precedence, TextBlock and Label selection,
live ancestor changes, cursor precedence, and a discrete transition. Existing
selection survives a CSS change to `none`.
The mapping extension test now uses a unique unsupported property name so it
cannot replace a supported built-in property for later tests.

The `@keyframes` definition pipeline now retains case-sensitive names, valid `from`/
`to` and percentage selectors, duplicate frames in source order, and declarations
except `!important` and animation properties other than `animation-timing-function`.
Nested media/supports conditions, imports, resource bases and generated CSS preserve
the definitions. Malformed frame selectors recover without losing later blocks, and
diagnostics point to the source line. The 54 focused parser, code-generation and
compiled-import cases pass (`css-keyframes-compiled-imports.trx`). CSS `animation`
properties and keyframe playback were still unsupported at that stage; this was
not a runtime animation conformance claim.
The Release CSS/Flex/scroll-control/Window/ListBox/DataGrid/TreeDataGrid/input
regression then passed 4,734/4,734 without skips
(`css-keyframes-regression.trx`).

Keyframe lookup now resolves the last matching name in native document order,
using ordinal case-sensitive matching across application and attached sheets.
Conditional definitions are evaluated against the current document when looked up,
so viewport and system media changes can select a different winner without a stale
cache. Repeated selector offsets combine their declaration lists in source order
before property compilation; a later duplicate value replaces only its property.
The focused parser, lookup and generated-resource set passes 57/57
(`css-keyframes-resolution-final.trx`). Playback and cascade priority were added
after this resolution stage.

The keyframe pipeline now compiles declarations through the normal CSS property
registry and evaluates each offset against the target element's custom properties,
font size and `currentColor`. Invalid values are omitted for that property at
that offset. Frame-local timing functions are retained, and independent property
tracks synthesize missing 0%/100% endpoints from the underlying computed value.
The focused keyframe suite passes 25/25 (`css-keyframes-local-context.trx`), and
the Release CSS test filter passes 4,090/4,090 without skips
(`css-keyframes-frames-final-css.trx`). At this stage these were internal values;
CSS animation configuration and playback had not been implemented yet.

The animation sampling stage can now sample each property track using the existing
numeric and brush interpolators, frame-local easing, discrete fallback and
the supported `display` exit behavior. A separate timing calculation handles
positive/negative delays, fractional or infinite iterations, direction and
fill. Thirty-six focused sampling/timing cases pass
(`css-animation-sampler-display.trx`). At this stage CSS `animation-*`
declarations did not start a clock; transform interpolation, animation events
and cascade priority between keyframes and `!important` were also pending.

CSS `animation` and its eight longhands now parse into list-matched definitions and
start per-element keyframe clocks through the central animation manager. The first
animation in a list wins when tracks overlap. The runner samples normal, reversed,
alternating and fractional iterations, negative delays, forwards/backwards fill,
and paused state; removing a declaration clears its animated values. CSS keyframe
values outrank normal CSS declarations, while important declarations, native local
values and explicit native animations retain their framework precedence.
Composite background, border and box slots carry important-priority information.
Those native properties aggregate several CSS longhands, so an important
component currently masks the whole animated native property.
Focused parsing, playback, manager and priority tests pass; the Release CSS and
native-animation regression filter passes 4,149/4,149 without skips
(`css-transform-scale-final-regression.trx`). Full CSS animation conformance, events,
composition, 3D transform interpolation and platform pixels remain open.

The [CSS Transforms 1 interpolation model](https://www.w3.org/TR/css-transforms-1/#interpolation-of-transforms)
now drives supported 2D `transform` keyframes and transitions. Matched translate,
scale, rotate and skew functions interpolate their parameters, including authored
full turns. Missing list entries use the matching identity function; an unmatched
suffix is converted to matrices and interpolated through 2D decomposition.
Non-invertible matrix pairs use discrete sampling. Matrix reflection keeps the
selected scale axis and aligns opposite reflected axes before rotation.
CSS cubic Bézier output beyond 0–1 now extrapolates numeric and 2D transform
keyframes and transitions, including overshoot and undershoot. `skewX()`,
`skewY()` and `skew()` retain their CSS function identities: matching functions
interpolate angles while mixed names use matrix interpolation. Running keyframe
tracks refresh after computed-style and element-size changes while retaining clock progress;
tests cover implicit endpoints, custom properties and percent translations.

`transform-origin` accepts keywords, percentages, absolute and relative lengths,
and mixed length-percentage math in its two planar coordinates. Lengths resolve
against the `transform-box` reference rectangle and refresh when its size, insets
or length context changes; rotation bounds follow the resolved pivot. The third
Z coordinate, 3D transform functions and backend pixel comparisons remain open.

[CSS Transforms 2](https://www.w3.org/TR/css-transforms-2/#two-d-transform-functions)
also permits percentages in `scale()`, `scaleX()` and `scaleY()`; they now resolve
to numeric factors in direct values and keyframes, including calculated percentages.
For translate lengths and rotate/skew angles, a bare zero is valid but a nonzero
unitless argument is rejected. For CSS layout boxes, `transform-box` now uses one
reference rectangle for both percentage translation and the origin. `content-box`
and `fill-box` subtract the used border and padding insets; `border-box`,
`stroke-box` and `view-box` use the border box. The style pass resolves current
CSS border and padding before transforms, including percentage padding and native
local overrides. Discrete `transform-box` keyframes and `allow-discrete`
transitions switch the used rectangle at the sampled midpoint; with normal
transition behavior the box changes immediately. The focused transform-box
cases pass 16/16 (`css-transform-box-animation-2.trx`), and the expanded Debug
CSS/animation/transform filter passes 4,449/4,449
(`css-transform-box-animation-regression.trx`).
The previous three `CssBackdropFilterNativeTests` grayscale failures came from
test-output native DLLs older than the Rec.709 backdrop shaders and software
source. Rebuilding the stamped win-x64 Debug native payload and copying it through
the test project restored all three backend pixels
(`css-backdrop-native-rebuilt.trx`).

An ordered `backdrop-filter` list requires one backdrop capture, sequential stages on that snapshot, and one final composite. The current `DrawBackdropMaterial` samples and composites per call, while `CompositeBackdropEffect` collapses function order; neither path preserves CSS list semantics.

RichTextBox text-shadow inheritance includes Paragraph and FlowDocument declarations.
Explicit `none` and subsequent declaration removal are covered by direct and
recorded software/D3D12/Vulkan pixel tests.

[CSS Text Decoration Level 4 `text-decoration-inset`](https://drafts.csswg.org/css-text-decor-4/#text-decoration-inset-property)
is supported for horizontal CSS underline, overline and line-through strokes in
TextBlock, TextBox, AccessText and RichTextBox, including supported Run/Span/Paragraph
sources. It is non-inherited, accepts signed lengths, percentages, math and `auto`,
and leaves `text-decoration` shorthand values independent. With the default
[`box-decoration-break: slice`](https://drafts.csswg.org/css-break-4/#break-decoration),
percentages use the decorating source's total inline size, and the logical
start/end adjustments begin on its first/last line fragments. Oversized positive
insets carry across subsequent fragments. `box-decoration-break: clone` is
non-inherited and applies each adjustment independently to every fragment,
with percentages based on that fragment's width. Recorded geometry and
direct/recorded software, D3D12 and Vulkan pixels cover both modes.
Fragmented inline backgrounds, borders, padding, margins, masks and box shadows,
paged/multicolumn boxes, vertical writing, and author control of spelling and
grammar marker endpoints remain open.

[CSS Text Level 4](https://drafts.csswg.org/css-text-4/#white-space-property)
defines `white-space` as a shorthand. The implemented subset expands the legacy
keywords and combinations of `white-space-collapse`, `text-wrap-mode` and
`white-space-trim` into separately cascading values. The first two longhands
inherit; `white-space-trim` does not. TextBlock processes `discard` and
`preserve-spaces` without changing source or selection offsets, and trims its
outer blank lines with `discard-inner`. Run/Span `discard-before`/`discard-after`
remove adjacent whitespace, while `discard-inner` trims their own edge spaces.
`text-wrap-mode` also mirrors native `TextWrapping` on directly styled text
controls. Tests cover declaration order, inherited changes, CSS-wide keywords,
explicit inline breaks and style removal. Outside-edge trimming of an atomic
TextBlock, per-Run collapse/wrap overrides and whitespace
transformation in other text controls remain open.

[`tab-size`](https://drafts.csswg.org/css-text-4/#tab-size-property) accepts
nonnegative numbers and lengths, defaults to eight spaces and inherits.
TextBlock positions preserved tabs at successive stops in horizontal text,
including `text-indent`; numbers use its space advance with word and letter
spacing. Drawing, selection and hit testing anchor the stops to the block
content edge even on centered and right-aligned lines, including CSS padding
and consecutive tabs. Run/Span declarations can override the tab size.
Decoration endpoints and selected line-break positions use that same painted
line end. Exact width choice and wrapping after centered/right alignment,
transformed paint-wrap, bidi/vertical text, other text controls and full WPT
pixel coverage remain open. Twenty focused `tab-size` cases pass in Release;
the Debug CSS regression passes 3,895 cases without skips.

[`hyphens`](https://drafts.csswg.org/css-text-4/#hyphens-property) accepts
`none`, `manual`, and `auto`, defaults to `manual`, and inherits into TextBlock
Run/Span content. TextBlock keeps U+00AD SOFT HYPHEN in source and selection
space while omitting it from unbroken paint. `manual` and `auto` can wrap at
that point, count the inserted marker in line width, then draw it with the
soft hyphen's Run font and color. `none` suppresses the conditional opportunity
without suppressing breaks after visible hyphens. `auto` currently uses explicit
soft hyphens only. The inherited
[`hyphenate-character`](https://drafts.csswg.org/css-text-4/#hyphenate-character)
accepts `auto` or a CSS string. Custom strings, including `""`, replace the
marker at a soft-hyphen break; their width can select an earlier word break.
The current `auto` marker is `-`. Automatic language dictionaries, language-
specific marker choice and placement remain open. Tests cover syntax,
unbroken/forced paint, Run styling, marker width and inherited CSS changes.

The inherited
[`hyphenate-limit-chars`](https://drafts.csswg.org/css-text-4/#hyphenate-limit-chars)
accepts one to three `auto` or nonnegative integer values for the word, before
the break, and after the break. An omitted third value repeats the second;
`auto` currently uses the draft's suggested 5/2/2 counts. TextBlock rejects
explicit soft-hyphen opportunities that fail a limit and excludes Unicode Mn
marks and recognized intra-word punctuation from the count. Run/Span overrides
and live inheritance changes relayout the text. Full Unicode word segmentation
and automatic dictionary hyphenation remain open.

The inherited
[`hyphenate-limit-lines`](https://drafts.csswg.org/css-text-4/#hyphenate-limit-lines)
accepts `no-limit` or a nonnegative integer for block containers. TextBlock
tracks successive lines ending at an accepted U+00AD break, then suppresses
further soft-hyphen breaks until a line without one resets the count. The
initial `no-limit` value leaves the count unrestricted; `0` suppresses all
soft-hyphen breaks. A word may overflow rather than take a disallowed break.
Emergency wrapping can still split words without a hyphen marker. Other
emergency-breaking interactions and block containers remain open.

The inherited
[`hyphenate-limit-zone`](https://drafts.csswg.org/css-text-4/#hyphenate-limit-zone)
accepts a CSS length or percentage and starts at `0`. TextBlock compares a
soft-hyphen break with the nearest available ordinary break. If the latter
leaves no more than the zone of unused width before justification, the ordinary
break wins; otherwise the soft hyphen can pull text onto the line. Percentages
resolve against the full line box width rather than the width remaining after
text indentation. With no ordinary break available, soft hyphenation can still
prevent word overflow.
Automatic dictionary hyphenation and other block containers remain open.

The inherited
[`hyphenate-limit-last`](https://drafts.csswg.org/css-text-4/#hyphenate-limit-last)
accepts `none`, `always`, `column`, `page`, and `spread`. In TextBlock,
`always` avoids a soft-hyphen break on the last full line before the element's
final line. Earlier forced-break groups retain their hyphenation opportunities.
The fragmentation-specific values parse and cascade, but TextBlock has no
column, page, or spread break model on which to apply them yet.

The [`text-wrap` shorthand and `text-wrap-style`](https://drafts.csswg.org/css-text-4/#text-wrap-style)
now cascade independently from `white-space-collapse`. TextBlock compares
line-break candidates at narrower widths while retaining the actual container
width for alignment and percentage indentation. `balance` reduces the variance
of occupied line widths within each forced-break group without changing its
greedy line count; groups over ten lines use `auto`. `stable` uses the greedy
breaks for static TextBlock content. `pretty` and `avoid-short-last-line` also
consider final-line shortfall; the latter avoids making preceding lines very
short. Their search is bounded to 24 lines and 4096 source code units per
group. Text wrapping style in other controls, floats, complex bidi and vertical
writing remain open.

## WPF coexistence requirements

The default layout path remains native. CSS formatting boxes reference the original
controls and never replace user Children collections, namescopes, templates or bindings.
Explicit display declarations select a formatting algorithm through Measure/Arrange;
removing them restores the native algorithm. Native local values and bindings outrank
CSS; native Storyboards retain native animation precedence.
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

### CSS input preprocessing

Before tokenization, stylesheet strings, decoded external stylesheets and inline
declarations follow [CSS Syntax 3 input preprocessing](https://drafts.csswg.org/css-syntax-3/#input-preprocessing):
CR, form feed and CRLF become a single LF; NUL and unpaired UTF-16 surrogates
become U+FFFD. Valid surrogate pairs remain intact. The same normalized text
drives selector matching, declaration values and diagnostic line numbers.

### CSS syntax recovery boundaries

[CSS Syntax 3 rule and declaration consumption](https://drafts.csswg.org/css-syntax-3/#parser-algorithms)
keeps semicolons inside component-value blocks or escapes from ending the current
construct. The parser now tracks matching parentheses, brackets and braces when
skipping at-rules, style blocks and malformed inline declarations. An unescaped
newline ends a malformed string token, allowing the following rule to be parsed;
a trailing backslash in an unterminated string stops safely at EOF. Regression
tests also check that diagnostic line numbers include escaped newlines.

### Declaration priority tokens

[CSS Syntax 3 declaration consumption](https://drafts.csswg.org/css-syntax-3/#consume-declaration)
sets the important flag only when the last two non-whitespace component values
are a literal `!` delimiter and an ASCII-insensitive `important` identifier.
The parser now recognizes CSS escapes within that identifier, rejects escaped
`!` characters and nested or quoted suffixes, and trims only CSS whitespace
around declaration values. Non-breaking spaces remain part of an identifier.

### Legacy stylesheet comment tokens

[CSS Syntax 3 stylesheet consumption](https://drafts.csswg.org/css-syntax-3/#parser-algorithms)
discards `<!--` and `-->` tokens at the stylesheet top level. The runtime
parser now does so before classifying the next rule, keeping neighboring style
rules and `@import` placement intact. In nested blocks these tokens remain
part of the ordinary rule input.

### Custom property value syntax

[CSS Variables 1](https://drafts.csswg.org/css-variables-1/#syntax) permits an
empty custom-property value but requires nonempty values to match
`<declaration-value>`. The parser now drops declarations containing bad string
or URL tokens, unmatched closing brackets, or a top-level `!` after removing a
valid trailing `!important`; escaped and nested `!` remain valid. It also rejects
the reserved `--` property name. An invalid later declaration therefore leaves
an earlier valid custom-property value in the cascade. Quoted `url()` functions
remain valid after any amount of CSS whitespace following `(`, and variable
substitution does not interpret `var` text inside hash, at-keyword, or dimension
tokens as a function.

The [CSS Snapshot 2026](https://www.w3.org/TR/2026/NOTE-css-2026-20260622/)
includes the [2022 Variables 1 CR](https://www.w3.org/TR/css-variables-1/).
The newer [CSS Variables 2 draft](https://drafts.csswg.org/css-variables-2/#replace-a-var-function)
permits the first `var()` argument to become a custom-property name after
substitution. The parser accepts a nonempty token sequence; variable lookup,
name validation and fallback selection happen at computed-value time. Nested
`var()`, `attr()` names, braced names and invalid-name fallbacks are covered;
static and dynamic name references both participate in cycle detection.
Malformed declaration tokens and an empty first argument are still rejected.
[CSS Syntax 3](https://www.w3.org/TR/css-syntax-3/) closes functions at EOF,
which the function reader applies to variable fallbacks. Name substitution
through `if()` and `random-item()` is supported. Three adjacent dots before a supported arbitrary
substitution function trigger early replacement inside `var()` argument lists,
so a replacement can provide the name and fallback comma.

[CSS Conditional 3](https://www.w3.org/TR/css-conditional-3/#support-definition)
defines declaration support by whether parsing would keep that declaration.
`Css.Supports(property, value)`, `Css.Supports(condition)`, and `@supports`
now use the same declaration-value token check as style parsing, so invalid
custom-property values and empty `var()` first arguments cannot claim support.
`var(--missing)` and `var(foo)` both pass a support query because variable lookup
and name validation happen at computed-value time. The shared check also ignores an invalid
`initial-value` descriptor in an `@property` rule; with universal `syntax: '*'`,
the registration can still succeed without that optional descriptor.

### Conditional values

[CSS Values 5 `if()`](https://drafts.csswg.org/css-values-5/#if-notation)
is parsed as ordered condition/value branches. Conditions may combine supported
`style()`, `media()` and `supports()` tests; only the selected branch is
substituted. A false `style()` test of the active custom property can select a
later branch without making that property cyclic, while a selected value that
references itself remains invalid. Custom-property `style()` comparisons reuse
the container style-query computed-value rules, including registered syntax.
Attribute values used by a condition taint the selected result for `url()` and
`src()` checks. Conditional `revert-rule` results enter the ordinary cascade.
The [CSS Values 5 spread syntax](https://drafts.csswg.org/css-values-5/#component-function-commas)
substitutes `...var()`, `...attr()`, `...env()` and nested `...if()` before
splitting `if()` branches. Unmarked nested functions and unselected branch values
remain lazy. The three dots must be adjacent to one another and the function;
outside an arbitrary substitution function they retain their literal meaning.
The current draft's active-property style-query rule differs from some older
[WPT `if-cycle.html` expectations](https://github.com/web-platform-tests/wpt/blob/master/css/css-values/if-cycle.html);
the local self-query regression follows the current draft. The remaining
conditional corpus is pending.

### Random values

[CSS Values 5 `random-item()`](https://drafts.csswg.org/css-values-5/#funcdef-random-item)
now splits the key and options at the top-level commas before substituting
variables. Only the selected option is substituted. A `fixed` key chooses a
deterministic index; other keys share a stable random base according to their
name, property/index scope and element scope. Free-form `{}` wrappers allow
commas within one option. Parse-time support checks validate the argument
structure while deferring key validity to computed time. Selection also works
through custom properties, `var()` names, shorthands and `...` spread syntax.
The numeric [`random()`](https://drafts.csswg.org/css-values-5/#random) function
uses the same random base cache and resolves its range and optional step after
unit and percentage conversion. Unregistered custom properties preserve the
function until substitution into a typed property; registered non-universal
properties compute it before inheritance. Random functions evaluated in
`if()` conditions use `ua-early-*` scopes and an index counter separate from
the selected property's random values, including conditions computed for
unregistered custom properties. The current index counters follow evaluated
substitutions; exact index assignment through nested substitution and spread
syntax, and computed serialization, remain pending. Early spread substitution
leaves numeric functions in `random-item()` options unevaluated until an
option is chosen, and validating an unused registered `var()` fallback uses
its own random index context.

### Stylesheet byte encoding and MIME type

The [CSS Syntax 3 decoding rules](https://www.w3.org/TR/css-syntax-3/#input-byte-stream)
now determine external stylesheet text from a BOM, HTTP `Content-Type` charset,
an exact initial `@charset "…";` byte sequence, then the referring sheet's
encoding for `@import`, with UTF-8 as the XAML document default. A BOM overrides
the other hints. [CSS Cascade 3 imports](https://www.w3.org/TR/css-cascade-3/#processing-stylesheet-imports)
inherit the parent encoding when the imported resource has no own hint; one
fetched resource can be parsed under two parent encodings without another fetch.
Only UTF-8 and UTF-16 BOMs override the fallback encoding; UTF-32 prefixes
follow the standard UTF-16 or UTF-8 decoding rules. Encoding labels use ASCII
case-insensitive matching, including quoted transport charset values. An
explicit non-`text/css` Content-Type rejects that resource. `FromStream`,
`FromUri`, async loading and build-time CSS compilation share byte decoding.
The loader recognizes all 228 labels in the pinned
[WHATWG Encoding Standard data](https://encoding.spec.whatwg.org/), including
compatibility aliases. Its 28 legacy single-byte decoders use the standard's
indexes, including ISO-8859-10, ISO-8859-14 and ISO-8859-16, which lack .NET
code pages. `replacement` and `x-user-defined` have dedicated decoders. Seven
legacy multibyte encodings also use pinned standard indexes and state machines:
GBK and gb18030 share the same decoder; Big5 handles its four two-code-point
mappings; EUC-JP, ISO-2022-JP, Shift_JIS and EUC-KR use the corresponding JIS
and Korean indexes. The resource tests cover representative mappings, malformed
byte recovery, MIME rejection and import inheritance. The complete malformed-byte
corpus and host-provided environment encodings for document formats beyond XAML
remain to be verified.

### Display resolution media queries

The [Media Queries 4 `resolution` feature](https://www.w3.org/TR/mediaqueries-4/#resolution)
now uses the native root's vertical device-pixel ratio. `dppx`/`x`, `dpi`,
`dpcm`, calculated values, boolean queries, min/max forms, reversed and chained
ranges, and `infinite` comparisons share the existing media boolean grammar.
Changing the root DPI re-evaluates CSS on its descendants, including the host
window DPI-notification path. The Release CSS, toolbar and theme regression
passed 2,479 tests without skips in
`tests/Jalium.UI.Tests/TestResults/css-media-resolution-final.trx`.
This does not claim the remaining media features or device-specific DPI execution.

The [Media Queries 4 color features](https://www.w3.org/TR/mediaqueries-4/#color)
report the current eight-bit sRGB output pipeline: `color` is 8 bits per
component, `monochrome` and `color-index` are zero, and `color-gamut:srgb`
matches while `p3` and `rec2020` do not. The
[Media Queries 5 `scripting` feature](https://www.w3.org/TR/mediaqueries-5/#scripting)
matches `none` because this native XAML document has no script runtime.
Unknown values and invalid range syntax remain unknown under negation. These
queries select real CSS rules on the native control tree. The expanded Release
CSS, toolbar and theme regression passed 2,504 tests without skips in
`tests/Jalium.UI.Tests/TestResults/css-media-capabilities-final.trx`.

The [Media Queries 5 `prefers-reduced-motion` feature](https://www.w3.org/TR/mediaqueries-5/#prefers-reduced-motion)
uses the existing native client-area animation and UI-effects settings. Disabled
system animation matches `reduce`; otherwise `no-preference` matches. The
boolean form follows `reduce`, and invalid values stay unknown under negation.
System-setting notifications re-evaluate affected CSS nodes through weak
observers, including detached trees. Windows and Linux provide the current
animation-setting inputs; other-platform preference detection remains to be
verified. The expanded Release CSS, toolbar and theme regression passed 2,512
tests without skips in
`tests/Jalium.UI.Tests/TestResults/css-prefers-reduced-motion-weak-final.trx`.

The [Media Queries 5 `prefers-color-scheme` feature](https://www.w3.org/TR/mediaqueries-5/#prefers-color-scheme)
reads the user's desktop preference independently of an application's explicit
Light/Dark theme. Windows uses `AppsUseLightTheme`; Linux uses the existing desktop
portal color-scheme subscription. The boolean form matches, `light` includes no
expressed preference, and invalid values stay unknown under negation. System
preference notifications re-evaluate affected CSS nodes through the same weak
observer used for reduced motion. Other-platform preference detection and live
desktop transitions still need device verification. The expanded Release CSS,
toolbar and theme regression passed 2,520 tests without skips in
`tests/Jalium.UI.Tests/TestResults/css-prefers-color-scheme-final.trx`.

The [Media Queries 5 `prefers-contrast` feature](https://www.w3.org/TR/mediaqueries-5/#prefers-contrast)
recognizes `no-preference`, `less`, `more` and `custom`. Windows' high-contrast
setting and Linux's [desktop portal contrast setting](https://flatpak.github.io/xdg-desktop-portal/docs/doc-org.freedesktop.portal.Settings.html)
or high-contrast theme currently supply `more`; otherwise `no-preference`
matches. The boolean form matches only when more contrast is requested. The
available host inputs cannot signal `less` or `custom`, so those valid queries
currently do not match. Unknown values stay unknown under negation. System
notifications, including Windows system-color changes, refresh subscribed CSS
nodes. `forced-colors: active` is not reported because the renderer does not
yet enforce a user color palette. Live desktop transitions need device testing.
The expanded Release CSS, toolbar and theme regression passed 2,532 tests
without skips in
`tests/Jalium.UI.Tests/TestResults/css-prefers-contrast-final.trx`.

The [Media Queries 5 `display-mode` feature](https://www.w3.org/TR/mediaqueries-5/#display-mode)
reports `standalone` for native windows and detached XAML trees, switching to
`fullscreen` while the host `WindowState` is `FullScreen`. The boolean form
matches in both states; the web-only `browser`, `minimal-ui` and
`picture-in-picture` modes do not match this host. Window-state changes
invalidate CSS on the hosted tree even if the viewport dimensions do not
change. The [Media Queries 5 pointing features](https://www.w3.org/TR/mediaqueries-5/#pointer)
now distinguish the primary device from all attached devices. A touchscreen
and mouse can match both `any-pointer: coarse` and `any-pointer: fine`. The
desktop mouse/trackpad is primary when present; touch remains primary on Android.
Pen-only devices report `pointer: fine` without assuming hover. The boolean
forms and `none` values follow the same inventory. Invalid keywords and range
syntax remain unknown under `not`.

Windows combines a ready digitizer with the raw mouse device list and receives
`WM_DEVICECHANGE`; RDP uses its virtual mouse status. Wayland uses seat pointer,
touch and tablet notifications. X11 uses enabled XInput2 slave devices and
hierarchy notifications, excluding the virtual master and XTEST pointer. When
XInput2 is unavailable, the feature is unknown instead of claiming no pointer.
Android enumerates each `InputDevice` source. The Android entry Activity now
registers an [input-device listener](https://developer.android.com/reference/android/hardware/input/InputManager.InputDeviceListener)
while resumed and forwards additions, removals and source changes to the CSS
dispatcher. Resume and observed pointer input (throttled to one query per
second) remain refresh fallbacks for hosts without that listener. macOS
currently assumes a conventional fine hovering pointer, and iOS reports its
touchscreen without enumerating optional
external mice or pencils. These Apple results require hardware-backed inventory.
All observed capability changes invalidate subscribed CSS nodes. Desktop hot-plug,
Android and Apple device behavior still need device validation.
The 35 focused pointing cases and the expanded 2,627-case CSS, touch, toolbar
and theme regression passed without skips in
`tests/Jalium.UI.Tests/TestResults/css-pointing-cached-final.trx`.
After adding the Android listener, the focused pointing/touch run passed 38 cases
in `tests/Jalium.UI.Tests/TestResults/css-pointing-android-observer-focused.trx`.
Windows core, platform and D3D12 libraries and the Linux platform library built.
The Android managed entry package and both full native ABI payloads built with
NDK 27.2.12479018; their ELF libraries passed 16 KiB alignment checks. The
default Release AOT demo built and its signed APK installed and launched on an
API 24 x86_64 emulator. The process remained alive after touch input, with
native platform initialization and no fatal exception in logcat. A local NuGet
pack inspection retained the build-tool and source-generator dependencies;
the pack provenance guard was explicitly relaxed for this inspection because
the working tree already contains uncommitted native changes. Actual device
addition/removal callbacks and CSS changes from hot-plug remain unverified.
Apple binaries and device behavior were not exercised on this host.
The expanded Release CSS, toolbar and theme regression passed 2,547 tests
without skips in `tests/Jalium.UI.Tests/TestResults/css-display-mode-final.trx`.

The Media Queries 5 [`grid`](https://www.w3.org/TR/mediaqueries-5/#grid),
[`environment-blending`](https://www.w3.org/TR/mediaqueries-5/#environment-blending)
and [`dynamic-range`](https://www.w3.org/TR/mediaqueries-5/#dynamic-range)
features now report the current native presentation path: bitmap `grid: 0`,
`environment-blending: opaque`, and `dynamic-range: standard`. The eight-bit
per-component output cannot meet the `dynamic-range: high` color-depth
requirement, even on an HDR-capable physical monitor. Their boolean forms
follow the specified truth values, and invalid values or range syntax stay
unknown under negation. The expanded Release CSS, toolbar and theme regression
passed 2,568 tests without skips in
`tests/Jalium.UI.Tests/TestResults/css-media-display-quality-final.trx`.

The [Media Queries 5 video features](https://www.w3.org/TR/mediaqueries-5/#video-prefixed)
report the same output capabilities because native video surfaces are drawn into
the eight-bit graphics target rather than a separate display plane.
`video-color-gamut: srgb` and `video-dynamic-range: standard` match;
`p3`, `rec2020` and `high` do not. Boolean queries match, while invalid values
and range syntax remain unknown under negation. The expanded Release CSS,
toolbar and theme regression passed 2,581 tests without skips in
`tests/Jalium.UI.Tests/TestResults/css-media-video-output-final.trx`.

### Font support feature queries

The [CSS Conditional Rules 5 font queries](https://www.w3.org/TR/css-conditional-5/#at-supports-ext)
now share a capability table with `@font-face src` hints. `font-format()` returns true
for the formats that the private font path can decode and register: `truetype`,
`opentype`, `woff`, `woff2` and `collection`. `font-tech()` returns true for
`features-opentype`, which the native shapers use through GSUB/GPOS. Variation
selection, palette controls, individual color-font technologies and incremental
transfer remain false until the complete corresponding rendering paths are
verified. A supported `tech(features-opentype)` source is now eligible for loading;
unsupported hints are still skipped before fetching.

The nine font-query assertions from WPT `css/css-conditional/js/CSS-supports-L5.html`
are ported to the native `Css.Supports(condition)` overload. They cover a known format and
technology, unknown names, lists and quoted strings. Additional cases cover every
advertised format, unsupported technologies, boolean composition, real `@supports`
style application, conditional import fetching, generated CSS round trips and font
source fallback. The Windows CSS/Flex regression passed 1,560 cases with no skips in
`artifacts/css-font-support-validation/test-results/css-font-repeat-final-v3.trx`.
This is a query and source-selection increment, not complete CSS Fonts 4 support.

### CSS Color 4 absolute colors

The shared CSS color parser now accepts the absolute functional forms in
[CSS Color 4](https://www.w3.org/TR/css-color-4/): `hwb()`, `lab()`, `lch()`,
`oklab()`, `oklch()` and `color()` in sRGB, linear sRGB, Display P3, linear P3,
A98 RGB, ProPhoto RGB, Rec. 2020 and the three XYZ spellings. Percent reference
ranges, angles, optional alpha, `none` as zero for direct rendering, and the modern
space/slash grammar are parsed before conversion. Out-of-sRGB colors use the
specification's binary-search chroma reduction with local MINDE on the current
sRGB surfaces. `Color` retains linear sRGB channel values; its current rendering
backends use the mapped sRGB result.

The existing RGB/HSL paths now accept `none` in modern syntax, allow numeric
S/L in modern HSL, require component separation, and reject mixed number/percentage
components in comma-separated legacy RGB. They retain the native color and text
property precedence rules.

`CssColor4Tests` retains the input values and green references from WPT
`hwb-001`, `lab-001`, `lch-001`, `oklab-001`, `oklch-001` and `predefined-001`.
Tests also cover invalid grammar, percent reference ranges, cross-space examples,
background/border values, custom converters, `@supports`, variables and registered
`<color>` values. `CssColor4NativeTests` checks the five WPT green cases at an
interior pixel on Windows software, D3D12 and Vulkan. The final Windows CSS
class-name regression passed 1,403 cases with no skips in
`artifacts/css-color4-validation/test-results/css-color4-full-regression-final.trx`;
the native backend run passed three tests in `css-color4-native-final.trx`.
Linux passed 35 focused `CssColor4Tests` cases with no skips in
`css-color4-linux-final.trx`. That Linux run used the available older SDK, which
reports a source-generator compiler-version warning; it does not qualify Linux
generated XAML or native rendering for this increment.

Standalone colors still map to linear sRGB for the native surface. CSS gradients
now retain source channels and `none` markers while constructing their color ramp;
see the increment below. Typed custom-property computation and animation still
lose that source identity. Wide-gamut display output, relative colors, color
mixing and the full WPT/device corpus remain open. The WPT ports here preserve
individual colors and reference pixels, not their HTML page layout.

### CSS Color 5 color mixing

The [2 September 2026 CSS Color 5 draft](https://www.w3.org/TR/2026/WD-css-color-5-20260902/#color-mix)
adds `color-mix()` with Oklab as the default interpolation space. The parser now
accepts one or more absolute colors, optional percentages before or after each
color (including constant calculated percentages), and all fifteen supported interpolation
spaces with four polar hue methods. It normalizes omitted and over-100% weights,
multiplies alpha when fully specified weights sum below 100%, and returns
transparent for an all-zero mix. Multi-color mixes proceed in source order.
Nested mixes retain source-space channels and missing-component markers before
final sRGB surface mapping, so an intermediate wide-gamut color is not clipped
before the next mix. The common color parser makes this available to CSS color
properties, custom-property substitution and gradient stops.

`CssColorMixTests` checks selected valid/invalid WPT grammar, numeric channels,
alpha premultiplication, hue direction, nested gamut/missing-component behavior,
variables, brushes and gradient integration. Generated CSS also round-trips a
mix value. The Windows CSS/Flex regression passed 1,602 cases without skips in
`artifacts/css-color-mix-validation/test-results/css-color-mix-regression-final-v2.trx`.
This run used the previously rebuilt Vulkan DLL described in the repeating-gradient
section. Element-dependent calculated weights, full WPT serialization and
wide-gamut display surfaces remain open. Relative colors are covered below.
CurrentColor-dependent
mixes in the main color properties are addressed below.

### CSS Color 5 relative colors

[CSS Color 5](https://www.w3.org/TR/css-color-5/#relative-colors) allows
`from <color>` in the modern RGB, HSL, HWB, Lab/LCH, Oklab/OkLCh and predefined
`color()` functions, plus `alpha()`. The parser converts the origin into the
target processing space before exposing its named channels, supports channel
permutations and math, inherits omitted alpha, clamps explicit alpha, and
carries missing channels and out-of-gamut source values into later color mixes
and gradients. Relative results select modern Oklab gradient interpolation by
default. `currentColor` origins stay element-dependent for background, border,
outline, color and gradient stops, including inherited expressions and CSS
variables. Current surfaces still map the final color to sRGB for painting.

`CssRelativeColorTests` checks CSS Color 5 examples, all ten predefined
`color()` spaces, cross-space origins, nested forms, invalid comma syntax,
missing components, gamut retention, inheritance, dynamic `currentColor`,
gradients and generated CSS. `CssRelativeColorNativeTests` verifies painted
colors and gradients on software, D3D12 and Vulkan. The Windows CSS/Flex
regression passed 1,742 cases without skips in
`artifacts/css-relative-color-validation/test-results/css-relative-color-regression-final.trx`.
Custom `@color-profile` spaces, browser CSSOM serialization, complete WPT
corpora, wide-gamut output surfaces and Linux/Metal native pixels remain open.

### Element-dependent currentColor colors

Per [CSS Color 4 currentColor resolution](https://www.w3.org/TR/css-color-4/#currentcolor-color)
and [CSS Color 5 mixed-color resolution](https://www.w3.org/TR/2026/WD-css-color-5-20260902/#resolving-mix),
mixes containing `currentColor` now keep their expression until they are applied
to an element. On `color`, the mix uses the parent's foreground; on background,
border and outline colors it uses the element's winning foreground, including a
native local value. Nested mixes, escaped `currentColor` identifiers, CSS
variables, foreground property replacements and in-place `SolidColorBrush.Color`
changes follow that same path. Explicit `inherit`
retains a symbolic background/border/outline expression so descendants resolve
it against their own color. Weak child subscriptions update inherited values
when a parent's native brush changes and are removed when inheritance stops.

Linear, radial and conic gradients also accept direct or mixed `currentColor`
stops. They validate with a placeholder at compile time, capture the styled
element's foreground at apply time, and substitute it when resolving the used
paint size. Managed tests cover same-element updates, native foreground changes,
conic pixels, generated CSS, and selected WPT currentColor inheritance cases.
Weak brush subscriptions follow source replacement and are released when a
declaration stops depending on the mutable foreground.
Per-element frozen-brush reuse keeps repeated CSS evaluation stable; the focused
tests require the evaluation queue to drain after one flush. The Windows CSS/Flex
regression passed 1,640 cases with no skips in
`artifacts/css-currentcolor-validation/test-results/css-currentcolor-mutable-regression.trx`.
The isolated test directory uses the previously rebuilt Vulkan DLL.
Typed registered-color source preservation and whole-image/device validation
remain open. Shadow colors are covered in the following section.

### Contextual shadow colors and filter defaults

[CSS Backgrounds and Borders 3](https://www.w3.org/TR/css-backgrounds-3/#box-shadow)
sets an omitted `box-shadow` color to `currentColor`. The
[Filter Effects 1 drop-shadow definition](https://www.w3.org/TR/filter-effects-1/#funcdef-filter-drop-shadow)
also takes its omitted color from `color`, accepts two or three lengths, and
rejects spread and inset. Its `blur()` function accepts an omitted length as
zero. The shared shadow parser now enforces those forms and resolves explicit
`currentColor` or nested `color-mix()` for the styled element. A changed CSS
foreground or mutable native foreground brush updates the effect; per-element
effect reuse keeps CSS evaluation stable. Native local `Effect` values retain
priority, then reveal the updated CSS effect when cleared. Mixed shadow alpha
is transferred to native effect opacity.

`CssShadowColorTests` checks omitted/direct/mixed colors, combined filters,
mutable foreground updates, alpha, native precedence, `blur()` and invalid
shadow forms. The Windows CSS/Flex regression passed 1,650 cases with no skips
in `artifacts/css-shadow-color-validation/test-results/css-shadow-color-regression-final.trx`.
The isolated test output uses the previously rebuilt Vulkan DLL documented
above. These are native XAML assertions from the specifications, not a WPT
page port. Shadow blur unit conversion is covered below; full visual parity
remains open.

### Outer box-shadow spread

[CSS Backgrounds 3 shadow shape](https://www.w3.org/TR/css-backgrounds-3/#shadow-shape)
expands an outer shadow's perimeter for positive spread and contracts it for
negative spread. The `DropShadowEffect` now retains `SpreadRadius`, includes
positive spread in capture padding and expands or contracts each used corner
radius, including the small-radius cubic correction. Software, D3D12 and
Vulkan draw the spread contour behind the unchanged captured content. A new
optional native side interface keeps the existing backend vtable stable;
older native payloads fall back to the unspread effect with a diagnostic.

`CssOuterShadowNativeTests` probes the side interface and compares positive,
zero and negative spread plus rounded-corner pixels on all three Windows
backends. The Vulkan CPU capture path also passed with
`JALIUM_VK_EFFECT_CPU_CAPTURE=1` and `JALIUM_VK_EFFECT_GPU_RT=0`. The full
Windows CSS/Flex filter passed 1,655 cases without skips in
`artifacts/css-outer-spread-validation/test-results/css-outer-spread-regression-final.trx`,
using core/software/D3D12/Vulkan DLLs rebuilt in the isolated native output.
Metal and Linux execution of this increment remain unverified. Exact CSS
Gaussian blur width and whole-image WPT parity are still open.

### CSS shadow blur units

[CSS Backgrounds 3](https://www.w3.org/TR/css-backgrounds-3/#shadow-blur)
defines a box-shadow blur radius whose Gaussian standard deviation is half
that radius. [Filter Effects 1](https://www.w3.org/TR/filter-effects-1/#funcdef-filter-drop-shadow)
instead takes the optional third `drop-shadow()` length as the standard
deviation itself. CSS effects now retain that standard deviation through
cloning while exposing the authored length through the existing native
`BlurRadius` property. The drawing context converts it for the software
three-pass box blur and for the analytic software/D3D12/Vulkan shadow paths.
Capture padding includes the resulting Gaussian reach.

The software backend also stopped premultiplying shadow RGB before a blit
that already applies alpha, correcting overly dark blurred edges. Managed
tests check both CSS units and clone preservation. Native pixels compare a
12px box-shadow blur with a 6px filter drop-shadow standard deviation across
software, D3D12 and Vulkan. The Windows CSS/Flex regression passed 1,656
cases without skips in
`artifacts/css-shadow-blur-validation/test-results/css-shadow-blur-regression.trx`.
An additional 26 native effect/capture regressions passed. The run uses the
isolated native DLLs from the outer-spread validation with a rebuilt software
DLL. Software's box kernel is still an approximation. D3D12/Vulkan at this
checkpoint still shaded the box contour for `drop-shadow()`; that is corrected
below.
`filter: blur()` is covered below. Exact whole-image pixels, Metal and Linux
execution remain open.

### CSS filter blur standard deviation and paint extent

[Filter Effects 1](https://www.w3.org/TR/filter-effects-1/#funcdef-filter-blur)
defines `blur()`'s length as its Gaussian standard deviation. CSS blur effects
now retain that value through cloning, convert it for the software and GPU
kernels, and capture/draw the full three-sigma apron around the element. The
CSS path no longer clips the blurred output back to the element's rounded
border; native `BlurEffect` instances keep their existing radius and clip
behavior.

Software blurs premultiplied channels before converting to its straight-alpha
blit. D3D12 and Vulkan shader assets were rebuilt after fixing the sRGB/linear
conversion of premultiplied pixels, which had made translucent edges too
bright. Vulkan's explicit CPU-capture path now really selects CPU rendering,
uses a separable Gaussian for the fallback blur, and composites full-surface
captures without scaling them into clipped element rectangles.

Managed tests verify CSS sigma and clone retention. Software, D3D12 and
Vulkan pixel tests compare `blur(6px)` with a 12px box-shadow blur, check
paint outside the box and its rounded corner, and check half-alpha content.
The Windows CSS/Flex regression passed 1,657 cases without skips in
`artifacts/css-filter-blur-validation/test-results/css-filter-blur-regression-final.trx`;
74 additional effect/capture/backdrop regressions passed in
`artifacts/css-filter-blur-validation/test-results/effect-backdrop-regression-final.trx`.
The forced Vulkan CPU path passed separately in
`artifacts/css-filter-blur-validation/test-results/css-filter-blur-vulkan-cpu-final.trx`.
Effect length units are covered below. Full filter-chain compositing,
whole-image WPT parity, and Linux/Metal execution remain open.

### Relative shadow and filter lengths

[CSS Values 4 lengths](https://www.w3.org/TR/css-values-4/#lengths)
allow unitless zero, while nonzero lengths need a unit. `box-shadow`,
`filter: blur()` and `filter: drop-shadow()` now keep supported font-relative,
viewport, container-relative and math lengths until application to an element.
Percentages and nonzero unitless lengths are rejected. The resolved pixel
values form a per-element effect cache key, so font-size changes, viewport
metric updates and query-container resizes recreate only effects whose used
lengths changed. Container and font-metric dependencies use the shared weak
observers. Direct negative blur lengths invalidate the declaration;
[out-of-range math results](https://www.w3.org/TR/css-values-4/#calc-range)
are clamped to zero at computed-value time and can recover when their font
basis changes.

`CssEffectLengthTests` checks grammar, CSS and native font-size changes,
calculated lengths, viewport/container updates, stable effect reuse and
computed negative-value recovery. Generated CSS round-trips the same syntax
and updates its effects after font-size changes. Native software/D3D12/Vulkan
pixels verify an `em`-based outer spread changes with font size. The Windows
CSS/Flex regression passed 1,675 cases without skips in
`artifacts/css-effect-length-validation/test-results/css-effect-length-regression-final.trx`,
using the previously rebuilt and hash-verified core/software/D3D12/Vulkan DLLs.
Filter-chain work continues below; whole-image WPT comparisons and Linux/Metal
execution remain open.

### Ordered color-matrix filter chains

[Filter Effects 1](https://www.w3.org/TR/filter-effects-1/#FilterProperty)
passes the output of each filter function to the next. A native `EffectGroup`
instead draws every child from the same captured source. A `filter` value
containing only color-matrix functions now retains each ordered stage in a
dedicated effect. An optional native side interface applies each matrix to
straight RGBA, clamps the four outputs, then feeds that result to the next
stage. Software runs the stages per pixel; D3D12 and Vulkan use shaders over
the isolated capture. This covers ordered
brightness, contrast, grayscale, hue rotation, invert, opacity, saturate and
sepia chains up to 64 stages on all three Windows backends. Native software,
D3D12 and Vulkan pixel tests distinguish `brightness(.5) invert(1)` from its
reverse, verify that two `opacity(.5)` functions multiply to one-quarter
opacity. They also verify intermediate clipping: `brightness(2) contrast(.5)`
on RGB(180,0,0) yields approximately RGB(191,64,64), not the unclipped fused
matrix result. Opacity chains with 20 and 64 stages render through the native
loop; 65 stages exercise the documented GPU fallback.
Generated CSS and effect cloning retain the ordered stages.
The Windows CSS/Flex regression passed 1,685 cases without skips in
`artifacts/css-filter-matrix-clamp-validation/test-results/css-filter-matrix-clamp-regression-final.trx`;
74 effect/capture/backdrop regressions passed in
`artifacts/css-filter-matrix-clamp-validation/test-results/effect-backdrop-regression-final.trx`;
the forced Vulkan CPU test passed in
`artifacts/css-filter-matrix-clamp-validation/test-results/css-filter-matrix-vulkan-cpu-final.trx`.
The test output uses hash-verified
core/software/D3D12/Vulkan DLLs from the isolated native build.

Older native payloads and GPU chains longer than 64 stages fall back to matrix
fusion with a lossy diagnostic. Chains mixing blur or drop-shadow with color
matrices still use the native group and do not yet feed each result into the
next function. Full serial filter compositing, corresponding WPT pixels, and
Linux/Metal execution remain open.

### Filter drop-shadow alpha masks and CSS box contours

[Filter Effects 1](https://www.w3.org/TR/filter-effects-1/#funcdef-filter-drop-shadow)
requires `drop-shadow()` to blur and offset the input image's alpha mask.
CSS filter shadows now carry that distinction through the effect object and a
new optional native side interface. Software reuses its captured-alpha path;
D3D12 and Vulkan sample the isolated capture through dedicated Gaussian
shaders and composite the untouched input above the tinted shadow. On the
same nonrectangular element, CSS `box-shadow` uses the full border-box contour,
including the zero-spread case; software now follows that contour too. Native
`DropShadowEffect` values retain their previous backend behavior.

`CssFilterDropShadowNativeTests` checks a small opaque shape inside a larger
transparent element, half-alpha source changes, positive/negative offsets,
a wider blur skirt, and the
different `box-shadow` perimeter on software/D3D12/Vulkan. Generated CSS
retains the filter-shadow marker. The Windows CSS/Flex regression passed 1,682
cases without skips in
`artifacts/css-alpha-shadow-validation/test-results/css-alpha-shadow-regression-final.trx`;
74 effect/capture/backdrop regressions passed in
`artifacts/css-alpha-shadow-validation/test-results/effect-backdrop-regression-final.trx`.
The forced Vulkan CPU path passed separately in
`artifacts/css-alpha-shadow-validation/test-results/css-alpha-shadow-vulkan-cpu-final.trx`.
The test output uses hash-verified core/software/D3D12/Vulkan DLLs rebuilt in
the isolated native directory. Older native payloads report a lossy fallback.
The GPU shader's 2D sampling grid is capped at 12 taps per axis, so large
radius whole-image parity, mixed serial filter chains, and Linux/Metal
execution remain open.

### Multiple box-shadow paint order

[CSS Backgrounds 3](https://www.w3.org/TR/css-backgrounds-3/#box-shadow)
orders each `box-shadow` list from front to back. CSS shadow lists now retain
their authored child order for inspection but paint those children in reverse,
so the first shadow is visually on top. The list marker survives cloning and
is preserved as a nested group when `box-shadow` and `filter` both contribute
to the native `Effect`. Vulkan GPU replay can insert repeated shadow commands
under the already captured content, rather than consuming the capture after
the first shadow; a tail check keeps that insertion tied to one contiguous
list. Three overlapping outer shadows render in the specified order on
software, D3D12, Vulkan and the forced Vulkan CPU path.

The Windows CSS/Flex regression passed 1,689 cases without skips in
`artifacts/css-shadow-order-validation/test-results/css-shadow-order-regression-final.trx`;
74 effect/capture/backdrop regressions passed in
`artifacts/css-shadow-order-validation/test-results/effect-backdrop-regression-final.trx`.
The Vulkan CPU variant passed separately in
`artifacts/css-shadow-order-validation/test-results/css-shadow-order-vulkan-cpu-final.trx`.
The isolated test output uses hash-verified native DLLs. CSS shadow knockout
is covered in a later increment below; whole-image WPT parity and Linux/Metal
execution remain open.

CSS Backgrounds 3 also places outer shadows below the element background and
inset shadows above it, regardless of their relative positions in the authored
list. CSS effect groups now paint outer shadows first and inset shadows second,
reversing authored order within each layer. Software and D3D12 composite the
captured content once before the first inset, then use a shadow-only native
operation for later insets. Vulkan GPU replay already retains the content
composite; its forced CPU capture now draws inset contours analytically without
erasing previous inset layers. `CssMixedShadowNativeTests` checks both authored
outer/inset orders and overlapping inset colors on all three Windows backends,
including a separate forced Vulkan CPU run. The Windows CSS/Flex regression
passed 1,692 cases without skips in
`artifacts/css-shadow-layers-validation/test-results/css-shadow-layers-regression-final.trx`.
Another 74 effect/capture/backdrop regressions passed in
`artifacts/css-shadow-layers-validation/test-results/effect-backdrop-regression-final.trx`;
the forced Vulkan CPU path passed three shadow and inset cases in
`artifacts/css-shadow-layers-validation/test-results/css-shadow-layers-vulkan-cpu-final.trx`.
The isolated test output uses hash-verified rebuilt native DLLs. Older native
payloads report a lossy fallback for later inset layers. Pure CSS `box-shadow`
on `Border` and the built-in `Canvas`, `DockPanel`, `FlexPanel`, `Grid`,
`StackPanel`, `WrapPanel` and `UniformGrid` now completes its capture after the
background draw, before descendants and the `Border` stroke. This keeps opaque
child content above inset shadows and works with a transparent `Border`
background. Custom visuals and custom Panel subclasses still use whole-subtree
capture; their inset/content layer order remains open. Combined `filter`
composition on the supported hosts is covered below. The added child-content
pixels pass on software, D3D12, Vulkan and forced
Vulkan CPU. The current Windows CSS/Flex run passed 1,695 cases in
`artifacts/css-shadow-layers-validation/test-results/css-shadow-content-regression-final.trx`;
74 effect/capture/backdrop cases passed in
`artifacts/css-shadow-layers-validation/test-results/css-shadow-content-effect-regression-final.trx`;
four forced Vulkan CPU shadow/content cases passed in
`artifacts/css-shadow-layers-validation/test-results/css-shadow-content-vulkan-cpu-final.trx`.

### CSS outer box-shadow knockout

[CSS Backgrounds 3](https://www.w3.org/TR/css-backgrounds-3/#shadow-shape)
clips an outer `box-shadow` inside the original border-box, independently of
its offset, blur, spread and the element background's alpha. The CSS shadow
side interface now carries both the original border contour and the spread
shadow contour. Software and Vulkan CPU multiply Gaussian coverage by the
outside coverage of the original contour. D3D12 and Vulkan GPU snapshot an
inverse elliptical clip for the shadow draw, then composite the element
without that clip. Native `DropShadowEffect` retains its existing behavior.

`CssOuterShadowNativeTests` checks a transparent background, straight edges,
an elliptical corner, an offset shadow and a rounded ancestor clip on software,
D3D12 and Vulkan. The Vulkan CPU capture variant passes separately. The Windows
CSS/Flex regression passed 1,695 cases without skips in
`artifacts/css-shadow-layers-validation/test-results/css-shadow-knockout-regression-final.trx`;
74 effect/capture/backdrop cases passed in
`artifacts/css-shadow-layers-validation/test-results/css-shadow-knockout-effect-final.trx`.
The explicit Vulkan CPU check passed two shadow cases in
`artifacts/css-shadow-layers-validation/test-results/css-shadow-knockout-vulkan-cpu-final.trx`.
These pixel probes do not establish full WPT bitmap parity or Linux/Metal
native rendering.

### CSS box-shadow followed by filter

[Filter Effects 1](https://www.w3.org/TR/filter-effects-1/#FilterProperty)
applies a filter to the element's rendered group, including its decoration and
descendants. When both CSS `box-shadow` and `filter` are set on `Border` or the
seven supported background-only panels, the combined effect now preserves a
shadow child and a filter child. It captures once for the filter, paints outer
shadows directly before the background and inset shadows directly after the
background, paints descendants and the border stroke, then applies the filter
to the full capture including its shadow apron. This avoids D3D12's unsupported
nested offscreen capture. The combined group's capture padding adds the shadow
and filter extents. A new recorded drawing command preserves the phase order
when a whole frame is replayed. Older native payloads retain the former
parallel-effect fallback in direct rendering.

`CssShadowFilterCompositionNativeTests` checks that `brightness(0)`,
`grayscale(1)`, an ordered grayscale/brightness chain, `blur(4px)` and
`drop-shadow()` consume the painted outer shadow; it also checks inset/child
content and whole-frame replay. Software, D3D12, Vulkan and a forced Vulkan
CPU capture pass. The Windows CSS/Flex regression passed 1,698 cases without
skips in
`artifacts/css-shadow-layers-validation/test-results/css-shadow-filter-regression-final.trx`;
74 effect/capture/backdrop cases passed in
`artifacts/css-shadow-layers-validation/test-results/css-shadow-filter-effect-final.trx`;
18 whole-frame recorder cases passed in
`artifacts/css-shadow-layers-validation/test-results/css-shadow-filter-recording-final.trx`.
The forced Vulkan CPU composition case passed in
`artifacts/css-shadow-layers-validation/test-results/css-shadow-filter-vulkan-cpu-final.trx`.
Custom drawing hosts, mixed blur/shadow function chains within one `filter`
value, older native payloads during whole-frame replay, full WPT bitmap parity
and Linux/Metal native rendering remain open.

### CSS outlines inside filters

[Filter Effects 1](https://www.w3.org/TR/filter-effects-1/#FilterProperty)
includes an element's outline in the filtered drawing. CSS `filter` effects
now retain a source marker through cloning. Their capture includes the outline
after children and border painting, and adds the outline's outward reach to
the filter padding before snapping the offscreen bounds. The filter consumes
that full capture, including an outline outside the border box. Native XAML
effects keep the existing sharp outline above their effect output.

`CssFilterOutlineNativeTests` checks that `brightness(0)` transforms a red
outline, that `blur(4px)` paints beyond a rounded outline's outer edge, and
that the outline and box shadow both enter a combined filter. Software,
D3D12, Vulkan and forced Vulkan CPU pass the pixel checks. The Windows
CSS/Flex regression passed 1,701 cases without skips in
`artifacts/css-shadow-layers-validation/test-results/css-filter-outline-regression-final.trx`;
74 effect/capture/backdrop cases passed in
`artifacts/css-shadow-layers-validation/test-results/css-filter-outline-effect-final.trx`;
the forced Vulkan CPU filter cases passed in
`artifacts/css-shadow-layers-validation/test-results/css-filter-outline-vulkan-cpu-final.trx`.
Filter ordering against clipping, masks and opacity, exact whole-image WPT
parity and Linux/Metal native rendering remain open.

### Replaced image and video content

[CSS Images 3 object sizing](https://www.w3.org/TR/css-images-3/#the-object-fit)
is implemented for authored `object-fit` on XAML `Image`: `fill` stretches to the
content box; `contain` and `cover` retain the source aspect ratio; `none` uses
natural source dimensions; and `scale-down` selects the smaller result of
`none` and `contain`. The value changes the source rectangle, not the image
element's used width and height. CSS padding defines the content box. Source
pixels are clipped there, including when `overflow: visible` is authored.
`object-position` accepts the same one- to four-value position grammar as
`background-position`, including edge offsets and supported math lengths.
Percentages use the content box size minus the concrete source size, so they
also resolve correctly when `cover` makes that difference negative. It works
independently of `object-fit`. Local or animated native `Image.Stretch` still
outranks the CSS fit. Without an authored CSS fit, the native `Image.Stretch`
default continues to determine the source size. Deferred bitmaps request their
canonical pixel dimensions for an unscaled `none` fit even when only a small
part is visible. `MediaElement` uses the same fit and position calculation for
its video frame, with local native `Stretch` taking priority. Its requested
decode target follows the fitted content size; CSS padding and content clipping
apply to the video while its black background and subtitles retain their native
placement. `CssImageObjectTests`, `CssImageObjectNativeTests`,
`CssMediaObjectTests` and `CssMediaObjectNativeTests` cover parsing, layout,
decode sizing and direct/recorded software, D3D12 and Vulkan pixels for images
and video bitmap fallback frames. Live video-surface pixel tests, other replaced
controls and cross-platform device pixels remain open.

The inherited [CSS Images 3 `image-rendering` property](https://www.w3.org/TR/css-images-3/#the-image-rendering)
now accepts `auto`, `smooth`, `high-quality`, `crisp-edges` and `pixelated`, plus
legacy `optimizeSpeed` and `optimizeQuality` aliases. `smooth` uses linear
sampling, `high-quality` uses the native high-quality mode, and `crisp-edges`
uses nearest-neighbor sampling. `pixelated` first scales to the closest
positive integer multiple with nearest-neighbor sampling, then smoothly
resamples to the final size. It is distinct from `crisp-edges` at noninteger
scale factors. CSS values inherit through the visual tree and affect `Image`,
`MediaElement` bitmap frames, CSS bitmap background layers and native local
`ImageBrush` backgrounds on supported owners, image fills on Rectangle,
Ellipse, Path, Polygon and Polyline, plus image strokes on those shapes and
Line; an explicit native scaling mode on an image/video control or brush
retains priority. The authored brush is not mutated when CSS supplies the
mode. Managed software and native software, D3D12 and Vulkan implement the
two stages.
`CssImageRenderingTests` checks parsing, inheritance and pixel values;
`CssImageRenderingNativeTests` checks direct and recorded image/background/
shape pixels on Windows. Solid ImageBrush strokes on native software, D3D12
and Vulkan use widened path masks; dashed strokes and path segments marked
`IsStroked=false` use rasterized stroke masks while sampling the image across
the visible path. Managed software samples the expanded stroke bounds and
shares one sampling area across path segments and skips segments marked
`IsStroked=false` on open and closed paths. `CssImageBrushStrokeNativeTests`
checks rectangular, rounded, elliptical, line, dashed and partially stroked
open/closed paths, including source updates, zero-length dot caps and recorded
replay. Managed software now splits dashed lines, open/closed paths, rectangles
and ellipses by path length, preserving dash offsets, odd-pattern repetition and
one image sampling area. It composites overlapping dash pieces through a shared
coverage mask so translucent joins paint once. `CssImageBrushStrokeManagedTests`
covers texture, gaps, caps, hidden segments, closure seams and recorded replay;
`CssImageRenderingTests` covers inherited CSS sampling on a dashed `Line`.
Managed solid and dashed paths and native rasterized strokes now follow
`miter`/`bevel`/`round` joins and the miter limit; acute miter tips stay inside
the vector-image bounds and retain their source pixels. Managed SVG stroke
outlines and native software path strokes now widen before affine transforms,
preserving different horizontal/vertical widths, joins, caps and source-space
dash lengths under nonuniform scale. Native software direct lines use the
inverse transform to cover the corresponding elliptical capsule, including
rotation. The shared native stroke widener now orients round caps along the
outward half of each line endpoint, treats the miter limit as a ratio to the
half-width, and fills the whole outer corner. Native and managed vector paths
discard repeated vertices before constructing joins. Their dash minimum now
tracks device resolution, preserving subpixel source patterns under magnification;
native software also preserves half-pixel dash coverage. Native software
`StrokePath` traverses the closing edge for both explicit `ClosePath` commands
and `closed=true`, and merges dash runs that meet at the closure into one join.
Direct `DrawLine` and batched `DrawLines` now use that native path stroker for
nonempty dash patterns. They preserve odd-pattern repetition, normalize large
positive and negative offsets, and honor flat, square and round dash caps;
an all-zero pattern paints as a solid line. Direct/path pixel parity passes on
Windows software, D3D12 and Vulkan.
Direct rectangles, uniform and per-corner rounded rectangles, and ellipses now
stroke their closed contours through the same path stroker when dashed. Straight
`PathGeometry` figures also use native dash traversal so marks stay continuous
around corners and through the closing edge. The three Windows backends match
the equivalent path pixels for odd patterns, large offsets, round dash caps and
all-zero patterns.
Other backend transformed-stroke cases, remaining degenerate joins and denser
subpixel patterns,
ImageBrush paints in other controls or custom drawing, live video-surface
pixels, other image-valued paints and non-Windows device pixels remain open.

### CSS background image lists and color

[CSS Backgrounds 3](https://www.w3.org/TR/css-backgrounds-3/#layering)
paints the first `background-image` above subsequent images and
`background-color` below every image. Comma-separated image lists preserve
`none` entries; the `background` shorthand accepts the same order and resets
omitted image and color values. An invalid image-list member rejects the whole
declaration. The CSS background slot composes every visible image over the
color in a layered brush. Native drawing repeats the same rectangle, rounded
rectangle, ellipse or arbitrary geometry fill in bottom-to-top order and
strokes it once; managed software painting composes brush samples per pixel.
The shape and clipping remain those of the control that owns `Background`.
Native local/bound `Background` values still outrank CSS. A missing image
leaves lower layers visible. Whole-frame recording resolves layered gradients
at paint size, snapshots the layers, and detects a live bitmap nested inside.

`background-size`, `background-position` and `background-repeat` follow the image-list correspondence rule from
[CSS Backgrounds 3](https://www.w3.org/TR/css-backgrounds-3/#layering): shorter
lists cycle and excess entries are unused; `none` still consumes an entry.
The shorthand accepts sizes after a position and `/`, and resets omitted
sizes, positions and repeats. For CSS bitmap images, `auto` uses intrinsic
dimensions, defaults to top-left positioning and repeats on both axes.
The [background-size](https://www.w3.org/TR/css-backgrounds-3/#the-background-size)
longhand and shorthand accept `cover`, `contain`, and one or two `auto`,
nonnegative length, percentage or math dimensions. Percentages use the
positioning-area dimension; an `auto` axis paired with an explicit size preserves
the image's natural aspect ratio. A literal negative size rejects the declaration,
while a [calculated negative size](https://www.w3.org/TR/css-values-4/#calc-range)
clamps to zero; a zero-sized image paints nothing. Deferred bitmap decoding also
uses the sized tile dimensions when they exceed the paint bounds.
For bitmap layers, the [background-position](https://www.w3.org/TR/css-backgrounds-3/#the-background-position)
longhand and shorthand accept one- or two-value keyword/length/percentage positions,
three- or four-value edge offsets, and supported math lengths. Positions resolve
after the used image size is known: percentages multiply the positioning-area size
minus the image size, including when that difference is negative. Element font and
container lengths are bound when the style applies. Background owners using the
box painter use their padding box as the default bitmap positioning area;
remaining owners still use their paint bounds.
For bitmap layers, the [background-repeat](https://www.w3.org/TR/css-backgrounds-3/#the-background-repeat)
longhand and shorthand accept `repeat`, `no-repeat`, `space`, `round`, the
`repeat-x`/`repeat-y` aliases, and pairs of axis keywords. `space` distributes
whole tiles between the positioning-area edges and ignores the position on that
axis when at least two fit; otherwise its single tile uses the specified position.
`round` adjusts the tile dimension to fit an integer number of copies, then
restores the natural ratio on the other axis when that size component is `auto`.
Software sampling and native tile drawing use the same geometry. Non-CSS native
`ImageBrush` placement is unchanged. The [background-origin](https://www.w3.org/TR/css-backgrounds-3/#the-background-origin)
and [background-clip](https://www.w3.org/TR/css-backgrounds-3/#the-background-clip)
longhands and shorthand accept border, padding and content boxes with image-list
cycling. On `Border`, template-bound `Border`, and direct owners using the box
painter, bitmap positioning uses the chosen origin box and each image is clipped
to its chosen box. The color uses the
bottom-most layer's clip value. Box insets are read at paint time, so changing
border thickness or padding moves the pattern without reapplying the style.
The background shorthand and its box longhands also resolve `initial`, `unset`
and explicit inheritance through the same per-layer values.
Rounded clips retain the inset elliptical corner contour. CSS box geometry on
non-Border owners, grouped opacity after manual layered-brush mutation, and
gradient background sizing/positioning remain open.

`CssBackgroundLayerTests` checks managed software pixels, three-layer order,
`none` entries, size/position/repeat-list cycling and truncation, intrinsic bitmap tiling,
explicit size geometry, natural-ratio completion, literal-negative rejection,
calculated-negative clamping and scaled bitmap software pixels,
one- to four-value position grammar, signed free-space geometry, font-relative lengths,
`space`/`round` grammar, spacing, fallback and aspect-ratio geometry, box-list
cycling, shorthand resets, live border/padding insets, per-layer/color clipping,
invalid-list recovery, mutable foreground updates, cloning,
cross-thread replay after a gradient mutation and live-image
recordability. `CssBackgroundLayerNativeTests` checks software, D3D12 and
Vulkan pixels for direct and recorded multi-image painting, intrinsic auto
tiling, explicit dimensions/edge-offset positions, `no-repeat`, `space` and
`round`, origin/clip boxes and inset elliptical clips, per-layer `contain`/`cover`
raster brushes, translucent
gradients and raster images over blue, elliptical corners, shorthand syntax,
panels, a built-in Button template, image failure, native local precedence and
used-size recorded replay. The Windows CSS/Flex regression passed 1,840 cases
without skips in
`artifacts/css-background-geometry-validation/test-results/css-background-box-css-flex-complete.trx`;
69 additional brush, decode and whole-frame recorder regressions passed in
`artifacts/css-background-geometry-validation/test-results/css-background-box-brush-recording-verified.trx`.
Full per-layer geometry, whole-image WPT parity and
Linux/Metal native execution remain open.

The next background increment moved per-layer painting into a shared control
renderer and added direct TextBox and Label box geometry. CSS gradients now use
the positioning box as their default image size; an explicit size, position or
repeat mode creates gradient tiles whose color stops are resolved at the tile's
used dimensions. The paint remains clipped to each layer's box and the owner's
outer contour. Managed pixels cover direct controls, changing padding, gradient
size/position/repetition and the no-intrinsic-ratio `auto` axis. Direct and
recorded Windows software, D3D12 and Vulkan pixels cover a positioned gradient.
The Release CSS/Flex regression passed 1,828 cases with no skips in
`artifacts/css-background-geometry-validation/test-results/css-background-gradient-css-flex-complete.trx`;
67 additional brush, decode and recorder cases passed in
`artifacts/css-background-geometry-validation/test-results/css-background-gradient-brush-recording.trx`.
The earlier 1,840 count used a broader name filter that also included 17
unrelated tests; the current run adds five background cases under the stricter
class-name filter. Background box geometry for other direct-drawing controls,
special native shapes and full cross-platform gradient parity remain open.

The following increment joined CSS Grid/flow Panel backgrounds to their used
layout padding, including `background-origin: content-box` and per-layer clips.
Directly painted ScrollViewer, RichTextBox and TabControl backgrounds use their
current native border and padding edges through the same painter. Managed pixels
cover changing Panel CSS padding, those three control types, and native local
Background precedence. Software, D3D12 and Vulkan direct and recorded pixels
cover the padded Grid gradient. The Release CSS/Flex regression passed 1,832
cases without skips in
`artifacts/css-background-geometry-validation/test-results/css-background-more-owners-css-flex.trx`;
67 additional brush, decode and recorder cases passed in
`artifacts/css-background-geometry-validation/test-results/css-background-more-owners-brush-recording.trx`.

CSS flex layout now measures and arranges inside the used padding box for both
`display:flex` on an existing Panel and a native FlexPanel. It keeps percentage
padding relative to the containing width, offsets items on both axes and sizes
the background content box from the same insets. Direct and recorded Windows
software, D3D12 and Vulkan pixels cover the padded Flex background and item.
The Release CSS/Flex regression passed 1,835 cases without skips in
`artifacts/css-background-geometry-validation/test-results/css-flex-padding-css-flex-complete.trx`;
67 brush, decode and recorder cases passed in
`artifacts/css-background-geometry-validation/test-results/css-flex-padding-brush-recording.trx`.

TextBlock now paints CSS background layers against its effective Padding DP,
the same inset used to measure and position text. Managed pixels cover dynamic
CSS padding with a bitmap origin and content clip. Direct and recorded Windows
software, D3D12 and Vulkan pixels cover a padded gradient. The Release CSS/Flex
regression passed 1,836 cases without skips in
`artifacts/css-background-geometry-validation/test-results/css-textblock-background-css-flex.trx`.

[CSS Backgrounds and Borders 3](https://www.w3.org/TR/css-backgrounds-3/#border-radius)
applies `border-radius` even when an element has no border. CSS radii now stay
symbolic on visual elements without a native `CornerRadius` property. Panel and
TextBlock use the ellipse for CSS and native-local background painting, inset
`background-clip` boxes, overflow clipping and self hit testing. Explicit CSS
inheritance preserves percentage radii for the child's own size. Managed pixels
cover CSS removal, resize, inheritance, native Background precedence, clipped
Panel children and inset content-box curves. Windows software, D3D12 and Vulkan
direct and recorded pixels cover layered backgrounds and clipped children. The
Release CSS/Flex regression passed 1,840 cases without skips in
`artifacts/css-background-geometry-validation/test-results/css-generic-radius-css-flex-final.trx`;
60 additional native hit and clipping cases passed in
`artifacts/css-background-geometry-validation/test-results/css-generic-radius-hit-clip.trx`.
Self-drawn custom backgrounds still need to consume the CSS contour explicitly.

[CSS Overflow 3](https://www.w3.org/TR/css-overflow-3/#overflow-properties)
defines separate horizontal and vertical overflow values and promotes `visible`
to `auto` and `clip` to `hidden` when the other axis is scrollable. CSS now
combines each axis after the cascade, accepts the one- or two-value shorthand,
and maps the computed clipping axes to the native edge mask. A `clip`/`visible`
pair clips only one axis and leaves its corners square; a scrollable axis clips
both when the other value was `visible` or `clip`. Computed values remain
available for explicit inheritance. Managed tests cover shorthand/longhand
precedence, CSS-wide values, removal, native clip values, input and rounded
corners. Direct and recorded Windows software, D3D12 and Vulkan pixels cover
each axis, cross-axis promotion and the difference between square single-axis
corners and rounded two-axis corners in
`artifacts/css-background-geometry-validation/test-results/css-overflow-axis-native-rounded.trx`.
The Release CSS/Flex regression passed
1,848 cases without skips in
`artifacts/css-background-geometry-validation/test-results/css-overflow-axis-css-flex-final.trx`;
the expanded CSS and native clip/input regression passed 1,871 cases in
`artifacts/css-background-geometry-validation/test-results/css-overflow-axis-final-regression-v3.trx`.
`auto` and `scroll` currently clip content but do not create a CSS scroll
container; they retain a diagnostic.

[CSS Overflow 3 §3.1](https://www.w3.org/TR/css-overflow-3/#overflow-properties)
defines `overflow-inline` and `overflow-block` as logical counterparts of the
physical axes. Following [CSS Logical 1 §4](https://www.w3.org/TR/css-logical-1/#box),
the engine cascades physical and logical declarations for each computed axis
together, including `revert-layer`, by priority and order within a rule. The
current horizontal writing mode maps inline to X and
block to Y, including RTL direction. The axis computation and native clipping
path are shared with `overflow-x` and `overflow-y`. Managed tests cover syntax,
shorthands, source order, specificity, layers, importance, CSS-wide values,
variables, inheritance, pointer input, and cross-axis promotion. Vertical
writing modes and native CSS scroll containers remain outside this support.
The Release CSS/Flex regression passed 2,070 tests without skips in
`artifacts/css-background-geometry-validation/test-results/css-logical-borders-final.trx`.

[CSS Logical 1 §4.1](https://www.w3.org/TR/css-logical-1/#box) also defines
`inline-size`/`block-size` and their min/max counterparts. They now share the
physical width/height cascade groups in horizontal writing mode, including
same-rule declaration order, layer rollback, and native local-value precedence.
Percentages and math use the existing layout state; explicit inheritance
retains a parent's computed percentage for resolution against the child's
containing block. Managed tests cover parser acceptance, used sizes, resets,
dynamic percent inheritance, and physical/logical conflicts.

[CSS Logical 1 §§4.2 and 4.4](https://www.w3.org/TR/css-logical-1/#box)
define the flow-relative margin and padding edges and their two-sided
inline/block shorthands. The engine resolves the element's computed direction
after custom properties, then cascades each logical edge with its physical
counterpart, including importance, native precedence, and `revert-layer`.
Explicit inheritance reads the parent's corresponding logical side before
mapping to the child's direction; percentage and math values remain symbolic.
Managed tests cover LTR/RTL, same-rule variables, source order, layers,
native local values, percentages, and inheritance across opposite directions.

[CSS Logical 1 §4.3](https://www.w3.org/TR/css-logical-1/#inset-properties)
defines flow-relative positioning offsets. `inset-inline`/`inset-block` and
their side longhands now cascade against `left`/`right`/`top`/`bottom` after
direction computation. The physical `inset` shorthand and all inset longhands
also accept `auto`; `all: initial` resets them. Absolute positioning keeps
percentage/math offsets against the containing block; when both inline edges
and width constrain the box, the containing block's direction selects the
left or right edge as specified by [CSS Positioned Layout 3](https://www.w3.org/TR/css-position-3/).
Explicit inheritance transfers the
parent's logical edge and symbolic percentage. Static pixel offsets retain
Canvas attached-property compatibility, and native local Canvas offsets take
priority over absolute CSS offsets. Managed tests cover these paths,
including layers, native direction, dynamic styles and `revert-layer`.

`position:relative` preserves its normal flow slot and moves the visual and
hit-test box by the used inset offset. Horizontal percentages use the
containing block's content width; vertical percentages use its content height.
With both inline edges specified, the containing block's LTR/RTL direction
selects left/right; top wins over bottom. Native local Canvas positions keep
their normal Canvas placement and suppress the corresponding CSS relative
offset, so a local edge is not counted twice. Arrange-input tracking updates
percentage offsets when the parent resizes and recomputes offsets when native
Canvas values or containing-block direction change. `CssRelativePositionTests`
covers these cases in native StackPanel/Canvas and CSS Flex layouts, including
flow occupancy and pointer hit testing. `fixed`, `sticky`, full positioned
containing-block propagation and stacking-context semantics remain open.

[CSS Logical 1 §§4.5 and 4.6](https://www.w3.org/TR/css-logical-1/#box)
also define flow-relative border widths and corner radii. The horizontal
writing-mode implementation now accepts one- or two-value
`border-inline-width`/`border-block-width` shorthands, their four side
longhands, and all four `border-*-*-radius` corner longhands. They cascade
with their physical counterparts after direction computation. Explicit
inheritance reads the parent's logical side or corner and maps it into the
child's direction, retaining percentage radii until the child's border box is
known. Negative/percentage border widths are rejected. Managed tests cover
syntax, LTR/RTL mapping, declaration order, importance, layers, native border
thickness precedence, live native-parent changes during explicit inheritance,
equal-valued native radius takeover, symbolic radius inheritance, and rounded
hit geometry.
The [CSS Backgrounds 3 border-color rule](https://www.w3.org/TR/css-backgrounds-3/#border-color)
now expands one to four side colors in top/right/bottom/left order. Physical
and logical side-color longhands, plus `border-inline-color` and
`border-block-color`, use the same direction-aware cascade as widths. `Border`
paints the four colors around square, circular, elliptical and native rounded
contours; CSS and native local `BorderBrush` precedence is preserved. Focused
tests cover pixel colors, RTL, `currentColor`, explicit inheritance, live
native-parent and mutable foreground changes, invalid values and native
takeover. The Release CSS/Flex regression passed 2,085 tests without skips in
`artifacts/css-background-geometry-validation/test-results/css-border-colors-verified.trx`.
[CSS Backgrounds 3 border-style](https://www.w3.org/TR/css-backgrounds-3/#border-style)
and [CSS Logical 1 flow-relative border styles and shorthands](https://www.w3.org/TR/css-logical-1/#border-properties)
now expand one to four styles and one to two logical styles. Physical and
logical side shorthands set width, style and color together, with omitted
components reset. On `Border`, `none`/`hidden` zero the corresponding used
width; `solid`, `dashed`, round `dotted` and `double` paint on square and
rounded contours. The patterned marks are batched into geometry. Tests cover
pixels, RTL, cascade/layers, inheritance, invalid values, native precedence
and CSS-wide values. The Release CSS/Flex regression passed 2,107 tests
without skips in
`artifacts/css-background-geometry-validation/test-results/css-border-styles-final.trx`.
`groove`/`ridge`/`inset`/`outset` now use a chosen light/dark mix of each
side's color, preserving its alpha. `inset` darkens the top/left and lightens
the bottom/right; `outset` reverses that orientation. `groove`/`ridge` reverse
the shades between their outer and inner halves. This follows the visual
definitions in [CSS Backgrounds 3](https://www.w3.org/TR/css-backgrounds-3/#border-style),
which leaves the shade calculation to implementations. Square and rounded
pixel tests cover the orientation, two halves, transparency, black/white
endpoints and live `currentColor` updates. The Release CSS/Flex regression
passed 2,114 tests without skips in
`artifacts/css-background-geometry-validation/test-results/css-border-relief-css-flex.trx`.
The [CSS Backgrounds 3 border-width rule](https://www.w3.org/TR/css-backgrounds-3/#border-width)
now applies the initial `none` style to width-only and color-only declarations:
their used border width is zero on every unstyled side, including during
measurement. Visible sides with no declared width use the initial `medium`
(3 px). Tests cover isolated width/color declarations, partial sides, an
explicit `initial` reset and the earlier native-property precedence cases.
The Release CSS/Flex regression passed 2,121 tests without skips in
`artifacts/css-background-geometry-validation/test-results/css-border-default-final.trx`.
Solid `Border` sides with the same effective transparency now precompose their
four colors and paint the existing border ring once. Square and elliptical join
alpha matches a uniform-color reference in managed pixels; Windows software,
D3D12 and Vulkan check square and rounded joins, and a mutable translucent
`currentColor` updates the cached color image. Solid sides with different
effective alpha use a cached, premultiplied-aware color field and paint the
ring once. Managed tests cover square and rounded joins, all four corner
directions, asymmetric widths, color interpolation and mutable `currentColor`;
Windows software, D3D12 and Vulkan compare join alpha with the adjacent side
alphas. Translucent `inset`/`outset`/`groove`/`ridge` sides now shade a cached
color field and paint the ring once, including mixed side alphas and styles
combined with `solid`. Translucent `dashed`/`dotted` sides combined with one
another or `solid` now join their marks into one geometry before sampling the
side colors; differing widths and rounded gaps remain intact. Managed tests
cover those seams, colors, style combinations and mutable `currentColor`;
Windows software, D3D12 and Vulkan check `inset` and `dotted` joins.
Translucent solid-color borders can now also mix `double` with `solid`,
patterns and relief styles: a bounded image combines the two visible
`double` bands, pattern marks, per-side alpha and relief shades before one
ring draw. Square and rounded managed tests cover both bands and the gap,
different side alphas, mixed styles and live `currentColor`; Windows software,
D3D12 and Vulkan compare both visible bands and mixed `solid`/`double` gaps
with a solid reference. Non-solid brushes still use separate side draws. The
Release CSS/Flex/Outline regression passed 2,190 tests without skips in
`artifacts/css-background-geometry-validation/test-results/css-border-double-mixed-final.trx`.
Built-in `Panel` elements now retain CSS used border widths even without a
native `BorderThickness` property and paint their four side colors and styles
through the same border-ring path as `Border`. Grid, Flex and flow CSS
formatters reserve border and padding space for children; the widths also feed
`box-sizing:content-box` and background origin/clip geometry. Managed pixels
cover square and rounded joins, patterned and double borders, translucent
compositing, background clipping, style removal and child insets. Windows
software, D3D12 and Vulkan cover rounded side colors and double bands. Native
panel layouts paint CSS borders but retain their native child placement rules.
`TextBlock` now uses the same CSS border painter and used widths: its text
measurement, line origin, baseline, `box-sizing:content-box`, and background
clip account for the border. Managed tests cover those insets, square and
rounded sides, a `double` gap, and removal; software, D3D12 and Vulkan render
the border alongside text and a clipped background.
The focused Release panel-border run passed 16 tests without skips. A run from
the default output exposed an older D3D12 DLL and failed five existing
shadow/filter cases. With the hash-verified isolated native DLL and current
managed build, those cases and the panel-border cases passed together; the full
Release CSS/Flex/Outline regression passed 2,214 tests without skips in
`tests/Jalium.UI.Tests/TestResults/css-panel-border-isolated-final.trx`.
The expanded Release CSS/Flex/Outline/TextBlock regression passed 2,266 tests
without skips in
`tests/Jalium.UI.Tests/TestResults/css-text-border-current-final.trx`.
The same side painter now covers direct `ScrollViewer`, `ScrollBar`, `RichTextBox`,
`TabControl`, untemplated `TextBox` and `Label`, `Calendar` and `StatusBar`
painting. CSS paint replaces their former single-color native stroke only while
CSS owns `BorderBrush`; a native local value keeps its existing path. The
template-binding pipeline also forwards CSS side colors and line styles when a
control's `BorderBrush` and `BorderThickness` feed a template `Border`.
Both typed and markup bindings update after CSS changes, detach when the
template changes, and yield to native source or target values. The built-in
`Label`, `TextBox` and `Button` chrome paints all four sides. Direct `Label`
text and text hit positions now include the border inset. Managed pixels cover
side colors, `double` gaps, CSS updates and native takeover. The expanded
Release CSS/Flex/Outline/TextBlock/Label/TemplateBinding regression passed
2,283 tests without skips in
`tests/Jalium.UI.Tests/TestResults/css-border-control-current.trx`.
The `ScrollBar` constructor now supplies brush, thickness and padding defaults
as current values so CSS can take precedence; its border follows chrome
opacity. Its 207 ScrollBar/ScrollViewer/Track regressions passed without skips.
`TabControl` now measures and arranges its tab strip and selected-content
presenter inside the border and padding content box for all four strip
placements. Its strip fill and separator use the same inner geometry. Removing
CSS or setting a native local `BorderThickness` reflows those children. The
expanded Release CSS/Flex/Outline/TextBlock/Label/TemplateBinding/TabControl
regression passed 2,311 tests without skips in
`tests/Jalium.UI.Tests/TestResults/css-tab-control-insets-current.trx`.
An untemplated `StatusBar` now measures its items host inside the same content
box and arranges it there. Its theme border and padding, CSS changes, and a
native local border-thickness override all use the live insets. A templated
`StatusBar` keeps its template layout path. The expanded Release CSS/control/
items/template regression passed 2,339 tests without skips in
`tests/Jalium.UI.Tests/TestResults/css-control-edge-layout-current.trx`.
`ScrollViewer` now measures its viewport inside the border and padding content
box, arranges ordinary and `IScrollInfo` content in the resulting scrollport,
places classic bars in gutters and overlay bars over the scrollport, and keeps
its fast scroll translation at the inset origin. A separate
child clip, when clipping is enabled, prevents scrolled content from painting
or receiving input in the border and padding; CSS rounded corners shape that
inner clip while the viewer's own background remains visible. Empty viewers
include the edges in desired size.
CSS removal and native local border-thickness changes reflow the viewport.
The expanded Release CSS/layout/ScrollViewer/virtualization regression passed
2,514 tests without skips in
`tests/Jalium.UI.Tests/TestResults/css-scroll-viewer-insets-current.trx`.
`ScrollBar` now measures and arranges its desktop buttons and track or its
overlay track inside explicitly authored CSS or native border and padding.
Fallback arrows and the overlay indicator hit strip follow those inset parts;
intrinsic dimensions include the edges. The built-in `Padding=2` still only
insets track paint until an author sets a box edge, preserving existing desktop
and hosted overlay geometry. Source-only changes reflow the parts when CSS or a
native local value equal to that default is added or removed. The focused
Release ScrollBar/ScrollViewer/Track regression passed 214 tests without skips
in `tests/Jalium.UI.Tests/TestResults/css-scroll-bar-insets-relevant.trx`.
The expanded CSS/Flex/ScrollBar/ScrollViewer/Track run passed 2,372 tests
without skips in
`tests/Jalium.UI.Tests/TestResults/css-scroll-bar-insets-expanded.trx`.
`ScrollBar` now paints CSS background color and image layers with their chosen
origin and clip boxes against its full border box, including CSS rounded
corners. `background:none` suppresses its built-in track fill; a native local
`Background` keeps precedence. Four managed pixel cases and direct/recorded
software, D3D12 and Vulkan pixels passed. The expanded Release
CSS/Flex/ScrollBar/ScrollViewer/Track regression passed 2,376 tests without
skips in
`tests/Jalium.UI.Tests/TestResults/css-scroll-bar-background-expanded.trx`.
Direct `StatusBar` and `Calendar` backgrounds now use the same CSS layer origin,
clip and rounded border-box geometry. Native local backgrounds keep priority;
an explicit `CalendarItemStyle` background remains the Calendar item's surface.
Seven managed pixel cases and direct/recorded software, D3D12 and Vulkan
pixels passed for these controls. The expanded Release CSS/Flex and related
control regression passed 2,410 tests without skips in
`tests/Jalium.UI.Tests/TestResults/css-direct-background-controls-expanded.trx`.
Direct `PasswordBox` now paints CSS side border colors and styles and uses
background layer origin, clip and rounded geometry while keeping its existing
border-and-padding inset for password text, selection and caret. A native local
`Background` keeps priority. Managed pixels and direct/recorded software, D3D12
and Vulkan pixels passed. The expanded Release CSS and related control regression
passed 2,418 tests without skips in
`tests/Jalium.UI.Tests/TestResults/css-password-box-direct-current.trx`.
Direct `StatusBarItem` now paints CSS side borders and layered backgrounds,
and reserves border plus padding around string and child content. Native local
background and border brushes keep priority. Managed pixels and direct/recorded
software, D3D12 and Vulkan pixels passed. The expanded Release CSS and related
control regression passed 2,423 tests without skips in
`tests/Jalium.UI.Tests/TestResults/css-direct-statusbar-item-final.trx`.
`Page` now consumes CSS border widths and fixed or percentage padding without
native border/padding dependency properties. The resulting content box drives
child measurement and arrangement, `box-sizing:content-box`, layered background
origin/clip, rounded contours and side border painting. A native local
`Background` keeps priority. Managed pixels and direct/recorded software,
D3D12 and Vulkan pixels passed. The expanded Release CSS and page/navigation
regression passed 2,597 tests without skips in
`tests/Jalium.UI.Tests/TestResults/css-page-box-expanded.trx`.
Direct `NumberBox` now paints CSS background layers across its whole box and
CSS side borders above its input content. Authored CSS box edges inset the
header, value text and spin buttons together; pointer-to-text and IME caret
coordinates use the same input rectangle. Its built-in template continues to
paint CSS sides through the bound `OuterBorder`. Native local background and
border brushes retain priority. Managed pixels and direct/recorded software,
D3D12 and Vulkan pixels passed. The expanded Release CSS and related control
regression passed 2,616 tests without skips in
`tests/Jalium.UI.Tests/TestResults/css-number-box-expanded.trx`.
`DatePicker` now paints CSS background layers over the complete control box,
including its header, and CSS side borders over the input content. Authored
CSS edges inset the header, date text and calendar button together. Native
background and border brushes retain priority. Managed pixels and
direct/recorded software, D3D12 and Vulkan background pixels passed; the
calendar popup remains a separate styled element. The expanded Release CSS
and related control regression passed 2,631 tests without skips in
`tests/Jalium.UI.Tests/TestResults/css-date-picker-expanded.trx`.
The themed `AutoCompleteBox` input uses a template-bound `OuterBorder`, so CSS
backgrounds and sides follow the template path. Its untemplated fallback now
uses the input box for CSS background layers and side borders, reserving
authored border and padding around the input text. The suggestion list remains
below that box even though the fallback renders both in one element. Text,
IME caret and suggestion hit positions follow the input geometry. Native local
background and border brushes retain priority. Managed pixels and
direct/recorded software, D3D12 and Vulkan background pixels passed. The
expanded Release CSS and related control regression passed 2,641 tests
without skips in
`tests/Jalium.UI.Tests/TestResults/css-auto-complete-expanded.trx`.
Direct `TimePicker` now paints CSS background layers over its complete box,
including the header, and paints CSS side borders above the time text and
clock button. Authored CSS edges inset the header, text and button; the popup
has a separate styled border. Direct `ColorPicker` now paints CSS background
layers inside their selected border, padding and content boxes, including
rounded corners, and paints CSS border sides. Authored CSS edges reserve space
around the spectrum, sliders and preview, whose pointer regions use the moved
rectangles. Native local background and border brushes keep priority on both
pickers. Managed pixels and direct/recorded software, D3D12 and Vulkan
background pixels passed. The expanded Release CSS and related control
regression passed 2,664 tests without skips in
`tests/Jalium.UI.Tests/TestResults/css-picker-box-expanded.trx`.
`ProgressBar` now uses a template-bound `Border` for its capsule track, so CSS
background layers and side borders paint against the complete control box.
Its template indicator measures the content width or height after border and
padding, including after CSS changes and in indeterminate mode. The direct
fallback also paints CSS background layers and border sides and insets its
indicator; `background:none` removes its fallback track fill. Native local
background and border brushes retain priority. The
shared `Border` background painter normalizes oversized native corner radii
before computing CSS clips; this keeps the theme capsule visible with CSS
backgrounds. Managed pixels and direct/recorded software, D3D12 and Vulkan
background pixels passed. The expanded Release CSS and related control
regression passed 2,683 tests without skips in
`tests/Jalium.UI.Tests/TestResults/css-progress-bar-expanded.trx`.
`Slider` and `RangeSlider` now use `Background` for the complete control box
and `TrackBrush` for the narrow track. Their template roots are bound `Border`
elements; their direct paths paint CSS background layers and border sides,
including a native local border brush. Border and padding place the continuous
or segmented track, selected range, thumbs and pointer-to-value mapping in
the same content box in both orientations; direct tick painting uses it too.
Theme templates also lay out a vertical track. Their 32-DIP minimum height
grows when CSS edges need more room for the thumbs. Managed pixels and
templated and direct/recorded software, D3D12 and Vulkan
background pixels passed. The expanded Release CSS and related control
regression passed 2,771 tests without skips in
`tests/Jalium.UI.Tests/TestResults/css-sliders-expanded.trx`.
The standalone direct `Thumb` now paints CSS background layers against its own
border, padding and content boxes, supports elliptical corners and side border
styles, and suppresses its built-in fill for `background:none`. Native local
backgrounds remain authoritative. Asymmetric CSS edges move the direct grip
within its content box. Its implicit template passes CSS padding to
the bound `ThumbBorder`; managed pixels cover both paths, and direct/recorded
software, D3D12 and Vulkan background pixels pass.
The expanded Release CSS and related control regression passed 2,817 tests
without skips in `tests/Jalium.UI.Tests/TestResults/css-thumb-expanded-rerun.trx`.
`CommandBar` and `MenuBar` now paint CSS background layers, elliptical radii
and border side styles over the whole bar. Their item panels and the
`CommandBar` overflow button measure and arrange inside CSS border and padding;
removing the style restores the original placement. `background:none` removes
the `CommandBar` fallback fill, while native local background and border
brushes keep priority. The default bottom separator remains when no border
thickness is authored. Managed layout/pixel tests and direct/recorded software,
D3D12 and Vulkan background pixels pass.
The expanded Release CSS and related control regression passed 2,841 tests
without skips in `tests/Jalium.UI.Tests/TestResults/css-menu-bars-expanded.trx`.
`MenuBarItem` and `MenuFlyoutItem` now paint CSS background layers, elliptical
corners and border side styles. CSS border and padding move title, icon,
accelerator, submenu arrow and toggle checkmark positions; removing those
edges restores their original layout. CSS `color`, including an inherited color,
reaches menu text and glyphs, while native local foreground, background and
border brushes keep priority.
The original hover fill remains when only a theme background is present.
Managed pixel/layout tests and direct/recorded software, D3D12 and Vulkan
background pixels pass. The expanded Release CSS and related control regression
passed 2,906 tests without skips in
`tests/Jalium.UI.Tests/TestResults/css-menu-items-expanded.trx`.
The legacy `Menu` and `MenuItem` direct paths now paint CSS background layers,
elliptical corners and border side styles. The `Menu` item host uses the border
and padding content box; `MenuItem` moves its header, icon, checkmark, gesture
text and submenu arrow within that box. Effective `background:none` suppresses
the selected item fallback fill. `MenuFlyoutSeparator`, built-in `Separator`
and `AppBarSeparator` paint CSS box layers and borders, reserve CSS edges in
intrinsic size, and place their lines in the content box. The built-in
`Separator` style now uses its direct drawing path, so the themed line follows
those edges. CSS `color` reaches the lines when no native stroke brush takes
precedence. Managed layout/pixel tests and
direct/recorded software, D3D12 and Vulkan background pixels pass. The
expanded Release CSS and related control regression passed 2,987 tests
without skips in
`tests/Jalium.UI.Tests/TestResults/css-legacy-menu-expanded.trx`.
The themed separator and `AppBarSeparator` extension passed 2,440 Release
CSS, command bar, separator and theme regression tests without skips in
`tests/Jalium.UI.Tests/TestResults/css-separator-expanded.trx`, including
direct and recorded software, D3D12 and Vulkan background pixels.
Custom `Separator` templates that draw their own line and other custom
direct-rendering elements still need border painting review;
some native control layouts do not reserve CSS border space for their children.

Direct `ToolBar` and `ToolBarTray` now paint CSS background layers, rounded
contours and distinct border sides. `ToolBar` measures and arranges its item
panel inside the border and padding content box, and uses that inner size for
overflow decisions in both orientations. The tray includes those edges in its
intrinsic size and offsets its bands into the content box. Their theme
backgrounds paint on the direct paths; clearing a CSS background or edges
restores the theme fill and layout. Managed pixels and layout, plus direct and
recorded software, D3D12 and Vulkan background pixels, pass. The expanded
Release CSS, toolbar and theme regression passed 2,457 tests without skips in
`tests/Jalium.UI.Tests/TestResults/css-toolbar-expanded.trx`. Custom `ToolBar`
templates still own their own background and child layout.
`ToolBarPanel` and `ToolBarOverflowPanel` now measure and arrange children inside
their CSS border and padding boxes. The main panel applies the gap on its
stacking axis, and the overflow panel applies row and column gaps to wrapping.
The owning `ToolBar` subtracts the panel's edges and gaps when deciding which
items overflow, including when CSS is changed or removed. The expanded
Release CSS, toolbar and theme regression passed 2,464 tests without skips in
`tests/Jalium.UI.Tests/TestResults/css-toolbar-panels-padding-final.trx`.
The main panel resolves percentage gaps against its content width and keeps
a native local `Spacing` value ahead of CSS. Both panels expose native
`Padding` properties, so CSS padding maps without a missing-property diagnostic;
percentage padding follows the containing width and yields to a local value.

[CSS Overflow 3 §3.2](https://www.w3.org/TR/css-overflow-3/#overflow-clip-margin)
places `overflow-clip-margin` at the padding edge by default and permits a
nonnegative length from the content, padding, or border edge. The CSS `clip`
path now uses that edge for descendants and hit testing, with corner radii
adjusted from the border edge. `hidden` and cross-axis promotion to `hidden`
ignore the margin. Native local clip values remain authoritative. Built-in
background-only panels and `Border` paint their own decoration before the
descendant clip, so an inset clip edge does not erase the border or background.
Parser, cascade, inheritance, relative-unit, geometry, native-priority and
input tests passed, alongside direct and recorded software/D3D12/Vulkan pixels.
The latest Release runs passed 1,872 CSS/Flex cases and 1,901 expanded
CSS/clip/Border/ScrollViewer cases without skips (2026-09-24).

### Native input optionality selectors

[Selectors 4 optionality](https://www.w3.org/TR/selectors-4/#opt-pseudos)
now maps `:required` and `:optional` to the native
`AutomationProperties.IsRequiredForForm` state or a `RequiredValidationRule`
on a `Binding`, `MultiBinding`, or the active child of a `PriorityBinding`
attached to a value-input control's value property.
The selectors are mutually exclusive, and non-input containers and grid rows
match neither. Changes to the native attached property, and binding attachment,
replacement or removal, and active `PriorityBinding` child changes, refresh
self, sibling and `:has()`-dependent styles;
generated CSS preserves the same behavior. Adding, removing, replacing, or
clearing rules in an attached binding's `ValidationRules` collection also
invalidates dependent styles. XAML has no HTML form-submission model. The Release
targeted optionality/code-generation suite passed 67/67 cases
(`css-required-mutable-rules-weak-targets.trx`). With the current
Windows native payload, the Release `FullyQualifiedName~Css` suite passed
3278/3278 cases without skips (`css-required-mutable-rules-final-full-css.trx`). The
binding and validation suite, excluding performance benchmarks, passed
277/277 cases (`css-required-mutable-rules-final-binding-validation.trx`).

### Native and bound input range selectors

[Selectors 4 range pseudo-classes](https://www.w3.org/TR/selectors-4/#range-pseudos)
now map `:in-range` and `:out-of-range` to `NumberBox` edit text, `Slider` and
`RangeSlider` values, bounded `DatePicker` selection, and value bindings with
`RangeValidationRule` limits. Inputs without range limits and non-input elements
match neither. Non-numeric edit text does not count as underflow or overflow.
Changes to values, native limits, binding attachment or active priority child,
validation-rule collections, and a rule's `Minimum` or `Maximum` invalidate
dependent styles. Direct selectors, sibling and `:has()` dependencies, and
generated CSS are covered. The Release range/code-generation suite passed 63/63
cases (`css-range-numeric-safe-targeted.trx`); the Release `FullyQualifiedName~Css`
suite passed 3288/3288 without skips (`css-range-verified-full-css.trx`). Related binding,
validation and input-control regressions passed 332/332 without skips
(`css-range-verified-input-regression.trx`).

### Modal and fullscreen selectors

[Selectors 4 display-state pseudo-classes](https://www.w3.org/TR/selectors-4/#modal-state)
now map `:modal` to a `Window` inside `ShowDialog()` and to an open
`ContentDialog` in popup or in-place placement. A `Window` in
`WindowState.FullScreen` matches `:fullscreen` and `:modal`. Window state and
dialog lifecycle transitions refresh self and dependent selectors, including
generated CSS. The XAML window is the fullscreen element; descendants do not
match `:fullscreen` merely because their host window does. The existing
`(display-mode: fullscreen)` media query remains a host-window condition.
The Release `FullyQualifiedName~Css` suite passed 3296/3296 without skips
(`css-presentation-full-css.trx`). The window, generated-CSS, and
`ContentDialog` lifecycle suites passed 92/92 without skips
(`css-presentation-dialog-regression-final.trx`), including popup and in-place
modal style activation and deactivation.

### Media playback and sound selectors

[Selectors 4 resource-state pseudo-classes](https://www.w3.org/TR/selectors-4/#resource-pseudos)
now map `:playing` and `:paused` to a `MediaElement`'s live playback state,
`:buffering` to active playback while the native buffering state is set, and
`:muted` to native `IsMuted`. Buffering therefore also matches `:playing`, as
the specification requires. A media element with no started playback matches
`:paused`; non-media controls match none of these states. Changes to the media
clock, playback operations, buffering events, and mute property refresh direct,
descendant, sibling, and `:has()` selectors. Seeking, stalled, and locked-volume
states have no corresponding native signal yet and remain unsupported.
The Release `FullyQualifiedName~Css` suite passed 3308/3308 without skips
(`css-media-playback-final-full-css.trx`). Generated CSS selectors passed
64/64 alongside the direct media-state tests
(`css-media-playback-codegen-mask.trx`); native media playback and event
regressions passed 26/26 (`css-media-playback-final-native-regression.trx`).

### Inherited insertion-caret styling

[CSS UI 4 caret-color](https://www.w3.org/TR/css-ui-4/#caret-color) now accepts
`auto` and CSS colors as an inherited property. Explicit colors, including
`currentColor` and mixes using it, reach the insertion-caret paint in TextBox,
AutoCompleteBox, NumberBox, RichTextBox, PasswordBox, EditControl and HexEditor.
The inherited computed color remains the parent's color even when a child has a
different foreground. `auto` restores the native caret choice, and a local
native `CaretBrush` takes priority. The Release `FullyQualifiedName~Css` suite passed
3323/3323 without skips (`caret-css-full.trx`); related native editor suites
passed 197/197 (`caret-native-regression.trx`).

[CSS UI 4 caret-animation](https://www.w3.org/TR/css-ui-4/#caret-animation)
now accepts inherited `auto` and `manual`. `manual` keeps the caret visible and
stops its native blink/fade timer in TextBox and its derived editors, RichTextBox,
PasswordBox and EditControl; HexEditor also stops its blink-state toggle. An
inherited change reaches an already focused child through the visual tree, and
switching back to `auto` restores the native timer behavior. The Release
`FullyQualifiedName~Css` suite passed 3335/3335 without skips
(`caret-animation-final-full-css.trx`), including generated CSS activation;
related native editor and timer suites passed 198/198
(`caret-animation-native-regression.trx`).

[CSS UI 4 caret-shape](https://www.w3.org/TR/css-ui-4/#caret-shape) now accepts
inherited `auto`, `bar`, `block` and `underscore`. Explicit shapes reach the
same seven editors. The block and underscore use the next grapheme's measured
advance, or an approximate `1ch` width at line end; the block's translucent
interior keeps the covered glyph legible. `auto` preserves each editor's native
caret shape.
The [caret shorthand](https://www.w3.org/TR/css-ui-4/#caret) accepts the color,
animation and shape in any order and resets omitted parts to their initial
values. CSS-wide keywords apply to all three parts. The implementation covers
horizontal text and hex cells; vertical writing modes and platform-specific
caret variants still require separate work. The Release `FullyQualifiedName~Css`
suite passed 3362/3362 without skips (`caret-shape-final-full-css.trx`); relevant
native editor suites passed 280/280 (`caret-shape-native-regression.trx`).

### CSS pointer targeting and clip-path shapes

[CSS UI 4 §6.2](https://www.w3.org/TR/css-ui-4/#pointer-events) makes
`pointer-events` inherited while allowing an explicit `auto` descendant to
receive input through a `none` ancestor. CSS now filters the element's own
box after trying its children; native `IsHitTestVisible` continues to control
the native subtree. CSS-wide `inherit`/`unset` resolve through the current
ancestor value, and a hit-test epoch prevents same-point window memoization
from retaining the wrong target after style or native source changes. Parser,
inheritance, native-priority, rounded-border and window-input regressions pass.

[CSS Masking 1](https://www.w3.org/TR/css-masking-1/#the-clip-path) requires
`clip-path` to clip painting and descendants to the actual contour. `inset()`,
`xywh()` and `rect()` (including `round`), `circle()`, `ellipse()` and the
geometry boxes now use the
existing exact elliptical-rectangle native clip on software, D3D12 and Vulkan.
Circle and ellipse centers accept the one-, two- and four-value CSS `<position>`
grammar, including offsets from the right and bottom edges.
For `closest-side`, each radius uses the distance to an extended reference-box
edge, so centers outside the box retain their specified nonzero geometry.
Direct and recorded software, D3D12 and Vulkan pixels confirm that this
geometry clips overflowing descendants at the correct contour.
The CSS clip remains active around background, children, border and outline,
and intersects the independent native/overflow layout clip. Hit testing uses
the same used geometry and style changes invalidate the window hit memo.
Effect captures balance the clip inside their offscreen target and reapply it
to the final composite; the software pixel blitter and Vulkan premultiplied
blur shader now honor that destination clip.
Managed software rendering also samples the filled shape of an arbitrary
`Geometry` clip instead of reducing it to its bounding box.
The previously implemented shapes passed parsing, used-size, input and
direct/recorded software/D3D12/Vulkan pixel tests. The 2026-09-24 Release
run passed 40 focused clip-path cases and 2,214 related CSS/clip/border/
ScrollViewer/software cases without skips.

[CSS Shapes 1 `polygon()`](https://www.w3.org/TR/css-shapes/#funcdef-basic-shape-polygon) now
parses length/percentage/math vertex pairs, `nonzero`/`evenodd` winding and
the optional `round` radius. It resolves against the chosen geometry box and
uses one closed path for managed-software drawing and pointer targeting;
tests cover reference-box resizing, self-intersection fill rules and clamped
rounded corners. Native software receives the path command stream and
reconciles the clipped group's framebuffer against path coverage on pop. D3D12
and Vulkan flatten the path at clip push, then apply its winding coverage in
the shared GPU clip shader to bitmap, text and other draw calls. Direct and
recorded native pixel tests cover a triangle, rounded polygon, winding-rule
difference, descendants, nested clips and filtered paint on all three backends,
including Impeller and Vello on the GPU backends. The focused managed/native
clip-path suite passed 74 cases on 2026-09-26; the CSS and path-parser regression
passed 3,467 cases.

[CSS Shapes 1 `path()`](https://www.w3.org/TR/css-shapes/#funcdef-basic-shape-path)
now accepts a quoted SVG path string with optional `nonzero` or `evenodd` fill
rule. CSS parsing rejects empty paths, malformed commands and the shared XAML
parser's `F0`/`F1` extension. Each open subpath closes for clipping; its fixed
CSS-pixel coordinates are translated by the selected reference-box origin, not
scaled when that box grows. Managed painting and input tests cover that origin,
implicit closure and nested-subpath holes. Direct and recorded native pixel
tests cover lines, holes and arcs on software, D3D12 and Vulkan, with both GPU
engines.

[CSS Shapes 1 `shape()`](https://www.w3.org/TR/css-shapes/#shape-function)
now accepts its command list with optional `nonzero`/`evenodd`, CSS positions,
`to`/`by` endpoints, horizontal and vertical lines, quadratic and cubic curves,
smooth control reflection, arcs with radii/sweep/size/rotation, moves and close.
Relative control points can anchor to the segment start, end or reference-box
origin. Lengths, percentages, math and custom properties resolve with the CSS
length context; percentages retain their reference-box basis as the element
resizes. Horizontal writing mode logical positions honor LTR and RTL.
Managed painting/input tests cover reference boxes, resizing, fill-rule holes,
curve controls, logical edges and math. Direct and recorded native pixel tests
cover lines, holes, curves and arcs on software, D3D12 and Vulkan with both GPU engines.
The focused managed/native clip-path suite passed 102/102 cases and the CSS plus
path-parser regression passed 3,496/3,496 cases on 2026-09-26.

[CSS Masking 1 URL references](https://www.w3.org/TR/css-masking-1/#the-clip-path)
now load external SVG or SVGZ `<clipPath>` definitions asynchronously through the
owning stylesheet's resource resolver. Relative URLs in imported sheets use the
imported sheet's base URI. The default resolver accepts local files, HTTP(S)
and `data:` URLs. For `data:` it preserves percent-encoded octets, supports
forgiving base64 without padding, and passes the declared media type to the
stylesheet decoder. CSS stylesheets and SVG clip sources have loading tests.
`userSpaceOnUse` coordinates resolve against the element's used box;
`objectBoundingBox` coordinates scale with its width and height. Supported
children are rectangles, circles, ellipses, paths, polygons, polylines, text,
groups and local `<use>` references, with SVG transforms and inherited `clip-rule`.
Text clipping uses shaped glyph outlines with their internal counters, rather
than glyph bounding rectangles. Native DirectWrite supplies Windows outlines;
the self-hosted font engine supplies Linux/Android outlines. Text anchor uses
measured advance width and ignores paint-only properties such as `fill`.
Nested `<tspan>` elements inherit font and visibility, can override their font
size (including parent-relative `em` and `%` and root-relative `rem`), and
consume `x`, `y`, `dx`, `dy` and `rotate` lists across descendant characters,
using UTF-16 indices as SVG specifies. An omitted rotation entry repeats the
final angle; a child rotation list overrides its ancestor's corresponding
angles. Absolute positions start new text chunks; `text-anchor` shifts each
chunk after measuring its runs. Nested span transforms also affect glyph clip
contours.
SVG image text uses the same positioning and preserves each span's fill,
opacity and transform in the drawing tree.
The `textLength` attribute on `<text>`, `<tspan>` or `<textPath>` accepts a horizontal length,
number or percentage. Its default `spacing` mode changes inter-character gaps
without stretching contours; `spacingAndGlyphs` also scales glyphs in the
inline axis. Nested lengths are resolved from child to parent, with a child
length treated as one spacing unit by its parent. The same adjusted advances
drive SVG image text and glyph clip contours. Full bidirectional text,
forced wrapping and complex positioned clusters remain outside this path.
[SVG 2 text on a path](https://www.w3.org/TR/SVG2/text.html#TextOnAPath)
is implemented for horizontal text in SVG images and SVG `<clipPath>` glyph
contours. `<textPath>` accepts inline `path` data or a local `href`/`xlink:href`
to a path or basic shape. An inline path wins; malformed data falls back to
the reference. The referenced shape's own transform applies in the current
text coordinate system. `startOffset` accepts lengths and percentages of the
whole path, and `text-anchor` shifts each absolute-`x` chunk independently.
Each glyph's midpoint follows the path and its tangent; `side="right"` reverses direction,
`dy` moves along the normal, and an absolute `y` is ignored. Glyphs whose
midpoints fall outside an open path disappear. A single closed subpath permits
one circuit, with its allowed wrap interval set by `text-anchor`. Numeric
`startOffset` values are calibrated by a referenced shape's `pathLength`;
percentage values use the measured geometry length. Ordinary text after a
path resumes at its endpoint. `method="stretch"`, `spacing="auto"`, vertical and
bidirectional layout, and complex glyph shaping on a path remain pending.
Multiple child silhouettes form a union, preserving opposite-winding overlaps
and individual holes. A valid empty `<clipPath>` clips all paint and input;
missing or non-`<clipPath>` targets leave them unclipped. Loading completion
invalidates painting and cached hit tests. Managed and direct/recorded native
software, D3D12 and Vulkan tests cover these behaviors. SVG image documents
also resolve same-document `url(#id)` on shapes, groups and `<use>` through the
same geometry parser. Object bounding box units use the referencing SVG
object's bounds in its local user space; nested local clip paths intersect,
with cycle rejection, and valid empty clips hide all image paint. Ordinary
XAML trees do not expose SVG definition nodes, so a fragment-only URL there
has no target. Full `textLength` behavior for bidi and forced wrapping,
exact shaping across individually positioned characters, Apple text
outlines and additional reference-box semantics remain pending.
Composite clips with more than 512 nonhorizontal edges or 16,384 sweep levels
are currently left unresolved to bound geometry work.
The focused clip-path suite passed 114/114 cases; the CSS and path-parser
regression passed 3,693/3,693 cases on 2026-09-26.
After local SVG fragment support, the combined CSS/path/SVG/vector-rasterizer
regression passed 3,738/3,738 cases on 2026-09-26.
With default `data:` resource loading, the same regression passed 3,742/3,742
cases on 2026-09-26.
With shaped SVG text in clip paths, 132 CSS clip-path/SVG parser cases, 20 SVG
text/geometry cases and 5 direct/recorded native clip-path cases passed on
Windows. The dedicated Linux native outline-path test passed. The broader
Windows regression was stopped after an unrelated theme-template contract
failure; the existing Linux pixel/fallback suite encountered a missing
Fontconfig fallback face, while its new outline assertions passed.
After nested `<tspan>` positioning, UTF-16 indexing, rotation, `textLength` and
rendering changes, the focused CSS clip-path/SVG parser/geometry/native clip
suite passed 173/173 on Windows, including direct and recorded `textLength`
glyph clipping on software, D3D12 and Vulkan.
After horizontal `<textPath>` support, that focused suite passed 183/183 on
Windows. Direct and recorded native software, D3D12 and Vulkan rendering
preserved the path-positioned glyph contour in a CSS clip.
After referenced `pathLength` calibration and anchor-specific closed-path
wrapping, the focused suite passed 186/186 on Windows.
After correcting radial `closest-side` geometry for off-box centers and
verifying the CSS layout-box fallback of `fill-box`, `stroke-box` and `view-box`,
the same focused suite passed 190/190 on Windows.

### CSS visibility inheritance and visible descendants

[CSS Display 3 §4](https://www.w3.org/TR/css-display-3/#visibility) keeps a
`visibility:hidden` box in layout and permits an explicitly visible descendant.
CSS visibility now controls each box's paint and input separately from native
`Visibility`; `display:none` and a native hidden/collapsed ancestor still gate
the whole subtree. `visibility:collapse` behaves like hidden in ordinary layout.
An inherited value follows the current ancestor, including after style changes,
and a native local value retains precedence. Direct and recorded pixels on
software, D3D12 and Vulkan cover visible descendants, box shadows, filters and
`display:none`; point and callback hit tests, same-point window targeting, focus
and input capture cover the same visibility boundary. The 2026-09-24 Release
run passed eight focused visibility cases and 2,366 related CSS/visibility/
input/rendering cases without skips. The unfiltered suite was stopped before
completion, so it provides no broader result for this increment. Table
collapse rules, inline document-node painting and geometry callback hit tests
remain outstanding; flex collapse is covered below.

### Atomic inline vertical alignment

[CSS 2.2 vertical-align](https://www.w3.org/TR/CSS22/visudet.html#propdef-vertical-align)
defines `middle` by the margin-box midpoint and half the parent's x-height.
The ordinary flow formatter now uses the selected native font's x-height for
that alignment. `sub` and `super` shift the baseline without changing the
item's font size; where subscript/superscript metrics are unavailable, they
use the [CSS Inline 3](https://www.w3.org/TR/css-inline-3/#propdef-baseline-shift)
fallback offsets of one fifth and one third of the parent's used font size.
Normal and float-constrained line layout share this calculation. Positive and
negative percentage offsets continue to use the item's own line height.
CSS Inline 3 `vertical-align:center` places the margin-box midpoint at the
line-box midpoint alongside `top` and `bottom` placement. The final Release
CSS flow/float/display/line-height regression passed 138 cases without skips
(`css-inline-vertical-align-final.trx`). These rules cover
atomic inline XAML boxes; splittable text fragments, table-cell alignment and
CSS Inline 3 alignment longhands remain open.

### Flex item `visibility:collapse`

[CSS Flexbox 1 §4.4 and §9.4](https://www.w3.org/TR/css-flexbox-1/#visibility-collapse)
requires two layout rounds. `FlexPanel` now measures the original lines, stores
each collapsed item's original line cross size after `align-content:stretch`,
then rewraps with zero main size for collapsed items. The second round excludes
them from flex growth, main-axis gaps and justification, while their struts keep
the new lines tall enough.
Collapsed flex items and their descendants are removed from painting and hit
testing even when a descendant declares `visibility:visible`; ordinary hidden
boxes still keep their full layout size. CSS `display:flex` on an existing Panel
uses the same algorithm without reparenting children. Absolutely positioned
children retain ordinary collapse visibility because they are not flex items.
Native `Visibility` and `display:none` precedence are covered. When the final
box differs from the measured constraint on either axis, both layout rounds now
remeasure items at their resolved main size and final cross constraint, then
recompute line heights and struts. Percentage and `calc()` flex bases resolve
again against the final inner main size before rewrapping; a native local basis
still wins. Repeated arrange calls also re-evaluate when the panel returns to
its original measured size. The 2026-09-24 Release run passed 80 focused flex/display/visibility/
basis cases and 1,996 broader CSS/Flex/visibility cases without skips.

### Flex first-baseline alignment

[CSS Flexbox 1 §8.3 and §9.4](https://www.w3.org/TR/css-flexbox-1/#algo-cross-item)
requires baseline participants in each row to share a baseline and uses their
largest ascent plus largest descent when sizing a line. `align-items:baseline`
and `align-self:baseline` now retain a baseline alignment value. Row and
row-reverse lines use the first rendered TextBlock line, an explicit native
`BaselineOffset`, or a synthesized border-box end edge. Native and CSS margins
are included in the baseline distances. Each wrapped line calculates its own
baseline, including wrap-reverse cross-start anchoring. In the second
`visibility:collapse` round, collapsed items keep their original line strut
without contributing a baseline to the new line. Changing a native baseline
offset invalidates the parent layout. The 2026-09-24 Release run passed 83
focused flex/display/visibility cases and 2,005 broader CSS/Flex/visibility
cases without skips (`css-flex-baseline-regression.trx`).

[CSS Flexbox 1 §8.5](https://www.w3.org/TR/css-flexbox-1/#flex-baselines)
requires a flex container to export baselines when it participates in an outer
baseline alignment context. Native FlexPanel and CSS `display:flex` hosts now
export the first or last row line's shared baseline, or the startmost/endmost
item's baseline when that line has no baseline group. Column flex containers
export the corresponding item's baseline after main-axis justification. The
offset includes the container's content insets, line distribution, and
`wrap-reverse` placement. Flex items also consume a nested flow or flex
container's first or last baseline, according to their alignment preference.
Ordinary flow boxes retain their first and last
line baselines through nested block children, so inline-block continues to use
its last line while inline-flex uses the flex container's first baseline.
Changing an attached native `BaselineOffset` invalidates the ancestor layout
chain up to its isolation boundary, including unhosted layout trees.
Orthogonal writing modes, column cross-axis baseline sharing, and the full WPT/backend
corpus remain outstanding. The 2026-09-25 Release run passed 110 focused
flex/flow cases (`css-nested-baseline-regression.trx`), 201 affected
flex/flow/float/text cases (`css-nested-baseline-affected.trx`), and 2,012 broader
CSS/Flex cases (`css-nested-baseline-broad.trx`) without skips.

### Flex auto margins and direction

[CSS Flexbox 1 §8.1 and §9.6](https://www.w3.org/TR/css-flexbox-1/#auto-margins)
requires flex auto margins to measure as zero, then absorb positive free space
before main-axis justification or cross-axis alignment. `FlexPanel` now divides
main-axis space among all auto margin sides on each line. A cross-axis auto
margin overrides `align-self`, with two auto sides sharing positive space;
overflow remains anchored at cross-start. Items with a cross-axis auto margin
are excluded from baseline groups, as required by
[§9.4](https://www.w3.org/TR/css-flexbox-1/#algo-cross-align). Collapsed items
do not absorb auto-margin space. Native local `Margin` retains precedence.

`FlowDirection: RightToLeft` now maps row main-start and column cross-start
to the physical right edge. Reverse directions and `wrap-reverse` invert their
respective axes. The CSS `direction` longhand maps `ltr`/`rtl` to the same
inherited dependency property on visual and document nodes; `initial`, `unset`,
native value precedence, parent direction changes and detached document-node
reevaluation are covered. Document nodes now report inherited dependency-property
sources consistently with their effective values.
The [CSS Cascade `all` shorthand](https://www.w3.org/TR/css-cascade-4/#all-shorthand)
leaves `direction` untouched. The Release run passed 95 focused cases; the
broader CSS/text/document/binding regression passed 2,989 cases with one
unrelated device-removal test skipped (`css-flex-direction-inheritance-regression.trx`).
After the dynamic-inheritance fix, the two direction lifecycle cases passed in
`css-direction-source-regression.trx`; the document/binding/source regression
passed 307 cases in `css-document-inheritance-source-regression.trx`. The
additional main-axis auto-margin split and overflow cases passed in
`css-flex-auto-margin-overflow.trx`, and the existing-Panel RTL/auto-margin
integration case passed in `css-flex-rtl-existing-panel.trx`.

### Grid row baseline alignment and export

[CSS Grid 2 §11.6](https://www.w3.org/TR/css-grid-2/#grid-baselines)
and its [track-sizing algorithm](https://www.w3.org/TR/css-grid-2/#algo-track-sizing)
require row baseline groups to affect both item placement and intrinsic track
contributions. CSS `align-items:baseline` and `align-self:baseline` now share
the largest first-baseline ascent in each grid row. The resulting shim grows
non-subgrid intrinsic row contributions, including an item spanning rows from
its first row. Explicit native offsets, text metrics and nested flex/flow/grid
baselines contribute; native local vertical alignment retains precedence.
Block-axis CSS auto margins override baseline self-alignment and absorb positive
free space, splitting it when both sides are auto.

Grid containers now export their first/last row baseline to outer flex and
ordinary flow contexts. Inline-grid uses the first baseline. CSS `baseline`
and `first baseline` share the row-start group; `last baseline` uses a separate
row-end group and adds the needed shim to intrinsic row contributions. A
spanning item participates in the last row it spans for last-baseline alignment.
Flex rows likewise keep independent first/last groups, including `wrap-reverse`;
multi-line TextBlock supplies its last line baseline. The two-word values parse
in `align-items`, `align-self`, and as the first component of `place-items` or
`place-self` when the second component is supported.

When the used auto height differs from the measurement constraint, the baseline
export path sizes tracks against that height so `align-content` is reflected.
Grid order uses logical column indices under both LTR and RTL. Inline-axis
baseline groups, cross-level subgrid baseline sharing, column-flex baseline
sharing, cyclic intrinsic-size fallback and the full WPT/backend corpus remain open.
The 2026-09-25 Release run passed 210 focused grid/subgrid/flow/flex cases
(`css-grid-baseline-affected.trx`) and 2,027 broader CSS/Flex cases
(`css-grid-baseline-broad.trx`) without skips.
The last-baseline increment passed 122 focused grid/flex cases
(`css-last-baseline-focused-final.trx`) and 2,042 broader CSS/Flex cases
(`css-last-baseline-broad-final.trx`) without skips.

### CSS Color 4 gradient interpolation

For `linear-gradient()` and `radial-gradient()`, the parser now carries source
space, precise components, alpha and missing-component markers through stop
interpolation. In keeping with [CSS Color 4 interpolation](https://www.w3.org/TR/css-color-4/#interpolation),
gradients with only legacy colors use gamma-encoded sRGB; a modern color selects
Oklab by default. The prelude accepts all fifteen CSS Color 4 interpolation
spaces (plus the `xyz` alias) and `shorter`, `longer`, `increasing` or
`decreasing` hue on polar spaces. Interpolation is alpha-premultiplied;
analogous missing channels and alpha take their partner's value. A middle stop
with missing components gets separate left/right effective colors at the same
position. Intermediate wide-gamut values are converted only after interpolation.
Adaptive sRGB stops approximate that result on the existing gradient backends,
with a bounded native stop count.

The WPT `legacy-color-gradient.html` and
`css-color-4-colors-default-to-oklab-gradient.html` source gradients retain
their red/green endpoints and 480 × 50 reference size. Three interior pixels
from each upstream reference PNG were checked within four 8-bit channel values
on Windows software, D3D12 and Vulkan; all three backend tests passed in
`artifacts/css-gradient-interp/test-results/css-gradient-hint-single-native.trx`.
The same backends compare interior pixels for the HSL/LCH
`gradient-hue-direction.html` increasing/shorter and decreasing/longer pairs.
The Windows CSS class-name regression passed 1,450 tests without skips in
`css-gradient-hint-full-final.trx`. Managed checks cover all interpolation
spaces, four polar hue routes, radial forms, alpha premultiplication and
analogous missing-channel carry-forward, including the split middle stop from
`gradient-none-interpolation.html`. Linux passed all 44 focused managed
interpolation tests in `css-gradient-hint-linux-final.trx`; the
available older SDK reports a source-generator compiler-version warning, so
this does not qualify generated XAML or Linux native pixels. This is a sampled
rendering path; the remaining analogous-missing and out-of-gamut corpus,
full-gradient pixel comparison, animation source preservation
and device execution remain open.

### CSS gradient transition hints and single stops

The [CSS Images 4 gradient color rules](https://www.w3.org/TR/css-images-4/#coloring-gradient-line)
define a transition hint as the position of the 50% blend. Percentage hints,
computed percentages and unitless zero now use the specified exponential weighting.
Hints and authored stops share an ordered fixup pass before omitted positions
are distributed. Hints at either boundary use their limiting step behavior;
double-position stops and negative percentage positions remain distinct.
The parsed interpolation space still determines the actual halfway color.
One authored color stop now produces a solid gradient on both linear and radial
paths, regardless of its specified position.

Managed tests retain the valid/invalid percentage-hint forms from WPT
`color-stops-parsing.html`, and the single-stop solid-color assertion from
`gradient-single-stop-001.html`. Windows software, D3D12 and Vulkan also compare
three interior pixels against the CSS Images 4 red/blue 25% hint example and
paint the single green stop; all three backend cases passed in
`artifacts/css-gradient-interp/test-results/css-gradient-hint-single-native.trx`.
This hint increment preceded the used-size length resolution below. It does
not claim whole-frame WPT parity.

### CSS gradient used lengths and linear geometry

CSS stop and hint lengths now resolve against the actual gradient line at paint
time, following [CSS Images 3 linear-gradient geometry](https://www.w3.org/TR/css-images-3/#linear-gradients)
and [CSS Images 4 stop positions](https://www.w3.org/TR/css-images-4/#color-stop-syntax).
For a line at angle A inside a W × H box, its used length is
`|W sin(A)| + |H cos(A)|`; corner keywords derive their angle from the box's
aspect ratio. The original `CssLength` is retained through style application,
so absolute, font-relative and mixed `calc()` values use the styled element's
current rulers and the current paint bounds. The shared length resolver also
receives viewport/container rulers; their gradient-specific dynamic corpus
remains open.
Resolved brushes are cached per element and used size. The retained recorder
resolves them on the UI thread before snapshotting, while direct/native and
managed software draws resolve at their own paint bounds. Native local
Background values remain authoritative.

The WPT `linear-gradient-calc-em-units.html` blue/yellow pair uses 10px and 30px
font sizes; its `calc(2em)` stop lands at 20px and 60px respectively. The
`linear-gradient-non-square.html` port checks that the 50% boundary crosses
opposite corners of a 200 × 100 box. Windows managed and software pixels plus
software/D3D12/Vulkan native scenes cover those cases, a same-element resize,
mixed `calc(50% + 10px)`, radial rightward-radius stop lengths, generated CSS
and local-value precedence. Vulkan's shared stop search and hard-stop fan
boundary sampling were corrected to keep the specified later color on and
after an equal-position stop.

Windows passed 1,461 CSS class-name tests with no skips in
`artifacts/css-gradient-length/test-results/css-gradient-length-full-final.trx`.
The isolated rebuilt native core/D3D12/Vulkan payload passed all three focused
pixel cases in `css-gradient-length-native-hard-stop.trx`. Linux compiled
core/software/Vulkan in `/tmp/jalium-css-gradient-native`; eight focused managed
tests passed in `css-gradient-length-linux-final.trx`. Linux uses an older SDK whose
source-generator compiler-version warning means generated XAML and Linux GPU
pixels are not qualified here. The nondegenerate radial ending shapes are
addressed below. Conic forms are covered by the later used-size image increment;
gradient background sizing and positioning,
whole-frame WPT pixel comparisons and device execution remain open.

### CSS radial gradient ending shapes

Following [CSS Images 3 radial geometry](https://www.w3.org/TR/css-images-3/#radial-gradients)
and the [Images 4 explicit-size extension](https://www.w3.org/TR/css-images-4/#radial-size),
nondegenerate radial gradients now calculate their used center and ending shape
from the paint box. `circle` has equal physical X/Y radii on a non-square box.
Circle and ellipse `closest-side`, `farthest-side`, `closest-corner` and
`farthest-corner` select the respective edge/corner distances; omitted shape
and size default to a farthest-corner ellipse. Explicit one-radius circles
and two-radius ellipses use absolute/relative lengths, mixed math and the
specified percentage bases, including Images 4's scaled-diagonal percentage
for circles. Two- and four-component `at` positions resolve at used size.

`CssRadialGeometryTests` covers a 200 × 100 box, offset centers, each extent,
explicit radii, 10px/30px font-relative radii, same-element resize and the
`50cqw 50px` center from WPT
`radial-gradient-container-relative-units-001.html`, including a container
width change. `CssRadialGeometryNativeTests` checks circle isotropy, default
ellipse corner color and the CSS Images closest-side example at interior
pixels on Windows software, D3D12 and Vulkan. With the isolated rebuilt
Windows native payload, the CSS class-name regression passed 1,485 tests,
no skips, in `artifacts/css-radial-geometry/test-results/css-radial-full-final.trx`.
Linux passed 21 focused managed cases in `css-radial-linux.trx`; its older SDK
reports the existing source-generator version warning. Zero-radius ellipse
rules, remaining CSS position combinations, complete WPT image comparison
and physical device runs remain open.

### Repeating linear and radial gradients

The [CSS Images 3 repeat rules](https://www.w3.org/TR/css-images-3/#repeating-gradients)
shift fixed-up source stops by the first-to-last stop distance. Both repeating
functions now use the existing interpolation pipeline, then expand a bounded
stop list over the actual paint box. Radial gradients extend their ending shape
through the farthest painted corner without changing the source period.
Zero-length or subpixel periods use a premultiplied sRGBA average; very large
stop expansions use that bounded fallback as well.

Managed tests cover period phase, paint-size changes, radial coverage, average
colors and invalid grammar. Native Windows software, D3D12 and Vulkan tests
check linear repeats, radial corner colors and a zero-period average. The same
1,560-case CSS/Flex regression passed with no skips in
`artifacts/css-font-support-validation/test-results/css-font-repeat-final-v3.trx`.
The isolated Release test directory initially contained an older Vulkan DLL,
which failed an existing hard-stop pixel case. Staging the previously rebuilt,
validated Vulkan DLL from `artifacts/css-gradient-length/build/bin/Jalium.UI.Tests`
made that case and the new repeating-gradient pixels pass. This run does not
qualify Linux, Android, Apple or physical-device pixels for repeating gradients.

### Conic and repeating conic gradients

[CSS Images 4 conic gradients](https://www.w3.org/TR/css-images-4/#conic-gradients)
sample color by angle around a center. `conic-gradient()` and
`repeating-conic-gradient()` now parse `from` angles, `at` positions,
interpolation methods, angular/percentage stops, double-position stops and
transition hints through the shared stop pipeline. Used-size resolution creates
an Oklab-interpolated result by default, including legacy-color stops, then
an ImageBrush raster for the painted box; a bounded angular ramp avoids doing
color-space conversion once per pixel. The raster follows live box dimensions
and caps each axis at 2048 pixels while retaining aspect ratio. The native
ImageBrush path keeps rectangle, ellipse and rounded-corner clipping; software
drawing and retained recorder replay resolve the same used-size image. Its
static BitmapImage source remains recordable for cross-thread frame replay.

Managed tests retain the original 200 × 200 box and gradient expressions from
WPT `conic-gradient-angle.html`, `conic-gradient-angle-negative.html` and
`conic-gradient-center.html`, plus the first solid-color case from
`gradient/conic-gradient-001.html`. They also cover repeat phase, invalid
angular grammar, resize, the raster cap and recorder replay. Windows software,
D3D12 and Vulkan pixel tests check four quadrants, repeating phases and rounded
clipping. Tests also verify native background precedence and generated-CSS
application. The Windows CSS/Flex regression passed 1,624 cases without skips in
`artifacts/css-conic-validation/test-results/css-conic-regression-final-v7.trx`.
As in the preceding gradient validation, the test directory uses the previously
rebuilt Vulkan DLL. Large/high-DPI full-resolution output,
complete position grammar, whole-image WPT comparisons and other-platform/device
pixels remain open.

### Rounded-corner increment

The percentage/elliptical `Border` increment follows
[Backgrounds and Borders 3, 2024-03-11](https://www.w3.org/TR/2024/CRD-css-backgrounds-3-20240311/#border-radius).
It preserves circular native `CornerRadius` values and bindings while keeping the
two CSS axes in a separate computed snapshot. Removing CSS, overriding it with an
equal native value, and starting/removing native animation invalidate the correct
paint even when the public horizontal projection has not changed.

Validation used independent `artifacts/css-radius-*` roots:

| Check | Result | Evidence |
| --- | --- | --- |
| Windows functional regression | 1,852 passed, no skips | `artifacts/css-radius-validation/test-results/css-radius-wpf-regression.trx` |
| Linux functional regression | 1,845 passed, six skipped | `artifacts/css-radius-linux/test-results/css-radius-linux-regression.trx` |
| Gallery CSS pages | 23 passed, including the native width-binding radius demo | `artifacts/css-radius-gallery/test-results/css-radius-gallery.trx` |
| Android shared managed build | Passed, zero errors; existing FocusVisualAdorner and CollectionView warnings | `artifacts/css-radius-android.log` |
| win-x64 NativeAOT | Published and ran; elliptical painting, input and native takeover checked | `artifacts/css-radius-aot-publish.log`, `artifacts/css-radius-aot-run.log` |

The functional filter covers CSS, FlexPanel, corner/bounds clipping, bindings,
templates, property precedence, animations, TextBlock and relevant retained/software
drawing tests, excluding `Benchmark` classes. An initial broad filter accidentally
included the long-running template-performance benchmark; those runs were stopped,
diagnosed and superseded by the completed functional runs above. No benchmark result
is claimed. Existing trimming/AOT warnings remain.

Managed software pixels for the 50% ellipse match an independent ellipse primitive
exactly. Two WPT shorthand/longhand pairs retain their original box dimensions and
match exactly. Native software, D3D12 and Vulkan checks verify elliptical background/
border path colours away from antialiased contours; these are not full pixel-parity
or clip tests. Those initial tests preceded the native clip increment below.
Other controls/templates, outlines,
shadows, backdrops, symbolic transitions and device validation remain open. This
increment does not close the rounded-corner conformance gate.

### Native elliptical clip increment

`JaliumEllipticalRectClip` adds a versioned 56-byte C descriptor and an optional
side interface; existing RenderTarget vtable slots are unchanged. It carries used
radii and edge flags rather than expanding open edges to enormous native floats.
The software backend evaluates captured inverse transforms and retains every nested
contour. Its gradient, bitmap, glyph and effect caches include the new clip data.
D3D12 batches keep immutable clip snapshots in the frame upload buffer; Vulkan uses
separate, fence-protected storage buffers and descriptor pools for each in-flight
frame. Both native path engines carry the draw-time state through deferred replay.

`CssEllipticalClipNativeTests` checks rectangles, bitmaps, text and compound paths
through Windows software plus Impeller/Vello on D3D12/Vulkan. Cases cover native
`Border` overflow, transformed clips, open sides, thick opposite borders, empty
clips, restoration and 32 nested contours. Repeated identical contours have exact
whole-image equality; other comparisons require exact channels outside a documented
2px antialiasing band. This is not a claim of whole-image cross-backend AA parity.
The standalone native software test compares all pixels in eight independent C ABI
scenes and runs on Windows and Linux.

The expanded rendering checks also reproduced a pre-existing first-frame Vulkan
ColorMatrix crash in the old `artifacts/css-validation` binaries. Effect pipeline
creation now ensures a valid render pass and commits the per-frame descriptor aliases
before replay reloads them. All eight existing `VulkanEffectGpuRtSmokeTests` then pass.

Shader regeneration is reproducible with `tools/rebuild-css-clip-shaders.ps1`, using
FXC and DXC from the installed SDKs. The shared analytic HLSL source also generates
the C++ embedded source used by the D3D12 stencil cover path. This introduces no
new application runtime dependency.

The D3D12 embedded bytecode has been synchronized with the current elliptical-clip
and Gaussian-blur shader sources. Before regeneration, the stale bytecode allowed a
solid background beneath a gradient to escape its rounded clip and caused a blur
edge regression. The targeted D3D12 background, clip and shadow cases now pass;
the Windows Debug `FullyQualifiedName~Css` test filter passes 3,047/3,047 cases
with no skips.

| Check | Result | Evidence |
| --- | --- | --- |
| Windows CSS/WPF and expanded native rendering regression | 1,946 passed, no skips; GPU validation requested | `artifacts/css-radius-validation/test-results/css-clip-wpf-regression.trx` |
| Linux CSS/WPF regression | 1,846 passed, 11 Windows-only cases skipped | `artifacts/css-radius-linux/test-results/css-clip-linux-regression.trx` |
| Windows/Linux native software clip C ABI | Eight full-image scenes passed; Linux font-unit checks also passed | `jalium.native.software.elliptical-clip` CTest in `artifacts/css-clip-native/windows` and `artifacts/css-clip-native/linux`; `artifacts/css-clip-linux-native-tests.log` |
| Gallery | 24 CSS page checks passed, including clipped child ownership/input | `artifacts/css-radius-gallery/test-results/css-clip-gallery.trx` |
| NativeAOT | win-x64 publication and execution passed with the new native libraries | `artifacts/css-clip-aot-publish.log`, `artifacts/css-clip-aot-run.log` |
| Android native | Core/software/Vulkan arm64 API 24 builds passed; existing switch warning remains | `artifacts/css-clip-android-build.log` |
| Metal interface | Non-Apple `metal_backend.cpp` passed C++20 syntax compilation | No Apple SDK/device run; base RenderTarget vtable unchanged |

The Windows filter adds native software/D3D12/Vulkan rendering classes to the earlier
functional filter and still excludes benchmarks. The old-binary crash reproduction is
retained in `artifacts/css-clip-colormatrix-baseline.log`; it was repaired, not excluded.
Actual GPU coverage in these new clip cases is Windows; Linux GPU pixels and Android
device execution remain unverified. Specialized effect/shader paths and full
rounded-corner conformance remain open. Template propagation and ImageBrush shape
painting are addressed by the following increments.

### Template radius propagation

Explicit CornerRadius template bindings now transfer a separate CSS presentation
value at template precedence, preserving the original binding and child objects.
Typed, runtime/generated and public compatibility bindings are covered. Source
percentages use the control's border box before transfer. Native target/source local
values, triggers, current-value overrides, coercion and animation remain authoritative;
converters keep control of their own output. Vertical-only changes and height-only
resizes invalidate cached template paint even when the native horizontal projection
does not change.

Plain circular CSS radii now use the same border/clip geometry as elliptical CSS
radii, including independent insets on asymmetric borders. Controls clip at the outer
edge and bound Borders clip content at the padding edge. Explicit native clip values
on the template part are preserved. Template replacement clears the discarded parts'
owner and subscriptions, while user content and nested templates retain their own
ownership. Reattaching a template binding resolves the new owner; discarded parts
can be collected while the original control stays alive.

Windows passed 1,962 CSS/WPF/native rendering cases in
`artifacts/css-radius-validation/test-results/css-template-wpf-regression.trx`.
Linux passed 1,862 cases with 11 Windows-only skips in
`artifacts/css-radius-linux/test-results/css-template-linux-regression.trx`.
Android managed compilation passed (`artifacts/css-template-android.log`), and the
win-x64 NativeAOT executable verified template propagation and teardown
(`artifacts/css-template-aot-run.log`). Existing compilation/trimming warnings remain.
The native rendering cases exercise template-bound chrome and equal/local takeover
on software, D3D12 and Vulkan without changing the native clip ABI.
Gallery passed 25 CSS page checks, including the native Button-template example,
in `artifacts/css-radius-gallery/test-results/css-template-gallery.trx`.

### ImageBrush contours and arbitrary paths

A native XAML `ImageBrush` used as a CSS-rounded background now keeps its source
object and full tile-brush semantics. Viewport/viewbox units, stretch, alignment,
all four tile modes, brush opacity, `Transform` and `RelativeTransform` are evaluated
without converting the brush to a representative colour. Bilinear interpolation is
performed in premultiplied space so RGB hidden in a transparent texel cannot bleed
into a visible neighbour. Bitmap decode hints follow the live device extent, and a
cached upload no longer suppresses a larger deferred-decode request after resize.

Rectangle, uniform rounded-rectangle, independent circular-corner and two-axis CSS
contours clip the actual bitmap tiles through the native software/D3D12/Vulkan clip
stack. The test probes the optional two-axis native interface before checking pixels;
an older payload retains uniform X/Y radii through the legacy rounded-rectangle ABI,
keeps independent circular corners where possible, and reports a lossy diagnostic for
the remaining per-corner two-axis case.

An arbitrary `PathGeometry` fill is rasterized through an exact anti-aliased geometry
mask; the former bounding-box overpaint path has been removed. The adapter cache is
bounded to 16 geometry/brush pairs and 64 MiB and is cleared with the normal bitmap
cache. It tracks the outer DrawingImage generation and every sampled inner source, so
in-place WriteableBitmap edits, deferred publishes, animated frame changes, brush and
geometry mutations, and nested DrawingImage/SvgImage content invalidate the right
raster while preserving source/brush identity. Direct vector ImageBrush realizations
use the same dependency-aware cache.

Validation used current Windows and Linux native payloads:

| Check | Result | Evidence |
| --- | --- | --- |
| Focused image/decode/SVG behavior | 39 passed, no skips | `artifacts/css-radius-validation/test-results/css-image-brush-focused.trx` |
| Final image/outline rendering delta | 42 passed, no skips; Linux portable subset 36 passed | `artifacts/css-radius-validation/test-results/css-image-outline-focused-final.trx`, `artifacts/css-image-brush-linux-10401/test-results/css-image-outline-linux-focused-final.trx` |
| Windows CSS/WPF/rendering regression | 2,291 passed, no skips | `artifacts/css-radius-validation/test-results/css-elliptical-outline-wpf-regression.trx` |
| Linux CSS/WPF regression | 2,250 passed, 28 platform skips | `artifacts/css-image-brush-linux-10401/test-results/css-elliptical-outline-linux-regression.trx` |
| Native software/D3D12/Vulkan image contours | Three backends passed and each exposed the current two-axis side interface | `CssImageBrushNativeTests` in the focused and Windows regression results |
| Gallery | 26 CSS page checks passed, including ImageBrush and two-axis outline examples | `artifacts/css-image-brush-gallery/test-results/css-elliptical-outline-gallery.trx` |
| Android managed package | Built for `net10.0-android`, zero errors | `artifacts/css-image-brush-android/bin/Jalium.UI.Android/Debug/net10.0-android` |
| NativeAOT | win-x64 publish ran successfully and printed `CSS NativeAOT smoke passed`, including exact ImageBrush contour pixels | `artifacts/css-image-brush-aot/publish` |

The Linux run used an isolated .NET SDK 10.0.401 and freshly built Linux x64 native
libraries. One `SoftwareRasterFastPathTests` case selected only by the broad word
`Corner` was excluded from the final Linux filter because it directly creates a Win32
`user32.dll` hidden window; it passed in the Windows run. ImageBrush pens/strokes,
DrawingBrush/VisualBrush and masks remain open; CSS image lists are covered in
the background section above.

### Elliptical outlines

`FrameworkElement` outlines now resolve the same used CSS radii as background,
border, clipping and input. Each non-square corner expands independently by
`outline-offset + outline-width/2`; percentages re-resolve against the live border
box and the expanded result is normalized against the outline centreline rectangle.
Solid, dashed and dotted styles all consume that two-axis geometry. A local/current/
animated native `CornerRadius` still suppresses the CSS contour and restores the
existing native circular fast path.

The focused outline/radius set passed 47 cases. `CssOutlineRadiusNativeTests` then
distinguished the flat CSS ellipse from the former horizontal-radius circle at exact
pixels on software, D3D12 and Vulkan, and repeated the scene after native takeover.
The expanded Windows and Linux results are the 2,291/2,250 runs above. Gallery's
Outline page includes a four-corner slash-syntax example; Android compilation and
the NativeAOT executable also passed, with AOT checking the two distinguishing
outline pixels. Custom effects and backdrop geometry remain open; elliptical
inset shadows are covered below.

[CSS UI 4 outline patterns](https://www.w3.org/TR/css-ui-4/#outline-style)
also require `double`. It now uses two one-third-width strokes with a one-third-width
gap, each on its own centreline around the same elliptical contour. Managed
geometry and pixel tests, plus Windows software/D3D12/Vulkan pixels, verify both
bands and the gap. `groove`/`ridge`/`inset`/`outset` now use side-specific light
and dark shades with the same alpha as the source color. Groove and ridge use
opposite shades in their outer and inner halves. Explicit side-band paths paint
on managed software and Windows software/D3D12/Vulkan. An
opaque undercoat closes the four diagonal side joins. For translucent solid
colors, the four shaded sides now rasterize together into an opaque color field;
one final alpha and outline-ring mask avoid repeated blending at square and
elliptical joins. Tests compare join alpha against a solid-outline reference,
including a half-width split and a mutable `currentColor`. Windows software,
D3D12 and Vulkan also check a translucent square join. Solid `Border` sides
with shared alpha now use the same one-pass composition principle; side colors
with different alphas and patterned/relief styles remain separate draws.
Large negative `outline-offset` values now clamp the outside dimensions to at
least twice `outline-width`, as required by CSS UI 4. Solid, dashed, dotted,
double and relief contours share this limit; managed and native tests exercise
offsets large enough to collapse the old contour.
The `outline` shorthand now rejects duplicate width, style and color components.
`outline:none` resets the omitted width to `medium` (3 px) and the color to
`auto` while its `none` style suppresses painting, following CSS UI 4's
computed-width rule. Shorthand widths now share the longhand's relative-length
resolution, including font-relative lengths and math, and update when the font
changes. `outline-style:auto` keeps its distinct computed value and draws a
solid ring at the medium width; the authored width is ignored.
Standalone outline longhands materialize the omitted CSS initial width,
style and color, so `outline-style:auto` paints without a shorthand.
`outline-color:auto` uses the accent brush with auto style and `currentColor`
with other styles. Explicit `inherit` preserves the parent's computed `auto`
or symbolic `currentColor`, which resolves against the child's own color.
It responds when the parent's style changes. Explicit colors and native local
`OutlineBrush` values take priority, and the removed `invert` keyword is
rejected. The Release
`FullyQualifiedName~Css` suite passed 3382/3382 without skips
(`outline-longhand-initials-full-css.trx`); focused managed and native outline suites passed
64/64 (`outline-longhand-initials-native-regression.trx`).
The Release CSS/Flex/Outline regression passed 2,134 tests without skips in
`artifacts/css-background-geometry-validation/test-results/css-outline-double-final.trx`.
After the relief and negative-offset additions, the same Release filter passed
2,144 tests without skips in
`artifacts/css-background-geometry-validation/test-results/css-outline-relief-offset-final.trx`.
After the translucent outline join change, the Release CSS/Flex/Outline filter
passed 2,147 tests without skips in
`artifacts/css-background-geometry-validation/test-results/css-outline-relief-single-pass-final.trx`.

### Elliptical outer box shadows

Element-effect capture now carries all eight used CSS radii through direct rendering,
whole-frame recording and replay. A new optional `EllipticalEffectProvider` side
interface accepts the existing versioned `JaliumEllipticalRectClip` descriptor, so the
base `RenderTarget` vtable is unchanged and older native payloads retain the horizontal
projection with a one-shot lossy-conversion diagnostic.

D3D12 transports the four Y radii through previously unused fields in the existing
160-byte SDF instance; `shadowMode=2` evaluates a per-corner elliptical distance without
changing the instance ABI. Vulkan reuses its existing per-corner X/Y push constants and
224-byte layout. Both feed their analytic erf Gaussian falloff, while software continues
to blur the exact captured alpha. Native/current/animated `CornerRadius` takeover keeps
the established scalar path.

`CssEllipticalShadowNativeTests` probes the side interface and compares the red-only
shadow channel before and after native takeover on software, D3D12 and Vulkan. The
focused effect/capture/GPU set passed 48 tests. The expanded Windows run passed 2,470
tests; Linux passed 2,383 with 40 platform skips. Both Android ABIs rebuilt with NDK
27.2 for API 24 and passed the 16 KiB ELF/payload validation; Gallery's 26 CSS page
checks include the new shadow example. Blur clipping, backdrop materials and custom
effects still need the same two-axis integration.

### Elliptical inset box shadows

The inset shadow uses the padding edge derived from the live border widths and all eight
used CSS radii. The effect capture and whole-frame recorder preserve those border widths,
including when a native local radius takes over. The new optional C ABI entry accepts
an inset contour and signed spread; older native payloads report their lossy fallback.

The [Backgrounds and Borders 3 shadow-shape rules](https://www.w3.org/TR/css-backgrounds-3/#shadow-shape)
contract the inset shadow perimeter for positive spread and expand it for negative spread.
Each X/Y corner radius changes independently, including the cubic adjustment for small
radii on negative spread. Software, D3D12 and Vulkan evaluate the shifted perimeter with
an erf Gaussian transition, clipped to the padding contour. The shadow is visible with
zero offset and on a transparent background; ancestor rounded clips still constrain it.
The native-only effect path retains its existing WPF behavior.

`CssInsetShadowNativeTests` checks positive/negative spread, zero offset, padding-edge
clipping, transparent content, native radius takeover and a parent rounded clip on all
three Windows backends. The standalone software C ABI test covers the same signed-spread
and transparent-content cases on Windows and Linux. These pixel cases do not establish
whole-image cross-backend antialiasing parity. Full rotated-effect geometry,
non-CSS mixed effect-group compositing and WPT coverage remain open. CSS
`box-shadow` list layering is covered above.

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
| Windows managed | 1583 CSS/Flex/font/text/binding/template/precedence/animation/Grid/panel regressions passed (`css-woff2-wpf-regression.trx`), including 41 WOFF 2 load-result ports. Reconstructed TrueType/CFF fonts reached software, D3D12 and Vulkan; software pixels matched the uncompressed font exactly. Earlier broader layout/WPT evidence remains in `css-relative-unit-wpf-regression.trx` | Full conformance corpus and remaining functionality |
| Linux source build | Isolated SDK 10.0.401; 1580 selected CSS/Flex/font/text/binding/template/animation/layout cases passed, with two Windows-only pixel cases skipped (`css-woff2-linux-regression.trx`). WOFF 2 reconstruction and native font metrics passed on software/Vulkan; D3D12 is not a Linux backend | Full Vulkan/software platform visual corpus |
| Windows effects | 24 existing software/Vulkan effect parsing and render-target cases passed | CSS-specific visual corpus and D3D12 coverage |
| Gallery | 22 CSS page tests passed with the demo using WOFF 2 first and TTF fallback, including actual glyph-driven width changes, retained Text binding and local FontSize, plus prior unit/layout/selector/import/converter cases (`css-woff2-gallery.trx`) | Examples for remaining functionality |
| Native font interface | Windows D3D12/Vulkan/software and Linux core/text/software/Vulkan libraries built in isolated directories; Windows and Linux `jalium.native.font-units` passed, covering the C ABI, legacy-interface fallback, native face/glyph rulers, zero-glyph layout agreement and line-spacing independence | Broader typeface/fallback/variation and GPU/device corpus |
| Android | Shared managed code compiled with 0 errors (existing FocusVisualAdorner and CollectionView warnings). Core/text/software/Vulkan arm64 libraries compiled with NDK 27.2.12479018 for API 24; existing Vulkan switch warning remains | Emulator/device execution and Vulkan/software visual validation |
| NativeAOT | win-x64 publish succeeded; the executable loaded the native core/software libraries and printed `CSS NativeAOT smoke passed`, including embedded transformed WOFF 2 decoding, private native registration, arrival-driven layout and preserved text/size. Earlier glyph rulers, viewport, math, precedence, namespace, import, scope, registration, container and layout cases remain covered | Broader AOT app coverage and warning audit |
| Metal | Existing TextFormat vtable preserved; non-Apple Metal implementation compiled as an isolated C++ object target after removing an existing duplicate CacheIdentity declaration | Apple SDK/device validation and native Metal font-unit implementation; no new Apple host |

Retain original WPT identifiers and specification references when adding equivalent XAML cases. Record unavailable platform checks as unverified; do not count them as passes or hide missing functionality behind permissive pixel tolerances.

The font-unit native checks use `artifacts/css-native/windows` and
`artifacts/css-native/linux`, with binaries under `artifacts/css-native/bin/<rid>/Release`.
The separate `artifacts/css-math-native/windows` and `artifacts/css-math-native/linux`
builds validate the optional MATH-constants ABI, malformed-table fallbacks, and
Windows software/D3D12/Vulkan compilation. Windows managed tests use those
isolated DLLs to compare Cambria Math depth transitions with the selected
font's actual script percentages. Linux core/text/software and the native
`jalium.native.font-units` test also pass with Cambria Math exposed through a
temporary fontconfig configuration: the selected face returns its 0.73/0.60
script percentages. With `JALIUM_TEST_CAMBRIA_MATH=1`, the focused Linux managed
software-backend test passes the same depth transitions using the isolated
linux-x64 native libraries. The test is opt-in because the font is not part of
the repository. Metal's provider is wired to read the selected Core Text font's
MATH table through the optional interface; its non-Apple C++ syntax check passes,
while Apple SDK and device execution remain unverified.
`ctest -R jalium.native.font-units --output-on-failure` runs the C ABI and native
metric checks. The win-x64 AOT validation deploys this build's core/software backend
libraries into its isolated publish directory before running the executable; an older
payload is not evidence for the new native interface. Android arm64 was cross-compiled
with the installed NDK for API 24. The D3D12/Vulkan shared/static project-item validator
also passed. Device execution, full visual comparisons and release-package verification
remain required for the overall completion gate.

The font-face validation rebuilds native libraries into the isolated RID directory
and copies those libraries into isolated test/publish outputs before execution.
The test asset is the public-domain/CC0 WPT Ahem font (provenance and SHA-256 are in
`tests/Jalium.UI.Tests/TestAssets/Fonts/README.md`). Native software pixels are compared
against that original font installed through the OS test API, isolating private-name
rewriting from unrelated rasterization differences. No system font installation is
persisted. Generated `obj/bin` files under source trees are excluded from the merged
managed project; the native project-item validator likewise ignores generated build
directories. Existing build outputs are preserved.

WOFF 2 adds a managed decoder over .NET Brotli; it introduces no browser/font-engine
dependency. `css-woff2-native.trx` covers 58 focused cases, and the independent
fontTools comparison records 1,668 matching glyph records in
`artifacts/woff2-reference-comparison.json`. Fixture generation tooling stays under
the test assets. Android managed compilation passed with the existing warnings;
native interfaces are unchanged by this decoder addition.

### Named font collection faces

The [CSS Fonts 4 `src` loading rule](https://www.w3.org/TR/2024/WD-css-fonts-4-20240201/#font-face-src-loading)
selects a collection face by its exact PostScript name. URL fragments are validated
against [RFC 8081 section 4.2](https://www.rfc-editor.org/rfc/rfc8081#section-4.2)
before fetching; malformed names skip that source. A valid name is decoded and matched
against name ID 6 in TTC or WOFF 2 face directories. A missing face fails that source
and proceeds to the next `src` item. With no fragment, the first face remains the
default. The fragment is removed from the resolver URI.

The two-face Ahem test fixture has different PostScript names and `X` advances. Its
TTC and WOFF 2 paths reproduce the intended face's metrics, checksum and native
TextBlock width. The focused `CssWoff2Tests` run passed 21 cases; the font/resource/
code-generation regression filter passed 109 cases on Windows, with no skips. Linux
passed 19 focused cases with one Windows-only pixel test skipped, using the existing
Linux native libraries because this increment changes managed code only. TRX files
are under `artifacts/css-font-fragment/test-results/`. These fixtures test selection
and fallback; they are not a port of the full WPT collection page. NativeAOT,
Android and device pixel execution of this increment remain unverified.

### Relative font weights

The [CSS Fonts 4 relative-weight table](https://drafts.csswg.org/css-fonts/#relative-weights)
now drives `font-weight:bolder/lighter` and the `font` shorthand. The computed
weight uses the parent's effective CSS or native weight and updates when it
changes, including nested containers and document Runs. Generated CSS uses the
same deferred value. Duplicate weight, style and stretch components in the
shorthand are rejected. The managed `FontWeight` type now preserves the
[CSS Fonts 4](https://www.w3.org/TR/css-fonts-4/#font-weight-prop) and
[OpenType](https://learn.microsoft.com/en-us/typography/opentype/spec/os2#usweightclass)
integer range of 1–1000, including a computed CSS weight of 1000 through
inheritance and the shorthand. Legacy Windows
[DirectWrite weight parameters](https://learn.microsoft.com/en-us/windows/win32/api/dwrite/ne-dwrite-dwrite_font_weight)
only accept 1–999; those text format and outline paths clamp 1000 to 999 at
their native boundary. Fractional numeric weights still use the existing integer
conversion. Full variable-font precision remains open.
The focused CSS weight, generated stylesheet and converter regression passed
98/98 without skips (`artifacts/test-results/css-font-weight-1000-regression.trx`).
The Release CSS/Flex class filter passed 3959/3959 without skips
(`artifacts/test-results/css-font-weight-1000-full-css.trx`).
The Windows D3D12 and Vulkan native targets also built with the boundary clamp.
The Release CSS suite passed 3404/3404 without skips
(`artifacts/test-results/css-relative-font-weight-full-css.trx`), and the font
plus native converter regression passed 275/275 without skips
(`artifacts/test-results/css-relative-font-weight-font-regression.trx`).

The first subgrid WPT box-layout/paint ports are listed in [css-wpt-cases.md](css-wpt-cases.md).
Their Canvas references are independent of the CSS formatter. Their decorative glyphs
are replaced by fixed line boxes, so they do not claim text-raster conformance.

### Text decoration increment

The [CSS Text Decoration 4 line-decoration properties](https://www.w3.org/TR/css-text-decor-4/#line-decoration)
now have independent cascade slots for `text-decoration-line`, `-style`, `-color`,
and `-thickness`. The shorthand resets all four. Direct `TextBlock`, `TextBox`,
`AccessText`, RichTextBox Run/Span/Paragraph, and TextBlock Run/Span text render
underline, overline, and line-through using solid, double, dashed, dotted, or wavy
strokes. Exclusive `spelling-error` and `grammar-error` line values paint red and
green wavy markers respectively; the user-agent marker appearance overrides authored
decoration color and style. In RichTextBox and TextBlock, an inline ancestor line
continues across
child Runs and wraps onto each visual line; a child `text-decoration: none` leaves
its ancestor's line intact. TextBlock maps inline decoration ranges onto its
text layout. TextBlock Run/Span foreground colors paint in their own ranges,
including stylesheet changes and native local value precedence. Run/Span font
family, size, weight, style, and stretch now reach per-Run text measurement and
drawing. Mixed sizes affect wrapping, line height, baseline, selection, and hit
testing, and inline decoration placement uses the source element's font size.
Each Run is shaped separately; shaping across Run boundaries and exact
color/decoration endpoints in shaped clusters remain open.
Color accepts currentColor and explicit `inherit`; thickness accepts lengths and
percentages of the computed font size, with a one-device-pixel floor. The public
`TextDecorations` collection still reflects CSS line declarations, while a native
local value takes precedence. RichTextBox re-renders when a document node's CSS
or native decoration changes. A user-owned mutable foreground brush remains
mutable when CSS creates the compatibility collection.

The inherited `text-underline-offset` longhand accepts `auto`, signed lengths,
percentages of the decorating element's font size, and length/percentage math.
Explicit offsets use the alphabetic baseline as their default zero position;
`text-underline-position: under` moves that zero position to the text-under edge
of the content decorated by the source element. With the default position,
`text-underline-offset: auto` keeps the existing baseline placement. The value
stays independent of the `text-decoration` shorthand and moves only
CSS-originated underlines. Direct
`TextBlock`, `TextBox`, `AccessText`, RichTextBox Run/Span/Paragraph, and TextBlock
Run/Span decoration paths share this placement. The inherited
`text-underline-position` accepts `auto`, `from-font`, `under`, `left`, and `right`
in the specified combinations. In horizontal writing, `left` and `right` behave
as `auto`; `under` still takes effect when combined with either. `from-font`
reads the selected font's OpenType `post` underline position when available and
falls back to `auto` otherwise. The selected-font `post` thickness also drives
`text-decoration-thickness: from-font`, subject to the one-device-pixel floor;
missing or invalid thickness falls back to `auto`. Other underline skipping and
vertical writing modes remain open.

The inherited `text-decoration-skip-inset` longhand accepts `none` and `auto`.
For `auto`, CSS-originated decoration strokes start and end slightly inside
each painted line fragment; `none` keeps the full endpoints. The chosen inset
is half the stroke thickness, capped for very short spans. The property
applies to direct text controls and inline Run/Span/Paragraph decorations,
and is independent of the `text-decoration` shorthand. Native local
`TextDecorations` keep their own endpoints. Ink, space, box, and other
decoration-skipping rules remain open.

The preceding foreground increment passed 46 focused cases and 517 adjacent
regression cases. The inline typography increment adds direct and recorded Windows
software, D3D12, and Vulkan pixel checks for Run font size, plus layout, wrapping,
inheritance, precedence, selection, and hit testing checks. Its broader Release
CSS/text regression passed 435 cases in
`tests/Jalium.UI.Tests/TestResults/css-inline-font-regression.trx`.
`blink` is accepted but remains static, which the specification permits.
Cross-Run glyph shaping, other document viewers, decoration propagation through
visual ancestor boxes, skip rules, and a broader spec corpus remain open.

### Text overflow increment

The CSS `text-overflow: ellipsis` mapping now draws a visible ellipsis when an
unwrapped TextBlock line exceeds its content width. The visible prefix ends at a
grapheme boundary and leaves the source `Text` unchanged. Mixed-font and
color-only Runs supply the ellipsis's font and foreground; the visible prefix
also bounds selection and hit testing. `text-overflow: clip` keeps the source
glyphs and clips them at the box edge. Native `TextTrimming.WordEllipsis` backs
up to a complete word. Direct and recorded Windows software, D3D12, and Vulkan
renders produce distinct ellipsis and clip pixels.

The wider Release CSS/text/Flex/flow regression passed 538 cases in
`tests/Jalium.UI.Tests/TestResults/css-text-overflow-regression.trx`. A retained
drawing regression exposed and fixed a stale-cache path: direct rendering now
leaves an outdated cached drawing dirty until a cacheable render records its
replacement. A separate retained-render test run passed 55 cases and skipped
two device-removal injection cases.

### Text justification increment

`text-align: justify` now affects TextBlock's painted text. Wrapped lines divide
their remaining width across interior word gaps and permitted CJK grapheme
boundaries. Paragraph-final and explicit final
lines retain natural spacing. Run font/color fragments, selection, hit testing,
and inline and block decoration endpoints use the same adjusted positions.
Direct and recorded Windows software, D3D12, and Vulkan pixels show the
second word moving when justification is enabled. The expanded Release
CSS/text/Flex/flow regression passed 547 cases in
`tests/Jalium.UI.Tests/TestResults/css-text-justify-regression.trx`.
Complex-script expansion such as Arabic kashida, RTL placement, and full
CSS Text justification rules remain open.

### Logical text alignment increment

`text-align: start/end` now maps to the physical side of the element's computed
`direction`, independent of declaration order. The symbolic value inherits to
descendants, so a child with the opposite direction resolves its own side.
TextBlock, TextBox, AccessText and TextEffectPresenter use that value while
drawing, including after an ancestor direction change; explicit native local
alignment still wins. The change concerns line placement. Bidi glyph ordering
and RTL shaping remain open. The Release CSS regression passed 2,647 cases
without skips in
`tests/Jalium.UI.Tests/TestResults/css-text-logical-alignment-full-css-final.trx`.

### Text alignment family increment

The [CSS Text 3 alignment model](https://www.w3.org/TR/css-text-3/#text-align-property)
now drives the `text-align` shorthand, `text-align-all`, and inherited
`text-align-last`. Normal shorthand values reset last-line alignment to `auto`;
`justify-all` justifies the final line, and `match-parent` fixes a parent's
logical start/end against the parent's direction before inheriting. TextBlock
applies last-line values to wrapped paragraph endings and lines before explicit
breaks, including selection and hit testing when a final line is justified.
Ordinary flow positions its final line of atomic inline children; TextBox
positions unwrapped logical lines. Native local alignment remains authoritative.
Wrapped TextBox visual rows, justification of
atomic inline children, and full bidi layout remain open. The Release CSS
regression passed 2,659 cases without skips in
`tests/Jalium.UI.Tests/TestResults/css-text-align-family-full-css-final.trx`.

### Text group alignment increment

The [CSS Text 4 `text-group-align` property](https://drafts.csswg.org/css-text-4/#text-group-align)
now accepts `none`, `start`, `end`, `left`, `right`, and `center` as a
non-inherited block-container value. TextBlock finds the shortest remaining
space among its own lines, adds that amount to the requested side or splits it
for `center`, then applies each line's `text-align` within the shared area.
Justification, text indentation, selection and hit testing use that area too.
CSS ordinary flow applies the same rule to atomic inline line boxes across
separate runs and descendant block panels in the same block formatting context,
including lines with different float exclusion bands. Descendants that establish
independent formatting contexts are skipped. Its float path also applies
`text-align-last` only to each run's actual final line. Vertical writing and
other text controls remain open.

### Line padding increment

The [CSS Text 4 `line-padding` property](https://drafts.csswg.org/css-text-4/#line-padding-property)
now accepts inherited lengths, including relative units and supported math
expressions. TextBlock reserves the resolved amount at both edges of each line
before wrapping and alignment. The innermost Run supplies each edge value,
including a value inherited from Span. Measurement, tab stops, justification,
ellipsis, selection and hit testing use the same glyph origin. Ordinary CSS
flow applies the edge space to atomic inline line boxes, including lines shaped
by floats. Grammar, inheritance, CSS-wide keywords, Run boundary wrapping,
transformed paint and both flow paths are covered by `CssLinePaddingTests`.
Run/Span background colors now paint each TextBlock line fragment below
selection and text; an outer Span paints before nested backgrounds, and the
background at either line edge extends through the extra space. Ellipsized
and transformed fragments retain this coverage. Inline background images,
borders, full bidi inline painting, vertical writing and other text controls
remain open.

### Text justification method increment

The [CSS Text 3 `text-justify` property](https://www.w3.org/TR/css-text-3/#text-justify-property)
now selects the inherited justification method for TextBlock. `none` suppresses
expansion, `inter-word` uses interior word separators, and `inter-character`
(including its legacy `distribute` alias) distributes space between supported
grapheme clusters without splitting a cluster or a known cursive joining pair.
`auto` combines word and permitted CJK opportunities on the same line. If a
justified line has no opportunity, its origin follows `text-align-last`; a
`justify` last-line value falls back to center. Rendered fragments, selection,
hit testing and decoration endpoints use the same measured gap positions.
Grammar, inheritance, explicit initial reset and dynamic style changes are
covered. The Release CSS regression passed 2,672 cases without skips in
`tests/Jalium.UI.Tests/TestResults/css-text-justify-methods-full-css.trx`.
Language-specific and complex-script justification, exact typography across
Run boundaries, and text-justify rendering in TextBox/RichTextBox remain open.

### Text indentation increment

The [CSS Text 3 `text-indent` property](https://www.w3.org/TR/css-text-3/#text-indent-property)
now parses inherited length/percentage values, including supported math
expressions and the optional `hanging` and `each-line` keywords in either order.
TextBlock applies the indent at the logical start of selected lines and reduces
their wrapping width. `each-line` also selects the first line after a forced
break; `hanging` selects the other lines. Alignment, justification, selection,
hit testing and decorations use the resulting line origin. Ordinary flow
applies the same selection to atomic inline lines, including lines beside
floats; percentages resolve against the block container's own content width
even when a float narrows the available band. Grammar, inheritance, forced and soft breaks,
LTR/RTL placement, float exclusion, and dynamic style changes are covered.
The Release CSS regression passed 2,692 cases without skips in
`tests/Jalium.UI.Tests/TestResults/css-text-indent-full-css.trx`.
Indentation of TextBox/RichTextBox and splittable inline fragments remains open.

### Overflow wrapping increment

The [CSS Text 3 `overflow-wrap` property](https://www.w3.org/TR/css-text-3/#overflow-wrap-property)
now selects TextBlock's inherited fallback after ordinary whitespace, hyphen and
CJK break opportunities. `normal` lets an unbreakable sequence overflow;
`anywhere` and `break-word` break it at extended grapheme boundaries when no
ordinary break fits. At zero-width intrinsic measurement, `anywhere` contributes
the widest grapheme while `break-word` retains the unbroken word width. The
legacy `word-wrap` spelling resolves to the same cascade slot, so declaration
order and CSS-wide resets behave as one property. An explicit overflow-wrap
value enables CSS's initial wrapping behavior when no native local wrapping or
CSS `white-space` declaration overrides it. Parent style changes invalidate
descendant line caches and layouts. Grammar, alias ordering, inherited updates,
intrinsic sizing, emoji clusters, whitespace priority, native precedence, and
`white-space: nowrap` interaction are covered. The Release CSS regression passed
2,703 cases without skips in
`tests/Jalium.UI.Tests/TestResults/css-overflow-wrap-full-css.trx`.
Complex-script shaping across emergency breaks, full Unicode line-breaking
interactions, and other text controls remain open.

### Word breaking increment

The [CSS Text 3 `word-break` property](https://www.w3.org/TR/css-text-3/#word-break-property)
now parses and inherits `normal`, `keep-all`, `break-all`, and the deprecated
`break-word` keyword. TextBlock's `break-all` adds ordinary break opportunities
between letter and digit grapheme units, so it can use more of a line before
wrapping. `keep-all` suppresses implicit CJK/letter boundaries while keeping
spaces and explicit separators; with `overflow-wrap: normal`, it may relax
those restrictions at used width if no ordinary break fits. Intrinsic sizing
keeps the unbroken word width for `keep-all`. `word-break: break-word` uses the
same emergency breaks and min-content contribution as
`overflow-wrap: anywhere`, regardless of the authored overflow-wrap value.
Parent style changes rebuild descendant line layouts, and native local
`TextWrapping.NoWrap` still prevents wrapping. Grammar, CJK and Latin breaks,
grapheme safety, intrinsic sizing, inheritance, initial reset, and native
precedence are covered. The Release CSS regression passed 2,715 cases without
skips in `tests/Jalium.UI.Tests/TestResults/css-word-break-full-css.trx`.
Full Unicode line-break classes and complex-script shaping across fragment
boundaries remain open; the following increment covers selected `line-break`
interactions.

### Line breaking strictness increment

The [CSS Text 3 `line-break` property](https://www.w3.org/TR/css-text-3/#line-break-property)
now parses and inherits `auto`, `loose`, `normal`, `strict`, and `anywhere`.
TextBlock applies selected required distinctions before Japanese small kana,
iteration marks, hyphen-like characters, ellipses, and centered CJK punctuation.
Chinese/Japanese exceptions use inherited language and explicit script subtags,
including excluding Latin transliterations; changing the language rebuilds the
line cache. `anywhere` uses equal-priority opportunities at extended grapheme
boundaries, including around word joiners and despite `word-break: keep-all`.
It contributes the widest grapheme to min-content sizing, while CSS
`white-space: nowrap` and native local `TextWrapping.NoWrap` still suppress
wrapping. Parent style changes and CSS-wide `initial` reset update descendant
layout. Fifteen focused cases pass, and the Release CSS regression passed
2,730 cases without skips in
`tests/Jalium.UI.Tests/TestResults/css-line-break-full-css.trx`.
The complete Unicode line-break algorithm, complex-script shaping across split
lines, and other text controls remain open. The following increment adds
PO/PR punctuation tailoring.

### Unicode line-break class increment

TextBlock now uses pinned [Unicode 18 line-break classes](https://www.unicode.org/Public/18.0.0/ucd/LineBreak.txt)
and [East Asian width data](https://www.unicode.org/Public/18.0.0/ucd/EastAsianWidth.txt)
for the `line-break` distinctions required by CSS Text 3. Its `loose` mode
recognizes the full assigned ID class before U+2010/U+2013 hyphens, including
kana and emoji; `word-break: break-all` also treats a preceding letter or
number as ID for this rule. The CJ and IN classes replace the earlier hand
lists for small kana and inseparable punctuation. In Chinese or Japanese
writing systems, `loose` additionally allows breaks before wide PO suffixes
and after wide PR prefixes; `normal` and `strict` disallow them. The generated
table can be refreshed with `tools/generate-css-linebreak-unicode.py`.
Twenty-two focused cases pass, and the Release CSS regression passed 2,737
cases without skips in
`tests/Jalium.UI.Tests/TestResults/css-line-break-unicode-full-css.trx`.
The rest of the Unicode line-break pair algorithm, complex-script shaping,
and other text controls remain open.

### Word space transform increment

The [CSS Text 4 `word-space-transform` property](https://drafts.csswg.org/css-text-4/#word-space-transform)
accepts `none`, `space`, and `ideographic-space`, with optional `auto-phrase`
in either keyword order. TextBlock transforms source U+200B separators after
whitespace collapsing and before case/width transformation. The resulting
ordinary or ideographic spaces affect measured width, wrapping, and
`word-spacing` without changing selection or copied source text. Run/Span
overrides and inherited parent style changes update the displayed separators;
AccessText inherits the transformation into its internal TextBlock.
Adjacent forced line breaks suppress expansion. `auto-phrase` adds no virtual
separators because there is no language-specific phrase detector; XAML has no
equivalent for HTML `<wbr>`. The tests include the ordering of `space` and
`text-transform: full-width` from the CSS Text 4 WPT corpus. Other text controls,
phrase detection, and full WPT/backend coverage remain open.

### Word spacing increment

The [CSS Text 4 `word-spacing` property](https://drafts.csswg.org/css-text-4/#word-spacing-property)
now accepts inherited `normal` and length-percentage values, including negative
and calculated mixtures. Percentages resolve against each TextBlock or Run's
used font size; absolute length components retain their computed value through
inheritance. TextBlock adds the resolved spacing to the advance of each
recognized word separator after text processing, with half of the adjustment
on either side of the separator. It handles ordinary and nonbreaking spaces,
Ethiopic, Aegean, Ugaritic, and Phoenician word separators while leaving
fixed-width and zero-width spaces unchanged. Negative values stop at zero
separator advance. Wrapping, measured width, Run and Span inheritance, drawn
positions, selection, hit testing, justification, and ellipsis use the adjusted
advances, including numeric tab stops. Native local `TextWrapping` still takes
precedence. The earlier length-only baseline passed 25 focused cases and a
2,762-case Release CSS regression without skips in
`tests/Jalium.UI.Tests/TestResults/css-word-spacing-full-css.trx`.
Language-specific separator recognition beyond the listed characters,
complex-script shaping across word boundaries, and word spacing in other text
controls remain open.

### Letter spacing increment

The [CSS Text 3 `letter-spacing` property](https://www.w3.org/TR/css-text-3/#letter-spacing-property)
now accepts inherited `normal` and length values, including negative and
calculated lengths. TextBlock distributes the computed spacing between adjacent
extended grapheme clusters without adding space at line edges. Adjacent Run or
Span values contribute half each, and invisible Unicode format characters do
not create extra gaps. Negative tracking can overlap glyphs while keeping used
advances nonnegative. Letter and word spacing add together without changing
the source text. Wrapping, measured width, drawing, selection, hit testing,
justification, trimmed prefix width, and ellipsis placement use the same
spacing. Twenty-three focused cases pass, and the Release CSS regression passed
2,785 cases without skips in
`tests/Jalium.UI.Tests/TestResults/css-letter-spacing-full-css.trx`.
Transformed source characters that expand to multiple paint graphemes now
receive tracking between those paint graphemes. The same advances feed Run
drawing, width, wrapping, selection, hit testing, word spacing and ellipsis.
Native shaping and kerning across introduced tracking gaps, spacing after bidi
reordering, complex-script tailoring, and letter spacing in other text controls
remain open.

### Hanging punctuation increment

[CSS Text 4 `hanging-punctuation`](https://drafts.csswg.org/css-text-4/#hanging-punctuation-property)
now parses and inherits `none`, `first`, `last`, `force-end`, `allow-end`, and
valid combinations. In horizontal TextBlock layout, `first` hangs an opening
bracket, quote, or ideographic space at the first formatted line; `last` hangs
a closing bracket or quote on the final formatted line. `force-end` hangs a
listed stop or comma at any line end, while `allow-end` hangs only the part
that would otherwise overflow. Run/Span values are used at the relevant edge.
Line fitting, intrinsic widths, alignment and justification exclude the used
hanging advance. The glyph still paints and participates in selection, hit
testing, decoration and text copy; the render clip includes its overhang.
The first/last mode also recognizes a Run text boundary at a line edge,
including an inherited Span value, even when the TextBlock has later lines.
For conditional `allow-end`, min-content excludes the hangable stop/comma,
while max-content retains it because the glyph fits without hanging.
Bidi visual-edge placement, full inline box geometry and border/padding inhibition,
vertical text, other text controls and full pixel/WPT coverage remain open.
Thirty focused cases pass in Release; the Debug CSS regression passes
3,890 cases without skips.

### Text autospace increment

The [CSS Text 4 `text-autospace` property](https://drafts.csswg.org/css-text-4/#text-autospace-property)
now supports inherited `normal`, `auto`, `no-autospace`, `ideograph-alpha`,
`ideograph-numeric`, their combinations, and optional `insert` or `replace` in
TextBlock. The initial `normal` value inserts one eighth of the containing
font's ideographic advance between directly adjacent CJK ideographs and
non-wide letters or non-fullwidth decimal digits. An existing space or
punctuation suppresses insertion. Run/Span values apply at boundaries
controlled by their nearest common inline ancestor. This advance adds to
`letter-spacing` and participates in measurement, emergency wrapping,
drawing, selection and hit testing, including case-expanded paint text.
`replace` exchanges a qualifying painted U+0020 for the same advance while
preserving the source space for selection and copy. It combines with
`word-spacing`, works after whitespace collapse and across Run/Span boundaries,
and contributes no advance at wrapped line edges. The source and copied text
are unchanged. Unicode 18 `Scripts.txt`, `ScriptExtensions.txt`, and
`EastAsianWidth.txt` now determine Han extended-script and wide/fullwidth
classes instead of hand-maintained ranges.

For French content, `punctuation` follows the [Unicode French typography
table](https://web.archive.org/web/20201112013601/http://unicode.org/udhr/n/notes_fra.html): it inserts U+202F before
`;`, `!` and `?`, and U+00A0 before `:` and `»` and after `«`. The source and
copied text keep their original characters. Existing Unicode space separators
suppress `insert`; `replace` substitutes a qualifying U+0020 while retaining
its source offset and additive `word-spacing`. The innermost common Run/Span
ancestor supplies the spacing mode, language and font. The generated advance
participates in measurement, painting and ordinary line-break suppression.
Placement after bidi reordering, inline margin/border/padding suppression,
vertical writing, regional French typography differences and other text
controls remain open. Seventy-three focused autospace cases pass in Release;
the Debug CSS regression passes 3,860 cases without skips.

### White-space processing increment

The [CSS Text 3 `white-space` property](https://www.w3.org/TR/css-text-3/#white-space-property)
now stores its inherited mode separately from the native wrapping mirror.
TextBlock collapses spaces, tabs and source segment breaks for `normal` and
`nowrap`, removes collapsible space at line edges, and keeps source offsets for
selection and hit testing. `pre-line` preserves source line breaks while
collapsing surrounding space; `pre` and `pre-wrap` preserve source spacing;
`break-spaces` preserves each space and permits a break after it. Explicit
inline `LineBreak` remains forced in every mode. Paint and width measurement
use compact text, so collapsed positions contribute no glyph advance. Removing
an authored `normal` rule restores native unstyled text even though its
computed value equals the default. Twenty-seven focused cases pass. The
latest Release CSS regression passed 2,812 cases without skips in
`tests/Jalium.UI.Tests/TestResults/css-text-transform-full-css.trx`; 34 native
TextBlock parity, render-cache and selection cases also pass.

Per-Run white-space overrides, full tab-stop placement, CJK segment-break
tailoring, whitespace processing in other text controls, and the complete
Unicode line-breaking algorithm remain open.

### Text transformation increment

The [CSS Text 3 `text-transform` property](https://www.w3.org/TR/css-text-3/#text-transform-property)
accepts inherited `none`, `capitalize`, `uppercase`, `lowercase`, `full-width`,
and `full-size-kana`, including valid combinations in any keyword order, on
TextBlock and its Run/Span content. Transformation follows whitespace
processing and the specified case → width → kana order. Case conversion uses
[Unicode 18 full case mappings](https://www.unicode.org/Public/18.0.0/ucd/SpecialCasing.txt)
for unconditional multi-character results. [Unicode 18 casing context data](https://www.unicode.org/Public/18.0.0/ucd/DerivedCoreProperties.txt)
and [SpecialCasing rules](https://www.unicode.org/Public/18.0.0/ucd/SpecialCasing.txt)
handle Lithuanian dot insertion/removal, Turkish/Azeri Latin I across intervening
marks, and Greek final sigma. Full-width uses [Unicode 18 wide/narrow decompositions](https://www.unicode.org/Public/18.0.0/ucd/UnicodeData.txt);
full-size-kana uses the [normative CSS Text 3 small-kana table](https://www.w3.org/TR/css-text-3/#small-kana-mappings).
Only preserved U+0020 spaces become U+3000. `capitalize` finds word starts
across inline boundaries. A source-boundary map keeps `Text`, selection and
copied text unchanged when paint text changes length. Measured width, wrapping,
Run drawing, hit testing and ellipsis use transformed paint text. Paint graphemes
introduced by an expansion receive their own computed letter spacing while
the source character remains one selection unit. Painted grapheme boundaries
also drive `line-break:anywhere`, `overflow-wrap:anywhere`/`break-word` and
`word-break:break-all` soft wrapping inside expansions. A split source character
can appear on multiple visual rows; drawing, selection rectangles and hit testing
map each painted part back to its source range. Emergency wrapping still prefers
a fitting ordinary space break, and painted combining clusters remain intact.
`text-justify` now distributes word or character space at painted boundaries on
split and unwrapped expansions. Drawing, selection, hit testing and decoration
endpoints use the same adjusted positions, including Run fonts and tracking.
AccessText inherits the same painted transformation from its TextBlock child.
Its mnemonic underline follows the transformed source range, including an
expansion split across visual rows, while `Text` and `AccessKey` stay unchanged.
Parent and Run language or style changes rebuild the layout. Sixty-three focused
transformation cases and six focused cross-feature justification cases pass.
The latest Release regression passes 2,933 cases across CSS, AccessText, native
TextBlock and FlowDocument/RichTextBox without skips in
`tests/Jalium.UI.Tests/TestResults/css-access-transform-full-css.trx`.

CLDR-specific casing, language-sensitive shaping and justification,
and transformation in TextBox, RichTextBox and other text controls remain open.

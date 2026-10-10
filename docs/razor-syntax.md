# Razor Syntax (JALXAML)

Razor-style syntax in JALXAML is additive sugar over ordinary markup. Everything here coexists
with `{Binding ...}`, which remains fully supported.

## Value expressions

| Syntax | Meaning |
|---|---|
| `@Path` | Bind to `Path` |
| `@(expr)` | Bind to a C# expression |
| `@{ ... }` | Inline C# code block |
| `$.Path` | The target element's own property (`RelativeSource=Self`) |
| `$` | The target element itself |
| `#.Path` | A path on the DataContext only, with no code-behind fallback |
| `#.` | The DataContext object itself |
| `Hello @User.Name` | Mixed text template |
| `@*...*@` | Comment, dropped |
| `@@`, `\@` | A literal `@` |

`$.` and `#.` only take effect inside a Razor region, so write `@#.Name`, not a bare `#.Name` —
without the `@` the value is literal text.

### Resolution order

1. `DataContext`
2. Code-behind fallback (same member name)

`DataContext` wins when both provide the member. `#.` deliberately skips step 2, which is what
makes it the right sigil inside a template: it always means "this item".

### Update semantics

- Observable source (`INotifyPropertyChanged`, or a dependency property): updates live.
- Plain CLR value: evaluated once at load. Nothing polls.

### Type rules

A non-string target property accepts only a pure path (`@Count`), a pure expression
(`@(Count > 0 ? 1 : 0)`), or a code block with a single output segment
(`@{ var width = Count * 25; }@width`). A mixed template such as `100@x` on a non-string target
raises a parse error with its location.

## Conditionals

`@if` is a live binding, not a one-shot decision: the conditional child stays in the tree and its
`Visibility` follows the condition.

```xml
@if(IsOnline)   { <Border><TextBlock Text="Online" /></Border> }
@else if(IsBusy){ <Border><TextBlock Text="Busy" /></Border> }
@else           { <Border><TextBlock Text="Offline" /></Border> }
```

The inline equivalent for a single value is usually simpler:

```xml
<TextBlock Text='@(IsOnline ? "Online" : "Offline")' />
```

## Lists

### `@virtualize` — data-bound and virtualized

Use this for anything that could grow. It lowers to a bound, virtualized list: elements are created
for the visible window rather than for the data, containers are recycled as you scroll, and the list
follows `INotifyCollectionChanged`.

```xml
<ScrollViewer CanContentScroll="True">
  @virtualize(var row in Rows)
  {
    <Border Padding="8">
      <TextBlock Text="@row.Name" />
    </Border>
  }
</ScrollViewer>
```

A numeric form is available too:

```xml
@virtualize(var i = 0; i < Count; i++)
{
  <TextBlock Text="@i." />
}
```

Rules worth knowing:

- **The body is one element.** It becomes the item template, and a template has a single root. Wrap
  multiple elements in a container.
- **The loop variable is the item.** `@row.Name` binds to the item's `Name`; `@row` on its own is
  the item. Both are rewritten for you.
- **It needs a viewport.** Virtualization is only possible when something bounds the scrolling axis.
  The best shape is the one above — the block directly inside a `ScrollViewer` with
  `CanContentScroll="True"`, so the host receives the viewer's viewport and scroll commands.
  The viewer's default physical-scrolling mode does not delegate those commands to its content.
  Otherwise give the host an
  explicit `Height` or `MaxHeight`. Placed where neither holds (inside a `StackPanel`, an
  `Auto`-sized grid row, or another item template) it falls back to non-virtualized layout and says
  so in the trace; it will render correctly but without the benefit. This fallback is temporary:
  once a finite viewport is available, it resumes the application's virtualization setting,
  including its binding or an explicit `IsVirtualizing="False"` choice.
- **Nesting works, one scope at a time.** An inner `@virtualize` resolves its source against the
  outer item, so `@virtualize(var it in g.Items)` inside `@virtualize(var g in Groups)` is fine. The
  inner body cannot reach back for `g`, because a template has a single DataContext — project the
  value into the inner collection, or bind explicitly with a `RelativeSource`. An inner list should
  also carry an explicit height.
- Set `ScrollHost`, `Layout`, `Orientation`, or `UnboundedBehavior` on the generated host to
  override any of the defaults.

### `@foreach` / `@for` — static expansion

These expand while the document is being read: the body is emitted once per element and the result
parsed as ordinary markup. That means one real element per item, no recycling, and a cap of 10,000
iterations.

```xml
@foreach(var name in Names)
{
  <TextBlock Text="@name" />
}
```

They read the loaded component and its `DataContext`, but **once, at load**. A collection that
changes afterwards does not re-expand. `XamlReader.Parse` has no component to read from, so a loop
there sees only self-contained values such as `new[]{ "a", "b" }`.

Reach for them for a short, fixed list. For anything data-driven or open-ended, use `@virtualize`.

## Other statement directives

`@while`, `@do { } while(...);`, `@switch`, `@try/catch/finally`, `@using`, `@lock`, and
`@await foreach` all expand the same way `@foreach` does, with the same one-shot semantics.

## Sections

```xml
@section Footer { <TextBlock Text="© Contoso" /> }
@RenderSection("Footer")
```

A section is a definition rather than in-place content. `@RenderSection` renders it, and fills in
late if the section is registered after the host loads.

## Inline C#

```xml
<TextBlock Text='@{ var label = Count > 0 ? "Positive" : "Zero"; }@label' />
<TextBlock Text='@{ string Describe(int v) => v > 0 ? "Positive" : "Zero"; }@(Describe(Count))' />
```

`Write(...)` and `WriteLiteral(...)` are available inside `@{ ... }` for direct output:

```xml
<TextBlock>
  @{ for (var i = 0; i != Count; i++) { Write(i); } }
</TextBlock>
```

## Compile-time lowering

The source generator turns most of this into straight-line C# at build time, so nothing is
re-parsed at run time:

- `@if` / `@else if` / `@else`
- `@section` / `@RenderSection`
- `@virtualize`
- Value expressions (`@Path`, `@(expr)`, `$.`, `#.`)
- `{Binding ...}`

`Setter.Value` is intentionally left alone — markup there is resolved when the setter is applied.

A document containing a statement directive (`@foreach`, `@while`, and the rest) is handed to the
runtime reader instead, which costs the compile-time lowering above. `@virtualize` is lowered, so a
list written with it keeps it.

## Errors

- A build-time expression that fails to compile is a build error. A block that only fails because it
  names something that does not exist at build time — a view-model property, for instance — is left
  for the runtime instead.
- On the runtime path (`XamlReader.Parse`), a failed expression throws at parse time.
- A `@virtualize` the generator cannot lower is an error rather than a silent fallback, since
  falling back would quietly cost the virtualization it was written for.

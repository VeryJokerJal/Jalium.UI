# Razor Syntax (JALXAML)

This document describes Razor-style syntax support in JALXAML.

## Supported Syntax

- Path binding: `@Path`
- Expression binding: `@(expr)`
- Text-node mixed template: `Hello @User.Name, Count=@Count`
- Conditional block directive: `@if(expr){<Border />}`
- Escapes:
  - `@@` => literal `@`
  - `\@` => literal `@`

Razor syntax is additive sugar. Existing markup extensions like `{Binding ...}` remain fully supported.

## Resolution Order

Name/path resolution always uses:

1. `DataContext`
2. code-behind fallback (same path/member name)

If both provide the same member, `DataContext` wins.

## Update Semantics

- Observable dependency chain (`INotifyPropertyChanged` / dependency property notifications): updates in real time.
- Non-observable CLR values: evaluated once at load/initialization time.
- No polling is introduced.

## Type Rules

- Non-string target properties allow only:
  - pure path (`@Count`)
  - pure expression (`@(Count > 0 ? 1 : 0)`)
- Mixed templates (for example `100@x`) on non-string targets throw parse errors with location info.

## Error Behavior

- Build-time expression compile failures: build error.
- Runtime dynamic parse path (`XamlReader.Parse(string)`): explicit runtime exception when expression compile/evaluation fails.

## `@if` Examples

```xml
@if(IsOnline){<Border><TextBlock Text="Online" /></Border>}
@if(!IsOnline){<Border><TextBlock Text="Offline" /></Border>}
```

Inline expression equivalent:

```xml
<TextBlock Text='@(IsOnline ? "Online" : "Offline")' />
```

## Playground

A temporary verification app exists at `.tmp/RazorSyntaxPlayground` with these tabs:

1. Notify real-time
2. Plain one-time
3. Expression
4. Mixed text
5. DataContext vs code-behind fallback
6. If-like block/conditional
7. Multi-branch expression
8. Escapes and dynamic segments

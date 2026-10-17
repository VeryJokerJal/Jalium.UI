; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
JALXAML002 | Jalium.UI.Xaml | Error | JalxamlSourceGenerator could not lower a jalxaml document to compile-time C#.
JALXAML003 | Jalium.UI.Xaml | Error | JalxamlSourceGenerator was not supplied MSBuildProjectDirectory (reference Jalium.UI.Build.targets).
JALXAML004 | Jalium.UI.Xaml | Error | A @virtualize directive could not be lowered to compile-time C#.
JALXAML005 | Jalium.UI.Xaml | Error | A @virtualize body cannot be used as an item template.
JALXAML006 | Jalium.UI.Xaml | Error | A @virtualize body references an enclosing loop's item variable.
JALXAML007 | Jalium.UI.Xaml | Error | A template declares the same name more than once in its visual tree.

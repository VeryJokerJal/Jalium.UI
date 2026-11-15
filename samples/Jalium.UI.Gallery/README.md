# Control gallery

The catalog keeps the existing seventeen category groups and background-priority startup stages. `GalleryWindow.Build()` creates the platform window and schedules those stages; it does not assemble their default visual trees.

Reusable controls and their default layouts live in:

- `GallerySampleControls.cs`: scrollable catalog, category, card, heading, placeholder and startup status.
- `GalleryWindow.<Category>.cs`: category controls, bindable demo data and optional template-part behavior.
- `Themes/SampleGallery.jalxaml`: all category layouts, card templates, item templates, palette and visual states.

Override `Template`, `ContentTemplate`, `HeaderTemplate` or `ItemsPanel` with the normal control APIs. Cards clamp `PreferredWidth` to the available viewport and keep wider examples horizontally reachable. Set `ContentScrollBarVisibility="Disabled"` for flow text that should wrap.

Chart and diagram `Data`, picker values, deferred list sources, text values, material presets and replay settings are dependency properties. Their templates use bindings so source changes reach the existing views. Passwords retain `PasswordBox`'s non-dependency-property contract. Rich-text documents retain their identity, and streaming replay stops when unloaded or retemplated.

The portable contract project compiles these actual sample sources:

```sh
dotnet test tests/Jalium.UI.Sample.Tests/Jalium.UI.Sample.Tests.csproj
```

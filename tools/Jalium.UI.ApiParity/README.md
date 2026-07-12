# Jalium.UI API parity verifier

This tool reads compiled metadata with Roslyn. It does not edit the legacy CSV
files in `docs/wpf-parity-gap/csv`.

The verifier validates every legacy table in `docs/wpf-parity-gap/csv`:

- `missing_types.csv`
- `missing_ctors.csv`
- `missing_properties.csv`
- `missing_methods.csv`
- `missing_events.csv`
- `missing_fields.csv`
- `missing_enum_values.csv`
- `inconsistencies.csv`
- `ns_mismatch.csv`

It builds complete public/protected (plus public-interface explicit
implementation) symbol indexes for the four WPF owner assemblies and the seven
Jalium runtime assemblies. Ambiguous or non-metadata facets are emitted as
explicit `ambiguous-*` / `unsupported-*` states and are never counted as a pass.

## Commands

Run the synthetic generic/constructor/namespace checks:

```powershell
dotnet run --project tools/Jalium.UI.ApiParity/Jalium.UI.ApiParity.csproj -c Release -- self-test --repo-root .
```

Build all referenced Jalium assemblies serially, then validate the legacy rows:

```powershell
dotnet build tools/Jalium.UI.ApiParity/Jalium.UI.ApiParity.csproj -c Release -m:1 -p:BuildInParallel=false
dotnet run --project tools/Jalium.UI.ApiParity/Jalium.UI.ApiParity.csproj -c Release --no-build -- verify-legacy --repo-root .
```

By default, verification writes UTF-8 JSONL and UTF-8-with-BOM CSV to
`tools/Jalium.UI.ApiParity/artifacts`. Use `--output <directory>` to select a
different location. Add `--fail-on-gap` when every status other than `present`
or `resolved` should produce exit code 1.

`api_id` is the WPF assembly name plus Roslyn's documentation-comment ID.
`gap_id` is a versioned SHA-256-derived ID over `api_id` and a normalized legacy
gap facet (category, declaring type, API label, and expected signature). Neither
ID depends on row ordering, tier, suggestions, or output display formatting.

The namespace policy is stored in `namespace-map.json`. Rules use longest-prefix
matching; type-specific overrides are supported for exceptions.

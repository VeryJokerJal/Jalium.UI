<div align="center">

# Jalium.UI API Parity Verifier

**Roslyn metadata checks for WPF-compatible API contracts**

[`namespace-map.json`](namespace-map.json) · [Repository README](../../README.md)

</div>

> [!WARNING]
> The historical `verify-legacy` input set under `docs/wpf-parity-gap/csv`
> was retired from the repository after the parity close-out. On a fresh
> checkout, `verify-legacy` therefore stops with a missing-file error. With that
> dataset absent, the repository currently has no runnable end-to-end API parity
> gate.

## What It Checks

The verifier reads compiled metadata with Roslyn; it never edits source files.
The historical end-to-end verification path is designed to index:

| Side | Assemblies |
| --- | --- |
| WPF reference | `WindowsBase`, `PresentationCore`, `PresentationFramework`, `System.Xaml` |
| Jalium runtime | `Jalium.UI.Managed`, `Jalium.UI.Gpu`, `Jalium.UI.Xaml` |

The supported `self-test` exercises the verifier logic with synthetic contracts,
stable IDs, generic and constructor cases, exact enum values, the real namespace
map, external Windows Desktop reference mapping, and ambiguity detection. It
does **not** build the real WPF/Jalium indexes or audit the current Jalium
assemblies for parity.

## Run the Supported Self-Test

From the repository root, with the .NET 10 Windows Desktop reference pack
available:

```powershell
dotnet run --project tools/Jalium.UI.ApiParity/Jalium.UI.ApiParity.csproj `
  -c Release -- self-test --repo-root .
```

A successful run ends with `SELF-TEST PASS` and exit code `0`.

## Historical Legacy Verification

`verify-legacy` is retained in code so archived parity datasets can still be
audited. It expects all nine CSV files at the fixed path
`docs/wpf-parity-gap/csv`:

```text
missing_types.csv          missing_methods.csv       missing_enum_values.csv
missing_ctors.csv          missing_events.csv        inconsistencies.csv
missing_properties.csv     missing_fields.csv        ns_mismatch.csv
```

If that archived input tree is restored deliberately, the command shape is:

```powershell
dotnet build tools/Jalium.UI.ApiParity/Jalium.UI.ApiParity.csproj `
  -c Release -m:1 -p:BuildInParallel=false

dotnet run --project tools/Jalium.UI.ApiParity/Jalium.UI.ApiParity.csproj `
  -c Release --no-build -- verify-legacy --repo-root . `
  --output tools/Jalium.UI.ApiParity/artifacts
```

After legacy rows are processed, `unresolved-wpf-api` or
`legacy-input-mismatch` takes priority and returns `2`; otherwise
`--fail-on-gap` returns `1` for any status other than `present` or `resolved`.
A clean report returns `0`, an unknown command returns `64`, and missing inputs
or unexpected runtime failures return `70`.

## Output Contract

Legacy verification writes two equivalent reports:

| File | Encoding | Purpose |
| --- | --- | --- |
| `legacy-validation.jsonl` | UTF-8 | Machine-readable, one result per line |
| `legacy-validation.csv` | UTF-8 with BOM | Spreadsheet-friendly review |

`api_id` combines the WPF owner assembly with Roslyn's documentation-comment
ID. `gap_id` is a versioned SHA-256-derived ID over that API identity and the
normalized legacy facet. Neither ID depends on row order, tier, suggestions,
or display formatting.

Namespace rules live in [`namespace-map.json`](namespace-map.json), use
longest-prefix matching, and support type-specific overrides.

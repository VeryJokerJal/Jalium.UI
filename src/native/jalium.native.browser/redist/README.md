<div align="center">

# WebView2 Loader Redistributables

**Architecture-specific native loaders bundled by Jalium.UI on Windows**

`win-x64` · `win-arm64` · loaded beside the application

[Browser implementation](../src/browser.cpp) · [Managed compatibility surface](../../../managed/Jalium.UI.Controls/WebView2Compat/) · [Dependency guard](../../../../tests/Jalium.UI.Tests/WebViewDependencyTests.cs)

</div>

> [!IMPORTANT]
> These files are the thin WebView2 loader, not the Edge WebView2 Runtime.
> Applications still require a compatible Microsoft Edge WebView2 Runtime on
> the target machine.

## Why Jalium.UI Bundles the Loader

Jalium.UI implements the managed `CoreWebView2*` compatibility surface in-tree
and deliberately does not reference the `Microsoft.Web.WebView2` NuGet package.
The source-and-test project guard in
[`WebViewDependencyTests.cs`](../../../../tests/Jalium.UI.Tests/WebViewDependencyTests.cs)
scans project files beneath `src/` and `tests/` to prevent that package
dependency from being reintroduced there.

[`browser.cpp`](../src/browser.cpp) calls
`LoadLibraryW(L"WebView2Loader.dll")`. The loader must therefore sit next to the
application and match the **process architecture**. Its version does not need
to match the installed runtime or the loader used by another architecture.

## Checked-in Inventory

| RID | File | Version | PE architecture | Size | SHA-256 |
| --- | --- | --- | --- | ---: | --- |
| `win-x64` | [`win-x64/WebView2Loader.dll`](win-x64/WebView2Loader.dll) | `1.0.3800.47` | x86-64 (`0x8664`) | 160,880 B | `86545b66cdb0603bc26b626fb9ad610cb6e71f28d468f5ea66df23b03dda96d5` |
| `win-arm64` | [`win-arm64/WebView2Loader.dll`](win-arm64/WebView2Loader.dll) | `1.0.3719.77` | ARM64 (`0xAA64`) | 146,024 B | `2bbac5865ecd9e1ab245f997de13e22c8f9a7f4cf803efc0e304180926c3b313` |

Both files were sourced from the matching
`runtimes/<rid>/native/WebView2Loader.dll` path in a
`Microsoft.Web.WebView2` NuGet package. Review and preserve the upstream
license and redistribution notices whenever refreshing them.

## Packaging Contract

[`Jalium.UI.Interop.csproj`](../../../managed/Jalium.UI.Interop/Jalium.UI.Interop.csproj)
adds the matching loader to `runtimes/<rid>/native/` for `win-x64` and
`win-arm64`. This per-RID layout replaced the old flat x64-only file, which
failed in an ARM64 process with `0x8007000B` (`BadImageFormatException`).

## Refresh Checklist

1. Choose the desired `Microsoft.Web.WebView2` package version and review its
   current license/redistribution terms.
2. Copy each loader from the package's exact
   `runtimes/<rid>/native/WebView2Loader.dll` folder into the matching folder
   here. Never reuse the x64 binary for ARM64.
3. Verify version, architecture, size, and SHA-256, then update the inventory
   table in the same change.
4. Confirm the Interop package contains each loader only under its matching
   `runtimes/<rid>/native/` path.
5. Run the `WebViewDependencyTests` guard to ensure the managed WebView2 NuGet
   dependency has not returned.

Useful PowerShell checks:

```powershell
Get-ChildItem src/native/jalium.native.browser/redist -Recurse `
  -Filter WebView2Loader.dll |
  Select-Object FullName, Length, @{Name='Version'; Expression={$_.VersionInfo.FileVersion}}

Get-FileHash src/native/jalium.native.browser/redist/*/WebView2Loader.dll `
  -Algorithm SHA256

# Run from a Visual Studio Developer Command Prompt for each file.
dumpbin /headers src/native/jalium.native.browser/redist/win-x64/WebView2Loader.dll | findstr /i machine
dumpbin /headers src/native/jalium.native.browser/redist/win-arm64/WebView2Loader.dll | findstr /i machine
```

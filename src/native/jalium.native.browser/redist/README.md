# Bundled WebView2Loader.dll (per-RID)

Jalium self-hosts the WebView2 loader rather than taking a `Microsoft.Web.WebView2`
NuGet dependency (the managed `CoreWebView2*` surface is reimplemented in
`Jalium.UI.Controls/WebView2Compat/`, and `tests/.../WebViewDependencyTests.cs`
guards against the package being reintroduced). The loader is a thin stub that
locates the user-installed Edge WebView2 Runtime via COM, so it must match the
**process architecture**, not a particular runtime build.

`jalium.native.browser/src/browser.cpp` loads it by name (`LoadLibraryW("WebView2Loader.dll")`),
so the correct-arch copy has to sit next to the app. `Jalium.UI.Interop.csproj`
packs `redist/$(JaliumNativePackRid)/WebView2Loader.dll` into
`runtimes/$(JaliumNativePackRid)/native/`, so packing `-p:JaliumNativePackRid=win-arm64`
ships the ARM64 loader (fixes #136; the previous flat `redist/WebView2Loader.dll`
was x64-only and failed to load on win-arm64 with `0x8007000B`).

| RID         | Version       | Architecture | Source |
|-------------|---------------|--------------|--------|
| `win-x64`   | 1.0.3800.47   | x86-64       | Microsoft.Web.WebView2 NuGet, `runtimes/win-x64/native/` |
| `win-arm64` | 1.0.3719.77   | ARM64        | Microsoft.Web.WebView2 NuGet, `runtimes/win-arm64/native/` |

The loader version need not match across architectures (each just brokers to the
installed Edge runtime). To refresh, copy the matching
`runtimes/<rid>/native/WebView2Loader.dll` out of the Microsoft.Web.WebView2
package for the desired version into the matching folder here.

# Linux desktop support

Jalium.UI targets portable desktop Linux rather than a single distribution.
The release payload contains four .NET runtime identifiers:

| C library | Architecture | RID |
| --- | --- | --- |
| glibc | x86-64 | `linux-x64` |
| glibc | Arm64 | `linux-arm64` |
| musl | x86-64 | `linux-musl-x64` |
| musl | Arm64 | `linux-musl-arm64` |

This covers mainstream desktop distributions on x86-64 and Arm64: glibc 2.31
or newer systems such as supported Debian/Ubuntu releases, Fedora, RHEL 9 or
newer, current openSUSE/SLES and Arch-family releases, plus Alpine through the
separate musl payload. Linux Arm32, LoongArch64, bionic/Termux, mobile shells,
and framebuffer-only installations are not release RIDs in this repository.

The glibc release payload is built in Ubuntu 20.04 containers with glibc 2.31
and GCC 9. CI rejects any native library, self-contained apphost/runtime ELF,
or NativeAOT executable that imports symbols newer than `GLIBC_2.31` or
`GLIBCXX_3.4.28`. This is an ABI floor, not an extension of a distribution's
vendor support lifetime: a target desktop must still provide the runtime
dependencies below. Musl releases are built and tested separately on Alpine.

## Windowing and rendering

- `JALIUM_WINDOW_SYSTEM=auto` prefers Wayland when a compositor is available
  and falls back to X11.
- `JALIUM_WINDOW_SYSTEM=wayland` and `x11` force a backend and fail clearly
  when that backend is unavailable.
- Vulkan supports both `VK_KHR_wayland_surface` and
  `VK_KHR_xlib_surface`.
- The software renderer presents through `wl_shm` on Wayland and `XPutImage`
  on X11.
- `JALIUM_RENDER_BACKEND=auto` prefers Vulkan and keeps the software renderer
  as the portability fallback. `vulkan` and `software` force one backend.

The native libraries are deployed together under `runtimes/<rid>/native` and
use only `RUNPATH=$ORIGIN` for Jalium-to-Jalium dependencies.

## Desktop integration

- File open/save/folder selection and URI launching use
  `xdg-desktop-portal` through one persistent libgio D-Bus connection.
- Desktop notifications use libnotify only when a live
  `org.freedesktop.Notifications` service exists.
- Text clipboard, drag-and-drop, keyboard text input, key repeat/state, and
  composition are implemented for X11 and Wayland. Clipboard and drag-and-drop
  support UTF-8 text and URI lists; Wayland input supports text-input-v3 and
  the text-input-v1 protocol advertised by WSLg.
- The managed `AutomationPeer` tree is exported through AT-SPI2 on Linux,
  including Accessible, Component, Action, and Text interfaces plus focus,
  property, window, and child-change events. If the desktop accessibility bus
  is absent, startup remains safe and the bridge reports itself unavailable.
- The standard release payload links GStreamer 1.16 or newer directly for image, video,
  camera, and AAC/M4A integration. Its GLib/GObject and GStreamer base/app,
  pbutils, video, and audio libraries are therefore runtime dependencies, not
  optional plugins. A custom native build made without GStreamer keeps the ABI
  stubs but does not provide those media capabilities.

The Gallery capability-gates Windows-only WebView2, taskbar/jump-list, and
tray-icon samples. Printing is currently gated until the synchronous print API
can provide the document file descriptor required by the portal.

## Runtime dependencies

Package names vary between distributions. Ubuntu 24.04 uses the time64 GLib
package name:

```bash
sudo apt-get install \
  ca-certificates libc6 libgcc-s1 libgssapi-krb5-2 \
  libssl3 libstdc++6 tzdata zlib1g \
  libx11-6 libxrandr2 libwayland-client0 libxkbcommon0 \
  libvulkan1 mesa-vulkan-drivers fontconfig \
  libglib2.0-0t64 libnotify4 xdg-desktop-portal at-spi2-core \
  gstreamer1.0-plugins-base gstreamer1.0-plugins-good \
  libicu74
```

On Ubuntu 22.04 and Debian 12 the GLib runtime package is `libglib2.0-0`; install
the ICU package version supplied by that distribution (`libicu70` on Ubuntu
22.04 and `libicu72` on Debian 12). Alpine uses the equivalent runtime set:

```bash
apk add \
  ca-certificates libgcc libssl3 libstdc++ tzdata krb5 zlib \
  libx11 libxrandr wayland-libs libxkbcommon \
  vulkan-loader mesa-vulkan-swrast fontconfig \
  glib libnotify xdg-desktop-portal at-spi2-core \
  gstreamer gst-plugins-base gst-plugins-good icu-libs icu-data-full
```

A minimal session must also install one portal backend appropriate for its
desktop (`xdg-desktop-portal-gtk`, `xdg-desktop-portal-kde`, or the compositor's
equivalent) and at least one font package visible to Fontconfig. Full GNOME,
KDE Plasma, and other mainstream desktop installations normally provide both.

Fedora/RHEL, openSUSE, and Arch users should install their distribution's
equivalents for X11, Wayland, xkbcommon, the Vulkan loader/Mesa driver,
fontconfig, GLib/GObject, libnotify, xdg-desktop-portal, AT-SPI2, GStreamer 1.0
base/good plugins, and ICU. Video codecs outside the base/good sets may require
GStreamer libav or an additional codec plugin. Vulkan can be omitted only for
an application that forces the software backend and does not run the standard
Vulkan payload diagnostic.

To build the native libraries on Debian/Ubuntu:

```bash
sudo apt-get install \
  build-essential cmake ninja-build pkg-config \
  libx11-dev libxext-dev libxrandr-dev \
  libwayland-dev wayland-protocols libxkbcommon-dev \
  libvulkan-dev libfontconfig1-dev \
  libgstreamer1.0-dev libgstreamer-plugins-base1.0-dev
```

Equivalent development packages are installed by the Alpine CI job before it
builds the musl payload.

## Build and package

Build one native RID on a matching Linux host:

```bash
bash eng/linux/build-native.sh linux-x64 Release
```

Enable all native smoke targets:

```bash
JALIUM_NATIVE_BUILD_TESTS=1 \
  bash eng/linux/build-native.sh linux-x64 Release
```

Build managed projects in a checkout that is also mounted in Windows/WSL with
an isolated output root:

```bash
dotnet build src/packaging/Jalium.UI.Linux/Jalium.UI.Linux.csproj \
  -c Release \
  -p:JaliumBuildRoot=/tmp/jalium-ui-build \
  -p:GeneratePackageOnBuild=false
```

The Gallery repository provides the end-to-end publisher:

```bash
cd ../Jalium.UI.Gallery
bash eng/linux/publish.sh linux-x64 Release
```

It builds the matching native payload, restores and publishes a self-contained
Gallery, runs `--diagnostics-only`, validates all seven `.so` files, and writes
a `.tar.gz` archive under `artifacts/linux`.

Publish a NativeAOT variant with the same RID-specific native payload:

```bash
bash eng/linux/publish.sh linux-x64 Release --aot
```

NativeAOT requires a C toolchain (`clang` on the release builders) and zlib
development headers. The result contains a native Gallery executable and no
managed application DLLs; the seven Jalium backend `.so` files remain beside
it because their window-system, renderer, text, and media ABIs are selected at
runtime. The glibc NativeAOT executable is linked and run in the same Ubuntu
20.04 baseline container as the native payload, then the complete publish
directory is checked against the symbol ceiling. CI publishes both
self-contained and NativeAOT archives for all four release RIDs.

## Diagnostics

```bash
./Jalium.UI.Gallery --diagnostics-only
```

The command reports OS/RID/architecture, display selection, renderer override,
file presence and actual `dlopen` status for every native library, plus portal,
notification-daemon, and AT-SPI2 bridge status. It returns nonzero when the
native payload is incomplete or unloadable.

## Distribution matrix

Package names for the runtime dependencies listed above, per family. "Runtime"
is what end-user machines need; "-dev" packages are only needed to build the
native payload from source.

| Capability | Debian/Ubuntu | Fedora/RHEL | Arch | Alpine (musl) | openSUSE |
| --- | --- | --- | --- | --- | --- |
| X11 client | `libx11-6 libxext6 libxrandr2` | `libX11 libXext libXrandr` | `libx11 libxext libxrandr` | `libx11 libxext libxrandr` | `libX11-6 libXext6 libXrandr2` |
| Wayland client | `libwayland-client0` | `wayland` | `wayland` | `wayland-libs-client` | `libwayland-client0` |
| Keyboard maps | `libxkbcommon0` | `libxkbcommon` | `libxkbcommon` | `libxkbcommon` | `libxkbcommon0` |
| Font discovery | `libfontconfig1` | `fontconfig` | `fontconfig` | `fontconfig` | `fontconfig` |
| Vulkan loader | `libvulkan1` (+ `mesa-vulkan-drivers`) | `vulkan-loader` (+ `mesa-vulkan-drivers`) | `vulkan-icd-loader` (+ `vulkan-swrast`) | `vulkan-loader` (+ `mesa-vulkan-swrast`) | `libvulkan1` |
| Media (optional) | `gstreamer1.0-plugins-base/-good` | `gstreamer1-plugins-base/-good` | `gst-plugins-base/-good` | `gst-plugins-base/-good` | `gstreamer-plugins-base/-good` |
| Portals (dialogs) | `xdg-desktop-portal` + backend | `xdg-desktop-portal` + backend | `xdg-desktop-portal` + backend | `xdg-desktop-portal` + backend | `xdg-desktop-portal` + backend |
| Notifications | `libnotify4` | `libnotify` | `libnotify` | `libnotify` | `libnotify4` |
| CJK text | `fonts-noto-cjk` | `google-noto-sans-cjk-fonts` | `noto-fonts-cjk` | `font-noto-cjk` | `noto-sans-cjk-fonts` |

A CJK font is required for CJK glyphs — the self-hosted text engine falls back
per-codepoint through fontconfig, but it cannot draw glyphs no installed font
provides.

## Troubleshooting

**Window opens but stays black.** The software present path reports
`BACKEND_NOT_AVAILABLE` when the native payload was built without
X11/Wayland present support; rebuild with `libx11-dev`/`libwayland-dev`
installed, or check `--diagnostics-only` output for which `.so` failed to load.

**`DllNotFoundException: libjalium.native.*`.** The RID-specific native
payload is not next to the app. For NuGet consumers, publish or run with an
explicit `-r linux-x64` (or arm64/musl) so `runtimes/<rid>/native` assets are
materialized; for repo builds run `bash eng/linux/build-native.sh` first.

**No file dialog appears.** Portal dialogs need `xdg-desktop-portal` plus a
desktop backend (`-gtk`, `-kde`, `-wlr`, …) on the session bus. Check
`FileDialog.IsPortalAvailable`; without a portal the call returns `false`
rather than opening a fallback dialog.

**Vulkan fails in a VM/container.** Install the Mesa software rasterizer
(`mesa-vulkan-drivers` / `vulkan-swrast`) or force
`JALIUM_RENDER_BACKEND=software`. The renderer falls back to software
automatically when Vulkan initialization fails.

**Blurry on a HiDPI Wayland monitor.** Fixed by per-surface buffer scale;
if you run an older payload, update — the compositor was upscaling a
scale-1 buffer.

**IME does not compose (X11).** XIM requires a UTF-8 locale; in minimal
environments set e.g. `LANG=C.UTF-8`. On Wayland, text-input-v3 (or v1 on
WSLg) is negotiated automatically; check the diagnostics output for the
selected IME path.

**Which window system was picked?** Set `JALIUM_WINDOW_SYSTEM=x11|wayland`
to force one; default `auto` prefers Wayland when `WAYLAND_DISPLAY` is set.

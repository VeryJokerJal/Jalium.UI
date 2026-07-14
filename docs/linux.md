# Linux desktop support

Jalium.UI has a native Linux desktop backend for X11 and Wayland. Vulkan is
the accelerated renderer and the CPU renderer is the portability fallback.
Linux support is implemented by `Jalium.UI.Linux`; it is not a compatibility
layer over WPF or Win32.

The detailed verification ledger and the remaining limits are tracked in
[`linux-parity-status.md`](linux-parity-status.md). In particular, a feature
being present in the source is not treated as proof for every architecture,
desktop, device, or driver.

## Release RIDs and ABI floor

The package layout reserves four Linux runtime identifiers:

| C library | Architecture | RID |
| --- | --- | --- |
| glibc | x86-64 | `linux-x64` |
| glibc | Arm64 | `linux-arm64` |
| musl | x86-64 | `linux-musl-x64` |
| musl | Arm64 | `linux-musl-arm64` |

The glibc release build is produced in the Ubuntu 20.04 baseline image. The
release gates reject imports newer than `GLIBC_2.31` or
`GLIBCXX_3.4.28`, including imports from a self-contained apphost or a
NativeAOT executable. Native libraries use `RUNPATH=$ORIGIN` for Jalium-to-
Jalium dependencies.

`linux-x64` and `linux-musl-x64` have current local evidence for their native,
static-link, package, self-contained, trimmed single-file, and NativeAOT gates.
`linux-arm64` has the same evidence under local QEMU execution, which does not
replace physical Arm qualification. The workflow contains a native Arm runner
for `linux-musl-arm64`, but that RID and the final combined four-RID package
remain pending in this working tree. Consult the verification ledger before a
release.

Linux Arm32, LoongArch64, Android/bionic, Termux, and framebuffer-only systems
are not release targets here.

## Selecting the window system and renderer

```bash
# auto is the default
JALIUM_WINDOW_SYSTEM=auto   # prefer Wayland, then try X11
JALIUM_WINDOW_SYSTEM=wayland
JALIUM_WINDOW_SYSTEM=x11

JALIUM_RENDER_BACKEND=auto # prefer Vulkan, then use software
JALIUM_RENDER_BACKEND=vulkan
JALIUM_RENDER_BACKEND=software
```

Forcing a backend makes an unavailable backend fail explicitly. `auto` is the
appropriate setting for a distributable desktop application.

### X11

The X11 path implements:

- window creation, state, activation, owner relationships, min/max sizing,
  interactive move/resize, icon, opacity, topmost, and taskbar visibility;
- monitor enumeration through XRandR and work-area/state integration through
  ICCCM/EWMH;
- keyboard and XIM composition, themed cursors, mouse capture, XInput2 smooth
  scrolling, touch, and pen axes;
- XDND drag-and-drop and multi-format clipboard ownership;
- Vulkan presentation through `VK_KHR_xlib_surface`;
- software presentation through MIT-SHM when available, with a tested
  `XPutImage` fallback and damage-scoped updates.

Window-manager requests such as activation and topmost remain policy decisions:
a conforming request can still be refused by the active window manager.

### Wayland

The Wayland path implements:

- `xdg_toplevel` and `xdg_popup`, owner-relative popup placement, min/max
  sizing, compositor move/resize, maximize/minimize/fullscreen, and configure
  synchronization;
- compositor-native system menus and token-backed activation through
  `xdg_toplevel.show_window_menu` and `xdg-activation-v1` when an eligible
  input serial is available;
- integer `wl_output` scale, buffer scaling, monitor metadata, themed cursors,
  `zxdg-decoration`, optional `xdg-toplevel-icon-v1`, and `xdg-foreign-v2`
  portal parent handles;
- keyboard repeat/state, xkb text commits, text-input-v3, WSLg's
  text-input-v1, mouse, and `wl_touch`;
- data-device clipboard and drag-and-drop;
- Vulkan presentation through `VK_KHR_wayland_surface` and software
  presentation through double-buffered `wl_shm`.

Core Wayland protocols deliberately do not expose several global desktop
operations. Jalium therefore reports these operations as unsupported instead
of fabricating success:

- global cursor coordinates and absolute top-level window positions;
- always-on-top, whole-window opacity, and taskbar-list visibility;
- forced focus/activation without a compositor-issued activation token.

Per-pixel alpha is still available through a transparent render surface.
`xdg-toplevel-icon-v1`, decoration, and portal-parent support are conditional on
the compositor advertising the corresponding protocol. Integer output scaling
is implemented; a dedicated fractional-scale protocol path is not.

## Rendering and text

- Vulkan and software render targets use the same managed drawing pipeline.
- The self-hosted OpenType engine performs shaping and rasterization;
  Fontconfig is used for font discovery and per-codepoint fallback. It does not
  require FreeType or HarfBuzz for ordinary outline text.
- Explicit ClearType/LCD mode produces independent RGB coverage. Vulkan uses a
  dual-source blend pipeline when the device advertises that feature and
  degrades that run to grayscale when it does not.
- Color glyphs preserve authored colors for supported COLR/CPAL and CBDT/CBLC
  data. Optional runtime FreeType can extend COLR-v1 coverage, but this is not a
  promise that every color-font technology or paint graph is supported.
- A CJK font must be installed for CJK fallback; no renderer can draw a glyph
  absent from every installed font.

The current Linux GPU evidence is software Vulkan (`llvmpipe`) and WSL/virtual
compositors. It is not physical-vendor GPU qualification.

## Input, clipboard, and drag-and-drop

Both window systems support normal keyboard/mouse input, composition, capture,
key state, text and URI-list drag-and-drop, and common clipboard formats:

- UTF-8 plain text;
- `text/html` and RTF;
- `text/uri-list` / file-drop lists;
- `image/png` / bitmap data;
- byte-backed custom MIME representations through `IDataObject`.

The drag source exposes effect negotiation, feedback/cancellation, and a drag
image. The real interop tests use `xclip`, `wl-copy`/`wl-paste`, XDND, and a
nested Weston compositor rather than testing only an in-process data object.

`wl_touch` and XInput2 touch are wired into the managed pointer/touch pipeline.
XInput2 and Wayland tablet-v2 map proximity, hover/contact, pressure, tilt,
rotation, eraser/tool type, and barrel/secondary buttons into the managed pen
pipeline. The current evidence uses synthetic protocol events rather than
physical touch or pen hardware.

## Media

`libjalium.native.media.so` does not have direct GStreamer or GLib
`DT_NEEDED` entries. GStreamer 1.16+ is loaded with `dlopen`/`dlsym` only when a
GStreamer-backed capability is used. If the runtime or a codec plugin is
absent, the capability reports unavailable while the media library itself
remains loadable. Built-in audio codecs and GIF decoding therefore do not
disappear merely because GStreamer is not installed.

The Linux GStreamer backend implements:

- local files and HTTP/HTTPS sources, including HTTP range requests;
- H.264 video and AAC audio when the installed plugins provide the codecs;
- accurate microsecond seek, audio-track discovery/selection, and embedded
  subtitle discovery/read/seek;
- animated GIF, APNG, and WebP frame count, compositing/disposal/blend, frame
  decode, and frame timing;
- camera and microphone enumeration/open/read/close APIs;
- managed bridge coverage for the same local and HTTP media fixture.

The automated positive path generates a real H.264/AAC Matroska file with two
audio tracks and a subtitle track, then exercises local and HTTP 200/206
lifecycles. The HTTP fixture is streamable (its Matroska Cues are front-loaded);
opening an arbitrary remote Matroska file with tail-loaded Cues and seeking
immediately is not part of this qualification. Camera and microphone tests
currently prove enumeration and safe no-device failure only; no physical
capture device has been qualified.

### dma-buf boundary

`JALIUM_LINUX_MEDIA_DMABUF_EXPORT` is an observed capability, not a generic
"VAAPI is installed" flag. It is set only after a real sample exports a
Vulkan-importable, single-plane packed-RGB dma-buf (`AR24`, `XR24`, `AB24`, or
`XB24`).

NV12, P010, `DMA_DRM`, and multi-plane dma-bufs currently reopen the
timestamp-matched CPU BGRA/RGBA path. Vulkan does not yet own the YCbCr
immutable-sampler pipeline needed for those formats. The available WSL/CI
environment has no `/dev/dri`, so the fallback and lifetime rules are tested,
but physical VAAPI-to-Vulkan zero-copy is not.

## Desktop integration

### Portals and printing

One persistent libgio D-Bus connection backs:

- open/save/folder dialogs, filters, cancellation, and X11/Wayland parent
  handles through `org.freedesktop.portal.FileChooser`;
- URI and file launching through `org.freedesktop.portal.OpenURI`;
- color scheme, contrast, and reduced-motion reads/change signals through
  `org.freedesktop.portal.Settings`;
- printing through `org.freedesktop.portal.Print`.

Linux printing renders the requested visual or paginator to PDF, completes
`PreparePrint`, and passes the PDF descriptor to `Print` with a Unix FD list.
The desktop portal/backend normally hands that job to the desktop print stack,
commonly CUPS. Jalium does not directly enumerate or administer CUPS queues on
Linux: `PrintQueue` enumeration and queue control remain Windows-only. The
automated proof uses a protocol-faithful fake portal and validates the PDF file
descriptor; a physical printer has not been tested.

### Accessibility

The managed automation-peer tree is exported on the AT-SPI2 accessibility bus.
The bridge implements Accessible, Application, Component, Action, Text,
EditableText, Value, Selection, and Table contracts, plus focus, state,
property, child-tree, and window lifecycle events. Startup remains safe when
the accessibility bus is absent.

The integration smoke queries and mutates the tree with real accessibility-bus
D-Bus calls, including Unicode-scalar text, EditableText changes,
`ChildrenChanged`, focus/property changes, and window lifecycle. This is
protocol coverage, not an interoperability certification for every screen
reader.

### Tray, notifications, session, and file associations

- `NotifyIcon` exports both freedesktop and KDE StatusNotifierItem identities,
  properties, icon/tooltip/menu metadata, Activate, SecondaryActivate,
  ContextMenu, and Scroll callbacks. Balloon notifications use libnotify and
  surface action/closed callbacks when a notification service is present.
- `Application`/`Window.SessionEnding` integrates with systemd-logind's delay
  inhibitor and `PrepareForShutdown`, and with XSMP logout/cancellation for
  X11 sessions. A desktop may expose either path.
- File associations create per-user `.desktop` and shared-MIME-info files,
  update the MIME database, and select the handler with `xdg-mime`. Removal
  cleans only the association created by Jalium.
- Linux `SystemParameters` reads GNOME GSettings, KDE configuration, portal
  appearance settings, environment overrides, monitor/work-area data, and
  `/sys/class/power_supply`; changes raise the framework settings events.

## Runtime dependencies

Package names vary by distribution. The table distinguishes mandatory native
display dependencies from optional desktop/media capabilities.

| Area | Runtime requirement | When required |
| --- | --- | --- |
| X11 | X11, Xext, XRandR, Xi, Xcursor | X11 backend; Xext enables MIT-SHM |
| Wayland | wayland-client, xkbcommon | Wayland backend |
| Vulkan | Vulkan loader and a vendor/Mesa ICD | Vulkan renderer |
| Software | no GPU driver; X11 or Wayland display libraries | Software renderer |
| Text | Fontconfig and at least one font | All rendered text |
| Portals/settings/AT-SPI | GLib/GObject/GIO; portal and AT-SPI services as used | Corresponding integration |
| Notifications | libnotify and a notification daemon | Balloon/toast notifications |
| XSMP | libSM and libICE | X11 session-ending integration |
| Media | GStreamer 1.16 base/app/video/audio/allocators/pbutils; libwebp for WebP decode | Corresponding GStreamer/WebP-backed formats |
| File associations | `xdg-mime`, shared-mime-info; desktop-file-utils optional | Register/remove association |

Typical Ubuntu 24.04 runtime packages are:

```bash
sudo apt-get install \
  ca-certificates libc6 libgcc-s1 libstdc++6 libssl3 zlib1g libicu74 \
  libx11-6 libxext6 libxrandr2 libxi6 libxcursor1 \
  libwayland-client0 libxkbcommon0 \
  libvulkan1 mesa-vulkan-drivers \
  fontconfig fonts-dejavu-core fonts-noto-cjk \
  libglib2.0-0t64 libnotify4 libsm6 libice6 at-spi2-core \
  xdg-desktop-portal xdg-desktop-portal-gtk xdg-utils shared-mime-info \
  gstreamer1.0-plugins-base gstreamer1.0-plugins-good
```

Install `gstreamer1.0-libav` and/or `gstreamer1.0-plugins-bad` when the media
formats you ship require those codecs. Ubuntu 22.04 and Debian 12 use
`libglib2.0-0` rather than the Ubuntu 24.04 time64 package name and ship a
different ICU package version. Alpine uses the equivalent musl packages.

A desktop portal also needs one backend appropriate to the session, such as
the GTK, KDE, or compositor-specific backend.

Build dependencies on Debian/Ubuntu include:

```bash
sudo apt-get install \
  build-essential clang cmake ninja-build pkg-config python3 binutils \
  libx11-dev libxext-dev libxrandr-dev libxi-dev libxcursor-dev \
  libwayland-dev wayland-protocols libxkbcommon-dev \
  libvulkan-dev libfontconfig1-dev \
  libgstreamer1.0-dev libgstreamer-plugins-base1.0-dev
```

## Build, validate, and package

Build a native payload on a matching host:

```bash
JALIUM_NATIVE_BUILD_TESTS=1 \
  bash eng/linux/build-native.sh linux-x64 Release
```

The Ubuntu 20.04 baseline wrapper performs the glibc build, native CTest suite,
and real X11/Wayland presentation checks:

```bash
bash eng/linux/build-glibc-baseline.sh linux-x64 Release
```

Validate the release ABI and exports:

```bash
bash eng/linux/check-symbol-versions.sh \
  src/native/bin/native/linux-x64/Release 2.31 3.4.28
bash eng/linux/check-native-exports.sh \
  src/native/bin/native/linux-x64/Release
```

Exercise the static NativeAOT aggregate and its Linux media/audio link path on
a matching libc and architecture host:

```bash
bash eng/linux/test-native-aot-static.sh linux-x64 Release
```

This gate builds `libjalium.native.aot.a`, links the dedicated
`jalium.native.aot.linux.media-link` executable, runs it, and checks the
aggregate registration export. It is a static-link regression test, not a
single-file Linux packaging mode.

Cross builds require an actual compiler/sysroot for the requested architecture
and libc. A host build cannot be relabelled as another RID:

```bash
JALIUM_CROSS_C_COMPILER=/opt/cross/bin/aarch64-linux-gnu-gcc \
JALIUM_CROSS_CXX_COMPILER=/opt/cross/bin/aarch64-linux-gnu-g++ \
JALIUM_CROSS_SYSROOT=/opt/sysroots/aarch64-linux-gnu \
JALIUM_CROSS_PKG_CONFIG_LIBDIR=/opt/sysroots/aarch64-linux-gnu/usr/lib/aarch64-linux-gnu/pkgconfig:/opt/sysroots/aarch64-linux-gnu/usr/share/pkgconfig \
  bash eng/linux/build-native-cross.sh linux-arm64 Release

bash eng/linux/test-cross-toolchains.sh
```

Build the portable managed test project and the Linux sample with isolated
outputs when the checkout is shared with Windows/WSL:

```bash
dotnet test tests/Jalium.UI.Linux.Tests/Jalium.UI.Linux.Tests.csproj \
  -c Release -m:1 \
  -p:JaliumBuildRoot=/tmp/jalium-managed \
  -p:GeneratePackageOnBuild=false

dotnet build samples/Jalium.UI.LinuxDemo/Jalium.UI.LinuxDemo.csproj \
  -c Release -m:1 \
  -p:JaliumBuildRoot=/tmp/jalium-managed \
  -p:GeneratePackageOnBuild=false
```

Package one RID, then exercise a package-only self-contained, trimmed
single-file, or NativeAOT consumer:

```bash
bash eng/linux/pack-linux-packages.sh \
  linux-x64 Release artifacts/packages /tmp/jalium-pack

package_version="$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' Directory.Build.props | head -1)"
bash eng/linux/test-nuget-consumer.sh \
  linux-x64 artifacts/packages /tmp/jalium-consumer \
  "$package_version" self-contained
bash eng/linux/test-nuget-consumer.sh \
  linux-x64 artifacts/packages /tmp/jalium-consumer \
  "$package_version" single-file
bash eng/linux/test-nuget-consumer.sh \
  linux-x64 artifacts/packages /tmp/jalium-consumer \
  "$package_version" aot
```

Packing is protected by a stale-native guard
(`eng/msbuild/JaliumStaleNativeGuard.targets`): `dotnet pack` fails with
`JALSTALE` errors when `src/native` has uncommitted changes, when a packed
binary is older than the last commit touching its sources, or when a payload's
`.jalium-native-complete` stamp (which now records `head=`/`dirty=` build
provenance) does not match the commit being packed. Rebuild the payload
(`cmake --build <builddir> --target jalium.native.package.complete`, or
`src/native/build-android.sh all` for Android) to clear the errors, or pass
`-p:JaliumAllowStaleNative=true` to downgrade them to warnings deliberately.
Stamps written by builds older than the guard lack provenance and are rejected
once — rebuild each payload once to refresh them.

The workflow defines this sequence for all four RIDs and then assembles a
combined package. Three RIDs currently have local consumer evidence; the final
four-RID result remains pending until the native `linux-musl-arm64` job and the
combined-package job complete successfully for the current source.
The ordinary self-contained and NativeAOT consumers deploy the seven Jalium
shared libraries beside the application. The single-file mode sets
`PublishSingleFile`, `PublishTrimmed`, and
`IncludeNativeLibrariesForSelfExtract`; CI starts it with a fresh
`DOTNET_BUNDLE_EXTRACT_BASE_DIR`, proves the app opens, and verifies that all
seven Jalium `.so` files were extracted and loaded from the bundle. This is a
single deployment file, not a fully static executable: native payloads still
exist as shared libraries in the configured bundle extraction cache while the
app runs.
Passing the static aggregate link gate does not change that runtime boundary.

## Troubleshooting

**The application cannot load `libjalium.native.*.so`.** Publish with an
explicit matching RID and keep all seven libraries together. Run
`check-native-exports.sh` to distinguish a missing system dependency from a
missing Jalium export.

**Vulkan fails in a VM or container.** Install Mesa's Vulkan software ICD or
set `JALIUM_RENDER_BACKEND=software`. `auto` falls back automatically.

**No portal dialog or print UI appears.** Install both
`xdg-desktop-portal` and a desktop-specific portal backend. Check the session
bus and `XDG_CURRENT_DESKTOP`; a service package without a running backend is
not sufficient.

**A codec is unavailable.** Use `gst-inspect-1.0` to check the decoder/demuxer
on the target machine. The Jalium media library loading successfully only
proves that its own ABI is available, not that every GStreamer plugin is
installed.

**IME does not compose on X11.** Use a UTF-8 locale and verify that the session
has a working XIM provider. Wayland negotiates text-input-v3 and then v1 where
available; otherwise it uses xkb text commits.

**A Wayland API returns unsupported.** Check the protocol-limit list above.
Global cursor position, tokenless forced activation, topmost, taskbar
visibility, and whole-window opacity cannot be emulated correctly with core
Wayland APIs.

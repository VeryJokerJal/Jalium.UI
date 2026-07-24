<div align="center">

# Jalium.UI Linux NuGet Consumer Gate

**Release-shape validation from packages only**

Four Linux RIDs · Three deployment modes · Seven native payloads · Real X11 startup

[Linux packaging guide](../../docs/linux.md#build-validate-and-package) · [Verification matrix](../../docs/linux-parity-status.md)

</div>

> [!IMPORTANT]
> This project is a release gate, not a normal sample. It intentionally contains
> only a `PackageReference` to `Jalium.UI.Linux`; adding a Jalium
> `ProjectReference` would invalidate the consumer test. The script does not
> enforce this invariant, so review the project file before running the gate.

## Gate Matrix

| Dimension | Supported values |
| --- | --- |
| RID | `linux-x64`, `linux-arm64`, `linux-musl-x64`, `linux-musl-arm64` |
| Mode | `self-contained`, `single-file`, `aot` |
| Display path | X11 under Xvfb |
| Renderer | Software, so the gate does not require a physical GPU |

| Mode | Expected deployment shape |
| --- | --- |
| `self-contained` | Managed entry assembly plus the seven Jalium `.so` sidecars |
| `single-file` | Trimmed bundle with native payload self-extraction enabled; no Jalium `.so` sidecars remain beside the executable |
| `aot` | NativeAOT executable with the required Jalium native payloads and no managed entry assembly |

## Run the Gate

Run from the repository root on a host that matches the RID and preserves LF
line endings for shell scripts. The native build creates the stamped payload,
the pack step creates an isolated Jalium package closure, and the consumer step
restores every `Jalium.*` package from that directory. Framework and runtime
dependencies can still come from the other feeds configured in `NuGet.config`.

```bash
rid=linux-x64
package_dir="artifacts/linux-packages/$rid"
output_root="artifacts/linux-consumer/$rid"
package_version="$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' Directory.Build.props | head -1)"

bash eng/linux/build-native.sh "$rid" Release

bash eng/linux/pack-linux-packages.sh \
  "$rid" Release "$package_dir" "/tmp/jalium-pack-$rid"

bash eng/linux/test-nuget-consumer.sh \
  "$rid" "$package_dir" "$output_root" "$package_version" self-contained
```

Run all three deployment modes against the same package closure:

```bash
for mode in self-contained single-file aot; do
  bash eng/linux/test-nuget-consumer.sh \
    "$rid" "$package_dir" "$output_root" "$package_version" "$mode"
done
```

Required host tools include `dotnet`, `file`, `find`, `grep`, `ldd`, `mktemp`,
`readelf`, `timeout`, and `xvfb-run`. The native build and NativeAOT modes also
require the normal platform compiler/linker toolchain, CMake, and Ninja.

## What a Pass Proves

The gate uses an isolated local feed, restore cache, build root, explicit RID,
and package version. It then verifies that:

1. restore, build, and publish succeed with every Jalium dependency coming from
   the generated package closure;
2. all seven native libraries are present in the expected deployment location;
3. the output has the correct self-contained, single-file, or NativeAOT shape;
4. a real X11 window starts under Xvfb with the software renderer;
5. single-file mode starts with a clean `DOTNET_BUNDLE_EXTRACT_BASE_DIR`, then
   extracts and loads exactly the seven Jalium payloads from that directory.

The seven native payloads are:

```text
libjalium.native.core.so       libjalium.native.media.so
libjalium.native.platform.so   libjalium.native.text.so
libjalium.native.media.core.so libjalium.native.vulkan.so
libjalium.native.software.so
```

Each mode writes `build-<mode>/`, `publish-<mode>/`, and `nuget-<mode>/` beneath
`<output-root>`. The launch stage creates `run-<mode>.log`; single-file mode also
creates `bundle-extract-<mode>.*` evidence. A failure during restore, build, or
ELF inspection can occur before the run log exists, so inspect the console first
and then the run log when it was created.

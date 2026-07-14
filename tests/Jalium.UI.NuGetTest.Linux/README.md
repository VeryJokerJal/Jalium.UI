# Jalium.UI Linux NuGet consumer gate

This project intentionally contains only a `PackageReference` to
`Jalium.UI.Linux`; it must not acquire any Jalium assembly through a
`ProjectReference`.

Run it through `eng/linux/test-nuget-consumer.sh` after producing the package
dependency closure with `eng/linux/pack-linux-packages.sh`. The scripts set the
isolated local feed, restore cache and RID, publish a self-contained, trimmed
single-file, or NativeAOT executable, verify all seven native libraries, and
start a real X11 window under Xvfb. The single-file gate embeds the native
libraries with `IncludeNativeLibrariesForSelfExtract`, runs with a clean
`DOTNET_BUNDLE_EXTRACT_BASE_DIR`, and verifies that exactly the seven Jalium
payloads are extracted and loaded from that bundle directory.

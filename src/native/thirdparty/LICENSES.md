# Vendored third-party libraries

Vendored source is pinned and copied verbatim from upstream. Audio/image single-header licenses and the Apache-2.0 SheenBidi license are recorded below; retain each upstream license when distributing its code or linked binaries.

## Inventory

| File | Version | Upstream | License | Pinned commit |
|---|---|---|---|---|
| `miniaudio/miniaudio.h` | v0.11.25 | https://github.com/mackron/miniaudio | Public domain or MIT-0 | `9634bedb5b5a2ca38c1ee7108a9358a4e233f14d` |
| `dr_libs/dr_wav.h` | v0.14.6 | https://github.com/mackron/dr_libs | Public domain or MIT-0 | `47a4f08e777faddf59a8955c4ea84f69f41020d5` |
| `dr_libs/dr_flac.h` | v0.13.4 | https://github.com/mackron/dr_libs | Public domain or MIT-0 | `47a4f08e777faddf59a8955c4ea84f69f41020d5` |
| `minimp3/minimp3.h` | — | https://github.com/lieff/minimp3 | CC0 (public domain) | `7b590fdcfa5a79c033e76eacc05d0c3e4c79f536` |
| `minimp3/minimp3_ex.h` | — | https://github.com/lieff/minimp3 | CC0 (public domain) | `7b590fdcfa5a79c033e76eacc05d0c3e4c79f536` |
| `stb/stb_image.h` | v2.30 | https://github.com/nothings/stb | Public domain or MIT | `31c1ad37456438565541f4919958214b6e762fb4` |
| `stb/stb_vorbis.c` | v1.22 | https://github.com/nothings/stb | Public domain or MIT | `31c1ad37456438565541f4919958214b6e762fb4` |

| `sheenbidi/Headers`, `sheenbidi/Source` | v3.0.0, Unicode 17.0.0 | https://github.com/Tehreer/SheenBidi | Apache-2.0; bundled Unicode data | `cfe430e7375a7845b679adae9d51dac6deaa8858` |

SheenBidi's unmodified license is `sheenbidi/LICENSE`. Unicode data terms are retained in `UNICODE-LICENSE.txt`. The text module compiles the vendored C unity source with private visibility; no external SheenBidi runtime is required. Linux Interop and Android packages include both license files and this inventory under `licenses/`.

To update, re-pull from the corresponding raw URL at a chosen commit:

```powershell
Invoke-WebRequest "https://raw.githubusercontent.com/mackron/miniaudio/<sha>/miniaudio.h" `
    -OutFile src/native/thirdparty/miniaudio/miniaudio.h
Invoke-WebRequest "https://raw.githubusercontent.com/mackron/dr_libs/<sha>/dr_wav.h" `
    -OutFile src/native/thirdparty/dr_libs/dr_wav.h
```

then bump the SHA in the table above.

## Why vendor instead of pulling at build time

- Reproducible builds across cold checkouts and CI runners that may have no internet.
- Lets us pin to a specific upstream commit instead of riding `master`.
- Keeps the build hermetic — no `find_package`/`pkg-config` discovery, no version-skew surprises.

## Implementation TUs (where each `_IMPLEMENTATION` macro lives)

Each single-header is implemented in **exactly one** `.cpp` to avoid duplicate symbols:

| Library | Implementation TU |
|---|---|
| `miniaudio.h` | `src/native/jalium.native.media.core/src/audio/miniaudio_backend.cpp` (defines `MINIAUDIO_IMPLEMENTATION` before include) |
| `dr_wav.h`    | `src/native/jalium.native.media.core/src/audio/decoder_wav.cpp` (defines `DR_WAV_IMPLEMENTATION` before include) |
| `dr_flac.h`   | `src/native/jalium.native.media.core/src/audio/decoder_flac.cpp` (defines `DR_FLAC_IMPLEMENTATION` before include) |
| `minimp3_ex.h`| `src/native/jalium.native.media.core/src/audio/decoder_mp3.cpp` (defines `MINIMP3_IMPLEMENTATION` before include) |
| `stb_image.h` | `src/native/jalium.native.vulkan/src/vulkan_backend.cpp` and `src/native/jalium.native.software/src/software_backend.cpp` (one private implementation in each shared backend) |
| `stb_vorbis.c`| `src/native/jalium.native.media.core/src/audio/decoder_vorbis.cpp` (directly `#include "stb_vorbis.c"`; no separate header) |

Unicode 17 grapheme properties and the official conformance fixture are pinned in
`unicode-grapheme-17/UPSTREAM.json`. `tools/generate-css-grapheme-unicode.py` verifies
the input hashes and generates `unicode_grapheme_data.h`; no Unicode runtime
library is added. Their Unicode License V3 is retained in that directory and is
byte-identical to `UNICODE-LICENSE.txt`, which the native packages already ship.

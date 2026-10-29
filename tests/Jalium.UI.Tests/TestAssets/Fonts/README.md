Ahem.ttf is the Web Platform Tests font, downloaded from
https://raw.githubusercontent.com/web-platform-tests/wpt/master/fonts/Ahem.ttf.
SHA-256: b719ecb31c5b21fc573c03f6421c74ac63c271a5a3ff841e34f9705fb94b8448.

Ahem is public domain, with CC0 applying where public domain dedication is not
recognized. Attribution and its metrics are documented at
https://www.w3.org/Style/CSS/Test/Fonts/Ahem/.

This fixture supplies deterministic glyphs for CSS font loading and layout tests.

WOFF 2 variants are generated from Ahem using fontTools 4.65.0 and Brotli 1.2.0
by `generate-woff2-fixtures.py` (test tooling only). They cover null transforms,
glyf/loca transforms, hmtx with both transformed and untransformed outlines,
shared-table collections, instructions, overlap flags and composite transforms.
Woff2Shapes.ttf is an Ahem-derived test font with three deliberately modified
glyphs, under the same public-domain/CC0 terms.

`generate-font-collections.py` builds `Ahem-Second.ttf`, `Ahem-two-face.ttc` and
`Ahem-two-face.woff2` from the same Ahem source. The second face has PostScript name
`Ahem-Second` and a wider `X` advance. The collection fixtures test named-face
selection and default-face behavior for `@font-face` URL fragments. They inherit
Ahem's public-domain/CC0 terms. The WOFF 2 fixture uses null transforms so face
selection can be checked separately from glyph reconstruction.

`Woff2Wpt/manifest.json` records the original WPT identifiers, assertions and SHA-256
hashes for the selected WOFF 2 corpus. Sources are under
https://github.com/web-platform-tests/wpt/tree/master/css/WOFF2/support;
the upstream WPT test-suite license is at
https://github.com/web-platform-tests/wpt/blob/master/LICENSE.md.

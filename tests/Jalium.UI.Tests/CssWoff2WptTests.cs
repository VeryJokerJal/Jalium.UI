using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

public class CssWoff2WptTests
{
    public static IEnumerable<object[]> Cases()
    {
        string[] accepted =
        [
            "directory-knowntags-001", "header-reserved-001", "header-totalsfntsize-001", "header-totalsfntsize-002",
            "privatedata-noeffect-001", "privatedata-noeffect-002", "tabledata-glyf-bbox-001",
            "tabledata-glyf-origlength-001", "tabledata-glyf-origlength-002", "tabledata-glyf-origlength-003",
            "tabledata-recontruct-loca-001", "tabledata-transform-hmtx-001", "tabledata-transform-hmtx-002",
            "valid-001", "valid-002", "valid-003", "valid-004", "valid-005", "valid-006", "valid-007", "valid-008"
        ];
        string[] rejected =
        [
            "directory-mismatched-tables-001", "header-length-001", "header-length-002", "header-numTables-001", "header-signature-001",
            "tabledata-bad-origlength-loca-001", "tabledata-bad-origlength-loca-002", "tabledata-brotli-001",
            "tabledata-decompressed-length-001", "tabledata-decompressed-length-002", "tabledata-decompressed-length-003", "tabledata-decompressed-length-004",
            "tabledata-extraneous-data-001", "tabledata-glyf-bbox-002", "tabledata-glyf-bbox-003", "tabledata-non-zero-loca-001",
            "tabledata-transform-bad-flag-001", "tabledata-transform-bad-flag-002", "tabledata-transform-hmtx-003", "tabledata-transform-hmtx-004"
        ];
        foreach (var name in accepted) yield return ["css/WOFF2/" + name + ".xht", true];
        foreach (var name in rejected) yield return ["css/WOFF2/" + name + ".xht", false];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    [Trait("WPT", "css/WOFF2")]
    public void OriginalFontData_ObeysTheWptLoadResult(string id, bool accepted)
    {
        var name = Path.GetFileNameWithoutExtension(id);
        var bytes = CssWoff2Tests.Fixture("Woff2Wpt." + name + ".woff2");
        Assert.Equal(accepted, CssFontData.TryPrepare(bytes, "WptFont", out var font));
        if (accepted)
        {
            Assert.Equal(0xb1b0afbAu, CssFontData.Checksum(font));
            if (Environment.GetEnvironmentVariable("JALIUM_WOFF2_EVIDENCE_DIR") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, name + ".ttf"), font);
            }
        }
    }
}

using Jalium.UI.Controls.Helpers;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public sealed class LinuxFontEnumerationTests
{
    [Fact]
    public void BuildSystemFontFamilies_DeduplicatesSortsAndDropsBlankNames()
    {
        ICollection<FontFamily> families = Fonts.BuildSystemFontFamilies(
            ["Zulu Sans", "alpha serif", "ALPHA SERIF", "  ", "Beta Mono"]);

        Assert.Equal(["alpha serif", "Beta Mono", "Zulu Sans"],
            families.Select(static family => family.Source));
    }

    [Fact]
    public void BuildSystemFontFamilies_WhenDiscoveryUnavailable_UsesOnlySafeDefault()
    {
        ICollection<FontFamily> families = Fonts.BuildSystemFontFamilies(null);

        FontFamily family = Assert.Single(families);
        Assert.Equal(FrameworkElement.DefaultFontFamilyName, family.Source);
    }

    [Fact]
    public void PlatformFontEnumeration_ReturnsRealSortedUniqueFamiliesWhenAvailable()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsWindows())
            return;

        string[]? names = FontEnumerationHelper.EnumerateSystemFontFamilies();
        // Managed-only Linux test deployments intentionally omit the native
        // text payload; the native Fontconfig test covers the real ABI there.
        if (names == null)
        {
            Assert.NotEqual("1", Environment.GetEnvironmentVariable(
                "JALIUM_REQUIRE_NATIVE_FONT_ENUMERATION"));
            Assert.True(OperatingSystem.IsLinux());
            return;
        }
        Assert.NotEmpty(names);
        Assert.Equal(names.Length, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());

        string[] sorted = names.ToArray();
        Array.Sort(sorted, StringComparer.CurrentCultureIgnoreCase);
        Assert.Equal(sorted, names);
        Assert.Equal(names,
            Fonts.SystemFontFamilies.Select(static family => family.Source));
    }
}

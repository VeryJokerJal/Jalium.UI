using System.Text;
using Jalium.UI.Controls.Platform;
using RepeatButtonSystemParameters = Jalium.UI.Controls.Primitives.SystemParameters;

namespace Jalium.UI.Tests;

public sealed class LinuxDesktopSettingsTests
{
    [Fact]
    public void ResolveInputSettings_WhenNoProviderKnowsValue_UsesExplicitFallbacks()
    {
        LinuxResolvedInputSettings settings = LinuxDesktopSettings.ResolveInputSettings(new());

        AssertSetting(settings.DoubleClickTime, 500, LinuxDesktopSettingSource.Fallback);
        AssertSetting(settings.DoubleClickDistance, 5, LinuxDesktopSettingSource.Fallback);
        AssertSetting(settings.DragThreshold, 8, LinuxDesktopSettingSource.Fallback);
        AssertSetting(settings.KeyboardDelay, 1, LinuxDesktopSettingSource.Fallback);
        AssertSetting(settings.KeyboardSpeed, 31, LinuxDesktopSettingSource.Fallback);
        AssertSetting(settings.WheelScrollLines, 3, LinuxDesktopSettingSource.Fallback);
        AssertSetting(settings.SwapButtons, false, LinuxDesktopSettingSource.Fallback);
        AssertSetting(settings.MenuShowDelay, 400, LinuxDesktopSettingSource.Fallback);
    }

    [Fact]
    public void ResolveInputSettings_UsesProviderPrecedenceAndRejectsInvalidValues()
    {
        var input = new LinuxInputSettings
        {
            EnvironmentDoubleClickTime = 0,
            XSettingsDoubleClickTime = 420,
            GnomeDoubleClickTime = 460,
            EnvironmentDoubleClickDistance = 0,
            EnvironmentDragThreshold = 20_000,
            XSettingsDragThreshold = 12,
            EnvironmentKeyboardDelayMilliseconds = 60_001,
            GnomeKeyboardDelayMilliseconds = 500,
            EnvironmentKeyboardRepeatRate = 1000,
            GnomeKeyboardRepeatIntervalMilliseconds = 40,
            EnvironmentWheelScrollLines = 101,
            KdeWheelScrollLines = 6,
            EnvironmentSwapButtons = false,
            GnomeSwapButtons = true,
            EnvironmentMenuShowDelay = 60_001,
            XSettingsMenuShowDelay = 225,
        };

        LinuxResolvedInputSettings settings = LinuxDesktopSettings.ResolveInputSettings(input);

        AssertSetting(settings.DoubleClickTime, 420, LinuxDesktopSettingSource.XSettings);
        AssertSetting(settings.DoubleClickDistance, 0, LinuxDesktopSettingSource.Environment);
        AssertSetting(settings.DragThreshold, 12, LinuxDesktopSettingSource.XSettings);
        AssertSetting(settings.KeyboardDelay, 1, LinuxDesktopSettingSource.GSettings);
        AssertSetting(settings.KeyboardSpeed, 25, LinuxDesktopSettingSource.GSettings);
        AssertSetting(settings.WheelScrollLines, 6, LinuxDesktopSettingSource.Kde);
        AssertSetting(settings.SwapButtons, false, LinuxDesktopSettingSource.Environment);
        AssertSetting(settings.MenuShowDelay, 225, LinuxDesktopSettingSource.XSettings);
    }

    [Theory]
    [InlineData(250, 0)]
    [InlineData(500, 1)]
    [InlineData(750, 2)]
    [InlineData(1000, 3)]
    [InlineData(5000, 3)]
    public void KeyboardDelayConversion_MatchesSystemParametersScale(int milliseconds, int expected)
    {
        Assert.Equal(expected, LinuxDesktopSettings.KeyboardDelayFromMilliseconds(milliseconds));
        Assert.Equal(250 + expected * 250,
            RepeatButtonSystemParameters.KeyboardDelayMilliseconds(expected));
    }

    [Theory]
    [InlineData(2.5, 0)]
    [InlineData(30, 31)]
    [InlineData(25, 25)]
    public void KeyboardSpeedConversion_MatchesSystemParametersScale(double rate, int expected)
    {
        int setting = LinuxDesktopSettings.KeyboardSpeedFromRepeatsPerSecond(rate);
        Assert.Equal(expected, setting);
        Assert.InRange(RepeatButtonSystemParameters.KeyboardRepeatIntervalMilliseconds(setting), 1, 400);
    }

    [Theory]
    [InlineData("ubuntu:GNOME", null, null, 1)]
    [InlineData("KDE", "plasma", null, 2)]
    [InlineData("XFCE", null, null, 0)]
    [InlineData(null, null, null, 0)]
    public void DesktopDetection_DoesNotReadUnrelatedProviderStores(
        string? current,
        string? session,
        string? legacy,
        int expected)
    {
        Assert.Equal((LinuxDesktopEnvironmentKind)expected,
            LinuxDesktopSettings.DetectDesktopEnvironment(current, session, legacy));
    }

    [Fact]
    public void KdeConfigParser_UsesLastValueInExactGroup()
    {
        const string content = """
            [General]
            WheelScrollLines=2
            [KDE]
            WheelScrollLines=4
            WheelScrollLines=7
            [KDE Extra]
            WheelScrollLines=9
            """;

        Assert.True(LinuxDesktopSettings.TryReadKdeConfigValue(
            content, "KDE", "WheelScrollLines", out string value));
        Assert.Equal("7", value);
    }

    [Theory]
    [InlineData("Cantarell 11", "Cantarell", 14.666666666666666, 400, false)]
    [InlineData("Noto Sans Extra-Bold Italic 10.5", "Noto Sans", 14, 800, true)]
    [InlineData("Ubuntu Sans Semi Bold 12pt", "Ubuntu Sans", 16, 600, false)]
    [InlineData("Inter Medium 13px", "Inter", 13, 500, false)]
    public void PangoFontDescriptionParser_ReadsFamilySizeWeightAndStyle(
        string text,
        string family,
        double size,
        int weight,
        bool italic)
    {
        Assert.True(LinuxDesktopSettings.TryParsePangoFontDescription(
            text, out LinuxSystemFontDescription description));
        Assert.Equal(family, description.Family);
        Assert.Equal(size, description.Size, precision: 8);
        Assert.Equal(weight, description.Weight);
        Assert.Equal(italic, description.Italic);
    }

    [Theory]
    [InlineData("Noto Sans,10,-1,5,50,0,0,0,0,0", "Noto Sans", 13.333333333333334, 400, false)]
    [InlineData("Noto Sans,11,-1,5,75,1,0,0,0,0", "Noto Sans", 14.666666666666666, 700, true)]
    [InlineData("Inter,10.5,-1,5,600,0,0,0,0,0", "Inter", 14, 600, false)]
    [InlineData("Pixel Sans,-1,13,5,400,0,0,0,0,0", "Pixel Sans", 13, 400, false)]
    public void KdeFontDescriptionParser_HandlesQt5AndQt6Weights(
        string text,
        string family,
        double size,
        int weight,
        bool italic)
    {
        Assert.True(LinuxDesktopSettings.TryParseKdeFontDescription(
            text, out LinuxSystemFontDescription description));
        Assert.Equal(family, description.Family);
        Assert.Equal(size, description.Size, precision: 8);
        Assert.Equal(weight, description.Weight);
        Assert.Equal(italic, description.Italic);
    }

    [Fact]
    public void SystemFontResolver_EnvironmentOverrideWinsAndRolesRemainIndependent()
    {
        var environment = new LinuxSystemFontDescription("Override Sans", 13, 500, false);
        var caption = new LinuxSystemFontDescription("Override Title", 14, 700, true);
        var candidates = new LinuxSystemFontCandidates
        {
            EnvironmentGeneral = environment,
            EnvironmentCaption = caption,
            XSettingsGeneral = new("Gtk Sans", 10, 400, false),
            GnomeTitlebarUsesSystemFont = false,
            GnomeTitlebar = new("Gnome Title", 11, 700, false),
            KdeMenu = new("KDE Menu", 9, 400, false),
        };

        LinuxSystemFontSettings settings = LinuxDesktopSettings.ResolveSystemFontSettings(
            candidates, "Fallback Sans");

        AssertSetting(settings.Caption, caption, LinuxDesktopSettingSource.Environment);
        AssertSetting(settings.Menu, environment, LinuxDesktopSettingSource.Environment);
        AssertSetting(settings.Message, environment, LinuxDesktopSettingSource.Environment);
        AssertSetting(settings.Icon, environment, LinuxDesktopSettingSource.Environment);
    }

    [Fact]
    public void SystemFontResolver_GnomeUsesCustomTitlebarOnlyWhenEnabledByDesktop()
    {
        var general = new LinuxSystemFontDescription("GNOME Sans", 14, 400, false);
        var title = new LinuxSystemFontDescription("GNOME Title", 15, 700, true);
        LinuxSystemFontSettings custom = LinuxDesktopSettings.ResolveSystemFontSettings(
            new LinuxSystemFontCandidates
            {
                GnomeGeneral = general,
                GnomeTitlebarUsesSystemFont = false,
                GnomeTitlebar = title,
            },
            "Fallback Sans");
        LinuxSystemFontSettings system = LinuxDesktopSettings.ResolveSystemFontSettings(
            new LinuxSystemFontCandidates
            {
                GnomeGeneral = general,
                GnomeTitlebarUsesSystemFont = true,
                GnomeTitlebar = title,
            },
            "Fallback Sans");

        AssertSetting(custom.Caption, title, LinuxDesktopSettingSource.GSettings);
        AssertSetting(custom.Message, general, LinuxDesktopSettingSource.GSettings);
        AssertSetting(system.Caption, general, LinuxDesktopSettingSource.GSettings);
    }

    [Fact]
    public void SystemFontResolver_KdeUsesRoleSpecificFonts()
    {
        var general = new LinuxSystemFontDescription("KDE Sans", 14, 400, false);
        var title = new LinuxSystemFontDescription("KDE Title", 15, 700, false);
        var menu = new LinuxSystemFontDescription("KDE Menu", 13, 400, false);
        var status = new LinuxSystemFontDescription("KDE Taskbar", 12, 500, false);
        LinuxSystemFontSettings settings = LinuxDesktopSettings.ResolveSystemFontSettings(
            new LinuxSystemFontCandidates
            {
                KdeGeneral = general,
                KdeCaption = title,
                KdeMenu = menu,
                KdeStatus = status,
            },
            "Fallback Sans");

        AssertSetting(settings.Caption, title, LinuxDesktopSettingSource.Kde);
        AssertSetting(settings.Menu, menu, LinuxDesktopSettingSource.Kde);
        AssertSetting(settings.Status, status, LinuxDesktopSettingSource.Kde);
        AssertSetting(settings.Message, general, LinuxDesktopSettingSource.Kde);
    }

    [Fact]
    public void SystemFontResolver_FallbackIsExplicitAndPreservesRoleSizes()
    {
        LinuxSystemFontSettings settings = LinuxDesktopSettings.ResolveSystemFontSettings(
            new(), "Fallback Sans");

        AssertSetting(settings.Caption,
            new LinuxSystemFontDescription("Fallback Sans", 14, 400, false),
            LinuxDesktopSettingSource.Fallback);
        AssertSetting(settings.SmallCaption,
            new LinuxSystemFontDescription("Fallback Sans", 11, 400, false),
            LinuxDesktopSettingSource.Fallback);
        AssertSetting(settings.Icon,
            new LinuxSystemFontDescription("Fallback Sans", 9, 400, false),
            LinuxDesktopSettingSource.Fallback);
    }

    [Fact]
    public void SystemFonts_LinuxEnvironmentOverride_ReachesPublicProperties()
    {
        if (!OperatingSystem.IsLinux() ||
            Environment.GetEnvironmentVariable("JALIUM_REQUIRE_LINUX_SYSTEM_FONT_OVERRIDE") != "1")
        {
            return;
        }

        Assert.Equal("Integration Sans", global::Jalium.UI.SystemFonts.MessageFontFamily.Source);
        Assert.Equal(15.5, global::Jalium.UI.SystemFonts.MessageFontSize);
        Assert.Equal(700, global::Jalium.UI.SystemFonts.MessageFontWeight.ToOpenTypeWeight());
        Assert.Equal(global::Jalium.UI.FontStyles.Italic,
            global::Jalium.UI.SystemFonts.MessageFontStyle);
    }

    [Fact]
    public void SystemParameters_LinuxEnvironmentOverrides_ReachPublicProperties()
    {
        if (!OperatingSystem.IsLinux() ||
            Environment.GetEnvironmentVariable("JALIUM_REQUIRE_LINUX_INPUT_OVERRIDES") != "1")
        {
            return;
        }

        Assert.Equal(612, global::Jalium.UI.SystemParameters.DoubleClickTime);
        Assert.Equal(9, global::Jalium.UI.SystemParameters.MouseDoubleClickDistance);
        Assert.Equal(11, global::Jalium.UI.SystemParameters.MinimumHorizontalDragDistance);
        Assert.Equal(11, global::Jalium.UI.SystemParameters.MinimumVerticalDragDistance);
        Assert.Equal(2, global::Jalium.UI.SystemParameters.KeyboardDelay);
        Assert.Equal(20, global::Jalium.UI.SystemParameters.KeyboardSpeed);
        Assert.Equal(7, global::Jalium.UI.SystemParameters.WheelScrollLines);
        Assert.True(global::Jalium.UI.SystemParameters.SwapButtons);
        Assert.Equal(123, global::Jalium.UI.SystemParameters.MenuShowDelay);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void XSettingsParser_ReadsProtocolByteOrderAndTypedValues(bool littleEndian)
    {
        byte[] property = BuildXSettings(
            littleEndian,
            (LinuxXSettingKind.Integer, "Net/DoubleClickTime", (object)425),
            (LinuxXSettingKind.String, "Gtk/FontName", "Cantarell 11"),
            (LinuxXSettingKind.Color, "Gtk/AccentColor", (object)new ushort[] { 1, 2, 3, 65535 }));

        Assert.True(LinuxXSettings.TryParse(property, out var settings));
        Assert.True(LinuxXSettings.TryGetInteger(settings, "Net/DoubleClickTime", out int time));
        Assert.Equal(425, time);
        Assert.True(LinuxXSettings.TryGetString(settings, "Gtk/FontName", out string font));
        Assert.Equal("Cantarell 11", font);
        Assert.Equal(LinuxXSettingKind.Color, settings["Gtk/AccentColor"].Kind);
    }

    [Fact]
    public void XSettingsParser_RejectsInvalidByteOrderNamesDuplicatesAndTruncation()
    {
        byte[] valid = BuildXSettings(
            true,
            (LinuxXSettingKind.Integer, "Net/DoubleClickTime", (object)425));

        byte[] invalidOrder = valid.ToArray();
        invalidOrder[0] = (byte)'l';
        Assert.False(LinuxXSettings.TryParse(invalidOrder, out _));
        Assert.False(LinuxXSettings.TryParse(valid.AsSpan(0, valid.Length - 1), out _));

        byte[] invalidName = BuildXSettings(
            true,
            (LinuxXSettingKind.Integer, "Net//Broken", (object)1));
        Assert.False(LinuxXSettings.TryParse(invalidName, out _));

        byte[] duplicate = BuildXSettings(
            true,
            (LinuxXSettingKind.Integer, "Net/Value", (object)1),
            (LinuxXSettingKind.Integer, "Net/Value", (object)2));
        Assert.False(LinuxXSettings.TryParse(duplicate, out _));
    }

    private static void AssertSetting<T>(
        LinuxDesktopSetting<T> setting,
        T expected,
        LinuxDesktopSettingSource source)
    {
        Assert.Equal(expected, setting.Value);
        Assert.Equal(source, setting.Source);
    }

    private static byte[] BuildXSettings(
        bool littleEndian,
        params (LinuxXSettingKind Kind, string Name, object Value)[] entries)
    {
        var bytes = new List<byte> { littleEndian ? (byte)1 : (byte)0, 0, 0, 0 };
        WriteUInt32(bytes, 17, littleEndian);
        WriteUInt32(bytes, (uint)entries.Length, littleEndian);
        uint serial = 20;
        foreach ((LinuxXSettingKind kind, string name, object value) in entries)
        {
            byte[] nameBytes = Encoding.ASCII.GetBytes(name);
            bytes.Add((byte)kind);
            bytes.Add(0);
            WriteUInt16(bytes, checked((ushort)nameBytes.Length), littleEndian);
            bytes.AddRange(nameBytes);
            Align4(bytes);
            WriteUInt32(bytes, serial++, littleEndian);

            switch (kind)
            {
                case LinuxXSettingKind.Integer:
                    WriteUInt32(bytes, unchecked((uint)(int)value), littleEndian);
                    break;
                case LinuxXSettingKind.String:
                    byte[] text = Encoding.UTF8.GetBytes((string)value);
                    WriteUInt32(bytes, (uint)text.Length, littleEndian);
                    bytes.AddRange(text);
                    Align4(bytes);
                    break;
                case LinuxXSettingKind.Color:
                    foreach (ushort channel in (ushort[])value)
                        WriteUInt16(bytes, channel, littleEndian);
                    break;
            }
        }
        return bytes.ToArray();
    }

    private static void Align4(List<byte> bytes)
    {
        while ((bytes.Count & 3) != 0)
            bytes.Add(0);
    }

    private static void WriteUInt16(List<byte> bytes, ushort value, bool littleEndian)
    {
        if (littleEndian)
        {
            bytes.Add((byte)value);
            bytes.Add((byte)(value >> 8));
        }
        else
        {
            bytes.Add((byte)(value >> 8));
            bytes.Add((byte)value);
        }
    }

    private static void WriteUInt32(List<byte> bytes, uint value, bool littleEndian)
    {
        if (littleEndian)
        {
            bytes.Add((byte)value);
            bytes.Add((byte)(value >> 8));
            bytes.Add((byte)(value >> 16));
            bytes.Add((byte)(value >> 24));
        }
        else
        {
            bytes.Add((byte)(value >> 24));
            bytes.Add((byte)(value >> 16));
            bytes.Add((byte)(value >> 8));
            bytes.Add((byte)value);
        }
    }
}

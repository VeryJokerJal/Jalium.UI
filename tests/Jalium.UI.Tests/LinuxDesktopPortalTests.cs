using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Platform;
using Jalium.UI.Controls.Printing;
using Jalium.UI.Controls.Shell;

namespace Jalium.UI.Tests;

public sealed class LinuxDesktopPortalTests
{
    [Fact]
    public void FileChooserOptions_IncludePortalRequiredValuesAndFilters()
    {
        var options = new LinuxPortalFileChooserOptions(
            "Open 'assets'",
            Save: false,
            Multiple: true,
            Directory: false,
            CurrentFolder: "/tmp/My Files",
            CurrentName: null,
            Filters:
            [
                ("Images", "*.png;*.jpg"),
                ("All files", "*")
            ],
            FilterIndex: 2);

        var value = LinuxDesktopPortal.BuildFileChooserOptions(options, "jalium_test");

        Assert.Contains("'handle_token': <'jalium_test'>", value, StringComparison.Ordinal);
        Assert.Contains("'multiple': <true>", value, StringComparison.Ordinal);
        Assert.Contains("'directory': <false>", value, StringComparison.Ordinal);
        Assert.Contains("'current_folder': <[byte 47, 116, 109, 112", value, StringComparison.Ordinal);
        Assert.Contains("('Images', [(uint32 0, '*.png'), (uint32 0, '*.jpg')])", value,
            StringComparison.Ordinal);
        Assert.Contains("'current_filter': <('All files', [(uint32 0, '*')])>", value,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SaveFileOptions_IncludeCurrentNameAndEscapeStrings()
    {
        var options = new LinuxPortalFileChooserOptions(
            "Save",
            Save: true,
            Multiple: false,
            Directory: false,
            CurrentFolder: null,
            CurrentName: "report's draft.txt",
            Filters: Array.Empty<(string Name, string Pattern)>());

        var value = LinuxDesktopPortal.BuildFileChooserOptions(options, "token's");

        Assert.Contains("'handle_token': <'token\\'s'>", value, StringComparison.Ordinal);
        Assert.Contains("'current_name': <'report\\'s draft.txt'>", value, StringComparison.Ordinal);
        Assert.DoesNotContain("'directory'", value, StringComparison.Ordinal);
    }

    [Fact]
    public void ResponseParser_ParsesSuccessAndDecodesLocalFileUris()
    {
        const string path = "/org/freedesktop/portal/desktop/request/1_42/jalium_test";
        var message = path +
                      ": org.freedesktop.portal.Request.Response " +
                      "(uint32 0, {'uris': <['file:///tmp/hello%20world.txt', " +
                      "'file:///home/user/%E6%B5%8B%E8%AF%95.txt']>})";

        Assert.True(LinuxDesktopPortal.TryParseResponse(message, path, out var response));
        Assert.Equal(LinuxPortalResponseStatus.Success, response.Status);

        var values = LinuxDesktopPortal.ConvertFileUrisToPaths(response.Values);
        Assert.Equal(["/tmp/hello world.txt", "/home/user/测试.txt"], values);
    }

    [Theory]
    [InlineData(1, (int)LinuxPortalResponseStatus.Cancelled)]
    [InlineData(2, (int)LinuxPortalResponseStatus.Failed)]
    public void ResponseParser_MapsCancelAndPortalFailure(
        uint responseCode,
        int expectedStatusValue)
    {
        const string path = "/org/freedesktop/portal/desktop/request/1_42/jalium_test";
        var message = $"{path}: org.freedesktop.portal.Request.Response " +
                      $"(uint32 {responseCode}, @a{{sv}} {{}})";

        Assert.True(LinuxDesktopPortal.TryParseResponse(message, path, out var response));
        Assert.Equal((LinuxPortalResponseStatus)expectedStatusValue, response.Status);
    }

    [Fact]
    public void ResponseParser_IgnoresAnotherApplicationsRequest()
    {
        const string expected = "/org/freedesktop/portal/desktop/request/1_42/ours";
        const string message =
            "/org/freedesktop/portal/desktop/request/1_42/theirs: " +
            "org.freedesktop.portal.Request.Response (uint32 0, @a{sv} {})";

        Assert.False(LinuxDesktopPortal.TryParseResponse(message, expected, out _));
    }

    [Fact]
    public void RequestPathParser_ExtractsObjectPathFromGdbusResult()
    {
        const string output =
            "(objectpath '/org/freedesktop/portal/desktop/request/1_99/jalium_123',)";

        Assert.True(LinuxDesktopPortal.TryParseRequestPath(output, out var path));
        Assert.Equal("/org/freedesktop/portal/desktop/request/1_99/jalium_123", path);
    }

    [Fact]
    public void FileUriParser_RejectsRemoteAndNonFileUris()
    {
        var paths = LinuxDesktopPortal.ConvertFileUrisToPaths(
        [
            "https://example.test/file.txt",
            "file://remote-host/share/file.txt",
            "file:///tmp/ok.txt"
        ]);

        Assert.Equal(["/tmp/ok.txt"], paths);
    }

    [Theory]
    [InlineData("https://example.test/docs", "https://example.test/docs")]
    [InlineData("/tmp/a file.txt", "file:///tmp/a%20file.txt")]
    public void OpenUriTarget_NormalizesAbsoluteUriAndLinuxPath(string target, string expected)
    {
        Assert.True(LinuxDesktopPortal.TryNormalizeOpenUriTarget(target, out var uri));
        Assert.Equal(expected, uri);
    }

    [Fact]
    public void MissingOwner_UsesPortalSupportedEmptyParent()
    {
        Assert.Equal(string.Empty, LinuxDesktopPortal.BuildParentWindow(0));
    }

    [Fact]
    public void FileChooser_OnNonLinux_HasDeterministicUnavailableFallback()
    {
        if (OperatingSystem.IsLinux())
            return;

        var response = LinuxDesktopPortal.ShowFileChooser(
            0,
            new LinuxPortalFileChooserOptions(
                "Open",
                Save: false,
                Multiple: false,
                Directory: false,
                CurrentFolder: null,
                CurrentName: null,
                Filters: Array.Empty<(string Name, string Pattern)>()));

        Assert.Equal(LinuxPortalResponseStatus.Unavailable, response.Status);
        Assert.NotNull(response.Error);
    }

    [Fact]
    public void FileChooserOptions_ProduceStructurallyBalancedGVariantText()
    {
        // Substring assertions cannot catch container-nesting mistakes such as
        // "<[…>]" (which once shipped and broke every filtered dialog on Linux
        // because g_variant_parse rejects the whole options dictionary), so we
        // validate the full text structurally.
        var options = new LinuxPortalFileChooserOptions(
            "Open",
            Save: false,
            Multiple: true,
            Directory: false,
            CurrentFolder: "/tmp/data",
            CurrentName: null,
            Filters:
            [
                ("Images", "*.png;*.jpg"),
                ("All files", "*")
            ],
            FilterIndex: 1);

        var value = LinuxDesktopPortal.BuildFileChooserOptions(options, "jalium_balanced");

        AssertBalancedGVariantText(value);
        Assert.Contains(
            "'filters': <[('Images', [(uint32 0, '*.png'), (uint32 0, '*.jpg')]), " +
            "('All files', [(uint32 0, '*')])]>",
            value,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PrintParameters_ContainTypedDictionariesHandleAndTokens()
    {
        var prepare = LinuxDesktopPortal.BuildPreparePrintParameters(
            0,
            "Print 'report'",
            "jalium_prepare");
        var submit = LinuxDesktopPortal.BuildSubmitPrintParameters(
            0,
            "Report",
            "jalium_submit",
            42);

        Assert.Contains("@a{sv} {}, @a{sv} {}", prepare, StringComparison.Ordinal);
        Assert.Contains("'handle_token': <'jalium_prepare'>", prepare, StringComparison.Ordinal);
        Assert.Contains("'modal': <true>", prepare, StringComparison.Ordinal);
        Assert.Contains("@h 0", submit, StringComparison.Ordinal);
        Assert.Contains("'token': <uint32 42>", submit, StringComparison.Ordinal);
        AssertBalancedGVariantText(prepare);
        AssertBalancedGVariantText(submit);
    }

    [Fact]
    public void PrintTokenParser_ExtractsPreparePrintToken()
    {
        const string response =
            "/org/freedesktop/portal/desktop/request/1_42/jalium_print: " +
            "org.freedesktop.portal.Request.Response " +
            "(uint32 0, {'settings': <@a{sv} {}>, 'token': <uint32 913>})";

        Assert.True(LinuxDesktopPortal.TryParsePrintToken(response, out var token));
        Assert.Equal(913u, token);
    }

    [Fact]
    public void LinuxPdfWriter_ProducesSeekableSinglePagePdf()
    {
        var page = new LinuxPdfRasterPage(
            PixelWidth: 2,
            PixelHeight: 1,
            WidthPoints: 144,
            HeightPoints: 72,
            RgbPixels: [255, 0, 0, 0, 0, 255]);
        using var stream = new MemoryStream();

        LinuxPdfDocumentWriter.Write(stream, [page]);

        var text = System.Text.Encoding.Latin1.GetString(stream.ToArray());
        Assert.StartsWith("%PDF-1.4", text, StringComparison.Ordinal);
        Assert.Contains("/Type /Page", text, StringComparison.Ordinal);
        Assert.Contains("/Subtype /Image", text, StringComparison.Ordinal);
        Assert.Contains("xref", text, StringComparison.Ordinal);
        Assert.EndsWith("%%EOF\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void LinuxPdfWriter_CompositesBgraAlphaOntoWhite()
    {
        var rgb = LinuxPdfDocumentWriter.CompositeBgraOnWhite(
        [
            0, 0, 255, 255,
            0, 0, 0, 0
        ]);

        Assert.Equal([255, 0, 0, 255, 255, 255], rgb);
    }

    [Fact]
    public void LinuxFileAssociationBuilders_ProduceFreedesktopArtifacts()
    {
        var mimeType = FileRegistrationHelper.BuildLinuxMimeType("com.example.JaliumViewer");
        var desktop = FileRegistrationHelper.BuildLinuxDesktopEntry(
            "/opt/Jalium Viewer/jalium-viewer",
            "Jalium Viewer",
            mimeType,
            "/opt/Jalium Viewer/viewer.png");
        var mimePackage = FileRegistrationHelper.BuildLinuxMimePackage(
            ".jalium",
            mimeType,
            "Jalium Viewer");

        Assert.Equal("application/x-com.example.jaliumviewer", mimeType);
        Assert.Contains("Type=Application", desktop, StringComparison.Ordinal);
        Assert.Contains("Exec=\"/opt/Jalium Viewer/jalium-viewer\" %f", desktop, StringComparison.Ordinal);
        Assert.Contains($"MimeType={mimeType};", desktop, StringComparison.Ordinal);
        Assert.Contains("<glob pattern=\"*.jalium\"/>", mimePackage, StringComparison.Ordinal);
        Assert.Contains($"type=\"{mimeType}\"", mimePackage, StringComparison.Ordinal);
    }

    [Fact]
    public void LinuxDesktopEntry_EscapesPercentFieldCodesInExecutablePath()
    {
        string desktop = FileRegistrationHelper.BuildLinuxDesktopEntry(
            "/opt/Jalium%20Viewer/viewer",
            "Jalium Viewer",
            "application/x-jalium-viewer",
            iconPath: null);

        Assert.Contains("Exec=\"/opt/Jalium%%20Viewer/viewer\" %f", desktop, StringComparison.Ordinal);
    }

    [Fact]
    public void LinuxDesktopEntry_FrameworkDependentLaunch_IncludesManagedEntryAssembly()
    {
        string desktop = FileRegistrationHelper.BuildLinuxDesktopEntry(
            "/usr/bin/dotnet",
            "/opt/Jalium Viewer/Jalium.Viewer.dll",
            "Jalium Viewer",
            "application/x-jalium-viewer",
            iconPath: null);

        Assert.Contains(
            "Exec=\"/usr/bin/dotnet\" \"/opt/Jalium Viewer/Jalium.Viewer.dll\" %f",
            desktop,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("400", 400)]
    [InlineData("uint32 1200", 1200)]
    [InlineData("  32  ", 32)]
    public void LinuxDesktopSettings_ParsesGSettingsIntegers(string output, int expected)
    {
        Assert.True(LinuxDesktopSettings.TryParseGSettingsInteger(output, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("'Adwaita-dark'", "Adwaita-dark")]
    [InlineData("\"HighContrast\"", "HighContrast")]
    public void LinuxDesktopSettings_UnquotesThemeNames(string output, string expected)
    {
        Assert.Equal(expected, LinuxDesktopSettings.UnquoteGSettingsString(output));
    }

    [Theory]
    [InlineData("('org.freedesktop.appearance', 'contrast', <uint32 1>)", "org.freedesktop.appearance", "contrast", 1u)]
    [InlineData("('org.freedesktop.appearance', 'reduced-motion', <<uint32 0>>)", "org.freedesktop.appearance", "reduced-motion", 0u)]
    public void PortalSettingChangedParser_ExtractsNamespaceKeyAndValue(
        string message,
        string expectedNamespace,
        string expectedKey,
        uint expectedValue)
    {
        Assert.True(LinuxDesktopPortal.TryParseSettingChanged(
            message, out string settingsNamespace, out string key, out uint? value));
        Assert.Equal(expectedNamespace, settingsNamespace);
        Assert.Equal(expectedKey, key);
        Assert.Equal(expectedValue, value);
    }

    [Theory]
    [InlineData("(true,)", true)]
    [InlineData("(false,)", false)]
    [InlineData(" true ", true)]
    public void LogindPrepareForShutdownParser_ExtractsBoolean(string message, bool expected)
    {
        Assert.True(LinuxSessionMonitor.TryParsePrepareForShutdown(message, out bool actual));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void LogindInhibitParameters_RequestShutdownDelay()
    {
        string parameters = LinuxSessionMonitor.BuildInhibitParameters();
        Assert.Contains("'shutdown'", parameters, StringComparison.Ordinal);
        Assert.Contains("'delay'", parameters, StringComparison.Ordinal);
        AssertBalancedGVariantText(parameters);
    }

    [Fact]
    public void PlatformSystemSettingsNotification_ReachesWindowEvent()
    {
        var window = new Window();
        int raised = 0;
        window.SystemSettingsChanged += (_, _) => raised++;

        window.RaisePlatformSystemSettingsChanged();

        Assert.Equal(1, raised);
    }

    private static void AssertBalancedGVariantText(string text)
    {
        var stack = new Stack<char>();
        var inString = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inString)
            {
                if (c == '\\')
                {
                    i++;
                    continue;
                }

                if (c == '\'')
                    inString = false;
                continue;
            }

            switch (c)
            {
                case '\'':
                    inString = true;
                    break;
                case '<' or '[' or '(' or '{':
                    stack.Push(c);
                    break;
                case '>':
                    Assert.True(stack.Count > 0 && stack.Pop() == '<', $"unbalanced '>' at index {i} in: {text}");
                    break;
                case ']':
                    Assert.True(stack.Count > 0 && stack.Pop() == '[', $"unbalanced ']' at index {i} in: {text}");
                    break;
                case ')':
                    Assert.True(stack.Count > 0 && stack.Pop() == '(', $"unbalanced ')' at index {i} in: {text}");
                    break;
                case '}':
                    Assert.True(stack.Count > 0 && stack.Pop() == '{', $"unbalanced '}}' at index {i} in: {text}");
                    break;
            }
        }

        Assert.False(inString, "unterminated string literal in GVariant text");
        Assert.True(stack.Count == 0, $"unclosed containers remain: {string.Join(", ", stack)}");
    }
}

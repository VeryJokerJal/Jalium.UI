using Jalium.UI.Controls.Platform;

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
}

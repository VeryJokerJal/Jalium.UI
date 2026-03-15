using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

public class WindowTaskbarRelaunchTests
{
    [Fact]
    public void BuildTaskbarRelaunchInfo_ShouldReuseExecutable_WhenRunningViaAppHost()
    {
        var info = Window.BuildTaskbarRelaunchInfo(
            @"C:\Program Files\Jalium\Jalium.exe",
            [
                @"C:\Program Files\Jalium\Jalium.exe",
                "--profile",
                "Daily Build"
            ],
            "Ignored Window Title");

        Assert.Equal("App.Jalium", info.AppUserModelId);
        Assert.Equal("\"C:\\Program Files\\Jalium\\Jalium.exe\" --profile \"Daily Build\"", info.Command);
        Assert.Equal("Jalium", info.DisplayName);
        Assert.Equal(@"C:\Program Files\Jalium\Jalium.exe,0", info.IconResource);
    }

    [Fact]
    public void BuildTaskbarRelaunchInfo_ShouldIncludeManagedEntryPoint_WhenRunningViaDotnetHost()
    {
        var info = Window.BuildTaskbarRelaunchInfo(
            @"C:\Program Files\dotnet\dotnet.exe",
            [
                @"D:\Apps\Demo App\Demo.dll",
                "--workspace",
                @"C:\Temp Folder\Session\"
            ],
            "Demo Window");

        Assert.Equal("App.Demo", info.AppUserModelId);
        Assert.Equal(
            "\"C:\\Program Files\\dotnet\\dotnet.exe\" \"D:\\Apps\\Demo App\\Demo.dll\" --workspace \"C:\\Temp Folder\\Session\\\\\"",
            info.Command);
        Assert.Equal("Demo", info.DisplayName);
        Assert.Null(info.IconResource);
    }

    [Fact]
    public void QuoteCommandLineArgument_ShouldEscapeTrailingBackslashesInsideQuotedValues()
    {
        var quoted = Window.QuoteCommandLineArgument(@"C:\Temp Folder\Session\");

        Assert.Equal("\"C:\\Temp Folder\\Session\\\\\"", quoted);
    }
}

using Jalium.UI;

namespace Jalium.UI.Tests;

public class SystemParametersEnvironmentTests
{
    [Fact]
    public void CurrentEnvironment_OnWindowsTarget_ShouldIncludeWindows()
    {
        Assert.True(SystemParameters.IsWindows);
        Assert.True((SystemParameters.CurrentEnvironment & SystemEnvironmentKind.Windows) != 0);
        Assert.False(SystemParameters.IsBrowser);
        Assert.False(SystemParameters.IsFreeBSD);
        Assert.False(SystemParameters.IsMacOS);
        Assert.False(SystemParameters.IsMacCatalyst);
        Assert.False(SystemParameters.IsIOS);
        Assert.False(SystemParameters.IsAndroid);
        Assert.False(SystemParameters.IsTvOS);
        Assert.False(SystemParameters.IsWasi);
        Assert.False(SystemParameters.IsWatchOS);
    }

    [Theory]
    [InlineData(SystemEnvironmentKind.Windows, nameof(SystemParameters.IsWindows))]
    [InlineData(SystemEnvironmentKind.Browser, nameof(SystemParameters.IsBrowser))]
    [InlineData(SystemEnvironmentKind.FreeBSD, nameof(SystemParameters.IsFreeBSD))]
    [InlineData(SystemEnvironmentKind.MacOS, nameof(SystemParameters.IsMacOS))]
    [InlineData(SystemEnvironmentKind.MacCatalyst, nameof(SystemParameters.IsMacCatalyst))]
    [InlineData(SystemEnvironmentKind.IOS, nameof(SystemParameters.IsIOS))]
    [InlineData(SystemEnvironmentKind.Android, nameof(SystemParameters.IsAndroid))]
    [InlineData(SystemEnvironmentKind.Linux, nameof(SystemParameters.IsLinux))]
    [InlineData(SystemEnvironmentKind.TvOS, nameof(SystemParameters.IsTvOS))]
    [InlineData(SystemEnvironmentKind.Wasi, nameof(SystemParameters.IsWasi))]
    [InlineData(SystemEnvironmentKind.WatchOS, nameof(SystemParameters.IsWatchOS))]
    [InlineData(SystemEnvironmentKind.VirtualMachine, nameof(SystemParameters.IsVirtualMachine))]
    public void CurrentEnvironment_Flag_ShouldMatchConvenienceProperty(
        SystemEnvironmentKind environment,
        string propertyName)
    {
        var hasFlag = (SystemParameters.CurrentEnvironment & environment) != 0;
        var propertyValue = propertyName switch
        {
            nameof(SystemParameters.IsWindows) => SystemParameters.IsWindows,
            nameof(SystemParameters.IsBrowser) => SystemParameters.IsBrowser,
            nameof(SystemParameters.IsFreeBSD) => SystemParameters.IsFreeBSD,
            nameof(SystemParameters.IsMacOS) => SystemParameters.IsMacOS,
            nameof(SystemParameters.IsMacCatalyst) => SystemParameters.IsMacCatalyst,
            nameof(SystemParameters.IsIOS) => SystemParameters.IsIOS,
            nameof(SystemParameters.IsAndroid) => SystemParameters.IsAndroid,
            nameof(SystemParameters.IsLinux) => SystemParameters.IsLinux,
            nameof(SystemParameters.IsTvOS) => SystemParameters.IsTvOS,
            nameof(SystemParameters.IsWasi) => SystemParameters.IsWasi,
            nameof(SystemParameters.IsWatchOS) => SystemParameters.IsWatchOS,
            nameof(SystemParameters.IsVirtualMachine) => SystemParameters.IsVirtualMachine,
            _ => throw new ArgumentOutOfRangeException(nameof(propertyName), propertyName, null)
        };

        Assert.Equal(hasFlag, propertyValue);
    }

    [Fact]
    public void CurrentEnvironment_VirtualMachineFlag_ShouldMatchConvenienceProperty()
    {
        var hasVirtualMachineFlag =
            (SystemParameters.CurrentEnvironment & SystemEnvironmentKind.VirtualMachine) != 0;

        Assert.Equal(hasVirtualMachineFlag, SystemParameters.IsVirtualMachine);
    }

    [Theory]
    [InlineData("Microsoft Corporation Virtual Machine", true)]
    [InlineData("VMware, Inc.", true)]
    [InlineData("VirtualBox", true)]
    [InlineData("QEMU", true)]
    [InlineData("Dell Inc.", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ContainsVirtualMachineSignature_ShouldClassifyKnownValues(string? value, bool expected)
    {
        Assert.Equal(expected, SystemParameters.ContainsVirtualMachineSignature(value));
    }
}

using System.Reflection;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Tests;

public sealed class ToggleButtonWpfParityTests
{
    [Fact]
    public void OnToggleIsProtectedInternalVirtualAndRetainsStateCycle()
    {
        var method = typeof(ToggleButton).GetMethod(
            "OnToggle",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        Assert.NotNull(method);
        Assert.True(method!.IsFamilyOrAssembly);
        Assert.True(method.IsVirtual);
        Assert.False(method.IsFinal);

        var toggle = new ProbeToggleButton();
        toggle.Toggle();
        Assert.True(toggle.IsChecked);
        toggle.Toggle();
        Assert.False(toggle.IsChecked);

        toggle.IsThreeState = true;
        toggle.Toggle();
        Assert.True(toggle.IsChecked);
        toggle.Toggle();
        Assert.Null(toggle.IsChecked);
    }

    private sealed class ProbeToggleButton : ToggleButton
    {
        public void Toggle() => OnToggle();
    }
}

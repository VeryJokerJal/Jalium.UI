using System.Reflection;
using Jalium.UI.Controls.Primitives;

namespace Jalium.UI.Tests;

public sealed class ButtonBasePressedParityTests
{
    [Fact]
    public void IsPressedHasProtectedSetterAndChangeHookReceivesDependencyPropertyArgs()
    {
        var property = typeof(ButtonBase).GetProperty(
            nameof(ButtonBase.IsPressed),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)!;
        Assert.True(property.GetMethod!.IsPublic);
        Assert.True(property.SetMethod!.IsFamily);

        var button = new ProbeButton();
        button.SetPressed(true);

        Assert.True(button.IsPressed);
        Assert.NotNull(button.LastChange);
        Assert.Same(ButtonBase.IsPressedProperty, button.LastChange!.Value.Property);
        Assert.Equal(false, button.LastChange.Value.OldValue);
        Assert.Equal(true, button.LastChange.Value.NewValue);
    }

    private sealed class ProbeButton : ButtonBase
    {
        public DependencyPropertyChangedEventArgs? LastChange { get; private set; }

        public void SetPressed(bool value) => IsPressed = value;

        protected override void OnIsPressedChanged(DependencyPropertyChangedEventArgs e)
        {
            base.OnIsPressedChanged(e);
            LastChange = e;
        }
    }
}

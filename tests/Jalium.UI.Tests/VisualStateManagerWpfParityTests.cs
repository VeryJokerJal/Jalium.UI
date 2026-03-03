using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public class VisualStateManagerWpfParityTests
{
    [Fact]
    public void GoToState_ShouldApplyAndRemoveStateSetters_WithoutCreatingLocalValue()
    {
        var normalBrush = new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22));
        var pressedBrush = new SolidColorBrush(Color.FromRgb(0x33, 0x99, 0xFF));

        var commonStates = new VisualStateGroup(VisualStateNames.CommonStatesGroup);
        commonStates.States.Add(new VisualState(VisualStateNames.Normal)
        {
            Setters =
            {
                new Setter(Control.BackgroundProperty, normalBrush)
            }
        });
        commonStates.States.Add(new VisualState(VisualStateNames.Pressed)
        {
            Setters =
            {
                new Setter(Control.BackgroundProperty, pressedBrush)
            }
        });

        var button = new Button();
        VisualStateManager.SetVisualStateGroups(button, new List<VisualStateGroup> { commonStates });

        Assert.True(VisualStateManager.GoToState(button, VisualStateNames.Normal, useTransitions: false));
        Assert.Same(normalBrush, button.Background);
        Assert.False(button.HasLocalValue(Control.BackgroundProperty));

        Assert.True(VisualStateManager.GoToState(button, VisualStateNames.Pressed, useTransitions: false));
        Assert.Same(pressedBrush, button.Background);
        Assert.False(button.HasLocalValue(Control.BackgroundProperty));
        Assert.NotEqual(BaseValueSource.Local, DependencyPropertyHelper.GetValueSource(button, Control.BackgroundProperty).BaseValueSource);
    }
}

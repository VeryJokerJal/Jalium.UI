using System.Reflection;
using Jalium.UI.Input;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

public sealed class InputBaseChainParityTests
{
    [Fact]
    public void DispatcherOwnedInputTypesUseTheWpfBaseChain()
    {
        Assert.Equal(typeof(DispatcherObject), typeof(InputDevice).BaseType);
        Assert.True(typeof(InputDevice).IsAbstract);
        Assert.False(typeof(InputDevice).IsSealed);

        Assert.Equal(typeof(DispatcherObject), typeof(InputLanguageManager).BaseType);
        Assert.True(typeof(InputLanguageManager).IsSealed);
        Assert.Empty(typeof(InputLanguageManager).GetConstructors());

        Assert.Equal(typeof(DispatcherObject), typeof(InputManager).BaseType);
        Assert.True(typeof(InputManager).IsSealed);
        Assert.Empty(typeof(InputManager).GetConstructors());

        Assert.NotNull(InputLanguageManager.Current.Dispatcher);
        Assert.NotNull(InputManager.Current.Dispatcher);
    }

    [Fact]
    public void KeyEventArgsUsesKeyboardEventArgsAndOnlyTheWpfPublicConstructor()
    {
        Type type = typeof(KeyEventArgs);

        Assert.Equal(typeof(KeyboardEventArgs), type.BaseType);
        Assert.False(type.IsSealed);

        ConstructorInfo constructor = Assert.Single(type.GetConstructors());
        Assert.Equal(
            [typeof(KeyboardDevice), typeof(PresentationSource), typeof(int), typeof(Key)],
            constructor.GetParameters().Select(parameter => parameter.ParameterType));

        Assert.Equal(typeof(KeyboardDevice),
            typeof(KeyboardEventArgs).GetProperty(nameof(KeyboardEventArgs.KeyboardDevice))!.PropertyType);
        Assert.Equal(typeof(PresentationSource),
            type.GetProperty(nameof(KeyEventArgs.InputSource))!.PropertyType);
        Assert.Equal(typeof(KeyStates),
            type.GetProperty(nameof(KeyEventArgs.KeyStates))!.PropertyType);
    }
}

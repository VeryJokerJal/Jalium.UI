using System.ComponentModel;
using System.Globalization;
using Jalium.UI.Controls;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

public sealed class InputConvertersAccessKeyWpfParityTests
{
    [Fact]
    public void KeyAndGestureConvertersRequireAValidSerializationInstance()
    {
        var keyConverter = new KeyConverter();
        Assert.False(keyConverter.CanConvertTo(null, typeof(string)));
        Assert.True(keyConverter.CanConvertTo(new DescriptorContext(Key.A), typeof(string)));
        Assert.False(keyConverter.CanConvertTo(
            new DescriptorContext((Key)int.MaxValue),
            typeof(string)));

        var gestureConverter = new KeyGestureConverter();
        var gesture = new KeyGesture(Key.A, ModifierKeys.Control | ModifierKeys.Shift);
        Assert.False(gestureConverter.CanConvertTo(null, typeof(string)));
        Assert.True(gestureConverter.CanConvertTo(
            new DescriptorContext(gesture),
            typeof(string)));
        Assert.False(gestureConverter.CanConvertTo(
            new DescriptorContext(new KeyGesture((Key)int.MaxValue)),
            typeof(string)));
    }

    [Fact]
    public void ModifierConverterValidatesAndUsesCanonicalOrdering()
    {
        var converter = new ModifierKeysConverter();
        var all = ModifierKeys.Control |
                  ModifierKeys.Alt |
                  ModifierKeys.Windows |
                  ModifierKeys.Shift;

        Assert.True(ModifierKeysConverter.IsDefinedModifierKeys(ModifierKeys.None));
        Assert.True(ModifierKeysConverter.IsDefinedModifierKeys(all));
        Assert.False(ModifierKeysConverter.IsDefinedModifierKeys((ModifierKeys)16));
        Assert.False(converter.CanConvertTo(null, typeof(string)));
        Assert.True(converter.CanConvertTo(new DescriptorContext(all), typeof(string)));
        Assert.Equal(
            "Ctrl+Alt+Windows+Shift",
            converter.ConvertTo(null, CultureInfo.InvariantCulture, all, typeof(string)));
        Assert.Equal(
            ModifierKeys.Control | ModifierKeys.Alt,
            converter.ConvertFrom(null, CultureInfo.InvariantCulture, "Control+ALT"));
        Assert.Throws<NotSupportedException>(
            () => converter.ConvertFrom(null, CultureInfo.InvariantCulture, "Control+Hyper"));
        Assert.Throws<InvalidEnumArgumentException>(
            () => converter.ConvertTo(
                null,
                CultureInfo.InvariantCulture,
                (ModifierKeys)16,
                typeof(string)));
    }

    [Fact]
    public void AccessKeyPressedHandlerHelpersAttachAndDetachRoutedHandlers()
    {
        var element = new Border();
        var calls = 0;
        AccessKeyPressedEventHandler handler = (_, args) =>
        {
            calls++;
            Assert.Same(element, args.Source);
        };

        AccessKeyManager.AddAccessKeyPressedHandler(element, handler);
        element.RaiseEvent(new AccessKeyPressedEventArgs());
        Assert.Equal(1, calls);

        AccessKeyManager.RemoveAccessKeyPressedHandler(element, handler);
        element.RaiseEvent(new AccessKeyPressedEventArgs());
        Assert.Equal(1, calls);

        Assert.Throws<ArgumentException>(
            () => AccessKeyManager.AddAccessKeyPressedHandler(new DependencyObject(), handler));
    }

    private sealed class DescriptorContext(object instance) : ITypeDescriptorContext
    {
        public IContainer? Container => null;

        public object Instance => instance;

        public PropertyDescriptor? PropertyDescriptor => null;

        public object? GetService(Type serviceType) => null;

        public void OnComponentChanged()
        {
        }

        public bool OnComponentChanging() => true;
    }
}

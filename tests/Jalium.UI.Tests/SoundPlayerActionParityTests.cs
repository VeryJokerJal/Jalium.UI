using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

public sealed class SoundPlayerActionParityTests
{
    [Fact]
    public void SourceIsDependencyPropertyAndDisposeIsIdempotent()
    {
        Assert.True(typeof(DependencyObject).IsAssignableFrom(typeof(TriggerAction)));
        Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(SoundPlayerAction)));
        Assert.Equal(typeof(Uri), SoundPlayerAction.SourceProperty.PropertyType);
        Assert.Same(
            SoundPlayerAction.SourceProperty,
            typeof(SoundPlayerAction).GetField(nameof(SoundPlayerAction.SourceProperty))!.GetValue(null));

        var source = new Uri("sound.wav", UriKind.Relative);
        var action = new SoundPlayerAction { Source = source };
        Assert.Same(source, action.Source);
        Assert.Same(source, action.GetValue(SoundPlayerAction.SourceProperty));

        action.Dispose();
        action.Dispose();
    }
}

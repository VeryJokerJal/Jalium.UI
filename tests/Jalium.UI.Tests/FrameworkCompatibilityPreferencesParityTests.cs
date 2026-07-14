namespace Jalium.UI.Tests;

[Collection(nameof(ParityFoundationBehaviorCollection))]
public sealed class FrameworkCompatibilityPreferencesParityTests
{
    [Fact]
    public void PreferencesExposeModernDefaultsAndRoundTripBeforeUse()
    {
        FrameworkCompatibilityPreferences.ResetForTests();
        try
        {
            Assert.True(FrameworkCompatibilityPreferences.AreInactiveSelectionHighlightBrushKeysSupported);
            Assert.True(FrameworkCompatibilityPreferences.KeepTextBoxDisplaySynchronizedWithTextProperty);
            Assert.False(FrameworkCompatibilityPreferences.ShouldThrowOnCopyOrCutFailure);

            FrameworkCompatibilityPreferences.AreInactiveSelectionHighlightBrushKeysSupported = false;
            FrameworkCompatibilityPreferences.KeepTextBoxDisplaySynchronizedWithTextProperty = false;
            FrameworkCompatibilityPreferences.ShouldThrowOnCopyOrCutFailure = true;

            Assert.False(FrameworkCompatibilityPreferences.AreInactiveSelectionHighlightBrushKeysSupported);
            Assert.False(FrameworkCompatibilityPreferences.KeepTextBoxDisplaySynchronizedWithTextProperty);
            Assert.True(FrameworkCompatibilityPreferences.ShouldThrowOnCopyOrCutFailure);
        }
        finally
        {
            FrameworkCompatibilityPreferences.ResetForTests();
        }
    }

    [Fact]
    public void FrameworkConsumptionSealsPreferences()
    {
        FrameworkCompatibilityPreferences.ResetForTests();
        try
        {
            Assert.True(FrameworkCompatibilityPreferences.GetKeepTextBoxDisplaySynchronizedWithTextProperty());

            Assert.Throws<InvalidOperationException>(() =>
                FrameworkCompatibilityPreferences.ShouldThrowOnCopyOrCutFailure = true);
        }
        finally
        {
            FrameworkCompatibilityPreferences.ResetForTests();
        }
    }
}

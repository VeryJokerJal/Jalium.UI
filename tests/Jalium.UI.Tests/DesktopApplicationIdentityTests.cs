namespace Jalium.UI.Tests;

[Collection("Application")]
public class DesktopApplicationIdentityTests
{
    [Theory]
    [InlineData("")]
    [InlineData("org jalium.App")]
    [InlineData("org.jalium.App\0other")]
    [InlineData("应用标题")]
    public void InvalidIdsAreRejected(string value) =>
        Assert.Throws<ArgumentException>(() => Application.ValidateDesktopApplicationId(value));

    [Fact]
    public void DefaultIdentityIsStableAndDoesNotCollapseUnicodeHostNames()
    {
        Assert.Equal("Jalium.Example", Application.CreateDefaultDesktopApplicationId("Jalium.Example"));
        string first = Application.CreateDefaultDesktopApplicationId("示例应用");
        Assert.Equal(first, Application.CreateDefaultDesktopApplicationId("示例应用"));
        Assert.NotEqual(first, Application.CreateDefaultDesktopApplicationId("另一个应用"));
        Application.ValidateDesktopApplicationId(first);
        Assert.Throws<ArgumentException>(() => Application.ValidateDesktopApplicationId(new string('a', 256)));
    }

    [Fact]
    public void BuilderAppliesIdentityBeforeConsumerConfigurationAndLocksBeforeNativeWindows()
    {
        var currentField = typeof(Application).GetField("_current",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var original = currentField.GetValue(null);
        currentField.SetValue(null, null);
        try
        {
            var builder = AppBuilder.CreateBuilder(new AppBuilderSettings
            { DisableDefaults = true, ApplicationName = "Example.Host", DesktopApplicationId = "org.jalium.Example" });
            string? configured = null;
            builder.ConfigureApplication(app => configured = app.DesktopApplicationId);
            using var host = builder.Build();
            var app = host.Application;
            Assert.Equal("org.jalium.Example", configured);
            Assert.Equal(configured, app.LockDesktopApplicationId());
            app.DesktopApplicationId = configured!;
            Assert.Throws<InvalidOperationException>(() => app.DesktopApplicationId = "org.jalium.Other");
            Assert.Equal(configured, app.DesktopApplicationId);
        }
        finally { currentField.SetValue(null, original); }
    }

    [Theory]
    [InlineData(null, "org.jalium.ApplicationConfigured")]
    [InlineData("org.jalium.BuilderConfigured", "org.jalium.BuilderConfigured")]
    public void HostDefaultPreservesApplicationIdentityWhileExplicitSettingsOverride(string? builderId, string expected)
    {
        var currentField = typeof(Application).GetField("_current",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var original = currentField.GetValue(null);
        currentField.SetValue(null, null);
        try
        {
            using var host = AppBuilder.CreateBuilder(new AppBuilderSettings
            { DisableDefaults = true, ApplicationName = "Example.Host", DesktopApplicationId = builderId }).Build();
            host.UseApplication<IdentityApplication>();
            Assert.Equal(expected, host.Application.DesktopApplicationId);
        }
        finally { currentField.SetValue(null, original); }
    }

    [Fact]
    public void HostDefaultPreservesLockedIdentityOfPreconstructedApplication()
    {
        var currentField = typeof(Application).GetField("_current",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var original = currentField.GetValue(null);
        currentField.SetValue(null, null);
        try
        {
            var application = new Application();
            string identity = application.LockDesktopApplicationId();
            using var host = AppBuilder.CreateBuilder(new AppBuilderSettings
            { DisableDefaults = true, ApplicationName = "Different.Host" }).Build();
            host.UseApplication(application);
            Assert.Equal(identity, host.Application.DesktopApplicationId);
        }
        finally { currentField.SetValue(null, original); }
    }

    private sealed class IdentityApplication : Application
    {
        public IdentityApplication() => DesktopApplicationId = "org.jalium.ApplicationConfigured";
    }
}

using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Markup;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class StandaloneSystemResourceTests : IDisposable
{
    private readonly Func<object, object?>? _application = ResourceLookup.ApplicationResourceLookup;
    private readonly Func<object, (object? Value, ResourceDictionary? Dictionary)>? _source = ResourceLookup.ApplicationResourceLookupWithSource;
    private static readonly FieldInfo Motion = typeof(Window).Assembly.GetType("Jalium.UI.Controls.Platform.MacOSScrollBarSettings")!
        .GetField("s_prefersReducedMotion", BindingFlags.Static | BindingFlags.NonPublic)!;
    private readonly object? _motion = Motion.GetValue(null);

    public StandaloneSystemResourceTests()
    {
        ResourceLookup.ApplicationResourceLookup = null;
        ResourceLookup.ApplicationResourceLookupWithSource = null;
        ResourceLookup.InvalidateResourceCache();
    }

    public static IEnumerable<object[]> Flags()
    {
        foreach (string name in new[] { "ClientAreaAnimation", "ComboBoxAnimation", "ListBoxSmoothScrolling", "MenuAnimation", "MinimizeAnimation", "ToolTipAnimation", "UIEffects" })
            foreach (bool reduced in new[] { false, true }) yield return [name, reduced];
    }

    [Theory]
    [MemberData(nameof(Flags))]
    public void SystemParameterKeysResolveWithoutApplicationCallbacks(string name, bool reduced)
    {
        Motion.SetValue(null, reduced);
        object key = typeof(SystemParameters).GetProperty(name + "Key")!.GetValue(null)!;
        object? expected = typeof(SystemParameters).GetProperty(name)!.GetValue(null);
        Assert.Equal(expected, new Border().TryFindResource(key));
    }

    [Theory]
    [InlineData("color", false)]
    [InlineData("font", false)]
    [InlineData("parameter", false)]
    [InlineData("color", true)]
    [InlineData("font", true)]
    [InlineData("parameter", true)]
    public void BothElementKindsResolveAllSystemResourceFamilies(string family, bool content)
    {
        (object key, object expected) = family switch
        {
            "color" => (SystemColors.WindowColorKey, (object)SystemColors.WindowColor),
            "font" => (SystemFonts.CaptionFontSizeKey, (object)SystemFonts.CaptionFontSize),
            _ => (SystemParameters.ClientAreaAnimationKey, (object)SystemParameters.ClientAreaAnimation),
        };
        object? actual = content ? new Paragraph().TryFindResource(key) : new Border().TryFindResource(key);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SourceLookupReturnsSystemValuesWithoutAnApplicationDictionary(bool reduced)
    {
        Motion.SetValue(null, reduced);
        object? expected = SystemParameters.ClientAreaAnimation;
        var value = ResourceLookup.FindResource(new Border(), SystemParameters.ClientAreaAnimationKey, out var dictionary);
        Assert.Equal(expected, value); Assert.Null(dictionary);
    }

    [Theory]
    [InlineData("element", false)]
    [InlineData("element", null)]
    [InlineData("style", false)]
    [InlineData("style", null)]
    [InlineData("ancestor", false)]
    [InlineData("ancestor", null)]
    public void AuthorResourcesKeepPrecedenceIncludingExplicitNull(string layer, bool? authored)
    {
        var root = new StackPanel(); var child = new Border(); root.Children.Add(child);
        var dictionary = layer switch
        {
            "style" => (child.Style = new Style(typeof(Border))).Resources,
            "ancestor" => root.Resources,
            _ => child.Resources,
        };
        dictionary[SystemParameters.ClientAreaAnimationKey] = authored;
        Assert.Equal(authored, child.TryFindResource(SystemParameters.ClientAreaAnimationKey));
        Assert.Equal(authored, ResourceLookup.FindResource(child, SystemParameters.ClientAreaAnimationKey, out var source));
        Assert.Same(dictionary, source);
    }

    [Fact]
    public void ApplicationDictionaryValueAndSourceStayAuthoritative()
    {
        var key = SystemParameters.ClientAreaAnimationKey;
        var dictionary = new ResourceDictionary { [key] = false };
        ResourceLookup.ApplicationResourceLookup = k => Equals(k, key) ? false : null;
        ResourceLookup.ApplicationResourceLookupWithSource = k => Equals(k, key) ? (false, dictionary) : (null, null);
        var child = new Border();
        Assert.Equal(false, child.TryFindResource(key));
        Assert.Equal(false, ResourceLookup.FindResource(child, key, out var source)); Assert.Same(dictionary, source);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingApplicationValuesStillFallBackToSystemResources(bool withSource)
    {
        ResourceLookup.ApplicationResourceLookup = _ => null;
        if (withSource) ResourceLookup.ApplicationResourceLookupWithSource = _ => (null, null);
        var child = new Border(); var key = SystemParameters.ClientAreaAnimationKey;
        Assert.Equal(SystemParameters.ClientAreaAnimation, child.TryFindResource(key));
        Assert.Equal(SystemParameters.ClientAreaAnimation, ResourceLookup.FindResource(child, key, out var dictionary)); Assert.Null(dictionary);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DynamicSystemKeysRefreshWithoutApplicationCallbacks(bool initial)
    {
        Motion.SetValue(null, initial); var child = new Border();
        child.SetResourceReference(FrameworkElement.TagProperty, SystemParameters.ClientAreaAnimationKey);
        Assert.Equal(SystemParameters.ClientAreaAnimation, child.Tag);
        foreach (bool value in new[] { !initial, initial })
        {
            Motion.SetValue(null, value); SystemParameters.NotifyMacOSAnimationPreferencesChanged();
            Assert.Equal(SystemParameters.ClientAreaAnimation, child.Tag);
        }
        child.Resources[SystemParameters.ClientAreaAnimationKey] = "author";
        Motion.SetValue(null, !initial); SystemParameters.NotifyMacOSAnimationPreferencesChanged();
        Assert.Equal("author", child.Tag);
    }

    [Theory]
    [InlineData("StaticResource", false)]
    [InlineData("StaticResource", true)]
    [InlineData("DynamicResource", false)]
    [InlineData("DynamicResource", true)]
    public void MarkupExtensionsResolveSystemKeysWithoutAnApplication(string kind, bool reduced)
    {
        Motion.SetValue(null, reduced);
        var context = new XamlParserContext();
        Assert.True(context.TryGetResource(SystemParameters.ClientAreaAnimationKey, out var ambient));
        Assert.Equal(SystemParameters.ClientAreaAnimation, ambient);
        var child = new Border();
        var services = new MarkupExtensionServiceProvider();
        services.AddService(typeof(IAmbientResourceProvider), context);
        services.AddService(typeof(IProvideValueTarget), new ProvideValueTarget
        { TargetObject = child, TargetProperty = FrameworkElement.TagProperty });
        MarkupExtension extension = kind == "StaticResource"
            ? new StaticResourceExtension(SystemParameters.ClientAreaAnimationKey)
            : new DynamicResourceExtension(SystemParameters.ClientAreaAnimationKey);
        Assert.Equal(SystemParameters.ClientAreaAnimation, extension.ProvideValue(services));
        if (kind == "DynamicResource")
        {
            Motion.SetValue(null, !reduced); SystemParameters.NotifyMacOSAnimationPreferencesChanged();
            Assert.Equal(SystemParameters.ClientAreaAnimation, child.Tag);
        }
    }

    [Fact]
    public void ALocalDynamicAliasCanReachASystemKey()
    {
        var child = new Border(); child.Resources["alias"] = new Alias(SystemParameters.ClientAreaAnimationKey);
        Assert.Equal(SystemParameters.ClientAreaAnimation, child.TryFindResource("alias"));
    }

    [Fact]
    public void UnknownKeysRemainMissing()
    {
        var child = new Border(); Assert.Null(child.TryFindResource(new object()));
        Assert.Null(ResourceLookup.FindResource(child, new object(), out var dictionary)); Assert.Null(dictionary);
    }

    private sealed record Alias(object ResourceKey) : IDynamicResourceReference;

    public void Dispose()
    {
        ResourceLookup.ApplicationResourceLookup = _application;
        ResourceLookup.ApplicationResourceLookupWithSource = _source;
        Motion.SetValue(null, _motion); ResourceLookup.InvalidateResourceCache();
    }
}

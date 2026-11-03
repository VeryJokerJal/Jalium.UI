using System.Collections;
using Jalium.UI.Controls;
using Jalium.UI.Markup;
using Jalium.UI.Xaml;

namespace Jalium.UI.Tests;

public sealed class DeferredResourceDictionaryTests
{
    [Fact]
    public void FirstReadCreatesOnceCachesIdentityAndStaysNotificationSilent()
    {
        var dictionary = new ResourceDictionary();
        var factoryCalls = 0;
        var changedCalls = 0;
        dictionary.Changed += (_, _) => changedCalls++;

        XamlBuilder.AddDeferredResource(
            dictionary,
            "ControlStyle",
            (owner, context) =>
            {
                factoryCalls++;
                Assert.Same(dictionary, owner);
                Assert.NotNull(context.Inner);
                return new Style(typeof(Button));
            });

        // Key-only probes are used by diagnostics and theme refresh. They must not realize
        // the Style merely to report that the resource exists.
        changedCalls = 0;
        Assert.True(dictionary.Contains("ControlStyle"));
        Assert.Contains("ControlStyle", dictionary.Keys.Cast<object>());
        Assert.Equal(0, factoryCalls);

        var first = Assert.IsType<Style>(dictionary["ControlStyle"]);
        var second = Assert.IsType<Style>(dictionary["ControlStyle"]);

        Assert.Same(first, second);
        Assert.Equal(1, factoryCalls);
        Assert.Equal(0, changedCalls);
    }

    [Fact]
    public void FactoryUsesFreshContextWithRegistrationMetadata()
    {
        var dictionary = new ResourceDictionary();
        var originalBaseUri = new Uri(
            "resource:///Jalium.UI.Xaml/Themes/Original.jalxaml",
            UriKind.Absolute);
        var replacementBaseUri = new Uri(
            "resource:///Jalium.UI.Xaml/Themes/Replacement.jalxaml",
            UriKind.Absolute);
        var originalAssembly = typeof(Jalium.UI.Xaml.XamlReader).Assembly;
        var replacementAssembly = typeof(DeferredResourceDictionaryTests).Assembly;

        dictionary.BaseUri = originalBaseUri;
        dictionary.SourceAssembly = originalAssembly;
        var containingContext = XamlBuilder.BeginComponent(
            dictionary,
            originalBaseUri,
            originalAssembly);

        XamlBuildContext? observedContext = null;
        try
        {
            XamlBuilder.AddDeferredResource(
                dictionary,
                "Style",
                (_, context) =>
                {
                    observedContext = context;
                    return new Style(typeof(Button));
                });

            // Registration snapshots the metadata used by the original dictionary build.
            // Later owner mutation must not change relative URI or type resolution inside
            // the deferred Style factory.
            dictionary.BaseUri = replacementBaseUri;
            dictionary.SourceAssembly = replacementAssembly;

            _ = dictionary["Style"];
        }
        finally
        {
            XamlBuilder.EndComponent(dictionary, containingContext);
        }

        Assert.NotNull(observedContext);
        Assert.NotSame(containingContext, observedContext);
        var parserContext = Assert.IsType<XamlParserContext>(observedContext!.Inner);
        Assert.Equal(originalBaseUri, parserContext.BaseUri);
        Assert.Same(originalAssembly, parserContext.SourceAssembly);
        Assert.Same(dictionary, parserContext.CodeBehindInstance);
    }

    [Fact]
    public void FailedCreationCanRetryWithAnotherFreshContext()
    {
        var dictionary = new ResourceDictionary();
        XamlBuildContext? failedContext = null;
        var calls = 0;

        XamlBuilder.AddDeferredResource(
            dictionary,
            "Retry",
            (_, context) =>
            {
                calls++;
                if (calls == 1)
                {
                    failedContext = context;
                    throw new InvalidOperationException("dependency not ready");
                }

                Assert.NotSame(failedContext, context);
                return new Style(typeof(Button));
            });

        Assert.Throws<InvalidOperationException>(() => _ = dictionary["Retry"]);
        var style = Assert.IsType<Style>(dictionary["Retry"]);

        Assert.NotNull(style);
        Assert.Equal(2, calls);
    }

    [Fact]
    public void PreRegisteredStylesResolveBackwardAndForwardDependencies()
    {
        var dictionary = new ResourceDictionary();
        var baseCalls = 0;
        var backwardCalls = 0;
        var forwardCalls = 0;
        var laterCalls = 0;

        XamlBuilder.AddDeferredResource(
            dictionary,
            "Base",
            (_, _) =>
            {
                baseCalls++;
                return new Style(typeof(Button));
            });
        XamlBuilder.AddDeferredResource(
            dictionary,
            "BackwardDerived",
            (owner, _) =>
            {
                backwardCalls++;
                return new Style(typeof(Button), (Style)owner["Base"]!);
            });
        XamlBuilder.AddDeferredResource(
            dictionary,
            "ForwardDerived",
            (owner, _) =>
            {
                forwardCalls++;
                return new Style(typeof(Button), (Style)owner["LaterBase"]!);
            });
        XamlBuilder.AddDeferredResource(
            dictionary,
            "LaterBase",
            (_, _) =>
            {
                laterCalls++;
                return new Style(typeof(Button));
            });

        var backward = Assert.IsType<Style>(dictionary["BackwardDerived"]);
        var forward = Assert.IsType<Style>(dictionary["ForwardDerived"]);

        Assert.Same(dictionary["Base"], backward.BasedOn);
        Assert.Same(dictionary["LaterBase"], forward.BasedOn);
        Assert.Equal(1, baseCalls);
        Assert.Equal(1, backwardCalls);
        Assert.Equal(1, forwardCalls);
        Assert.Equal(1, laterCalls);
    }

    [Fact]
    public void CircularDependencyThrowsAndUnwindsEveryFactory()
    {
        var dictionary = new ResourceDictionary();
        var aCalls = 0;
        var bCalls = 0;

        XamlBuilder.AddDeferredResource(
            dictionary,
            "A",
            (owner, _) =>
            {
                aCalls++;
                return new Style(typeof(Button), (Style)owner["B"]!);
            });
        XamlBuilder.AddDeferredResource(
            dictionary,
            "B",
            (owner, _) =>
            {
                bCalls++;
                return new Style(typeof(Button), (Style)owner["A"]!);
            });

        var first = Assert.Throws<InvalidOperationException>(() => _ = dictionary["A"]);
        Assert.Contains("Circular deferred resource reference", first.Message);

        // Failure must not strand either entry in its Creating state.
        var second = Assert.Throws<InvalidOperationException>(() => _ = dictionary["A"]);
        Assert.Contains("Circular deferred resource reference", second.Message);
        Assert.Equal(2, aCalls);
        Assert.Equal(2, bCalls);
    }

    [Fact]
    public void FirstCreationMustRunOnRegistrationDispatcher()
    {
        var dictionary = new ResourceDictionary();
        var factoryCalls = 0;
        XamlBuilder.AddDeferredResource(
            dictionary,
            "Style",
            (_, _) =>
            {
                factoryCalls++;
                return new Style(typeof(Button));
            });

        Exception? backgroundException = null;
        var backgroundThread = new Thread(
            () => backgroundException = Record.Exception(
                () => _ = dictionary["Style"]));
        backgroundThread.Start();
        backgroundThread.Join();

        Assert.IsType<InvalidOperationException>(backgroundException);
        Assert.Equal(0, factoryCalls);

        Assert.IsType<Style>(dictionary["Style"]);
        Assert.Equal(1, factoryCalls);
    }

    [Fact]
    public void ValuesEnumerationRealizesEachEntryOnce()
    {
        var dictionary = new ResourceDictionary();
        var calls = 0;
        XamlBuilder.AddDeferredResource(
            dictionary,
            "One",
            (_, _) =>
            {
                calls++;
                return new Style(typeof(Button));
            });
        XamlBuilder.AddDeferredResource(
            dictionary,
            "Two",
            (_, _) =>
            {
                calls++;
                return new Style(typeof(Button));
            });

        var values = dictionary.Values.Cast<object?>().ToArray();
        Assert.Equal(2, calls);
        Assert.All(values, value => Assert.IsType<Style>(value));

        var copy = new DictionaryEntry[dictionary.Count];
        ((ICollection)dictionary).CopyTo(copy, 0);
        Assert.Equal(2, calls);
        Assert.All(copy, entry => Assert.IsType<Style>(entry.Value));
    }

    [Fact]
    public void ThemeSwitchCreatesOnlySelectedStyleAndRetainsPerThemeIdentity()
    {
        var previousTheme = ResourceDictionary.CurrentThemeKey;
        try
        {
            var host = new ResourceDictionary();
            var light = new ResourceDictionary();
            var dark = new ResourceDictionary();
            var lightCalls = 0;
            var darkCalls = 0;

            XamlBuilder.AddDeferredResource(
                light,
                "ControlStyle",
                (_, _) =>
                {
                    lightCalls++;
                    return new Style(typeof(Button));
                });
            XamlBuilder.AddDeferredResource(
                dark,
                "ControlStyle",
                (_, _) =>
                {
                    darkCalls++;
                    return new Style(typeof(Button));
                });

            host.ThemeDictionaries["Light"] = light;
            host.ThemeDictionaries["Dark"] = dark;

            ResourceDictionary.CurrentThemeKey = "Light";
            var firstLight = Assert.IsType<Style>(host["ControlStyle"]);
            Assert.Equal(1, lightCalls);
            Assert.Equal(0, darkCalls);

            ResourceDictionary.CurrentThemeKey = "Dark";
            var firstDark = Assert.IsType<Style>(host["ControlStyle"]);
            Assert.NotSame(firstLight, firstDark);
            Assert.Equal(1, darkCalls);

            ResourceDictionary.CurrentThemeKey = "Light";
            Assert.Same(firstLight, host["ControlStyle"]);
            Assert.Equal(1, lightCalls);
        }
        finally
        {
            ResourceDictionary.CurrentThemeKey = previousTheme;
        }
    }
}

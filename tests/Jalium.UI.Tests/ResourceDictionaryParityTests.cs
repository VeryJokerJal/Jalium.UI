using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;

namespace Jalium.UI.Tests;

public sealed class ResourceDictionaryParityTests
{
    [Fact]
    public void PublicSurfaceExposesTierOneResourceDictionaryApis()
    {
        Type dictionaryType = typeof(ResourceDictionary);

        Assert.Contains(typeof(ISupportInitialize), dictionaryType.GetInterfaces());
        Assert.Contains(typeof(Jalium.UI.Markup.INameScope), dictionaryType.GetInterfaces());
        Assert.Contains(typeof(Jalium.UI.Markup.IUriContext), dictionaryType.GetInterfaces());
        Assert.Contains(typeof(IDictionary), dictionaryType.GetInterfaces());
        Assert.DoesNotContain(typeof(IDictionary<object, object?>), dictionaryType.GetInterfaces());
        Assert.DoesNotContain(
            dictionaryType.Assembly.GetExportedTypes(),
            type => type.FullName == "Jalium.UI.INameScope");
        Assert.Equal(
            typeof(Collection<ResourceDictionary>),
            dictionaryType.GetProperty(nameof(ResourceDictionary.MergedDictionaries))!.PropertyType);
        Assert.Equal(
            typeof(DeferrableContent),
            dictionaryType.GetProperty(nameof(ResourceDictionary.DeferrableContent))!.PropertyType);
        Assert.Equal(typeof(ICollection), dictionaryType.GetProperty(nameof(ResourceDictionary.Keys))!.PropertyType);
        Assert.Equal(typeof(ICollection), dictionaryType.GetProperty(nameof(ResourceDictionary.Values))!.PropertyType);
        Assert.Equal(
            typeof(IDictionaryEnumerator),
            dictionaryType.GetMethod(nameof(ResourceDictionary.GetEnumerator), Type.EmptyTypes)!.ReturnType);
        Assert.Equal(
            typeof(void),
            dictionaryType.GetMethod(nameof(ResourceDictionary.Remove), [typeof(object)])!.ReturnType);

        MethodInfo onGettingValue = dictionaryType.GetMethod(
            "OnGettingValue",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            [typeof(object), typeof(object).MakeByRefType(), typeof(bool).MakeByRefType()],
            modifiers: null)!;

        Assert.True(onGettingValue.IsFamily);
        Assert.True(onGettingValue.IsVirtual);
        Assert.True(typeof(DeferrableContent).IsPublic);
        Assert.Contains(
            typeof(DeferrableContent).GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic),
            constructor => constructor.IsAssembly && constructor.GetParameters().Length == 0);
    }

    [Fact]
    public void DeferrableContentAndImplicitTemplateFlagRoundTrip()
    {
        var dictionary = new ResourceDictionary();
        var content = new DeferrableContent();

        Assert.Null(dictionary.DeferrableContent);
        Assert.False(dictionary.InvalidatesImplicitDataTemplateResources);

        dictionary.DeferrableContent = content;
        dictionary.InvalidatesImplicitDataTemplateResources = true;

        Assert.Same(content, dictionary.DeferrableContent);
        Assert.True(dictionary.InvalidatesImplicitDataTemplateResources);
    }

    [Fact]
    public void NameScopeRegistersFindsAndUnregistersObjects()
    {
        var dictionary = new ResourceDictionary();
        var value = new object();

        dictionary.RegisterName("resourcePart", value);

        Assert.Same(value, dictionary.FindName("resourcePart"));
        Assert.Same(value, ((Jalium.UI.Markup.INameScope)dictionary).FindName("resourcePart"));
        Assert.Throws<ArgumentException>(() => dictionary.RegisterName("resourcePart", new object()));

        dictionary.UnregisterName("resourcePart");

        Assert.Null(dictionary.FindName("resourcePart"));
        Assert.Throws<ArgumentException>(() => dictionary.UnregisterName("resourcePart"));
    }

    [Fact]
    public void InitializationTransactionCoalescesNotificationsAndRejectsInvalidNesting()
    {
        var dictionary = new ResourceDictionary();
        int changedCount = 0;
        ResourceDictionary.ResourcesChangedEventArgs? changedArgs = null;
        dictionary.Changed += (_, _) => changedCount++;
        dictionary.ChangedWithKeys += (_, args) => changedArgs = args;

        dictionary.BeginInit();
        dictionary.Add("first", 1);
        dictionary["second"] = 2;

        Assert.Equal(0, changedCount);
        Assert.Throws<InvalidOperationException>(dictionary.BeginInit);

        dictionary.EndInit();

        Assert.Equal(1, changedCount);
        Assert.NotNull(changedArgs?.ChangedKeys);
        Assert.Equal(2, changedArgs!.ChangedKeys!.Count);
        Assert.Contains("first", changedArgs.ChangedKeys);
        Assert.Contains("second", changedArgs.ChangedKeys);
        Assert.Throws<InvalidOperationException>(dictionary.EndInit);
    }

    [Fact]
    public void OnGettingValueRunsForDirectAndMergedLookupsAndHonorsCanCache()
    {
        var uncached = new TransformingResourceDictionary(canCache: false);
        uncached.Add("key", "raw");

        Assert.True(uncached.Contains("key"));
        Assert.Equal(0, uncached.GettingValueCallCount);
        Assert.Equal("raw:1", uncached["key"]);
        Assert.True(uncached.TryGetValue("key", out object? secondUncached));
        Assert.Equal("raw:2", secondUncached);

        var cached = new TransformingResourceDictionary(canCache: true);
        cached.Add("key", "raw");

        Assert.Equal("raw:1", cached["key"]);
        Assert.Equal("raw:1:2", cached["key"]);

        var owner = new ResourceDictionary();
        owner.MergedDictionaries.Add(uncached);

        Assert.Equal("raw:3", owner["key"]);
    }

    [Fact]
    public void CopyAndEnumerationPathsRunOnGettingValue()
    {
        var dictionary = new TransformingResourceDictionary(canCache: false);
        dictionary.Add("key", "raw");

        var entries = new DictionaryEntry[1];
        dictionary.CopyTo(entries, 0);
        Assert.Equal("raw:1", entries[0].Value);

        IDictionaryEnumerator directEnumerator = dictionary.GetEnumerator();
        Assert.True(directEnumerator.MoveNext());
        Assert.Equal("raw:2", directEnumerator.Value);

        IEnumerator valuesEnumerator = dictionary.Values.GetEnumerator();
        Assert.True(valuesEnumerator.MoveNext());
        Assert.Equal("raw:3", valuesEnumerator.Current);

        IDictionaryEnumerator dictionaryEnumerator = ((IDictionary)dictionary).GetEnumerator();
        Assert.True(dictionaryEnumerator.MoveNext());
        Assert.Equal("raw:4", dictionaryEnumerator.Value);

        Assert.Equal(4, dictionary.GettingValueCallCount);
    }

    [Fact]
    public void DictionaryEntryCopyValidatesDestinationAndCopiesOnlyLocalEntries()
    {
        var merged = new ResourceDictionary { ["merged"] = 1 };
        var dictionary = new ResourceDictionary { ["local"] = 2 };
        dictionary.MergedDictionaries.Add(merged);

        var destination = new DictionaryEntry[2];
        dictionary.CopyTo(destination, 1);

        Assert.Null(destination[0].Key);
        Assert.Equal("local", destination[1].Key);
        Assert.Equal(2, destination[1].Value);
        Assert.Throws<ArgumentNullException>(() => dictionary.CopyTo((DictionaryEntry[])null!, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => dictionary.CopyTo(destination, -1));
        Assert.Throws<ArgumentException>(() => dictionary.CopyTo(Array.Empty<DictionaryEntry>(), 0));
    }

    [Fact]
    public void MergedDictionaryCollectionPreservesLookupPriorityAndChangePropagation()
    {
        var first = new ResourceDictionary { ["key"] = "first" };
        var second = new ResourceDictionary { ["key"] = "second" };
        var owner = new ResourceDictionary();
        int changeCount = 0;
        owner.Changed += (_, _) => changeCount++;

        Collection<ResourceDictionary> mergedDictionaries = owner.MergedDictionaries;
        mergedDictionaries.Add(first);
        mergedDictionaries.Add(second);

        Assert.Equal("second", owner["key"]);

        second["key"] = "updated";

        Assert.Equal("updated", owner["key"]);
        Assert.Equal(3, changeCount);
    }

    private sealed class TransformingResourceDictionary : ResourceDictionary
    {
        private readonly bool _canCache;

        public TransformingResourceDictionary(bool canCache)
        {
            _canCache = canCache;
        }

        public int GettingValueCallCount { get; private set; }

        protected override void OnGettingValue(object key, ref object? value, out bool canCache)
        {
            GettingValueCallCount++;
            value = $"{value}:{GettingValueCallCount}";
            canCache = _canCache;
        }
    }
}

using System.Collections;
using System.Reflection;
using Jalium.UI.Media;
using Jalium.UI.Threading;

namespace Jalium.UI.Tests;

public sealed class DeferredLiteralBrushResourceDictionaryTests
{
    [Fact]
    public void PublicConstructorsRemainCanonicalAndExplicitDispatcherPathUsesNormalColorSetter()
    {
        ConstructorInfo[] publicConstructors = typeof(SolidColorBrush).GetConstructors(
            BindingFlags.Instance | BindingFlags.Public);

        Assert.Empty(typeof(DispatcherObject).GetConstructors(BindingFlags.Instance | BindingFlags.Public));
        ConstructorInfo dependencyObjectConstructor = Assert.Single(
            typeof(DependencyObject).GetConstructors(BindingFlags.Instance | BindingFlags.Public));
        Assert.Empty(dependencyObjectConstructor.GetParameters());
        Assert.Empty(typeof(Freezable).GetConstructors(BindingFlags.Instance | BindingFlags.Public));
        Assert.Empty(typeof(Jalium.UI.Media.Animation.Animatable).GetConstructors(
            BindingFlags.Instance | BindingFlags.Public));
        Assert.Empty(typeof(Brush).GetConstructors(BindingFlags.Instance | BindingFlags.Public));
        Assert.Equal(2, publicConstructors.Length);
        Assert.Contains(publicConstructors, static constructor => constructor.GetParameters().Length == 0);
        Assert.Contains(
            publicConstructors,
            static constructor => constructor.GetParameters() is [{ ParameterType: var type }]
                && type == typeof(Color));

        Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
        Color color = Color.FromArgb(0xA1, 0x23, 0x45, 0x67);
        SolidColorBrush? brush = null;
        var worker = new Thread(() => brush = new SolidColorBrush(dispatcher, color));

        worker.Start();
        worker.Join();

        Assert.NotNull(brush);
        Assert.Same(dispatcher, brush!.Dispatcher);
        Assert.Equal(color, brush.Color);
        Assert.Equal(color, brush.ReadLocalValue(SolidColorBrush.ColorProperty));
        Assert.True(brush.CheckAccess());
    }

    [Fact]
    public void KeyOnlyOperationsDoNotRealizeButValuesEnumerationAndCopyDo()
    {
        var keyOnly = CreateDictionary("KeyOnly", 0xFF102030u, out object keyOnlyDescriptor);

        Assert.Single(keyOnly.Keys.Cast<object>());
        Assert.True(keyOnly.Contains("KeyOnly"));
        Assert.Equal(new object[] { "KeyOnly" }, keyOnly.Keys.Cast<object>());
        Assert.Null(GetDescriptorState(keyOnlyDescriptor));

        AssertMaterializes(
            "Values",
            0xFF203040u,
            static dictionary => Assert.Single(dictionary.Values.Cast<object?>()));

        AssertMaterializes(
            "Enumerator",
            0xFF304050u,
            static dictionary =>
            {
                IDictionaryEnumerator enumerator = dictionary.GetEnumerator();
                Assert.True(enumerator.MoveNext());
                return enumerator.Value;
            });

        AssertMaterializes(
            "TypedCopy",
            0xFF405060u,
            static dictionary =>
            {
                var entries = new DictionaryEntry[1];
                dictionary.CopyTo(entries, 0);
                return entries[0].Value;
            });

        AssertMaterializes(
            "CollectionCopy",
            0xFF506070u,
            static dictionary =>
            {
                var entries = new KeyValuePair<object, object?>[1];
                ((ICollection)dictionary).CopyTo(entries, 0);
                return entries[0].Value;
            });

        AssertMaterializes(
            "ValuesCopy",
            0xFF607080u,
            static dictionary =>
            {
                var values = new object?[1];
                dictionary.Values.CopyTo(values, 0);
                return values[0];
            });
    }

    [Fact]
    public void ConcurrentFirstReadsPublishOneIdentityWithRegistrationDispatcher()
    {
        Dispatcher registrationDispatcher = Dispatcher.CurrentDispatcher;
        var dictionary = new ResourceDictionary();
        dictionary.AddDeferredLiteralSolidColorBrush("Brush", 0xFF718293u);

        const int readerCount = 12;
        using var start = new ManualResetEventSlim();
        var results = new SolidColorBrush?[readerCount];
        var workerAccess = new bool[readerCount];
        var failures = new Exception?[readerCount];
        var workers = new Thread[readerCount];

        for (int index = 0; index < workers.Length; index++)
        {
            int capture = index;
            workers[index] = new Thread(() =>
            {
                try
                {
                    start.Wait();
                    var brush = Assert.IsType<SolidColorBrush>(dictionary["Brush"]);
                    results[capture] = brush;
                    workerAccess[capture] = brush.CheckAccess();
                }
                catch (Exception exception)
                {
                    failures[capture] = exception;
                }
            });
            workers[index].Start();
        }

        start.Set();
        foreach (Thread worker in workers)
        {
            worker.Join();
        }

        Assert.All(failures, Assert.Null);
        SolidColorBrush first = Assert.IsType<SolidColorBrush>(results[0]);
        Assert.All(results, result => Assert.Same(first, result));
        Assert.All(workerAccess, Assert.False);
        Assert.Same(registrationDispatcher, first.Dispatcher);
        Assert.True(first.CheckAccess());
        Assert.Equal(Color.FromArgb(0xFF, 0x71, 0x82, 0x93), first.Color);
    }

    [Fact]
    public void FailedConstructionResetsDescriptorAndLaterReadCanRetry()
    {
        Type descriptorType = GetDescriptorType();
        ConstructorInfo constructor = Assert.Single(descriptorType.GetConstructors(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic));
        object descriptor = constructor.Invoke([null, 0xFF8192A3u]);
        MethodInfo getValue = descriptorType.GetMethod(
            "GetValue",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;

        var firstFailure = Assert.Throws<TargetInvocationException>(
            () => getValue.Invoke(descriptor, null));
        Assert.IsType<ArgumentNullException>(firstFailure.InnerException);
        Assert.Null(GetDescriptorState(descriptor));

        descriptorType.GetField(
                "_dispatcher",
                BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(descriptor, Dispatcher.CurrentDispatcher);

        var first = Assert.IsType<SolidColorBrush>(getValue.Invoke(descriptor, null));
        var second = Assert.IsType<SolidColorBrush>(getValue.Invoke(descriptor, null));

        Assert.Same(first, second);
        Assert.Same(first, GetDescriptorState(descriptor));
    }

    [Fact]
    public void CopyFromSharesDescriptorAndRealizedBrushIdentity()
    {
        var source = CreateDictionary("Shared", 0xFF91A2B3u, out object sourceDescriptor);
        var copy = new ResourceDictionary();

        copy.CopyFrom(source);

        object copyDescriptor = GetStoredEntry(copy, "Shared");
        Assert.Same(sourceDescriptor, copyDescriptor);
        Assert.Null(GetDescriptorState(sourceDescriptor));

        var sourceBrush = Assert.IsType<SolidColorBrush>(source["Shared"]);
        var copyBrush = Assert.IsType<SolidColorBrush>(copy["Shared"]);

        Assert.Same(sourceBrush, copyBrush);
        Assert.Same(sourceBrush, GetDescriptorState(sourceDescriptor));
    }

    [Fact]
    public void DerivedGettingValueSeesBrushAndCanCacheAReplacement()
    {
        var replacement = new object();
        var dictionary = new ReplacingResourceDictionary(replacement);
        dictionary.AddDeferredLiteralSolidColorBrush("Brush", 0xFFA1B2C3u);

        Assert.Same(replacement, dictionary["Brush"]);
        Assert.Same(replacement, dictionary["Brush"]);

        Assert.Equal(2, dictionary.ObservedValues.Count);
        Assert.IsType<SolidColorBrush>(dictionary.ObservedValues[0]);
        Assert.Same(replacement, dictionary.ObservedValues[1]);
    }

    [Fact]
    public void RegistrationUsesAssignmentNotificationsAndRealizationIsSilent()
    {
        var dictionary = new ResourceDictionary();
        int changedCalls = 0;
        dictionary.Changed += (_, _) => changedCalls++;

        dictionary.AddDeferredLiteralSolidColorBrush("Brush", 0xFFB1C2D3u);
        dictionary.AddDeferredLiteralSolidColorBrush("Brush", 0xFFC1D2E3u);

        Assert.Single(dictionary.Keys.Cast<object>());
        Assert.Equal(2, changedCalls);

        var brush = Assert.IsType<SolidColorBrush>(dictionary["Brush"]);
        Assert.Equal(Color.FromArgb(0xFF, 0xC1, 0xD2, 0xE3), brush.Color);
        Assert.Equal(2, changedCalls);
    }

    [Fact]
    public void RealizedBrushKeepsExistingCrossThreadMutationAndChangedBehavior()
    {
        var dictionary = new ResourceDictionary();
        dictionary.AddDeferredLiteralSolidColorBrush("Brush", 0xFFD1E2F3u);
        var brush = Assert.IsType<SolidColorBrush>(dictionary["Brush"]);

        int mutationThreadId = 0;
        int changedThreadId = 0;
        Exception? mutationFailure = null;
        brush.Changed += (_, _) => changedThreadId = Environment.CurrentManagedThreadId;

        var worker = new Thread(() =>
        {
            try
            {
                mutationThreadId = Environment.CurrentManagedThreadId;
                Assert.False(brush.CheckAccess());
                brush.Color = Color.FromArgb(0xFF, 0x01, 0x02, 0x03);
            }
            catch (Exception exception)
            {
                mutationFailure = exception;
            }
        });
        worker.Start();
        worker.Join();

        Assert.Null(mutationFailure);
        Assert.Equal(mutationThreadId, changedThreadId);
        Assert.Equal(Color.FromArgb(0xFF, 0x01, 0x02, 0x03), brush.Color);
    }

    private static ResourceDictionary CreateDictionary(
        string key,
        uint argb,
        out object descriptor)
    {
        var dictionary = new ResourceDictionary();
        dictionary.AddDeferredLiteralSolidColorBrush(key, argb);
        descriptor = GetStoredEntry(dictionary, key);
        Assert.Equal(GetDescriptorType(), descriptor.GetType());
        return dictionary;
    }

    private static void AssertMaterializes(
        string key,
        uint argb,
        Func<ResourceDictionary, object?> read)
    {
        var dictionary = CreateDictionary(key, argb, out object descriptor);
        Assert.Null(GetDescriptorState(descriptor));

        object? value = read(dictionary);

        var brush = Assert.IsType<SolidColorBrush>(value);
        Assert.Same(brush, GetDescriptorState(descriptor));
    }

    private static Type GetDescriptorType()
        => typeof(ResourceDictionary).GetNestedType(
            "DeferredLiteralSolidColorBrushEntry",
            BindingFlags.NonPublic)!;

    private static object GetStoredEntry(ResourceDictionary dictionary, object key)
    {
        var entries = (IDictionary)typeof(ResourceDictionary).GetField(
                "_innerDictionary",
                BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(dictionary)!;
        return entries[key]!;
    }

    private static object? GetDescriptorState(object descriptor)
        => descriptor.GetType().GetField(
                "_valueOrState",
                BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(descriptor);

    private sealed class ReplacingResourceDictionary : ResourceDictionary
    {
        private readonly object _replacement;

        public ReplacingResourceDictionary(object replacement)
        {
            _replacement = replacement;
        }

        public List<object?> ObservedValues { get; } = [];

        protected override void OnGettingValue(object key, ref object? value, out bool canCache)
        {
            ObservedValues.Add(value);
            if (value is SolidColorBrush)
            {
                value = _replacement;
            }

            canCache = true;
        }
    }
}

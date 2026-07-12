using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Jalium.UI.Controls;
using Jalium.UI.Data;
using Jalium.UI.Diagnostics;
using Jalium.UI.Markup;
using MarkupXamlReader = Jalium.UI.Markup.XamlReader;

namespace Jalium.UI.Tests;

public sealed class DiagnosticsWpfParityTests
{
    [Fact]
    public void PresentationTraceSourcesExposeTheWpfSourcesAndWeakTraceLevelStore()
    {
        Assert.Equal(0, (int)PresentationTraceLevel.None);
        Assert.Equal(1, (int)PresentationTraceLevel.Low);
        Assert.Equal(2, (int)PresentationTraceLevel.Medium);
        Assert.Equal(3, (int)PresentationTraceLevel.High);

        Assert.Equal("TraceLevel", PresentationTraceSources.TraceLevelProperty.Name);
        Assert.Equal(typeof(PresentationTraceLevel), PresentationTraceSources.TraceLevelProperty.PropertyType);
        Assert.Equal(typeof(PresentationTraceSources), PresentationTraceSources.TraceLevelProperty.OwnerType);

        var element = new object();
        Assert.Equal(PresentationTraceLevel.None, PresentationTraceSources.GetTraceLevel(element));
        PresentationTraceSources.SetTraceLevel(element, PresentationTraceLevel.High);
        Assert.Equal(PresentationTraceLevel.High, PresentationTraceSources.GetTraceLevel(element));
        PresentationTraceSources.SetTraceLevel(element, PresentationTraceLevel.None);
        Assert.Equal(PresentationTraceLevel.None, PresentationTraceSources.GetTraceLevel(element));
        Assert.Equal(PresentationTraceLevel.None, PresentationTraceSources.GetTraceLevel(null));
        PresentationTraceSources.SetTraceLevel(null, PresentationTraceLevel.High);

        Assert.Equal("System.Windows.DependencyProperty", PresentationTraceSources.DependencyPropertySource.Name);
        Assert.Equal("System.Windows.Freezable", PresentationTraceSources.FreezableSource.Name);
        Assert.Equal("System.Windows.NameScope", PresentationTraceSources.NameScopeSource.Name);
        Assert.Equal("System.Windows.RoutedEvent", PresentationTraceSources.RoutedEventSource.Name);
        Assert.Equal("System.Windows.Media.Animation", PresentationTraceSources.AnimationSource.Name);
        Assert.Equal("System.Windows.Data", PresentationTraceSources.DataBindingSource.Name);
        Assert.Equal("System.Windows.Documents", PresentationTraceSources.DocumentsSource.Name);
        Assert.Equal("System.Windows.ResourceDictionary", PresentationTraceSources.ResourceDictionarySource.Name);
        Assert.Equal("System.Windows.Markup", PresentationTraceSources.MarkupSource.Name);
        Assert.Equal("System.Windows.Interop.HwndHost", PresentationTraceSources.HwndHostSource.Name);
        Assert.Equal("System.Windows.Shell", PresentationTraceSources.ShellSource.Name);
        Assert.Same(PresentationTraceSources.DataBindingSource, PresentationTraceSources.DataBindingSource);
    }

    [Fact]
    public void BindingFailureEventCarriesTheExpressionAndFeedsTheExistingDiagnosticsPipeline()
    {
        var model = new NumericModel { Value = 10 };
        var target = new Border { DataContext = model };
        var binding = new Binding(nameof(NumericModel.Value)) { Mode = BindingMode.TwoWay };
        binding.ValidationRules.Add(new NonNegativeValidationRule());

        var failures = new List<BindingFailedEventArgs>();
        EventHandler<BindingFailedEventArgs> handler = (_, args) =>
        {
            lock (failures)
            {
                failures.Add(args);
            }
        };
        BindingDiagnostics.BindingFailed += handler;
        try
        {
            var expression = Assert.IsType<BindingExpression>(
                BindingOperations.SetBinding(target, FrameworkElement.TagProperty, binding));

            target.SetValue(FrameworkElement.TagProperty, -1);
            expression.UpdateSource();

            BindingFailedEventArgs? observed;
            lock (failures)
            {
                observed = failures.LastOrDefault(args => ReferenceEquals(args.Binding, expression));
            }

            Assert.NotNull(observed);
            Assert.Equal(TraceEventType.Error, observed!.EventType);
            Assert.Equal(0, observed.Code);
            Assert.Equal("Value must be non-negative.", observed.Message);
            Assert.Same(expression, observed.Binding);
            Assert.Equal(new object[] { target, FrameworkElement.TagProperty }, observed.Parameters);
            Assert.Contains(
                BindingDiagnostics.Snapshot(),
                entry => entry.Kind == BindingDiagnostics.BindingEventKind.Error &&
                         entry.Message == observed.Message);
        }
        finally
        {
            BindingDiagnostics.BindingFailed -= handler;
        }
    }

    [Fact]
    public void VisualTreeNotificationsReportAddAndRemoveWithWpfChildIndexes()
    {
        var parent = new TestVisual();
        var child = new TestVisual();
        var changes = new List<VisualTreeChangeEventArgs>();
        EventHandler<VisualTreeChangeEventArgs> handler = (_, args) =>
        {
            if (ReferenceEquals(args.Parent, parent))
            {
                changes.Add(args);
            }
        };

        VisualDiagnostics.EnableVisualTreeChanged();
        VisualDiagnostics.VisualTreeChanged += handler;
        try
        {
            parent.Attach(child);
            parent.Detach(child);
        }
        finally
        {
            VisualDiagnostics.VisualTreeChanged -= handler;
            VisualDiagnostics.DisableVisualTreeChanged();
        }

        Assert.Collection(
            changes,
            added =>
            {
                Assert.Same(parent, added.Parent);
                Assert.Same(child, added.Child);
                Assert.Equal(0, added.ChildIndex);
                Assert.Equal(VisualTreeChangeType.Add, added.ChangeType);
            },
            removed =>
            {
                Assert.Same(parent, removed.Parent);
                Assert.Same(child, removed.Child);
                Assert.Equal(-1, removed.ChildIndex);
                Assert.Equal(VisualTreeChangeType.Remove, removed.ChangeType);
            });
    }

    [Fact]
    public void XamlReaderAssociatesMaterializedObjectsWithTheirSourceLocation()
    {
        var sourceUri = new Uri("resource:///Tests/Diagnostics.xaml");
        var context = new ParserContext { BaseUri = sourceUri };

        var grid = Assert.IsType<Grid>(MarkupXamlReader.Parse("<Grid />", context));
        var info = VisualDiagnostics.GetXamlSourceInfo(grid);

        Assert.NotNull(info);
        Assert.Equal(sourceUri, info!.SourceUri);
        Assert.Equal(1, info.LineNumber);
        Assert.True(info.LinePosition > 0);
        Assert.Null(VisualDiagnostics.GetXamlSourceInfo(null));
    }

    [Fact]
    public void ResourceDictionaryDiagnosticsTrackSourcesOwnersMergesAndEvents()
    {
        var sourceUri = new Uri("resource:///Tests/Resources.xaml");
        var dictionary = new ResourceDictionary { Source = sourceUri };
        Assert.Contains(
            dictionary,
            ResourceDictionaryDiagnostics.GetResourceDictionariesForSource(sourceUri));

        var owner = new TestVisual { Resources = dictionary };
        Assert.Contains(owner, ResourceDictionaryDiagnostics.GetFrameworkElementOwners(dictionary));

        var merged = new ResourceDictionary();
        owner.Resources.MergedDictionaries.Add(merged);
        Assert.Contains(owner, ResourceDictionaryDiagnostics.GetFrameworkElementOwners(merged));

        var contentOwner = new FrameworkContentElement { Resources = dictionary };
        Assert.Contains(
            contentOwner,
            ResourceDictionaryDiagnostics.GetFrameworkContentElementOwners(dictionary));

        ResourceDictionaryLoadedEventArgs? genericLoaded = null;
        EventHandler<ResourceDictionaryLoadedEventArgs> loadedHandler = (_, args) => genericLoaded = args;
        ResourceDictionaryDiagnostics.GenericResourceDictionaryLoaded += loadedHandler;
        try
        {
            ResourceDictionaryDiagnostics.RegisterGenericResourceDictionary(dictionary);
            Assert.Same(dictionary, genericLoaded!.ResourceDictionaryInfo.ResourceDictionary);
            Assert.Equal(sourceUri, genericLoaded.ResourceDictionaryInfo.SourceUri);
            Assert.Contains(
                ResourceDictionaryDiagnostics.GenericResourceDictionaries,
                info => ReferenceEquals(info.ResourceDictionary, dictionary));
        }
        finally
        {
            ResourceDictionaryDiagnostics.UnregisterGenericResourceDictionary(dictionary);
            ResourceDictionaryDiagnostics.GenericResourceDictionaryLoaded -= loadedHandler;
        }

        StaticResourceResolvedEventArgs? resolved = null;
        EventHandler<StaticResourceResolvedEventArgs> resolvedHandler = (_, args) => resolved = args;
        ResourceDictionaryDiagnostics.StaticResourceResolved += resolvedHandler;
        try
        {
            ResourceDictionaryDiagnosticsStore.NotifyStaticResourceResolved(
                owner,
                FrameworkElement.TagProperty,
                dictionary,
                "key");
            Assert.Same(owner, resolved!.TargetObject);
            Assert.Same(FrameworkElement.TagProperty, resolved.TargetProperty);
            Assert.Same(dictionary, resolved.ResourceDictionary);
            Assert.Equal("key", resolved.ResourceKey);
        }
        finally
        {
            ResourceDictionaryDiagnostics.StaticResourceResolved -= resolvedHandler;
        }

        dictionary.Source = null;
        Assert.Empty(ResourceDictionaryDiagnostics.GetResourceDictionariesForSource(sourceUri));
    }

    [Fact]
    public void DiagnosticsPublicConstructorsAndParameterNamesMatchWpf()
    {
        Assert.Empty(typeof(BindingFailedEventArgs).GetConstructors(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        Assert.Empty(typeof(ResourceDictionaryInfo).GetConstructors(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        Assert.Empty(typeof(ResourceDictionaryLoadedEventArgs).GetConstructors(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        Assert.Empty(typeof(ResourceDictionaryUnloadedEventArgs).GetConstructors(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));
        Assert.Empty(typeof(StaticResourceResolvedEventArgs).GetConstructors(
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly));

        Assert.Equal(
            ["parent", "child", "childIndex", "changeType"],
            typeof(VisualTreeChangeEventArgs).GetConstructors(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Single()
                .GetParameters()
                .Select(parameter => parameter.Name));
        Assert.Equal(
            ["sourceUri", "lineNumber", "linePosition"],
            typeof(XamlSourceInfo).GetConstructors(
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Single()
                .GetParameters()
                .Select(parameter => parameter.Name));

        Assert.Equal(
            "obj",
            typeof(VisualDiagnostics).GetMethod(nameof(VisualDiagnostics.GetXamlSourceInfo))!
                .GetParameters()
                .Single()
                .Name);
        Assert.Equal(
            "uri",
            typeof(ResourceDictionaryDiagnostics)
                .GetMethod(nameof(ResourceDictionaryDiagnostics.GetResourceDictionariesForSource))!
                .GetParameters()
                .Single()
                .Name);
        Assert.All(
            new[]
            {
                nameof(ResourceDictionaryDiagnostics.GetFrameworkElementOwners),
                nameof(ResourceDictionaryDiagnostics.GetFrameworkContentElementOwners),
                nameof(ResourceDictionaryDiagnostics.GetApplicationOwners),
            },
            methodName => Assert.Equal(
                "dictionary",
                typeof(ResourceDictionaryDiagnostics).GetMethod(methodName)!
                    .GetParameters()
                    .Single()
                    .Name));
    }

    private sealed class TestVisual : FrameworkElement
    {
        public void Attach(Visual child) => AddVisualChild(child);
        public void Detach(Visual child) => RemoveVisualChild(child);
    }

    private sealed class NumericModel
    {
        public int Value { get; set; }
    }

    private sealed class NonNegativeValidationRule : ValidationRule
    {
        public override ValidationResult Validate(object? value, CultureInfo cultureInfo) =>
            value is int number && number >= 0
                ? ValidationResult.ValidResult
                : new ValidationResult(false, "Value must be non-negative.");
    }
}

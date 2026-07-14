using System.Collections;
using System.Globalization;
using Jalium.UI.Markup;
using Jalium.UI.Navigation;

namespace Jalium.UI.Tests;

public class NavigationCompatibilityThirdPassParityTests
{
    [Fact]
    public void BaseUriHelper_UsesUriContextAndApplicationFallback()
    {
        var relative = new Uri("views/home.xaml", UriKind.Relative);
        var element = new UriElement { BaseUri = relative };

        Uri resolved = BaseUriHelper.GetBaseUri(element);
        Assert.True(resolved.IsAbsoluteUri);
        Assert.EndsWith("views/home.xaml", resolved.LocalPath.Replace('\\', '/'));
        Assert.True(BaseUriHelper.GetBaseUri(new DependencyObject()).IsAbsoluteUri);
    }

    [Fact]
    public void PageFunction_RaisesTypedReturnAndTracksCompletion()
    {
        var page = new TestPageFunction { RemoveFromJournal = false };
        ReturnEventArgs<int>? received = null;
        bool finished = false;
        page.Return += (_, e) => received = e;
        page.Finished += (_, _) => finished = true;

        page.Complete(42);

        Assert.Equal(42, received?.Result);
        Assert.True(finished);
        Assert.False(page.RemoveFromJournal);
        Assert.NotEqual(Guid.Empty, page.PageFunctionId);
    }

    [Fact]
    public void JournalConverters_ProduceTypedListsAndPositionMetadata()
    {
        var back = new JournalEntry(new Uri("https://example.test/back"), null, null, false);
        var forward = new JournalEntry(new Uri("https://example.test/forward"), null, null, false);
        var converter = new JournalEntryUnifiedViewConverter();

        var combined = Assert.IsType<List<JournalEntry>>(converter.Convert(
            new object?[] { new[] { back }, new[] { forward } },
            typeof(IEnumerable<JournalEntry>),
            null,
            CultureInfo.InvariantCulture));

        Assert.Equal(new[] { forward, back }, combined);
        Assert.Equal(JournalEntryPosition.Forward,
            JournalEntryUnifiedViewConverter.GetJournalEntryPosition(forward));
        Assert.Equal(JournalEntryPosition.Back,
            JournalEntryUnifiedViewConverter.GetJournalEntryPosition(back));

        var list = Assert.IsType<List<object?>>(new JournalEntryListConverter().Convert(
            combined,
            typeof(IEnumerable),
            null,
            CultureInfo.InvariantCulture));
        Assert.Equal(2, list.Count);
    }

    private sealed class UriElement : DependencyObject, IUriContext
    {
        public Uri? BaseUri { get; set; }
    }

    private sealed class TestPageFunction : PageFunction<int>
    {
        internal void Complete(int result) => OnReturn(new ReturnEventArgs<int>(result));
    }
}

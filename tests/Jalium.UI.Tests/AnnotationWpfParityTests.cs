using System.Xml;
using Jalium.UI.Annotations;
using Jalium.UI.Annotations.Storage;
using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public sealed class AnnotationWpfParityTests
{
    [Fact]
    public void AnnotationCollectionsRaiseTypedEventsAndTrackResourceChanges()
    {
        var annotation = new Annotation(new XmlQualifiedName("Review", "urn:test"));
        AnnotationAuthorChangedEventArgs? authorChange = null;
        AnnotationResourceChangedEventArgs? anchorChange = null;
        annotation.AuthorChanged += (_, args) => authorChange = args;
        annotation.AnchorChanged += (_, args) => anchorChange = args;

        annotation.Authors.Add("Ada");
        Assert.NotNull(authorChange);
        Assert.Equal(AnnotationAction.Added, authorChange.Action);
        Assert.Equal("Ada", authorChange.Author);

        var anchor = new AnnotationResource("selection");
        annotation.Anchors.Add(anchor);
        Assert.Equal(AnnotationAction.Added, anchorChange!.Action);
        Assert.Same(anchor, anchorChange.Resource);

        anchor.Name = "updated";
        Assert.Equal(AnnotationAction.Modified, anchorChange.Action);

        annotation.Anchors.Remove(anchor);
        Assert.Equal(AnnotationAction.Removed, anchorChange.Action);
    }

    [Fact]
    public void ContentLocatorsCloneCompareAndMatchPrefixes()
    {
        var prefix = CreateLocator(("Page", "1"));
        var locator = CreateLocator(("Page", "1"), ("Run", "4"));

        Assert.True(locator.StartsWith(prefix));
        Assert.False(prefix.StartsWith(locator));

        var clone = Assert.IsType<ContentLocator>(locator.Clone());
        Assert.NotSame(locator, clone);
        Assert.Equal(locator.Parts[0], clone.Parts[0]);
        clone.Parts[0].NameValuePairs["Value"] = "2";
        Assert.NotEqual(locator.Parts[0], clone.Parts[0]);
    }

    [Fact]
    public void XmlStreamStorePersistsQueriesAndForwardsDetailedEvents()
    {
        using var stream = new MemoryStream();
        using (var store = new XmlStreamStore(stream))
        {
            StoreContentChangedEventArgs? storeChange = null;
            AnnotationResourceChangedEventArgs? anchorChange = null;
            store.StoreContentChanged += (_, args) => storeChange = args;
            store.AnchorChanged += (_, args) => anchorChange = args;

            var annotation = new Annotation(new XmlQualifiedName("Highlight", "urn:test"));
            var anchor = new AnnotationResource("anchor");
            anchor.ContentLocators.Add(CreateLocator(("Page", "7"), ("Run", "2")));
            annotation.Anchors.Add(anchor);
            annotation.Authors.Add("Grace");
            store.AddAnnotation(annotation);

            Assert.Equal(StoreContentAction.Added, storeChange!.Action);
            Assert.Single(store.GetAnnotations(CreateLocator(("Page", "7"))));

            anchor.Name = "changed";
            Assert.Equal(AnnotationAction.Modified, anchorChange!.Action);

            var document = new XmlDocument();
            var element = document.CreateElement("Payload", "urn:test");
            element.InnerText = "round trip";
            annotation.Cargos.Add(new AnnotationResource("cargo") { Contents = { element } });
            store.Flush();
        }

        stream.Position = 0;
        using var reloaded = new XmlStreamStore(stream);
        var restored = Assert.Single(reloaded.GetAnnotations());
        Assert.Equal("Grace", Assert.Single(restored.Authors));
        Assert.Equal("round trip", Assert.Single(Assert.Single(restored.Cargos).Contents).InnerText);
        Assert.Equal(restored, reloaded.DeleteAnnotation(restored.Id));
        Assert.Empty(reloaded.GetAnnotations());
    }

    [Fact]
    public void AnnotationServiceAndHelperOperateAgainstEnabledStore()
    {
        var reader = new FlowDocumentReader();
        var service = new AnnotationService(reader);
        Assert.Same(service, AnnotationService.GetService(reader));
        Assert.False(service.IsEnabled);

        using var stream = new MemoryStream();
        using var store = new XmlStreamStore(stream);
        service.Enable(store);

        var note = AnnotationHelper.CreateTextStickyNoteForSelection(service, "Lin");
        var highlight = AnnotationHelper.CreateHighlightForSelection(service, "Lin", Brushes.Yellow);
        Assert.Equal(2, store.GetAnnotations().Count);
        Assert.Single(note.Anchors);
        Assert.NotNull(AnnotationHelper.GetAnchorInfo(service, note));

        AnnotationHelper.ClearHighlightsForSelection(service);
        Assert.Null(store.GetAnnotation(highlight.Id));
        Assert.NotNull(store.GetAnnotation(note.Id));

        AnnotationHelper.DeleteTextStickyNotesForSelection(service);
        Assert.Empty(store.GetAnnotations());
        service.Disable();
        Assert.False(service.IsEnabled);
        Assert.Null(service.Store);
    }

    [Fact]
    public void AnnotationPaginatorDelegatesDocumentOperations()
    {
        var source = new TestPaginator();
        using var stream = new MemoryStream();
        var paginator = new AnnotationDocumentPaginator(source, stream, FlowDirection.RightToLeft);

        Assert.True(paginator.IsPageCountValid);
        Assert.Equal(1, paginator.PageCount);
        Assert.Same(source, paginator.Source);
        Assert.Same(DocumentPage.Missing, paginator.GetPage(0));
        Assert.Equal(FlowDirection.RightToLeft, paginator.FlowDirection);

        paginator.PageSize = new Size(320, 240);
        Assert.Equal(new Size(320, 240), source.PageSize);
    }

    private static ContentLocator CreateLocator(params (string Name, string Value)[] parts)
    {
        var locator = new ContentLocator();
        foreach (var (name, value) in parts)
        {
            var part = new ContentLocatorPart(new XmlQualifiedName(name, "urn:test"));
            part.NameValuePairs["Value"] = value;
            locator.Parts.Add(part);
        }
        return locator;
    }

    private sealed class TestPaginator : DocumentPaginator, IDocumentPaginatorSource
    {
        public override bool IsPageCountValid => true;
        public override int PageCount => 1;
        public override Size PageSize { get; set; } = new(100, 100);
        public override IDocumentPaginatorSource Source => this;
        public DocumentPaginator DocumentPaginator => this;
        public override DocumentPage GetPage(int pageNumber) => DocumentPage.Missing;
    }
}

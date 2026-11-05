using Jalium.UI.Controls;
using Jalium.UI.Documents;
using Jalium.UI.Interop;
using Xunit;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSFlowDocumentPageNavigationTests
{
    [Theory]
    [InlineData(RenderBackend.Software)]
    [InlineData(RenderBackend.Metal)]
    public void ExplicitPageNavigationSurvivesTheContinuousViewsContainingRow(RenderBackend backend)
    {
        using var context = RenderContext.GetOrCreateCurrent(backend);
        var document = FlowDocument.FromText(string.Join(' ', Enumerable.Repeat("segment", 120)));
        document.PageWidth = 100; document.PageHeight = 60;
        document.PagePadding = new Thickness(4); document.FontSize = 10;
        var viewer = new FlowDocumentScrollViewer
        {
            Style = (Style)MenuFlyoutTemplateTestHelper.Themes.Value[typeof(FlowDocumentScrollViewer)],
            Document = document,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        viewer.Measure(new Size(220, 120)); viewer.Arrange(new Rect(0, 0, 220, 120));
        Assert.True(viewer.PageCountInternal >= 6);
        // A continuous row is wider than a paginated row. The requested page's
        // first source offset can sit inside a row which began on an earlier page.
        foreach (int page in new[] { 2, 3, 4, 5, 6, 3, 1 })
        {
            viewer.GoToPageInternal(page);
            Assert.Equal(page, viewer.PageNumberInternal);
        }
    }
}

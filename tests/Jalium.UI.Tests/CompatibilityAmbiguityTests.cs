using System.Reflection;
using Jalium.UI;

namespace Jalium.UI.Tests;

public sealed class CompatibilityAmbiguityTests
{
    [Fact]
    public void RetiredCompatibilityAliasesAreNeitherExportedNorForwarded()
    {
        var retiredNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Jalium.UI.ContentPropertyAttribute",
            "Jalium.UI.DesignerSerializationOptions",
            "Jalium.UI.DesignerSerializationOptionsAttribute",
            "Jalium.UI.INameScope",
            "Jalium.UI.DataTemplateSelector",
            "Jalium.UI.ItemsPanelTemplate",
            "Jalium.UI.ItemContainerTemplate",
            "Jalium.UI.HandoffBehavior",
            "Jalium.UI.Controls.WindowInteropHelper",
            "Jalium.UI.Data.SystemFonts",
            "Jalium.UI.Controls.Clipboard",
            "Jalium.UI.Controls.FileDialog",
            "Jalium.UI.Controls.OpenFileDialog",
            "Jalium.UI.Controls.SaveFileDialog",
            "Jalium.UI.Controls.FileDialogCustomPlace",
            "Jalium.UI.Controls.DataFormats",
            "Jalium.UI.Controls.IDataObject",
            "Jalium.UI.Data.CurrentChangingEventArgs",
            "Jalium.UI.Data.CurrentChangingEventHandler",
            "Jalium.UI.Data.ICollectionView",
            "Jalium.UI.Data.IEditableCollectionView",
            "Jalium.UI.Data.ListSortDirection",
            "Jalium.UI.Data.SortDescription",
            "Jalium.UI.Data.SortDescriptionCollection",
            "Jalium.UI.Data.GroupDescription",
            "Jalium.UI.CollectionChangedEventManager",
            "Jalium.UI.PropertyChangedEventManager",
            "Jalium.UI.DependencyPropertyDescriptor",
            "Jalium.UI.Controls.DesignerProperties",
            "Jalium.UI.Media.D3DImage",
            "Jalium.UI.Media.D3DResourceType",
            "Jalium.UI.Documents.TextMarkerStyle",
            "Jalium.UI.Controls.Primitives.DoubleCollection",
            "Jalium.UI.Controls.LocalizabilityAttribute",
            "Jalium.UI.Controls.LocalizationCategory",
            "Jalium.UI.Controls.Readability",
            "Jalium.UI.Controls.Modifiability",
            "Jalium.UI.Controls.Primitives.DocumentPage",
            "Jalium.UI.Controls.Primitives.DocumentPaginator",
            "Jalium.UI.Controls.Primitives.IDocumentPaginatorSource",
            "Jalium.UI.Controls.Printing.DocumentPage",
            "Jalium.UI.Controls.Printing.DocumentPaginator",
            "Jalium.UI.Controls.Printing.IDocumentPaginatorSource",
            "Jalium.UI.Media.Imaging.InteropBitmap",
            "Jalium.UI.Media.Animation.MediaTimeline",
            "Jalium.UI.Media.Typography",
            "Jalium.UI.Media.IValueSerializerContext",
            "Jalium.UI.Media.ValueSerializer",
            "Jalium.UI.Markup.BrushConverter",
            "Jalium.UI.Markup.ColorConverter",
            "Jalium.UI.Markup.PointCollectionConverter",
            "Jalium.UI.Media.BitmapSource",
            "Jalium.UI.Media.BitmapImage",
            "Jalium.UI.Media.RenderTargetBitmap",
            "Jalium.UI.Media.WriteableBitmap",
            "Jalium.UI.Media.JaliumBitmapSource",
            "Jalium.UI.Media.JaliumBitmapImage",
            "Jalium.UI.Media.JaliumRenderTargetBitmap",
            "Jalium.UI.Media.JaliumWriteableBitmap",
            "Jalium.UI.Controls.Navigation.NavigationService",
            "Jalium.UI.Controls.Navigation.NavigationEventArgs",
            "Jalium.UI.Controls.Navigation.NavigatingCancelEventArgs",
            "Jalium.UI.Controls.Navigation.PageFunction`1",
        };

        Type[] exported = typeof(FrameworkElement).Assembly.GetExportedTypes();
        Assert.DoesNotContain(exported, type => type.FullName is { } name && retiredNames.Contains(name));

        foreach (string facadeName in new[] { "Jalium.UI.Core", "Jalium.UI.Media", "Jalium.UI.Controls" })
        {
            Type[] forwarded = Assembly.Load(facadeName).GetForwardedTypes();
            Assert.DoesNotContain(forwarded, type => type.FullName is { } name && retiredNames.Contains(name));
        }
    }

    [Fact]
    public void HistoricalSameNameMediaTypesAreNotExportedAlongsideCanonicalWpfTypes()
    {
        Type[] exported = typeof(FrameworkElement).Assembly.GetExportedTypes();

        string[] removedHistoricalNames =
        [
            "Jalium.UI.Media.Imaging.InteropBitmap",
            "Jalium.UI.Media.Animation.MediaTimeline",
            "Jalium.UI.Media.Typography",
            "Jalium.UI.Media.IValueSerializerContext",
            "Jalium.UI.Media.ValueSerializer",
            "Jalium.UI.Markup.BrushConverter",
            "Jalium.UI.Markup.ColorConverter",
            "Jalium.UI.Markup.PointCollectionConverter",
            "Jalium.UI.Media.BitmapSource",
            "Jalium.UI.Media.BitmapImage",
            "Jalium.UI.Media.RenderTargetBitmap",
            "Jalium.UI.Media.WriteableBitmap",
        ];

        Assert.DoesNotContain(exported, type => removedHistoricalNames.Contains(type.FullName));

        string[] canonicalWpfNames =
        [
            "Jalium.UI.Interop.InteropBitmap",
            "Jalium.UI.Media.MediaTimeline",
            "Jalium.UI.Documents.Typography",
            "Jalium.UI.Markup.IValueSerializerContext",
            "Jalium.UI.Markup.ValueSerializer",
            "Jalium.UI.Media.BrushConverter",
            "Jalium.UI.Media.ColorConverter",
            "Jalium.UI.Media.PointCollectionConverter",
            "Jalium.UI.Media.Imaging.BitmapSource",
            "Jalium.UI.Media.Imaging.BitmapImage",
            "Jalium.UI.Media.Imaging.RenderTargetBitmap",
            "Jalium.UI.Media.Imaging.WriteableBitmap",
        ];

        Assert.All(canonicalWpfNames, name => Assert.Contains(exported, type => type.FullName == name));
    }

    [Fact]
    public void JaliumBitmapImplementationNamesAreNotExported()
    {
        Type[] exported = typeof(FrameworkElement).Assembly.GetExportedTypes();

        string[] implementationNames =
        [
            "Jalium.UI.Media.JaliumBitmapSource",
            "Jalium.UI.Media.JaliumBitmapImage",
            "Jalium.UI.Media.JaliumRenderTargetBitmap",
            "Jalium.UI.Media.JaliumWriteableBitmap",
        ];

        Assert.All(implementationNames, name => Assert.DoesNotContain(exported, type => type.FullName == name));
    }
}

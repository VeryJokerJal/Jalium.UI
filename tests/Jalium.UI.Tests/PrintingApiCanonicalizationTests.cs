using System.ComponentModel;
using System.Reflection;
using System.Printing;
using Jalium.UI.Controls;
using Jalium.UI.Documents.Serialization;
using Jalium.UI.Xps;

namespace Jalium.UI.Tests;

public sealed class PrintingApiCanonicalizationTests
{
    [Fact]
    public void PrintDialogTypesExistOnlyInTheWpfControlsNamespace()
    {
        Type dialogType = typeof(PrintDialog);
        Assembly assembly = dialogType.Assembly;

        Assert.Equal("Jalium.UI.Controls", dialogType.Namespace);
        Assert.False(dialogType.IsSealed);
        Assert.Equal("Jalium.UI.Controls", typeof(PrintDialogException).Namespace);
        Assert.Null(assembly.GetType("Jalium.UI.Controls.Printing.PrintDialog"));
        Assert.Null(assembly.GetType("Jalium.UI.Controls.Printing.PrintDialogException"));

        string[] platformExtensions =
        [
            "IsSupported",
            "IsPortalPrintAvailable",
            "PageRangeFrom",
            "PageRangeTo",
            "CurrentPage",
        ];
        Assert.All(
            platformExtensions,
            name => Assert.Null(dialogType.GetMember(name, BindingFlags.Public | BindingFlags.Static |
                BindingFlags.Instance).SingleOrDefault()));

        MethodInfo[] publicShowDialogOverloads = dialogType.GetMethods()
            .Where(method => method.Name == nameof(PrintDialog.ShowDialog))
            .ToArray();
        Assert.Single(publicShowDialogOverloads);
        Assert.Empty(publicShowDialogOverloads[0].GetParameters());
    }

    [Fact]
    public void XpsWriterUsesTheCanonicalNamespaceBaseAndSerializationEvents()
    {
        Type writerType = typeof(XpsDocumentWriter);
        Assembly assembly = writerType.Assembly;

        Assert.Equal("Jalium.UI.Xps", writerType.Namespace);
        Assert.False(writerType.IsSealed);
        Assert.Equal(typeof(SerializerWriter), writerType.BaseType);
        Assert.Null(assembly.GetType("Jalium.UI.Controls.Printing.XpsDocumentWriter"));
        Assert.Null(assembly.GetType("Jalium.UI.Controls.Printing.WritingCompletedEventArgs"));
        Assert.Null(assembly.GetType("Jalium.UI.Controls.Printing.WritingProgressChangedEventArgs"));
        Assert.Null(assembly.GetType("Jalium.UI.Controls.Printing.WritingPrintTicketRequiredEventArgs"));
        Assert.Null(assembly.GetType("Jalium.UI.Controls.Printing.PrintTicketLevel"));

        Assert.Equal(
            typeof(WritingCompletedEventHandler),
            writerType.GetEvent(nameof(XpsDocumentWriter.WritingCompleted))!.EventHandlerType);
        Assert.Equal(
            typeof(WritingProgressChangedEventHandler),
            writerType.GetEvent(nameof(XpsDocumentWriter.WritingProgressChanged))!.EventHandlerType);
        Assert.Equal(
            typeof(WritingPrintTicketRequiredEventHandler),
            writerType.GetEvent(nameof(XpsDocumentWriter.WritingPrintTicketRequired))!.EventHandlerType);
        Assert.Equal(
            typeof(WritingCancelledEventHandler),
            writerType.GetEvent(nameof(XpsDocumentWriter.WritingCancelled))!.EventHandlerType);

        ConstructorInfo constructor = writerType.GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(PrintQueue)],
            modifiers: null)!;
        Assert.True(constructor.IsAssembly);
        Assert.Empty(writerType.GetConstructors(BindingFlags.Instance | BindingFlags.Public));

        var queue = new PrintQueue(new PrintServer(), "test-printer");
        Assert.IsType<XpsDocumentWriter>(queue.CreateXpsDocumentWriter());
    }

    [Fact]
    public void PrintModelsExistOnlyInTheCanonicalSystemPrintingNamespace()
    {
        Assembly assembly = typeof(PrintQueue).Assembly;
        string[] modelNames =
        [
            nameof(Collation),
            nameof(Duplexing),
            nameof(InputBin),
            nameof(OutputColor),
            nameof(OutputQuality),
            nameof(PageMediaSize),
            nameof(PageMediaSizeName),
            nameof(PageOrder),
            nameof(PageOrientation),
            nameof(PageResolution),
            nameof(PrintCapabilities),
            nameof(PrintJobStatus),
            nameof(PrintQueue),
            nameof(PrintServer),
            nameof(PrintSystemJobInfo),
            nameof(PrintTicket),
            nameof(Stapling),
        ];

        Assert.All(modelNames, name =>
        {
            Assert.NotNull(assembly.GetType("System.Printing." + name));
            Assert.Null(assembly.GetType("Jalium.UI.Controls.Printing." + name));
        });

        Assert.Null(assembly.GetType("System.Printing.PagesPerSheet"));
        Assert.Null(assembly.GetType("System.Printing.PaginationCompletedEventArgs"));
        Assert.Null(assembly.GetType("Jalium.UI.Controls.Printing.PagesPerSheet"));
        Assert.Null(assembly.GetType("Jalium.UI.Controls.Printing.PaginationCompletedEventArgs"));
    }

    [Fact]
    public void CorePrintModelsRetainTheWpfTypeShapes()
    {
        Assert.False(typeof(PrintQueue).IsSealed);
        Assert.Equal(typeof(PrintSystemObject), typeof(PrintQueue).BaseType);
        Assert.False(typeof(PrintServer).IsSealed);
        Assert.Equal(typeof(PrintSystemObject), typeof(PrintServer).BaseType);
        Assert.False(typeof(PrintSystemJobInfo).IsSealed);
        Assert.Equal(typeof(PrintSystemObject), typeof(PrintSystemJobInfo).BaseType);

        ConstructorInfo[] queueConstructors = typeof(PrintQueue).GetConstructors();
        Assert.NotEmpty(queueConstructors);
        Assert.All(queueConstructors, constructor =>
            Assert.Equal(typeof(PrintServer), constructor.GetParameters()[0].ParameterType));
        Assert.DoesNotContain(queueConstructors, constructor =>
            constructor.GetParameters().Select(parameter => parameter.ParameterType)
                .SequenceEqual([typeof(string)]));

        Assert.True(typeof(PrintTicket).IsSealed);
        Assert.Contains(typeof(INotifyPropertyChanged), typeof(PrintTicket).GetInterfaces());
        Assert.NotNull(typeof(PrintTicket).GetConstructor([typeof(Stream)]));
        Assert.Equal(typeof(int?), typeof(PrintTicket).GetProperty(nameof(PrintTicket.CopyCount))!.PropertyType);
        Assert.Equal(typeof(int?), typeof(PrintTicket).GetProperty(nameof(PrintTicket.PagesPerSheet))!.PropertyType);

        ConstructorInfo[] capabilitiesConstructors = typeof(PrintCapabilities).GetConstructors();
        Assert.Single(capabilitiesConstructors);
        Assert.Equal(typeof(Stream), capabilitiesConstructors[0].GetParameters().Single().ParameterType);
        Assert.Equal(
            typeof(System.Collections.ObjectModel.ReadOnlyCollection<Collation>),
            typeof(PrintCapabilities).GetProperty(nameof(PrintCapabilities.CollationCapability))!.PropertyType);
    }

    [Fact]
    public void PrintSchemaEnumsUseTheWpfValues()
    {
        Assert.Equal(1, (int)Collation.Collated);
        Assert.Equal(2, (int)Collation.Uncollated);
        Assert.Equal(3, (int)Duplexing.TwoSidedLongEdge);
        Assert.Equal(1, (int)PageOrientation.Landscape);
        Assert.Equal(2, (int)PageOrientation.Portrait);
        Assert.Equal(93, (int)PageMediaSizeName.NorthAmericaLetter);
        Assert.Equal(91, (int)PageMediaSizeName.NorthAmericaLegal);
        Assert.Equal(5, (int)InputBin.Manual);
        Assert.Equal(10, (int)Stapling.None);
        Assert.Equal(512, (int)PrintJobStatus.Blocked);
    }

    [Fact]
    public void SerializationEventArgumentsRetainTheWpfContracts()
    {
        Assert.Equal(typeof(AsyncCompletedEventArgs), typeof(WritingCompletedEventArgs).BaseType);
        Assert.Equal(typeof(ProgressChangedEventArgs), typeof(WritingProgressChangedEventArgs).BaseType);

        var completed = new WritingCompletedEventArgs(
            cancelled: true,
            state: "state",
            exception: null);
        Assert.True(completed.Cancelled);
        Assert.Equal("state", completed.UserState);

        var progress = new WritingProgressChangedEventArgs(
            WritingProgressChangeLevel.FixedPageWritingProgress,
            number: 4,
            progressPercentage: 75,
            state: "state");
        Assert.Equal(4, progress.Number);
        Assert.Equal(75, progress.ProgressPercentage);
        Assert.Equal(WritingProgressChangeLevel.FixedPageWritingProgress, progress.WritingLevel);
        Assert.Equal("state", progress.UserState);
    }
}

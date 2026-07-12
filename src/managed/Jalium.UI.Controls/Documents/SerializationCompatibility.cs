using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using Jalium.UI.Media;
using PrintTicket = System.Printing.PrintTicket;
using PrintTicketLevel = Jalium.UI.Xps.Serialization.PrintTicketLevel;

namespace Jalium.UI.Documents.Serialization
{
    /// <summary>Creates a serializer writer and describes it to discovery clients.</summary>
    public interface ISerializerFactory
    {
        string DisplayName { get; }
        string ManufacturerName { get; }
        Uri ManufacturerWebsite { get; }
        string DefaultFileExtension { get; }
        SerializerWriter CreateSerializerWriter(Stream stream);
    }

    /// <summary>Describes an installed document serializer.</summary>
    public sealed class SerializerDescriptor
    {
        private readonly ISerializerFactory? _factory;

        private SerializerDescriptor(ISerializerFactory factoryInstance)
        {
            _factory = factoryInstance;
            var type = factoryInstance.GetType();
            var assembly = type.Assembly;
            var name = assembly.GetName();
            DisplayName = factoryInstance.DisplayName;
            ManufacturerName = factoryInstance.ManufacturerName;
            ManufacturerWebsite = factoryInstance.ManufacturerWebsite;
            DefaultFileExtension = factoryInstance.DefaultFileExtension;
            AssemblyName = name.Name ?? string.Empty;
            // AOT and single-file deployments do not have a stable assembly location.
            AssemblyPath = string.Empty;
            FactoryInterfaceName = type.FullName ?? type.Name;
            AssemblyVersion = name.Version ?? new Version(0, 0);
            WinFXVersion = Environment.Version;
            IsLoadable = true;
        }

        public string DisplayName { get; }
        public string ManufacturerName { get; }
        public Uri ManufacturerWebsite { get; }
        public string DefaultFileExtension { get; }
        public string AssemblyName { get; }
        public string AssemblyPath { get; }
        public string FactoryInterfaceName { get; }
        public Version AssemblyVersion { get; }
        public Version WinFXVersion { get; }
        public bool IsLoadable { get; }

        public static SerializerDescriptor CreateFromFactoryInstance(ISerializerFactory factoryInstance)
        {
            ArgumentNullException.ThrowIfNull(factoryInstance);
            return new SerializerDescriptor(factoryInstance);
        }

        public override bool Equals(object? obj) => obj is SerializerDescriptor other
            && string.Equals(AssemblyName, other.AssemblyName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(FactoryInterfaceName, other.FactoryInterfaceName, StringComparison.Ordinal)
            && Equals(AssemblyVersion, other.AssemblyVersion);

        public override int GetHashCode() => HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(AssemblyName),
            StringComparer.Ordinal.GetHashCode(FactoryInterfaceName),
            AssemblyVersion);

        internal SerializerWriter CreateWriter(Stream stream)
        {
            if (_factory == null)
            {
                throw new InvalidOperationException("The serializer factory is not loadable.");
            }
            return _factory.CreateSerializerWriter(stream);
        }
    }

    /// <summary>Discovers and manages serializers available to this process.</summary>
    public sealed class SerializerProvider
    {
        private static readonly object s_syncRoot = new();
        private static readonly List<SerializerDescriptor> s_installed = new();

        public ReadOnlyCollection<SerializerDescriptor> InstalledSerializers
        {
            get
            {
                lock (s_syncRoot)
                {
                    return new ReadOnlyCollection<SerializerDescriptor>(s_installed.ToArray());
                }
            }
        }

        public void RegisterSerializer(SerializerDescriptor serializerDescriptor, bool overwrite)
        {
            ArgumentNullException.ThrowIfNull(serializerDescriptor);
            lock (s_syncRoot)
            {
                var index = s_installed.FindIndex(item => item.Equals(serializerDescriptor));
                if (index >= 0)
                {
                    if (!overwrite)
                    {
                        throw new InvalidOperationException("The serializer is already registered.");
                    }
                    s_installed[index] = serializerDescriptor;
                }
                else
                {
                    s_installed.Add(serializerDescriptor);
                }
            }
        }

        public void UnregisterSerializer(SerializerDescriptor serializerDescriptor)
        {
            ArgumentNullException.ThrowIfNull(serializerDescriptor);
            lock (s_syncRoot)
            {
                s_installed.RemoveAll(item => item.Equals(serializerDescriptor));
            }
        }

        public SerializerWriter CreateSerializerWriter(
            SerializerDescriptor serializerDescriptor,
            Stream stream)
        {
            ArgumentNullException.ThrowIfNull(serializerDescriptor);
            ArgumentNullException.ThrowIfNull(stream);
            if (!stream.CanWrite)
            {
                throw new ArgumentException("The stream must be writable.", nameof(stream));
            }
            return serializerDescriptor.CreateWriter(stream);
        }
    }

    /// <summary>Defines the WPF-compatible document serialization contract.</summary>
    public abstract class SerializerWriter
    {
        protected SerializerWriter()
        {
        }

        public abstract event WritingPrintTicketRequiredEventHandler? WritingPrintTicketRequired;
        public abstract event WritingProgressChangedEventHandler? WritingProgressChanged;
        public abstract event WritingCompletedEventHandler? WritingCompleted;
        public abstract event WritingCancelledEventHandler? WritingCancelled;

        public abstract void Write(Visual visual);
        public abstract void Write(Visual visual, PrintTicket printTicket);
        public abstract void WriteAsync(Visual visual);
        public abstract void WriteAsync(Visual visual, object? userState);
        public abstract void WriteAsync(Visual visual, PrintTicket printTicket);
        public abstract void WriteAsync(Visual visual, PrintTicket printTicket, object? userState);

        public abstract void Write(DocumentPaginator documentPaginator);
        public abstract void Write(DocumentPaginator documentPaginator, PrintTicket printTicket);
        public abstract void WriteAsync(DocumentPaginator documentPaginator);
        public abstract void WriteAsync(DocumentPaginator documentPaginator, PrintTicket printTicket);
        public abstract void WriteAsync(DocumentPaginator documentPaginator, object? userState);
        public abstract void WriteAsync(DocumentPaginator documentPaginator, PrintTicket printTicket, object? userState);

        public abstract void Write(FixedPage fixedPage);
        public abstract void Write(FixedPage fixedPage, PrintTicket printTicket);
        public abstract void WriteAsync(FixedPage fixedPage);
        public abstract void WriteAsync(FixedPage fixedPage, PrintTicket printTicket);
        public abstract void WriteAsync(FixedPage fixedPage, object? userState);
        public abstract void WriteAsync(FixedPage fixedPage, PrintTicket printTicket, object? userState);

        public abstract void Write(FixedDocument fixedDocument);
        public abstract void Write(FixedDocument fixedDocument, PrintTicket printTicket);
        public abstract void WriteAsync(FixedDocument fixedDocument);
        public abstract void WriteAsync(FixedDocument fixedDocument, PrintTicket printTicket);
        public abstract void WriteAsync(FixedDocument fixedDocument, object? userState);
        public abstract void WriteAsync(FixedDocument fixedDocument, PrintTicket printTicket, object? userState);

        public abstract void Write(FixedDocumentSequence fixedDocumentSequence);
        public abstract void Write(FixedDocumentSequence fixedDocumentSequence, PrintTicket printTicket);
        public abstract void WriteAsync(FixedDocumentSequence fixedDocumentSequence);
        public abstract void WriteAsync(FixedDocumentSequence fixedDocumentSequence, PrintTicket printTicket);
        public abstract void WriteAsync(FixedDocumentSequence fixedDocumentSequence, object? userState);
        public abstract void WriteAsync(
            FixedDocumentSequence fixedDocumentSequence,
            PrintTicket printTicket,
            object? userState);

        public abstract void CancelAsync();
        public abstract SerializerWriterCollator CreateVisualsCollator();
        public abstract SerializerWriterCollator CreateVisualsCollator(
            PrintTicket documentSequencePT,
            PrintTicket documentPT);
    }

    /// <summary>Collects multiple visual writes into one serializer batch.</summary>
    public abstract class SerializerWriterCollator
    {
        protected SerializerWriterCollator()
        {
        }

        public abstract void BeginBatchWrite();
        public abstract void EndBatchWrite();
        public abstract void Write(Visual visual);
        public abstract void Write(Visual visual, PrintTicket printTicket);
        public abstract void WriteAsync(Visual visual);
        public abstract void WriteAsync(Visual visual, object? userState);
        public abstract void WriteAsync(Visual visual, PrintTicket printTicket);
        public abstract void WriteAsync(Visual visual, PrintTicket printTicket, object? userState);
        public abstract void CancelAsync();
        public abstract void Cancel();
    }

    public class WritingCancelledEventArgs : EventArgs
    {
        public WritingCancelledEventArgs(Exception exception)
        {
            Error = exception;
        }

        public Exception Error { get; }
    }

    public delegate void WritingCancelledEventHandler(object sender, WritingCancelledEventArgs e);

    public class WritingCompletedEventArgs : AsyncCompletedEventArgs
    {
        public WritingCompletedEventArgs(bool cancelled, object? state, Exception? exception)
            : base(exception, cancelled, state)
        {
        }
    }

    public delegate void WritingCompletedEventHandler(object sender, WritingCompletedEventArgs e);

    public class WritingPrintTicketRequiredEventArgs : EventArgs
    {
        public WritingPrintTicketRequiredEventArgs(PrintTicketLevel printTicketLevel, int sequence)
        {
            CurrentPrintTicketLevel = printTicketLevel;
            Sequence = sequence;
        }

        public PrintTicketLevel CurrentPrintTicketLevel { get; }
        public int Sequence { get; }
        public PrintTicket? CurrentPrintTicket { get; set; }
    }

    public delegate void WritingPrintTicketRequiredEventHandler(
        object sender,
        WritingPrintTicketRequiredEventArgs e);

    public enum WritingProgressChangeLevel
    {
        None = 0,
        FixedDocumentSequenceWritingProgress = 1,
        FixedDocumentWritingProgress = 2,
        FixedPageWritingProgress = 3,
    }

    public class WritingProgressChangedEventArgs : ProgressChangedEventArgs
    {
        public WritingProgressChangedEventArgs(
            WritingProgressChangeLevel writingLevel,
            int number,
            int progressPercentage,
            object? state)
            : base(progressPercentage, state)
        {
            WritingLevel = writingLevel;
            Number = number;
        }

        public int Number { get; }
        public WritingProgressChangeLevel WritingLevel { get; }
    }

    public delegate void WritingProgressChangedEventHandler(
        object sender,
        WritingProgressChangedEventArgs e);
}

namespace Jalium.UI.Xps.Serialization
{
    /// <summary>Specifies the fixed-document scope requesting a print ticket.</summary>
    public enum PrintTicketLevel
    {
        None = 0,
        FixedDocumentSequencePrintTicket = 1,
        FixedDocumentPrintTicket = 2,
        FixedPagePrintTicket = 3,
    }
}

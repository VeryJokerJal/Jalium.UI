namespace Jalium.UI.Markup;

/// <summary>
/// Settings for the Baml2006Reader.
/// </summary>
public class Baml2006ReaderSettings
{
    /// <summary>
    /// Gets or sets whether the reader owns the stream and should close it when disposed.
    /// </summary>
    public bool OwnsStream { get; set; }

    /// <summary>
    /// Gets or sets the local assembly for type resolution.
    /// </summary>
    public System.Reflection.Assembly? LocalAssembly { get; set; }

    /// <summary>
    /// Gets or sets whether values must be returned as strings.
    /// </summary>
    public bool ValuesMustBeString { get; set; }
}

/// <summary>
/// Reads BAML (Binary Application Markup Language) and produces a XAML node stream.
/// </summary>
/// <remarks>
/// In Jalium.UI, BAML is not used (JALXAML uses its own binary format .uic).
/// This class is provided as a stub for WPF API compatibility only.
/// Use <see cref="JalxamlLoader"/> for loading compiled Jalium.UI markup.
/// </remarks>
public class Baml2006Reader : IDisposable
{
    private readonly Stream _stream;
    private readonly Baml2006ReaderSettings _settings;
    private bool _isDisposed;
    private bool _isEof = true;

    /// <summary>
    /// Constructs a Baml2006Reader from a file path.
    /// </summary>
    /// <param name="fileName">The path to the BAML file.</param>
    public Baml2006Reader(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        _stream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
        _settings = new Baml2006ReaderSettings { OwnsStream = true };
    }

    /// <summary>
    /// Constructs a Baml2006Reader from a stream.
    /// </summary>
    /// <param name="stream">The stream containing BAML data.</param>
    public Baml2006Reader(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _stream = stream;
        _settings = new Baml2006ReaderSettings();
    }

    /// <summary>
    /// Constructs a Baml2006Reader from a stream with the specified settings.
    /// </summary>
    /// <param name="stream">The stream containing BAML data.</param>
    /// <param name="settings">Reader settings.</param>
    public Baml2006Reader(Stream stream, Baml2006ReaderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(settings);
        _stream = stream;
        _settings = settings;
    }

    /// <summary>
    /// Gets a value indicating whether the reader has reached the end of the stream.
    /// </summary>
    public bool IsEof => _isEof;

    /// <summary>
    /// Reads the next node from the BAML stream.
    /// </summary>
    /// <returns>true if a node was read; false if the end of the stream was reached.</returns>
    public bool Read()
    {
        // Stub implementation - Jalium.UI does not use BAML format
        _isEof = true;
        return false;
    }

    /// <summary>
    /// Releases all resources used by the Baml2006Reader.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the unmanaged resources used by the Baml2006Reader and optionally releases managed resources.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (!_isDisposed)
        {
            if (disposing && _settings.OwnsStream)
            {
                _stream.Dispose();
            }
            _isDisposed = true;
        }
    }
}

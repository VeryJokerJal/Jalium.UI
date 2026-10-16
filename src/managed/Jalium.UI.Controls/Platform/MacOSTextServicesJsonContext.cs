using System.Text.Json.Serialization;

namespace Jalium.UI.Controls.Platform;

// Concrete contracts keep the synchronous AppKit bridge usable in trimmed and
// native AOT applications. Never serialize an anonymous runtime object here.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(MacOSTextServicesResponse))]
internal partial class MacOSTextServicesJsonContext : JsonSerializerContext;

internal sealed class MacOSTextServicesResponse
{
    public string Editor { get; set; } = "";
    public string? Session { get; set; }
    public bool? CanRead { get; set; }
    public bool? CanEdit { get; set; }
    public int? ResultOptions { get; set; }
    public string? Text16 { get; set; }
    public string? Styles { get; set; }
    public WritingToolsParagraph[]? Structure { get; set; }
    public int[]? Selection { get; set; }
    public int[]? Visible { get; set; }
    public bool? UndoRedo { get; set; }
    public bool? Applied { get; set; }
    public int? Index { get; set; }
    public double[][]? Rects { get; set; }
    public string? Png { get; set; }
    public double[]? Frame { get; set; }
}

internal sealed record WritingToolsParagraph(int Start, int Length, WritingToolsList[] Lists, WritingToolsCell[] Tables);
internal sealed record WritingToolsList(string Id, string Format, int Start);
internal sealed record WritingToolsCell(string Id, int Row, int Column, int Rows, int Columns, int TableColumns);

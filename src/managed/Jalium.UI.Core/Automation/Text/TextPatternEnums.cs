namespace Jalium.UI.Automation.Text;

/// <summary>Specifies an endpoint of a text range.</summary>
public enum TextPatternRangeEndpoint
{
    Start = 0,
    End = 1,
}

/// <summary>Specifies the unit used to navigate a text range.</summary>
public enum TextUnit
{
    Character = 0,
    Format = 1,
    Word = 2,
    Line = 3,
    Paragraph = 4,
    Page = 5,
    Document = 6,
}

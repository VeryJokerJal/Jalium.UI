namespace Jalium.UI.Styling;

/// <summary>A view-owned box whose computed style belongs to an original nonvisual node.</summary>
internal interface ICssStyledBox
{
    DependencyObject StyleSource { get; }
}

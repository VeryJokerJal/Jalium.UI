namespace Jalium.UI.Styling;

/// <summary>A formatting box with no corresponding document or accessibility node.</summary>
internal interface ICssAnonymousBox
{
    FrameworkElement Owner { get; }
    double BaselineOffset(double height, bool last);
    bool Contains(Point point) => true;
}

/// <summary>An anonymous text sequence, distinct from a table or other generated wrapper.</summary>
internal interface ICssAnonymousTextBox : ICssAnonymousBox { }

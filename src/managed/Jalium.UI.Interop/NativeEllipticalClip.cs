using System.Runtime.InteropServices;
using Jalium.UI.Styling;

namespace Jalium.UI.Interop;

[StructLayout(LayoutKind.Sequential)]
internal struct NativeEllipticalClip
{
    internal uint StructSize, Edges;
    internal float X, Y, Width, Height;
    internal float TopLeftX, TopRightX, BottomRightX, BottomLeftX;
    internal float TopLeftY, TopRightY, BottomRightY, BottomLeftY;

    internal NativeEllipticalClip(Rect rect, CssUsedBorderRadii radii, ClipEdges edges)
    {
        StructSize = 56; Edges = (uint)edges;
        X = (float)rect.X; Y = (float)rect.Y; Width = (float)rect.Width; Height = (float)rect.Height;
        TopLeftX = (float)radii.TopLeft.Width; TopRightX = (float)radii.TopRight.Width;
        BottomRightX = (float)radii.BottomRight.Width; BottomLeftX = (float)radii.BottomLeft.Width;
        TopLeftY = (float)radii.TopLeft.Height; TopRightY = (float)radii.TopRight.Height;
        BottomRightY = (float)radii.BottomRight.Height; BottomLeftY = (float)radii.BottomLeft.Height;
    }

    internal readonly NativeEllipticalClip Outset(float spread)
    {
        var dx = spread >= 0 ? spread : -Math.Min(-spread, Width / 2f);
        var dy = spread >= 0 ? spread : -Math.Min(-spread, Height / 2f);
        var rect = new Rect(X - dx, Y - dy, Math.Max(0, Width + 2 * dx), Math.Max(0, Height + 2 * dy));
        static double Radius(double radius, double spread)
        {
            if (spread <= 0) return Math.Max(0, radius + spread);
            var factor = radius < spread ? 1 + Math.Pow(radius / spread - 1, 3) : 1;
            return radius + spread * factor;
        }
        Size Corner(float rx, float ry) => new(Radius(rx, spread), Radius(ry, spread));
        var radii = new CssUsedBorderRadii(
            Corner(TopLeftX, TopLeftY), Corner(TopRightX, TopRightY),
            Corner(BottomRightX, BottomRightY), Corner(BottomLeftX, BottomLeftY))
            .Normalize(rect.Size);
        return new NativeEllipticalClip(rect, radii, (ClipEdges)Edges);
    }
}

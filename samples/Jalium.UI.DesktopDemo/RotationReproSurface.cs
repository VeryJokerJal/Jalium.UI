using Jalium.UI.Controls;
using Jalium.UI.DesktopDemo.Themes;
using Jalium.UI.Media;
using Jalium.UI.Media.Imaging;

namespace Jalium.UI.DesktopDemo;

/// <summary>Rotation cases retain the original bitmap/SDF/container render paths.</summary>
public class RotationReproSurface : Control
{
    public sealed record RotationCase(string Caption, double Angle);
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(nameof(Description), typeof(string), typeof(RotationReproSurface), new PropertyMetadata("Rotation / 180deg negative-diagonal repro — content must stay inside its grey frame and be correctly oriented"));
    private static readonly DependencyPropertyKey QuadrantBitmapPropertyKey = DependencyProperty.RegisterReadOnly(nameof(QuadrantBitmap), typeof(ImageSource), typeof(RotationReproSurface), new PropertyMetadata(null));
    public static readonly DependencyProperty QuadrantBitmapProperty = QuadrantBitmapPropertyKey.DependencyProperty;
    public string Description { get => (string)GetValue(DescriptionProperty)!; set => SetValue(DescriptionProperty, value); }
    public ImageSource QuadrantBitmap => (ImageSource)GetValue(QuadrantBitmapProperty)!;
    public static readonly DependencyProperty ImageCasesProperty = DependencyProperty.Register(nameof(ImageCases), typeof(IReadOnlyList<RotationCase>), typeof(RotationReproSurface), new PropertyMetadata(Array.AsReadOnly(new[] { new RotationCase("Image 0", 0), new RotationCase("Image 90", 90), new RotationCase("Image 180", 180), new RotationCase("Image 270", 270) })));
    public static readonly DependencyProperty BorderCasesProperty = DependencyProperty.Register(nameof(BorderCases), typeof(IReadOnlyList<RotationCase>), typeof(RotationReproSurface), new PropertyMetadata(Array.AsReadOnly(new[] { new RotationCase("Border 0", 0), new RotationCase("Border 90", 90), new RotationCase("Border 180", 180), new RotationCase("Border 270", 270) })));
    public static readonly DependencyProperty ContainerCasesProperty = DependencyProperty.Register(nameof(ContainerCases), typeof(IReadOnlyList<RotationCase>), typeof(RotationReproSurface), new PropertyMetadata(Array.AsReadOnly(new[] { new RotationCase("Cont 0", 0), new RotationCase("Cont 180", 180) })));
    public IReadOnlyList<RotationCase> ImageCases { get => (IReadOnlyList<RotationCase>)GetValue(ImageCasesProperty)!; set => SetValue(ImageCasesProperty, value); }
    public IReadOnlyList<RotationCase> BorderCases { get => (IReadOnlyList<RotationCase>)GetValue(BorderCasesProperty)!; set => SetValue(BorderCasesProperty, value); }
    public IReadOnlyList<RotationCase> ContainerCases { get => (IReadOnlyList<RotationCase>)GetValue(ContainerCasesProperty)!; set => SetValue(ContainerCasesProperty, value); }
    public RotationReproSurface() { RotationReproTheme.Register(); SetValue(QuadrantBitmapPropertyKey, MakeQuadrantBitmap(64)); DefaultStyleKey = typeof(RotationReproSurface); }
    private static BitmapImage MakeQuadrantBitmap(int size)
    {
        // TL=red, TR=green, BL=blue, BR=yellow — direction is unambiguous so any
        // rotation/flip is visually obvious, and a missing/garbled transform shows
        // immediately. BitmapImage.FromPixels expects BGRA8.
        var px = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int i = (y * size + x) * 4;
            bool left = x < size / 2;
            bool top = y < size / 2;
            byte r, g, b;
            if (top && left) { r = 230; g = 40; b = 40; }       // TL red
            else if (top) { r = 40; g = 200; b = 40; }          // TR green
            else if (left) { r = 50; g = 90; b = 230; }         // BL blue
            else { r = 235; g = 210; b = 40; }                  // BR yellow
            px[i + 0] = b; px[i + 1] = g; px[i + 2] = r; px[i + 3] = 255;
        }
        return BitmapImage.FromPixels(px, size, size);
    }

}

public class RotationReproFrame : HeaderedContentControl
{
    public RotationReproFrame() { RotationReproTheme.Register(); DefaultStyleKey = typeof(RotationReproFrame); }
}
public abstract class RotationSpecimen : Control
{
    public static readonly DependencyProperty AngleProperty = DependencyProperty.Register(nameof(Angle), typeof(double), typeof(RotationSpecimen), new PropertyMetadata(0d, OnAngleChanged));
    private static readonly DependencyPropertyKey RotationPropertyKey = DependencyProperty.RegisterReadOnly(nameof(Rotation), typeof(RotateTransform), typeof(RotationSpecimen), new PropertyMetadata(null));
    public static readonly DependencyProperty RotationProperty = RotationPropertyKey.DependencyProperty;
    public RotateTransform Rotation => (RotateTransform)GetValue(RotationProperty)!;
    public double Angle { get => (double)GetValue(AngleProperty)!; set => SetValue(AngleProperty, value); }
    protected RotationSpecimen() { RotationReproTheme.Register(); SetValue(RotationPropertyKey, new RotateTransform()); DefaultStyleKey = GetType(); }
    private static void OnAngleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) { if (d.GetValue(RotationProperty) is RotateTransform rotation) rotation.Angle = (double)e.NewValue!; }
}
public class RotationImageSpecimen : RotationSpecimen
{
    public static readonly DependencyProperty SourceProperty = DependencyProperty.Register(nameof(Source), typeof(ImageSource), typeof(RotationImageSpecimen), new PropertyMetadata(null));
    public ImageSource? Source { get => (ImageSource?)GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
}
public class RotationBorderSpecimen : RotationSpecimen { }
public class RotationContainerSpecimen : RotationSpecimen { }

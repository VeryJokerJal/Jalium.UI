using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Tests;

public class IconSupportTests
{
    private sealed class TestDrawingContext : DrawingContextAdapter
    {
        public List<Geometry> DrawnGeometries { get; } = new();
        public List<Brush?> DrawnBrushes { get; } = new();
        public List<Pen?> DrawnPens { get; } = new();
        public List<FormattedText> DrawnTexts { get; } = new();
        public List<ImageSource> DrawnImages { get; } = new();
        public List<Transform> PushedTransforms { get; } = new();

        public override void DrawLine(Pen pen, Point point0, Point point1) { }
        public override void DrawRectangle(Brush? brush, Pen? pen, Rect rectangle) { }
        public override void DrawRoundedRectangle(Brush? brush, Pen? pen, Rect rectangle, double radiusX, double radiusY) { }
        public override void DrawEllipse(Brush? brush, Pen? pen, Point center, double radiusX, double radiusY) { }

        public override void DrawText(FormattedText formattedText, Point origin)
        {
            DrawnTexts.Add(formattedText);
        }

        public override void DrawGeometry(Brush? brush, Pen? pen, Geometry geometry)
        {
            DrawnBrushes.Add(brush);
            DrawnPens.Add(pen);
            DrawnGeometries.Add(geometry);
        }

        public override void DrawImage(ImageSource imageSource, Rect rectangle)
        {
            DrawnImages.Add(imageSource);
        }

        public override void DrawBackdropEffect(Rect rectangle, IBackdropEffect effect, CornerRadius cornerRadius) { }

        public override void PushTransform(Transform transform)
        {
            PushedTransforms.Add(transform);
        }

        public override void PushClip(Geometry clipGeometry) { }
        public override void PushOpacity(double opacity) { }
        public override void Pop() { }
        public override void Close() { }
    }

    private sealed class SubclassedMenuFlyoutItem : MenuFlyoutItem
    {
        public bool DrawIconCalled { get; private set; }
        public Rect LastIconBounds { get; private set; }

        protected override void DrawIcon(DrawingContext dc, Rect iconBounds, Brush foreground)
        {
            DrawIconCalled = true;
            LastIconBounds = iconBounds;
            base.DrawIcon(dc, iconBounds, foreground);
        }
    }

    private sealed class SubclassedMenuFlyoutSubItem : MenuFlyoutSubItem
    {
        public bool DrawIconCalled { get; private set; }
        public Rect LastIconBounds { get; private set; }

        protected override void DrawIcon(DrawingContext dc, Rect iconBounds, Brush foreground)
        {
            DrawIconCalled = true;
            LastIconBounds = iconBounds;
            base.DrawIcon(dc, iconBounds, foreground);
        }
    }

    private sealed class SubclassedMenuItem : MenuItem
    {
        public bool DrawIconCalled { get; private set; }

        protected override void DrawIcon(DrawingContext dc, Rect iconBounds, Brush foreground)
        {
            DrawIconCalled = true;
            base.DrawIcon(dc, iconBounds, foreground);
        }
    }

    [Fact]
    public void MenuFlyoutSubItem_CanBeSubclassed_AndIsNotSealed()
    {
        // Verifies MenuFlyoutSubItem is unsealed
        Assert.False(typeof(MenuFlyoutSubItem).IsSealed, "MenuFlyoutSubItem should be unsealed to allow user customization.");
        var customSubItem = new SubclassedMenuFlyoutSubItem();
        Assert.IsAssignableFrom<MenuFlyoutSubItem>(customSubItem);
    }

    [Fact]
    public void MenuFlyoutItem_RendersVectorGeometryIcon()
    {
        var geom = Geometry.Parse("M 0,0 L 10,10 L 0,10 Z");
        var item = new MenuFlyoutItem
        {
            Text = "Vector Item",
            Icon = geom,
            Width = 150,
            Height = 32
        };

        item.Measure(new Size(150, 32));
        item.Arrange(new Rect(0, 0, 150, 32));

        var dc = new TestDrawingContext();
        item.Render(dc);

        Assert.NotEmpty(dc.DrawnGeometries);
        Assert.Contains(geom, dc.DrawnGeometries);
    }

    [Fact]
    public void MenuFlyoutSubItem_RendersVectorGeometryIcon()
    {
        var geom = Geometry.Parse("M 2,2 L 12,12 L 2,12 Z");
        var subItem = new SubclassedMenuFlyoutSubItem
        {
            Text = "Submenu with Vector",
            Icon = geom,
            Width = 180,
            Height = 32
        };

        subItem.Measure(new Size(180, 32));
        subItem.Arrange(new Rect(0, 0, 180, 32));

        var dc = new TestDrawingContext();
        subItem.Render(dc);

        Assert.True(subItem.DrawIconCalled, "Custom DrawIcon on MenuFlyoutSubItem should have been called.");
        Assert.NotEmpty(dc.DrawnGeometries);
        Assert.Contains(geom, dc.DrawnGeometries);
    }

    [Fact]
    public void MenuFlyoutItem_CallsVirtualDrawIcon()
    {
        var geom = Geometry.Parse("M 0,0 L 5,5");
        var item = new SubclassedMenuFlyoutItem
        {
            Text = "Custom DrawIcon Item",
            Icon = geom,
            Width = 160,
            Height = 32
        };

        item.Measure(new Size(160, 32));
        item.Arrange(new Rect(0, 0, 160, 32));

        var dc = new TestDrawingContext();
        item.Render(dc);

        Assert.True(item.DrawIconCalled);
        Assert.True(item.LastIconBounds.Width > 0 && item.LastIconBounds.Height > 0);
    }

    [Fact]
    public void MenuItem_RendersVectorGeometry_AndCallsVirtualDrawIcon()
    {
        var geom = Geometry.Parse("M 0,0 L 8,8 Z");
        var item = new SubclassedMenuItem
        {
            Header = "Menu Item",
            Icon = geom,
            Width = 120,
            Height = 30
        };

        item.Measure(new Size(120, 30));
        item.Arrange(new Rect(0, 0, 120, 30));

        var dc = new TestDrawingContext();
        item.Render(dc);

        Assert.True(item.DrawIconCalled);
        Assert.NotEmpty(dc.DrawnGeometries);
        Assert.Contains(geom, dc.DrawnGeometries);
    }

    [Fact]
    public void MenuFlyoutItem_RendersPathIcon()
    {
        var geom = Geometry.Parse("M 0,0 L 10,0 L 10,10 Z");
        var pathIcon = new PathIcon { Data = geom };
        var item = new MenuFlyoutItem
        {
            Text = "PathIcon Item",
            Icon = pathIcon,
            Width = 150,
            Height = 32
        };

        item.Measure(new Size(150, 32));
        item.Arrange(new Rect(0, 0, 150, 32));

        var dc = new TestDrawingContext();
        item.Render(dc);

        Assert.Contains(geom, dc.DrawnGeometries);
    }

    [Fact]
    public void MenuFlyoutItem_RendersSymbolEnum_AndSymbolIcon()
    {
        var item = new MenuFlyoutItem
        {
            Text = "Symbol Item",
            Icon = Symbol.Save,
            Width = 150,
            Height = 32
        };

        item.Measure(new Size(150, 32));
        item.Arrange(new Rect(0, 0, 150, 32));

        var dc = new TestDrawingContext();
        item.Render(dc);

        // Drawing a symbol will draw text (glyph)
        Assert.NotEmpty(dc.DrawnTexts);
    }

    [Fact]
    public void MenuFlyoutItem_RendersStringMarkup_AsGeometry()
    {
        var item = new MenuFlyoutItem
        {
            Text = "String Markup Item",
            Icon = "M 0,0 L 12,0 L 12,12 Z",
            Width = 150,
            Height = 32
        };

        item.Measure(new Size(150, 32));
        item.Arrange(new Rect(0, 0, 150, 32));

        var dc = new TestDrawingContext();
        item.Render(dc);

        Assert.NotEmpty(dc.DrawnGeometries);
    }

    [Fact]
    public void MenuFlyoutItem_RendersIconSource_PathIconSource()
    {
        var geom = Geometry.Parse("M 1,1 L 9,9");
        var iconSource = new PathIconSource { Data = geom };
        var item = new MenuFlyoutItem
        {
            Text = "IconSource Item",
            Icon = iconSource,
            Width = 150,
            Height = 32
        };

        item.Measure(new Size(150, 32));
        item.Arrange(new Rect(0, 0, 150, 32));

        var dc = new TestDrawingContext();
        item.Render(dc);

        Assert.Contains(geom, dc.DrawnGeometries);
    }

    [Fact]
    public void ToggleMenuFlyoutItem_SuppressesCustomIcon_WhenChecked()
    {
        var geom = Geometry.Parse("M 0,0 L 10,10");
        var item = new ToggleMenuFlyoutItem
        {
            Text = "Toggle Item",
            Icon = geom,
            IsChecked = true,
            Width = 150,
            Height = 32
        };

        item.Measure(new Size(150, 32));
        item.Arrange(new Rect(0, 0, 150, 32));

        var dc = new TestDrawingContext();
        item.Render(dc);

        // When checked, the checkmark is drawn instead of the custom icon geometry
        Assert.DoesNotContain(geom, dc.DrawnGeometries);

        // Uncheck and render again
        item.IsChecked = false;
        var dcUnchecked = new TestDrawingContext();
        item.Render(dcUnchecked);

        // Now custom icon is drawn
        Assert.Contains(geom, dcUnchecked.DrawnGeometries);
    }

    [Fact]
    public void IconElementConverter_ConvertsStrings_Geometries_AndSymbols()
    {
        var converter = new IconElementConverter();
        Assert.True(converter.CanConvertFrom(typeof(string)));
        Assert.True(converter.CanConvertFrom(typeof(Geometry)));
        Assert.True(converter.CanConvertFrom(typeof(Symbol)));
        Assert.True(converter.CanConvertFrom(typeof(IconSource)));

        // Path string
        var pathResult = converter.ConvertFrom("M0,0 L10,10 Z");
        var pathIcon = Assert.IsType<PathIcon>(pathResult);
        Assert.NotNull(pathIcon.Data);

        // Symbol name
        var symbolResult = converter.ConvertFrom("Save");
        var symbolIcon = Assert.IsType<SymbolIcon>(symbolResult);
        Assert.Equal(Symbol.Save, symbolIcon.Symbol);

        // "Symbol." prefix
        var symbolPrefixed = converter.ConvertFrom("Symbol.Cut");
        var cutIcon = Assert.IsType<SymbolIcon>(symbolPrefixed);
        Assert.Equal(Symbol.Cut, cutIcon.Symbol);

        // Direct Geometry
        var geom = Geometry.Parse("M1,1 L2,2");
        var geomResult = converter.ConvertFrom(geom);
        var pathFromGeom = Assert.IsType<PathIcon>(geomResult);
        Assert.Same(geom, pathFromGeom.Data);

        // Direct Symbol
        var symResult = converter.ConvertFrom(Symbol.Copy);
        var symIcon = Assert.IsType<SymbolIcon>(symResult);
        Assert.Equal(Symbol.Copy, symIcon.Symbol);

        // IconSource
        var pis = new PathIconSource { Data = geom };
        var sourceResult = converter.ConvertFrom(pis);
        var elementFromSource = Assert.IsType<PathIcon>(sourceResult);
        Assert.Same(geom, elementFromSource.Data);
    }

    [Fact]
    public void IconSourceConverter_ConvertsStrings_Geometries_AndSymbols()
    {
        var converter = new IconSourceConverter();
        Assert.True(converter.CanConvertFrom(typeof(string)));
        Assert.True(converter.CanConvertFrom(typeof(Geometry)));
        Assert.True(converter.CanConvertFrom(typeof(Symbol)));

        // Path string
        var pathResult = converter.ConvertFrom("M0,0 L15,15");
        var pathSource = Assert.IsType<PathIconSource>(pathResult);
        Assert.NotNull(pathSource.Data);

        // Symbol name
        var symResult = converter.ConvertFrom("Add");
        var symSource = Assert.IsType<SymbolIconSource>(symResult);
        Assert.Equal(Symbol.Add, symSource.Symbol);

        // Direct Geometry
        var geom = Geometry.Parse("M3,3 L7,7");
        var geomResult = converter.ConvertFrom(geom);
        var pathFromGeom = Assert.IsType<PathIconSource>(geomResult);
        Assert.Same(geom, pathFromGeom.Data);

        // Direct Symbol
        var symbolEnumResult = converter.ConvertFrom(Symbol.Delete);
        var symbolFromEnum = Assert.IsType<SymbolIconSource>(symbolEnumResult);
        Assert.Equal(Symbol.Delete, symbolFromEnum.Symbol);
    }

    [Fact]
    public void IconSourceElement_SynchronizesWithUnderlyingSource()
    {
        var geom1 = Geometry.Parse("M0,0 L5,5");
        var geom2 = Geometry.Parse("M0,0 L10,10");
        var pathSource = new PathIconSource { Data = geom1 };

        var element = new IconSourceElement { IconSource = pathSource };
        var child = Assert.IsType<PathIcon>(element.RealizedElement);
        Assert.Same(geom1, child.Data);

        // Update Data on source
        pathSource.Data = geom2;
        var child2 = Assert.IsType<PathIcon>(element.RealizedElement);
        Assert.Same(geom2, child2.Data);

        // Update Foreground on source
        var redBrush = new SolidColorBrush(Color.FromRgb(255, 0, 0));
        pathSource.Foreground = redBrush;
        var child3 = Assert.IsType<PathIcon>(element.RealizedElement);
        Assert.Same(redBrush, child3.Foreground);
    }

    [Fact]
    public void BitmapIcon_And_ImageIcon_PropertiesWorkCorrectly()
    {
        var uri = new Uri("https://example.com/icon.png");
        var bitmapIcon = new BitmapIcon
        {
            UriSource = uri,
            ShowAsMonochrome = false
        };
        Assert.Equal(uri, bitmapIcon.UriSource);
        Assert.False(bitmapIcon.ShowAsMonochrome);

        var imageIcon = new ImageIcon();
        Assert.Null(imageIcon.Source);
    }
}

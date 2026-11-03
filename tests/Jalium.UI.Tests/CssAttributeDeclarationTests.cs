using Jalium.UI.Controls;
using Jalium.UI.Markup;
using Jalium.UI.Media;
using Jalium.UI.Styling;

namespace Jalium.UI.Tests;

[Collection("Application")]
public sealed class CssAttributeDeclarationTests
{
    [Fact]
    public void TypedAttributesInOrdinaryDeclarationsFollowTheStyledElement()
    {
        var block = new TextBlock { Text = "x" };
        var root = new Border { Child = block };
        Css.SetClass(block, "probe");
        Css.SetAttribute(root, "data-width", "80");
        Css.SetAttribute(block, "data-width", "34");
        Css.SetAttribute(block, "data-color", "#00ff00");
        Css.SetAttribute(block, "data-inset", "5");
        Css.SetStyleSheet(root, ".probe { width:attr(data-width px, 11px);" +
            "color:attr(data-color type(<color>), blue); margin:attr(data-inset px, 2px) }");

        Layout(root);
        Assert.Equal(34, block.Width);
        Assert.Equal(Colors.Lime, Assert.IsType<SolidColorBrush>(block.Foreground).Color);
        Assert.Equal(new Thickness(5), block.Margin);

        Css.SetAttribute(block, "data-width", null);
        Css.SetAttribute(block, "data-color", "invalid");
        Css.SetAttribute(block, "data-inset", null);
        Layout(root);
        Assert.Equal(11, block.Width);
        Assert.Equal(Colors.Blue, Assert.IsType<SolidColorBrush>(block.Foreground).Color);
        Assert.Equal(new Thickness(2), block.Margin);
    }

    [Fact]
    public void VariablesAndTypedAttributesResolveAtComputedValueTime()
    {
        var block = new TextBlock { Text = "x" };
        var root = new Border { Child = block };
        Assert.True(Css.Supports("width", "attr(data-width px, 11px)"));
        Assert.False(Css.Supports("width", "attr()"));
        Assert.False(Css.Supports("width", "attr(, 11px)"));
        Assert.True(Css.Supports("width", "attr(())"));
        Css.SetAttribute(block, "data-steps", "3");
        Css.SetAttribute(block, "data-height", "1in");
        Css.SetStyle(block, "--steps:attr(data-steps number, 2);" +
            "width:calc(var(--steps) * 10px);height:attr(data-height type(<length>), 17px)");

        Layout(root);
        Assert.Equal(30, block.Width);
        Assert.Equal(96, block.Height);

        Css.SetAttribute(block, "data-steps", "invalid");
        Css.SetAttribute(block, "data-height", null);
        Layout(root);
        Assert.Equal(20, block.Width);
        Assert.Equal(17, block.Height);

        Css.SetStyle(block, "width:77px;width:attr(data-width px)");
        Layout(root);
        Assert.True(double.IsNaN(block.Width));
        Css.SetAttribute(block, "data-width", "25");
        Layout(root);
        Assert.Equal(25, block.Width);

        Css.SetStyle(block, "--attribute:data-width;width:attr(var(--attribute) px, var(--missing))");
        Layout(root);
        Assert.Equal(25, block.Width);
        Css.SetAttribute(block, "data-width", null);
        Layout(root);
        Assert.True(double.IsNaN(block.Width));

        Css.SetStyle(block, "width:77px;width:attr()");
        Layout(root);
        Assert.Equal(77, block.Width);
    }

    [Fact]
    public void UnknownAttributeUnitSelectsFallbackEvenWhenAttributeExists()
    {
        var border = new Border();
        Css.SetAttribute(border, "data-size", "42");
        Assert.True(Css.Supports("width", "attr(data-size madeup, 17px)"));
        Css.SetStyle(border, "width:attr(data-size madeup, 17px);" +
            "height:attr(data-size madeup)");

        Layout(border);
        Assert.Equal(17, border.Width);
        Assert.True(double.IsNaN(border.Height));

        Css.SetStyle(border, "--unit:madeup;width:attr(data-size var(--unit), 23px)");
        Layout(border);
        Assert.Equal(23, border.Width);

        Css.SetAttribute(border, "data-size", null);
        Layout(border);
        Assert.Equal(23, border.Width);
    }

    [Theory]
    [InlineData("attr(var(--missing), 19px)")]
    [InlineData("attr(data-size type(<url>), 19px)")]
    [InlineData("attr(data-size type(<unknown>), 19px)")]
    public void InvalidComputedAttributeDescriptorSelectsFallback(string value)
    {
        var border = new Border();
        Css.SetAttribute(border, "data-size", "42");
        Assert.True(Css.Supports("width", value));
        Css.SetStyle(border, "width:" + value);

        Layout(border);
        Assert.Equal(19, border.Width);
    }

    [Fact]
    [Trait("WPT", "css/css-values/attr-namespace-valid.xhtml")]
    public void QualifiedAttributeUsesStylesheetNamespaceAndRefreshes()
    {
        const string uri = "http://www.example.com/";
        var border = new Border();
        Css.SetClass(border, "probe");
        Css.SetAttribute(border, "myAttr", "red");
        Css.SetAttribute(border, uri, "myAttr", "green");
        Css.SetStyleSheet(border, "@namespace color '" + uri + "';" +
            ".probe { background-color:attr(color|myAttr type(*), red) }");

        Layout(border);
        var paint = Assert.IsType<CssBackgroundPaintState>(CssBackgroundPaintProperties.Get(border));
        Assert.Equal(Colors.Green, Assert.IsType<SolidColorBrush>(paint.Color).Color);

        Css.SetAttribute(border, uri, "myAttr", "blue");
        Layout(border);
        paint = Assert.IsType<CssBackgroundPaintState>(CssBackgroundPaintProperties.Get(border));
        Assert.Equal(Colors.Blue, Assert.IsType<SolidColorBrush>(paint.Color).Color);
    }

    [Fact]
    [Trait("WPT", "css/css-values/attr-null-namespace.xhtml")]
    public void UnqualifiedAttributeIgnoresDefaultNamespace()
    {
        var border = new Border();
        Css.SetClass(border, "probe");
        Css.SetAttribute(border, "urn:one", "data-x", "qualified");
        Css.SetAttribute(border, "data-x", "plain");
        Css.SetAttribute(border, "urn:one", "data-y", "qualified-only");
        Css.SetStyleSheet(border, "@namespace 'urn:one'; @namespace p 'urn:one';" +
            "*|*.probe { --x:attr(data-x type(*), fallback);" +
            "--y:attr(|data-y type(*), fallback);" +
            "--named:attr(p|data-x type(*), fallback) }");

        Layout(border);
        var values = CssNode.Get(border).CssRuntimeState!.CustomProperties!;
        Assert.Equal("plain", values["--x"].Trim());
        Assert.Equal("fallback", values["--y"].Trim());
        Assert.Equal("qualified", values["--named"].Trim());
    }

    [Fact]
    public void QualifiedAttributesKeepStylesheetPrefixesIsolated()
    {
        var border = new Border();
        Css.SetClass(border, "probe");
        Css.SetAttribute(border, "urn:one", "size", "42");
        Css.SetAttribute(border, "size", "99");
        Css.GetStyleSheets(border).Add(CssStyleSheet.Parse(
            "@namespace p 'urn:one'; .probe { width:attr(p|size px, 17px) }"));
        Css.GetStyleSheets(border).Add(CssStyleSheet.Parse(
            ".probe { height:attr(p|size px, 19px) }"));

        Layout(border);
        Assert.Equal(42, border.Width);
        Assert.Equal(19, border.Height);
    }

    [Fact]
    public void QualifiedAttributeReadsXamlXmlMetadata()
    {
        var border = new Border();
        XamlBuilder.RecordXmlAttribute(border, "urn:props", "size", "42");
        Css.SetStyleSheet(border, "@namespace p 'urn:props'; Border {width:attr(p|size px, 7px)}");

        Layout(border);
        Assert.Equal(42, border.Width);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QualifiedAttributeInCustomPropertyComputesOnOwnerBeforeInheritance(bool registered)
    {
        var child = new Border();
        var root = new Border { Child = child };
        Css.SetClass(root, "host");
        Css.SetClass(child, "child");
        Css.SetAttribute(root, "urn:props", "size", "42");
        Css.SetAttribute(child, "urn:props", "size", "99");
        Css.SetStyleSheet(root, "@namespace p 'urn:props';" +
            (registered ? "@property --size {syntax:'<length>';inherits:true;initial-value:3px}" : "") +
            ".host {--size:attr(p|size px, 7px)} .child {width:var(--size)}");

        Layout(root);
        Assert.Equal(42, child.Width);
        Css.SetAttribute(root, "urn:props", "size", "51");
        Layout(root);
        Assert.Equal(51, child.Width);
    }

    [Theory]
    [InlineData("attr(*|size px, 17px)")]
    [InlineData("attr(p |size px, 17px)")]
    [InlineData("attr(p| size px, 17px)")]
    [InlineData("attr(P|size px, 17px)")]
    public void InvalidQualifiedAttributeNameSelectsFallback(string value)
    {
        var border = new Border();
        Css.SetClass(border, "probe");
        Css.SetAttribute(border, "urn:props", "size", "42");
        Css.SetAttribute(border, "size", "99");
        Css.SetStyleSheet(border, "@namespace p 'urn:props'; .probe {width:" + value + "}");

        Layout(border);
        Assert.Equal(17, border.Width);
    }

    [Fact]
    public void FirstArgumentVariableCanSupplyNamespacePrefix()
    {
        var border = new Border();
        Css.SetAttribute(border, "urn:props", "size", "42");
        Css.SetStyleSheet(border, "@namespace p 'urn:props'; Border {--prefix:p;" +
            "width:attr(var(--prefix)|size px, 17px)}");

        Layout(border);
        Assert.Equal(42, border.Width);
    }

    [Fact]
    public void TypedStringAttributeParsesCssStringsWhileRawStringPreservesLiteralText()
    {
        var border = new Border();
        Css.SetAttribute(border, "data-text", "'he\\6c lo'");
        Css.SetStyle(border, "--typed:attr(data-text type(<string>), \"fallback\");" +
            "--raw:attr(data-text raw-string, \"fallback\")");

        Layout(border);
        var values = CssNode.Get(border).CssRuntimeState!.CustomProperties!;
        Assert.Equal("\"hello\"", values["--typed"].Trim());
        Assert.Equal("\"'he\\\\6c lo'\"", values["--raw"].Trim());

        Css.SetAttribute(border, "data-text", "bare");
        Layout(border);
        values = CssNode.Get(border).CssRuntimeState!.CustomProperties!;
        Assert.Equal("\"fallback\"", values["--typed"].Trim());
        Assert.Equal("\"bare\"", values["--raw"].Trim());
    }

    [Fact]
    [Trait("WPT", "css/css-values/attr-all-types.html")]
    public void TypedStringAndStringListAttributesComputeRegisteredValues()
    {
        var border = new Border();
        Css.SetAttribute(border, "data-text", "\"hello\"");
        Css.SetAttribute(border, "data-words", "\"hello\" 'world'");
        Css.SetStyleSheet(border, """
            @property --text { syntax:'<string>'; inherits:false; initial-value:"none" }
            @property --words { syntax:'<string>+'; inherits:false; initial-value:"none" }
            Border { --text:attr(data-text type(<string>)); --words:attr(data-words type(<string>+)) }
            """);

        Layout(border);
        var values = CssNode.Get(border).CssRuntimeState!.CustomProperties!;
        Assert.Equal("\"hello\"", values["--text"]);
        Assert.Equal("\"hello\" \"world\"", values["--words"]);

        Css.SetAttribute(border, "data-text", "hello");
        Css.SetAttribute(border, "data-words", "\"hello\" world");
        Layout(border);
        values = CssNode.Get(border).CssRuntimeState!.CustomProperties!;
        Assert.Equal("\"none\"", values["--text"]);
        Assert.Equal("\"none\"", values["--words"]);
    }

    [Fact]
    public void AttributeValuesCannotBecomeAUrlThroughVariables()
    {
        var border = new Border();
        Css.SetAttribute(border, "data-image", "private.png");
        Css.SetStyle(border, "--image:url(attr(data-image));" +
            "background-image:var(--image);background-color:red");
        Layout(border);

        var paint = Assert.IsType<CssBackgroundPaintState>(CssBackgroundPaintProperties.Get(border));
        Assert.DoesNotContain(paint.Layers, layer => layer.Image is not null);
        Assert.Equal(Colors.Red, Assert.IsType<SolidColorBrush>(paint.Color).Color);

        Css.SetStyle(border, "--secret:attr(data-image);" +
            "background-image:url(var(--secret));background-color:red");
        Layout(border);
        paint = Assert.IsType<CssBackgroundPaintState>(CssBackgroundPaintProperties.Get(border));
        Assert.DoesNotContain(paint.Layers, layer => layer.Image is not null);

        Css.SetAttribute(border, "data-image", "url('private.png')");
        Css.SetStyle(border, "background-image:attr(data-image type(<image>));background-color:red");
        Layout(border);
        paint = Assert.IsType<CssBackgroundPaintState>(CssBackgroundPaintProperties.Get(border));
        Assert.DoesNotContain(paint.Layers, layer => layer.Image is not null);

        Css.SetAttribute(border, "data-image", "linear-gradient(red, blue)");
        Layout(border);
        paint = Assert.IsType<CssBackgroundPaintState>(CssBackgroundPaintProperties.Get(border));
        Assert.Contains(paint.Layers, layer => layer.Image is not null);
    }

    [Fact]
    public void RegisteredAttributesComputeBeforeInheritanceAndFollowAttributeChanges()
    {
        var child = new Border();
        var root = new Border { Child = child };
        Css.SetStyleSheet(root, "@property --size { syntax:'<length>'; inherits:true; initial-value:3px }");
        Css.SetStyle(root, "--size:attr(data-size px, 7px)");
        Css.SetStyle(child, "width:var(--size)");
        Css.SetAttribute(root, "data-size", "42");
        Css.SetAttribute(child, "data-size", "99");

        Layout(root);
        Assert.Equal("42px", CssNode.Get(root).CssRuntimeState?.CustomProperties?.GetValueOrDefault("--size"));
        Assert.Equal("42px", CssNode.Get(child).CssRuntimeState?.CustomProperties?.GetValueOrDefault("--size"));
        Assert.Equal(42, child.Width);

        Css.SetAttribute(root, "data-size", null);
        Layout(root);
        Assert.Equal("7px", CssNode.Get(child).CssRuntimeState?.CustomProperties?.GetValueOrDefault("--size"));
        Assert.Equal(7, child.Width);
    }

    [Fact]
    public void UnregisteredAttributesResolveOnTheirOwnerBeforeInheritance()
    {
        var child = new Border();
        var root = new Border { Child = child };
        Css.SetStyle(root, "--size:attr(data-size px, 7px)");
        Css.SetStyle(child, "width:var(--size, 9px)");
        Css.SetAttribute(root, "data-size", "42");
        Css.SetAttribute(child, "data-size", "99");

        Layout(root);
        Assert.Equal(42, child.Width);
        Assert.DoesNotContain("attr(", CssNode.Get(child).CssRuntimeState!.CustomProperties!["--size"]);
        Assert.Contains("--size", CssNode.Get(child).CssRuntimeState!.AttrTaintedProperties!);

        Css.SetAttribute(root, "data-size", null);
        Layout(root);
        Assert.Equal(7, child.Width);

        Css.SetStyle(root, "--size:attr(data-size px)");
        Layout(root);
        Assert.False(CssNode.Get(child).CssRuntimeState!.CustomProperties!.ContainsKey("--size"));
        Assert.Equal(9, child.Width);
    }

    [Fact]
    public void RegisteredAttributeTaintCannotEnterUrlThroughInheritedVariable()
    {
        var child = new Border();
        var root = new Border { Child = child };
        Css.SetStyleSheet(root, "@property --secret { syntax:'<custom-ident>'; inherits:true; initial-value:safe }");
        Css.SetStyle(root, "--secret:attr(data-image type(<custom-ident>), safe);" +
            "--relay:var(--secret);--safe:public;--clean:var(--safe,var(--secret));" +
            "--whole:url(public) attr(data-color type(<color>),red)");
        Css.SetStyle(child, "background-image:url(var(--secret)); background-color:red");
        Css.SetAttribute(root, "data-image", "private");

        Layout(root);
        Assert.Equal("private", CssNode.Get(child).CssRuntimeState?.CustomProperties?.GetValueOrDefault("--secret"));
        Assert.Contains("--secret", CssNode.Get(root).CssRuntimeState!.AttrTaintedProperties!);
        Assert.Contains("--secret", CssNode.Get(child).CssRuntimeState!.AttrTaintedProperties!);
        Assert.Contains("--relay", CssNode.Get(child).CssRuntimeState!.AttrTaintedProperties!);
        Assert.Contains("--whole", CssNode.Get(child).CssRuntimeState!.AttrTaintedProperties!);
        Assert.DoesNotContain("--clean", CssNode.Get(child).CssRuntimeState!.AttrTaintedProperties!);
        Assert.False(CssCustomProperties.TrySubstitute("url(var(--secret))", CssNode.Get(child), out _));
        Assert.False(CssCustomProperties.TrySubstitute("url(var(--relay))", CssNode.Get(child), out _));
        Assert.False(CssCustomProperties.TrySubstitute("var(--whole)", CssNode.Get(child), out _));
        Assert.True(CssCustomProperties.TrySubstitute("url(var(--safe, var(--secret)))", CssNode.Get(child), out _));
        var paint = Assert.IsType<CssBackgroundPaintState>(CssBackgroundPaintProperties.Get(child));
        Assert.DoesNotContain(paint.Layers, layer => layer.Image is not null);
        Assert.Equal(Colors.Red, Assert.IsType<SolidColorBrush>(paint.Color).Color);

        Css.SetAttribute(root, "data-image", "public");
        Layout(root);
        Assert.Equal("public", CssNode.Get(child).CssRuntimeState!.CustomProperties!["--secret"]);
        Assert.Contains("--secret", CssNode.Get(child).CssRuntimeState!.AttrTaintedProperties!);

        Css.SetStyle(root, "--secret:public;--relay:var(--secret);--safe:public");
        Layout(root);
        Assert.DoesNotContain("--secret", CssNode.Get(child).CssRuntimeState!.AttrTaintedProperties!);
        Assert.True(CssCustomProperties.TrySubstitute("url(var(--secret))", CssNode.Get(child), out var safeUrl));
        Assert.Contains("public", safeUrl);
    }

    private static void Layout(Border root)
    {
        root.Measure(new Size(500, 500));
        root.Arrange(new Rect(0, 0, 500, 500));
        root.UpdateLayout();
    }
}

using System.Reflection;

namespace Jalium.UI.Tests;

public sealed class TextFormatterCanonicalOwnershipTests
{
    [Fact]
    public void OnlyWpfTextFormattingFormatterIsPublic()
    {
        Type[] exported = typeof(FrameworkElement).Assembly.GetExportedTypes();

        Assert.Contains(
            exported,
            type => type.FullName == "Jalium.UI.Media.TextFormatting.TextFormatter");
        Assert.DoesNotContain(
            exported,
            type => type.FullName == "Jalium.UI.Controls.TextFormatter");
        Assert.DoesNotContain(
            exported,
            type => type.FullName == "Jalium.UI.Controls.TextInputFormatter");
    }
}

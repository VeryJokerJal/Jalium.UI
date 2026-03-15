using Jalium.UI;
using Jalium.UI.Markup;
using Jalium.UI.Media;

namespace Jalium.UI.Tests.Resources;

public partial class TestCodeBehindDictionary : ResourceDictionary
{
    public TestCodeBehindDictionary()
    {
        InitializeComponent();
    }

    public string Marker { get; } = "DictionaryCodeBehind";

    public SolidColorBrush AccentBrush =>
        this["CodeBehindAccentBrush"] as SolidColorBrush
        ?? throw new InvalidOperationException("CodeBehindAccentBrush was not loaded.");

    private void InitializeComponent()
    {
        XamlReader.LoadComponent(this, "Jalium.UI.Tests.TestAssets.CodeBehindDictionary.jalxaml", typeof(TestCodeBehindDictionary).Assembly);
    }
}

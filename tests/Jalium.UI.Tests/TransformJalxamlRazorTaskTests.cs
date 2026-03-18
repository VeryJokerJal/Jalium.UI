using Jalium.UI.Build;
using Microsoft.Build.Utilities;

namespace Jalium.UI.Tests;

public class TransformJalxamlRazorTaskTests
{
    [Fact]
    public void TransformJalxamlRazorTask_ShouldNotTreatTemplateLocalsAsExternalRoots()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "Jalium.UI.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            var sourcePath = Path.Combine(tempRoot, "Template.jalxaml");
            var outputPath = Path.Combine(tempRoot, "obj");

            File.WriteAllText(sourcePath, """
                <Border xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                        Width='@{ string Describe(int value) => value > 0 ? "Positive" : "Zero"; var computed = Count * 25; }@computed' />
                """);

            var task = new TransformJalxamlRazorTask
            {
                SourceFiles = [new TaskItem(sourcePath)],
                OutputDirectory = outputPath,
                ProjectDirectory = tempRoot
            };

            Assert.True(task.Execute());

            var generatedPath = Path.Combine(outputPath, "Jalxaml.RazorMetadata.g.cs");
            Assert.True(File.Exists(generatedPath));

            var generated = File.ReadAllText(generatedPath);

            Assert.Contains("""RegisterTemplateEvaluator("Count|::""", generated);
            Assert.Contains("""dynamic Count = __resolve("Count");""", generated);
            Assert.DoesNotContain("""dynamic computed = __resolve("computed");""", generated);
            Assert.DoesNotContain("""dynamic value = __resolve("value");""", generated);
            Assert.DoesNotContain("""dynamic var = __resolve("var");""", generated);
            Assert.DoesNotContain("""dynamic string = __resolve("string");""", generated);
            Assert.DoesNotContain("""dynamic int = __resolve("int");""", generated);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}

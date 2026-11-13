using Jalium.UI.Controls;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class TemplateNameScopeRetirementTests
{
    [Fact]
    public void NamesRegisteredBeforeACallerScopeAreRetiredWithoutTouchingTheNewScope()
    {
        var control = new Control { Template = Template("OldPart") };
        control.ApplyTemplate();
        var callerScope = new NameScope();
        NameScope.SetNameScope(control, callerScope);
        var callerName = new object(); callerScope.RegisterName("CallerOnly", callerName);
        control.Template = Template("NewPart"); control.ApplyTemplate();
        Assert.Same(callerName, callerScope.FindName("CallerOnly"));
        Assert.Null(control.FindName("OldPart"));
        Assert.IsType<Border>(callerScope.FindName("NewPart"));
    }

    [Fact]
    public void ReplacedCallerScopeKeepsItsNamesWhileTheOriginalScopeReleasesTheOldTemplate()
    {
        var control = new Control(); var firstScope = new NameScope();
        NameScope.SetNameScope(control, firstScope);
        control.Template = Template("OldPart"); control.ApplyTemplate();
        Assert.IsType<Border>(firstScope.FindName("OldPart"));
        var secondScope = new NameScope(); var callerName = new object();
        secondScope.RegisterName("OldPart", callerName); NameScope.SetNameScope(control, secondScope);
        control.Template = Template("NewPart"); control.ApplyTemplate();
        Assert.Null(firstScope.FindName("OldPart"));
        Assert.Same(callerName, secondScope.FindName("OldPart"));
        Assert.IsType<Border>(secondScope.FindName("NewPart"));
    }

    private static ControlTemplate Template(string name)
    {
        var template = new ControlTemplate(typeof(Control));
        template.SetVisualTree(() => new Border { Name = name });
        return template;
    }
}

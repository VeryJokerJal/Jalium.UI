using System.Reflection;
using Jalium.UI.Automation;
using Jalium.UI.Automation.Peers;
using Jalium.UI.Automation.Provider;
using Jalium.UI.Controls;
using Jalium.UI.Controls.DevTools;
using Jalium.UI.Input;

namespace Jalium.UI.Tests;

[Collection("macOS Window globals")]
public sealed class MacOSDevToolsReplTests
{
    [Fact]
    public void ActualWorkspaceExecutesLocalsSelectedVisualAndWindowAssignmentsAndRetainsThemAcrossPages()
    {
        Jalium.UI.Markup.ThemeLoader.Initialize(); _ = Application.Current ?? new Application();
        var selected = new Button { Name = "SelectedButton", Content = "Selected target" };
        var target = new Window { Title = "REPL host", Content = selected }; var window = new DevToolsWindow(target);
        try
        {
            typeof(DevToolsWindow).GetMethod("RevealInInspector", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, new object[] { selected });
            var workspace = Assert.IsType<DevToolsWorkspace>(window.Content); workspace.SelectedPage = DevToolsPage.Repl; Layout(window);
            var view = Assert.IsType<DevToolsReplView>(workspace.ReplContent);
            void Run(string command) { view.InputText = command; Assert.True(view.Submit()); }
            Run("let sample = \"中文😀\"; sample"); Run("$.Name"); Run("window.Title = \"Updated host\"; root.Title"); Run("app.ToString()");
            Assert.Equal("Updated host", target.Title);
            var terminal = Assert.IsType<Terminal>(view.FindName("PART_Terminal"));
            string Output() => Assert.IsAssignableFrom<ITextProvider>(terminal.GetAutomationPeer()!.GetPattern(PatternInterface.Text)).DocumentRange.GetText(-1);
            Assert.Contains("=> \"中文😀\"", Output()); Assert.Contains("=> \"SelectedButton\"", Output()); Assert.Contains("=> \"Updated host\"", Output());
            Run("unknownVariable"); Assert.Contains("Unknown identifier", view.LastError); Assert.Contains("E: Unknown identifier", Output());
            workspace.SelectedPage = DevToolsPage.Inspector; workspace.SelectedPage = DevToolsPage.Repl; Layout(window);
            Run("sample"); Assert.Equal("", view.LastError); Assert.Equal(6, view.HistoryCount);
            var editor = Assert.IsType<TextBox>(view.FindName("PART_InputTextBox")); editor.Text = "cancel draft";
            editor.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, new KeySource { RootVisual = editor }, 1, Key.Escape) { RoutedEvent = UIElement.PreviewKeyDownEvent });
            Assert.Equal("", editor.Text); Assert.Equal("", view.InputText);
        }
        finally { Keyboard.ClearFocus(); window.CloseDevTools(); target.Close(); }
    }
    private static void Layout(UIElement element) { element.Measure(new Size(1040, 860)); element.Arrange(new Rect(0, 0, 1040, 860)); element.UpdateLayout(); }
    private sealed class KeySource : PresentationSource { public override Jalium.UI.Media.Visual? RootVisual { get; set; } public override bool IsDisposed => false; protected override Jalium.UI.Media.CompositionTarget? GetCompositionTargetCore() => null; }
}

using System.Text;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.DevTools;

public partial class DevToolsWindow
{
    private void AppendStyleXamlViewer(Style style)
    {
        _propertiesPanel.Children.Add(MakeRevealXamlButton("View style XAML", () => BuildStyleXaml(style)));
    }

    private void AppendTemplateXamlViewer(FrameworkElement fe)
    {
        if (fe is Control ctrl && ctrl.Template != null)
            _propertiesPanel.Children.Add(MakeRevealXamlButton("View ControlTemplate XAML", () => BuildTemplateXaml(ctrl)));
    }

    private Border MakeRevealXamlButton(string label, Func<string> xamlProvider)
    {
        var btn = new Border
        {
            Background = DevToolsTheme.AccentSoft,
            BorderBrush = new SolidColorBrush(Color.FromArgb(0xB4, DevToolsTheme.AccentColor.R, DevToolsTheme.AccentColor.G, DevToolsTheme.AccentColor.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(8, 4, 8, 4),
            Child = new TextBlock
            {
                Text = label.ToUpperInvariant(),
                FontSize = 11,
                FontFamily = DevToolsTheme.DisplayFont,
                Foreground = DevToolsTheme.Accent,
            },
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        btn.MouseDown += (_, _) =>
        {
            try
            {
                var xaml = xamlProvider();
                ShowXamlPopup(label, xaml);
            }
            catch (Exception ex)
            {
                ShowXamlPopup(label, $"(failed to render XAML: {ex.Message})");
            }
        };
        return btn;
    }

    private void ShowXamlPopup(string title, string xaml)
    {
        var popup = new Window
        {
            Title = "DevTools — " + title,
            Width = 640,
            Height = 520,
            SystemBackdrop = WindowBackdropType.Mica,
            Background = DevToolsTheme.Chrome,
        };

        var tb = new TextBox
        {
            Text = xaml,
            AcceptsReturn = true,
            FontSize = 12,
            FontFamily = DevToolsTheme.MonoFont,
            Foreground = DevToolsTheme.TextPrimary,
            Background = DevToolsTheme.Surface,
            BorderBrush = DevToolsTheme.Border,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            Margin = new Thickness(6),
            IsReadOnly = true,
        };
        var scroll = new ScrollViewer
        {
            Content = tb,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        popup.Content = scroll;
        popup.Show();
    }

    private static string BuildStyleXaml(Style style)
    {
        var sb = new StringBuilder();
        sb.Append("<Style");
        if (style.TargetType != null)
            sb.Append(" TargetType=\"").Append(style.TargetType.Name).Append('"');
        if (style.BasedOn != null && style.BasedOn.TargetType != null)
            sb.Append(" BasedOn=\"{StaticResource ").Append(style.BasedOn.TargetType.Name).Append("Style}\"");
        sb.AppendLine(">");

        foreach (var setter in style.Setters)
        {
            var name = setter.Property?.Name ?? "?";
            var value = setter.Value switch
            {
                null => "{x:Null}",
                string s => s,
                SolidColorBrush scb => $"#{scb.Color.A:X2}{scb.Color.R:X2}{scb.Color.G:X2}{scb.Color.B:X2}",
                _ => setter.Value.ToString() ?? "",
            };
            sb.Append("  <Setter Property=\"").Append(name).Append("\" Value=\"").Append(EscapeXml(value)).AppendLine("\" />");
        }

        if (style.Triggers.Count > 0)
        {
            sb.AppendLine("  <Style.Triggers>");
            foreach (var trig in style.Triggers)
            {
                sb.Append("    <!-- Trigger: ").Append(trig.GetType().Name).AppendLine(" -->");
            }
            sb.AppendLine("  </Style.Triggers>");
        }

        sb.AppendLine("</Style>");
        return sb.ToString();
    }

    private static string BuildTemplateXaml(Control ctrl)
    {
        var template = ctrl.Template;
        if (template == null) return "(no template)";
        var sb = new StringBuilder();
        sb.Append("<ControlTemplate");
        if (template.TargetType != null)
            sb.Append(" TargetType=\"").Append(template.TargetType.Name).Append('"');
        sb.AppendLine(">");

        // Walk the live visual tree of the control — the template has been instantiated,
        // so the first templated child is a reasonable approximation of the template body.
        if (ctrl.VisualChildrenCount > 0 && ctrl.GetVisualChild(0) is Visual root)
        {
            WriteTemplateNode(root, 1, sb);
        }
        else
        {
            sb.AppendLine("  <!-- template not yet applied -->");
        }

        sb.AppendLine("</ControlTemplate>");
        return sb.ToString();
    }

    private static void WriteTemplateNode(Visual visual, int depth, StringBuilder sb)
    {
        var indent = new string(' ', depth * 2);
        string typeName = visual.GetType().Name;
        sb.Append(indent).Append('<').Append(typeName);

        if (visual is FrameworkElement fe)
        {
            if (!string.IsNullOrEmpty(fe.Name))
                sb.Append(" Name=\"").Append(EscapeXml(fe.Name)).Append('"');
        }

        int childCount = visual.VisualChildrenCount;
        if (childCount == 0)
        {
            sb.AppendLine(" />");
            return;
        }
        sb.AppendLine(">");
        for (int i = 0; i < childCount; i++)
        {
            if (visual.GetVisualChild(i) is Visual c)
                WriteTemplateNode(c, depth + 1, sb);
        }
        sb.Append(indent).Append("</").Append(typeName).AppendLine(">");
    }
}

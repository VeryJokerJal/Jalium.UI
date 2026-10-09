using Jalium.UI;
using Jalium.UI.Automation;
using Jalium.UI.Controls;
using Jalium.UI.Controls.Primitives;
using Jalium.UI.Documents;
using Jalium.UI.Media;

namespace MacOSWindowLab;

internal static class WindowEditingLab
{
    internal static Window Create(bool nativeTitleBar, bool small)
    {
        var ink = new SolidColorBrush(Color.FromRgb(0x16, 0x32, 0x42));
        var window = new Window
        {
            Title = "原生编辑菜单 · Window 验证", Width = small ? 520 : 840, Height = small ? 580 : 690,
            MinWidth = 520, MinHeight = 580, WindowStartupLocation = WindowStartupLocation.CenterScreen,
            TitleBarStyle = nativeTitleBar ? WindowTitleBarStyle.Native : WindowTitleBarStyle.Custom,
            Background = new SolidColorBrush(Color.FromRgb(0xf5, 0xfa, 0xfc))
        };
        var body = new StackPanel { Margin = new Thickness(24), Spacing = 12 };
        body.Children.Add(new TextBlock { Text = "原生编辑菜单", FontSize = 28, Foreground = ink });
        body.Children.Add(new TextBlock
        {
            Text = "在样例中编辑或选择文字，再打开系统编辑菜单。检查可用状态，执行撤销、重做和全选后继续编辑。",
            FontSize = 15, TextWrapping = TextWrapping.Wrap, Foreground = ink
        });
        var types = new WrapPanel();
        body.Children.Add(types);
        var label = new TextBlock { Text = "编辑样例", FontSize = 15, Foreground = ink };
        body.Children.Add(label);
        var host = new ContentControl { Height = 160 };
        body.Children.Add(host);
        var readOnly = new CheckBox { Content = "只读", MinHeight = 44, FontSize = 15 };
        body.Children.Add(readOnly);
        var actions = new WrapPanel(); body.Children.Add(actions);
        var status = new TextBlock { FontSize = 15, Foreground = ink, TextWrapping = TextWrapping.Wrap, MinHeight = 60 };
        body.Children.Add(status);
        body.Children.Add(new TextBlock
        {
            Text = "只读时允许复制与全选。密码框不提供复制或剪切；焦点在操作按钮上时，编辑命令应不可用。",
            FontSize = 14, TextWrapping = TextWrapping.Wrap, Foreground = ink
        });
        Control editor = null!;
        int currentKind = 0;
        void Refresh()
        {
            status.Text = $"当前控件：{editor.GetType().Name} · {(readOnly.IsChecked == true ? "只读" : "可编辑")}\n"
                + $"编辑焦点：{(editor.IsKeyboardFocused ? "有" : "无")} · 从菜单返回后检查文字和选区";
        }
        void ApplyReadOnly()
        {
            bool value = readOnly.IsChecked == true;
            switch (editor)
            {
                case TextBoxBase text: text.IsReadOnly = value; break;
                case EditControl edit: edit.IsReadOnly = value; break;
            }
            Refresh();
        }
        void SelectEditor(int kind, bool focus)
        {
            currentKind = kind;
            editor = kind switch
            {
                1 => new RichTextBox(FlowDocument.FromText("中文富文本🙂 原生编辑菜单")) { Height = 160 },
                2 => new EditControl { Text = "中文编辑器🙂 原生编辑菜单", Height = 160 },
                3 => new PasswordBox { Password = "菜单验证样例", Height = 48, FontSize = 16 },
                _ => new TextBox { Text = "中文文本框🙂 原生编辑菜单", Height = 160, AcceptsReturn = true, FontSize = 16 }
            };
            AutomationProperties.SetName(editor, "原生菜单验证编辑框");
            AutomationProperties.SetLabeledBy(editor, label);
            editor.IsKeyboardFocusedChanged += (_, _) => Refresh();
            host.Content = editor;
            readOnly.Visibility = kind == 3 ? Visibility.Collapsed : Visibility.Visible;
            if (kind == 3) readOnly.IsChecked = false;
            ApplyReadOnly();
            if (focus) { window.UpdateLayout(); editor.Focus(); }
            Refresh();
        }
        foreach (var (name, kind) in new[] { ("文本框", 0), ("富文本", 1), ("编辑器", 2), ("密码框", 3) })
        {
            var button = new Button { Content = name, MinWidth = 82, MinHeight = 40, Margin = new Thickness(0, 0, 8, 8) };
            button.Click += (_, _) => SelectEditor(kind, true);
            types.Children.Add(button);
        }
        var reset = new Button { Content = "重置样例", MinHeight = 44, Margin = new Thickness(0, 0, 8, 8) };
        reset.Click += (_, _) => SelectEditor(currentKind, true);
        actions.Children.Add(reset);
        var menu = new Button { Content = "打开原生编辑菜单", MinHeight = 44, Margin = new Thickness(0, 0, 8, 8) };
        menu.Click += (_, _) => { editor.Focus(); Refresh(); Program.ShowNativeEditingMenu(window); };
        actions.Children.Add(menu);
        var format = new Button { Content = "格式样例", MinHeight = 44, Margin = new Thickness(0, 0, 8, 8) };
        format.Click += (_, _) =>
        {
            if (editor is EditControl code)
            {
                code.FontFamily = new FontFamily("Arial"); code.FontSize = 21;
                code.FontWeight = FontWeights.Bold; code.FontStyle = FontStyles.Italic;
                code.Text = "Bold Italic · 中文🙂 e\u0301\n编辑与选区应保留格式";
                window.UpdateLayout(); code.Focus(); Refresh(); return;
            }
            SelectEditor(1, false);
            var rich = (RichTextBox)editor;
            var document = new FlowDocument { FontFamily = new FontFamily("Arial"), FontSize = 21 };
            var paragraph = new Paragraph();
            paragraph.Inlines.Add(new Run("普通中文🙂 ") { Foreground = ink });
            var red = new SolidColorBrush(Color.FromRgb(0xa0, 0x12, 0x34));
            paragraph.Inlines.Add(new Bold(new Underline(new Run("粗体下划线🙂")
                { FontSize = 27, Foreground = red }) { Foreground = red }));
            paragraph.Inlines.Add(new Run("\ne\u0301 组合字符 · ") { Foreground = ink });
            paragraph.Inlines.Add(new Italic(new Run("Italic") { Foreground = ink }));
            document.Blocks.Add(paragraph); rich.Document = document;
            window.UpdateLayout(); rich.Focus(); Refresh();
        };
        actions.Children.Add(format);
        var matchedFont = new Button { Content = "字体匹配", MinHeight = 44, Margin = new Thickness(0, 0, 8, 8) };
        matchedFont.Click += (_, _) => ShowFontSample("'Missing, Window121', 'Helvetica Neue'",FontStyles.Normal,
            "窄粗体 · Miii 中文🙂 e\u0301\n缺失字体后按顺序匹配 Helvetica Neue");
        actions.Children.Add(matchedFont);
        var slantedFont = new Button { Content = "窄斜体", MinHeight = 44, Margin = new Thickness(0, 0, 8, 8) };
        slantedFont.Click += (_, _) => ShowFontSample("SF Pro",FontStyles.Oblique,
            "窄斜体 · Miii 中文🙂 e\u0301\n变量字体字宽和倾斜应保留");
        actions.Children.Add(slantedFont);
        void ShowFontSample(string family,FontStyle style,string sample)
        {
            if (editor is PasswordBox) SelectEditor(0, false);
            editor.FontFamily = new FontFamily(family);
            editor.FontSize = 21; editor.FontWeight = FontWeights.Bold; editor.FontStyle = style;
            editor.FontStretch = FontStretches.Condensed;
            if (editor is RichTextBox rich)
                rich.Document = new FlowDocument { FontFamily = editor.FontFamily, FontSize = 21,
                    FontWeight = editor.FontWeight, FontStretch = editor.FontStretch, FontStyle = editor.FontStyle,
                    Blocks = { new Paragraph(new Run(sample)) } };
            else if (editor is TextBox text) text.Text = sample;
            else if (editor is EditControl code) code.Text = sample;
            window.UpdateLayout(); editor.Focus(); Refresh();
        }
        readOnly.Checked += (_, _) => ApplyReadOnly();
        readOnly.Unchecked += (_, _) => ApplyReadOnly();
        SelectEditor(0, false);
        window.Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        return window;
    }
}

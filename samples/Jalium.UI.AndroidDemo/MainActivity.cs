using Android.App;
using Jalium.UI.Input.TextInput;
using Jalium.UI.Media;
using Button = Jalium.UI.Controls.Button;
using Orientation = Jalium.UI.Controls.Orientation;
using Window = Jalium.UI.Window;
using TextBlock = Jalium.UI.Controls.TextBlock;
using StackPanel = Jalium.UI.Controls.StackPanel;
using TextBox = Jalium.UI.Controls.TextBox;
using PasswordBox = Jalium.UI.Controls.PasswordBox;
using ScrollViewer = Jalium.UI.Controls.ScrollViewer;
using ScrollBarVisibility = Jalium.UI.Controls.ScrollBarVisibility;

namespace Jalium.UI.AndroidDemo;

[Activity(
    Label = "Jalium Demo",
    MainLauncher = true,
    Theme = "@android:style/Theme.NoTitleBar.Fullscreen",
    ConfigurationChanges = Android.Content.PM.ConfigChanges.Orientation
        | Android.Content.PM.ConfigChanges.ScreenSize
        | Android.Content.PM.ConfigChanges.KeyboardHidden)]
public class MainActivity : JaliumActivity
{
    private static readonly SolidColorBrush FieldBackground = new(Color.FromRgb(49, 50, 68));
    private static readonly SolidColorBrush FieldBorder = new(Color.FromRgb(88, 91, 112));
    private static readonly SolidColorBrush FieldForeground = new(Colors.White);
    private static readonly SolidColorBrush LabelForeground = new(Color.FromRgb(166, 173, 200));
    private static readonly SolidColorBrush DisabledForeground = new(Color.FromRgb(108, 112, 134));

    protected override JaliumApp CreateHostedApp()
    {
        var builder = AppBuilder.CreateBuilder();
        builder.ConfigureApplication(app => app.MainWindow = BuildMainWindow());
        return builder.Build();
    }

    private static Window BuildMainWindow()
    {
        var window = new Window
        {
            Title = "Jalium Keyboard Demo",
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 46)),
        };

        var form = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(24, 32, 24, 48),
        };

        form.Children.Add(new TextBlock
        {
            Text = "系统键盘适配演示",
            FontSize = 30,
            Foreground = FieldForeground,
            Margin = new Thickness(0, 0, 0, 4),
        });
        form.Children.Add(new TextBlock
        {
            Text = "点击任意输入框弹出对应的系统键盘；回车在字段间跳转。",
            FontSize = 14,
            Foreground = LabelForeground,
            Margin = new Thickness(0, 0, 0, 24),
        });

        // Requirement 2: one field per content type, plus continuous-form Return keys.
        form.Children.Add(Field("普通文本", MakeTextBox(TextInputContentType.Normal, TextInputReturnKeyType.Next)));
        form.Children.Add(Field("多行文本", MakeMultilineTextBox()));
        form.Children.Add(Field("数字 / 数量", MakeTextBox(TextInputContentType.Digits, TextInputReturnKeyType.Next)));
        form.Children.Add(Field("金额（含小数点）", MakeTextBox(TextInputContentType.Number, TextInputReturnKeyType.Next)));
        form.Children.Add(Field("手机号", MakeTextBox(TextInputContentType.Telephone, TextInputReturnKeyType.Next)));
        form.Children.Add(Field("邮箱", MakeTextBox(TextInputContentType.Email, TextInputReturnKeyType.Next)));
        form.Children.Add(Field("密码", MakePasswordBox()));
        form.Children.Add(Field("搜索（回车显示“搜索”）", MakeTextBox(TextInputContentType.Search, TextInputReturnKeyType.Search)));

        // Requirement 8: a short continuous form ending in a Done key.
        form.Children.Add(Field("姓名（下一项）", MakeTextBox(TextInputContentType.Name, TextInputReturnKeyType.Next)));
        form.Children.Add(Field("备注（完成）", MakeTextBox(TextInputContentType.Normal, TextInputReturnKeyType.Done)));

        // Requirement 9: read-only and disabled fields must not raise the keyboard.
        var readOnly = MakeTextBox(TextInputContentType.Normal, TextInputReturnKeyType.Default);
        readOnly.Text = "只读文本（不弹键盘）";
        readOnly.IsReadOnly = true;
        form.Children.Add(Field("只读", readOnly));

        var disabled = MakeTextBox(TextInputContentType.Normal, TextInputReturnKeyType.Default);
        disabled.Text = "禁用文本（不弹键盘）";
        disabled.IsEnabled = false;
        disabled.Foreground = DisabledForeground;
        form.Children.Add(Field("禁用", disabled));

        var submit = new Button
        {
            Background = new SolidColorBrush(Color.FromRgb(137, 180, 250)),
            CornerRadius = new CornerRadius(8),
            MinHeight = 52,
            Margin = new Thickness(0, 16, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Content = new TextBlock
            {
                Text = "提交",
                FontSize = 18,
                Foreground = new SolidColorBrush(Color.FromRgb(30, 30, 46)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        form.Children.Add(submit);

        window.Content = new ScrollViewer
        {
            Content = form,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        return window;
    }

    private static StackPanel Field(string label, Jalium.UI.Controls.Control control)
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(0, 0, 0, 16),
        };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 13,
            Foreground = LabelForeground,
            Margin = new Thickness(2, 0, 0, 6),
        });
        panel.Children.Add(control);
        return panel;
    }

    private static TextBox MakeTextBox(TextInputContentType contentType, TextInputReturnKeyType returnKey)
    {
        var box = new TextBox
        {
            FontSize = 18,
            MinHeight = 48,
            Padding = new Thickness(12, 10, 12, 10),
            Background = FieldBackground,
            BorderBrush = FieldBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Foreground = FieldForeground,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        TextInputOptions.SetContentType(box, contentType);
        TextInputOptions.SetReturnKeyType(box, returnKey);
        return box;
    }

    private static TextBox MakeMultilineTextBox()
    {
        var box = MakeTextBox(TextInputContentType.Normal, TextInputReturnKeyType.Default);
        box.AcceptsReturn = true;
        box.MinHeight = 96;
        TextInputOptions.SetMultiline(box, true);
        return box;
    }

    private static PasswordBox MakePasswordBox()
    {
        return new PasswordBox
        {
            FontSize = 18,
            MinHeight = 48,
            Padding = new Thickness(12, 10, 12, 10),
            Background = FieldBackground,
            BorderBrush = FieldBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Foreground = FieldForeground,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
    }
}

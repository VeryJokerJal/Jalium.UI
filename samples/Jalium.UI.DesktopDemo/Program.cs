using Jalium.UI;
using Jalium.UI.Controls;
using Jalium.UI.Hosting;
using Jalium.UI.Interop;
using Jalium.UI.Media;
using Jalium.UI.Notifications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Jalium.UI.DesktopDemo;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        Console.WriteLine("[Demo] step 0: Main entered");

        // Auto = honour JALIUM_RENDER_BACKEND (vulkan/d3d12/software); the
        // platform default order still picks D3D12 on Windows when unset.
        var renderContext = RenderContext.GetOrCreateCurrent(RenderBackend.Auto);
        renderContext.DefaultRenderingEngine = RenderingEngine.Impeller;
        Console.WriteLine($"[Demo] step 1: render ctx backend={renderContext.Backend} engine={renderContext.DefaultRenderingEngine}");

        var builder = AppBuilder.CreateBuilder(args);
        Console.WriteLine("[Demo] step 2: builder created");

        builder.Logging.SetMinimumLevel(LogLevel.Information);
        builder.Services.AddSingleton<ToastNotificationHost>(_ => new ToastNotificationHost
        {
            Position = ToastPosition.TopRight,
            MaxVisibleToasts = 5,
            ToastWidth = 350
        });

        builder.ConfigureApplication(app =>
        {
            Console.WriteLine($"[Demo] step 3a: ConfigureApplication, app type={app.GetType().FullName}");
            SystemNotificationManager.Current.Initialize("Jalium.UI.DesktopDemo", "Jalium Desktop Demo");
            Console.WriteLine("[Demo] step 3b: SystemNotificationManager initialized");

            var toastHost = app.Services!.GetRequiredService<ToastNotificationHost>();
            ToastService.SetHost(toastHost);
            Console.WriteLine("[Demo] step 3c: toast host wired");

            Console.WriteLine("[Demo] step 3d: building EffectReproWindow...");
            app.MainWindow = EffectReproWindow.Build();
            Console.WriteLine($"[Demo] step 3e: MainWindow set, title='{app.MainWindow?.Title}'");
        });

        Console.WriteLine("[Demo] step 4: about to Build host");
        using var host = builder.Build();
        Console.WriteLine("[Demo] step 5: host built; about to host.Run()");
        host.UseDeveloperTools();
        var exit = host.Run();
        Console.WriteLine($"[Demo] step 6: host.Run() returned exit={exit}");
        return exit;
    }

    private static Window BuildMainWindow(ToastNotificationHost toastHost)
    {
        var window = new Window
        {
            Title = "Jalium.UI 通知测试",
            Width = 600,
            Height = 500,
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 46))
        };

        var root = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(32)
        };

        root.Children.Add(new TextBlock
        {
            Text = "系统通知测试",
            FontSize = 28,
            Foreground = new SolidColorBrush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 32)
        });

        var statusText = new TextBlock
        {
            Text = "点击按钮发送系统通知",
            FontSize = 14,
            Foreground = new SolidColorBrush(Color.FromRgb(160, 160, 180)),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 24)
        };
        root.Children.Add(statusText);

        // ── 按钮 1: 简单通知 ──
        root.Children.Add(CreateButton("发送简单通知", () =>
        {
            SystemNotificationManager.Current.Show("你好！", "这是来自 Jalium.UI 的系统通知");
            statusText.Text = "✓ 已发送简单通知";
        }));

        // ── 按钮 2: 带按钮的通知 ──
        root.Children.Add(CreateButton("发送带操作按钮的通知", () =>
        {
            var handle = SystemNotificationManager.Current.Show(new NotificationContent
            {
                Title = "文件下载完成",
                Body = "report.pdf (2.3 MB) 已保存到下载目录",
                Tag = "download-1",
                Group = "downloads",
                Actions =
                {
                    new NotificationAction { Id = "open", Label = "打开文件" },
                    new NotificationAction { Id = "folder", Label = "打开文件夹" }
                }
            });

            handle.Activated += (_, e) =>
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    statusText.Text = $"通知被点击: action={e.ActionId ?? "(body)"}";
                });
            };

            statusText.Text = "✓ 已发送带按钮通知";
        }));

        // ── 按钮 3: 高优先级通知 ──
        root.Children.Add(CreateButton("发送高优先级通知", () =>
        {
            SystemNotificationManager.Current.Show(new NotificationContent
            {
                Title = "⚠ 磁盘空间不足",
                Body = "C: 盘剩余空间不足 1GB，请清理磁盘",
                Priority = NotificationPriority.High,
                Tag = "disk-warning"
            });
            statusText.Text = "✓ 已发送高优先级通知";
        }));

        // ── 按钮 4: 静默通知 ──
        root.Children.Add(CreateButton("发送静默通知（无声音）", () =>
        {
            SystemNotificationManager.Current.Show(new NotificationContent
            {
                Title = "后台同步完成",
                Body = "已同步 42 个文件到云端",
                Silent = true,
                Priority = NotificationPriority.Low
            });
            statusText.Text = "✓ 已发送静默通知";
        }));

        // ── 按钮 5: 带图片的通知 ──
        root.Children.Add(CreateButton("发送带图片的通知", () =>
        {
            int size = 32;
            byte[] pixels = new byte[size * size * 4];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int i = (y * size + x) * 4;
                    pixels[i + 0] = (byte)(x * 8);
                    pixels[i + 1] = (byte)(y * 8);
                    pixels[i + 2] = 200;
                    pixels[i + 3] = 255;
                }
            }

            var icon = BitmapImage.FromPixels(pixels, size, size);

            SystemNotificationManager.Current.Show(new NotificationContent
            {
                Title = "图片通知",
                Body = "这条通知包含一个动态生成的图标",
                Icon = icon
            });
            statusText.Text = "✓ 已发送带图片通知";
        }));

        // ── 按钮 6: 清除全部 ──
        root.Children.Add(CreateButton("清除所有通知", () =>
        {
            SystemNotificationManager.Current.ClearAll();
            statusText.Text = "✓ 已清除所有通知";
        }));

        // ── In-App Toast 测试 ──
        root.Children.Add(new TextBlock
        {
            Text = "In-App Toast 测试",
            FontSize = 20,
            Foreground = new SolidColorBrush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 32, 0, 16)
        });

        root.Children.Add(CreateButton("显示 In-App Toast", () =>
        {
            ToastService.ShowSuccess("操作成功", "数据已保存", TimeSpan.FromSeconds(3));
        }));

        // ── Path 性能测试 ──
        root.Children.Add(new TextBlock
        {
            Text = "Path 性能测试 (临时,本轮 path 优化的验证页)",
            FontSize = 20,
            Foreground = new SolidColorBrush(Colors.White),
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 32, 0, 16)
        });

        root.Children.Add(CreateButton("打开 Path 性能测试窗口", () =>
        {
            var w = PathPerfWindow.Build();
            w.Show();
        }));

        root.Children.Add(CreateButton("打开滚动压力测试 (复现按次累积)", () =>
        {
            var w = ScrollStressWindow.Build();
            w.Show();
        }));

        var grid = new Grid();
        grid.Children.Add(root);
        grid.Children.Add(toastHost);

        window.Content = grid;
        return window;
    }

    private static Button CreateButton(string text, Action onClick)
    {
        var button = new Button
        {
            Background = new SolidColorBrush(Color.FromRgb(88, 91, 112)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(108, 112, 134)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            MinWidth = 280,
            MinHeight = 40,
            HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(16, 8, 16, 8),
            Margin = new Thickness(0, 4, 0, 4),
            Content = new TextBlock
            {
                Text = text,
                FontSize = 14,
                Foreground = new SolidColorBrush(Colors.White),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        button.Click += (_, _) => onClick();
        return button;
    }
}

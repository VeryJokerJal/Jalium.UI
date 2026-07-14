using System.Runtime.InteropServices;
using Jalium.UI;
using Jalium.UI.Controls;

namespace Jalium.UI.NuGetTest.Linux;

internal static class Program
{
    private static readonly string[] NativeLibraries =
    [
        "libjalium.native.core.so",
        "libjalium.native.platform.so",
        "libjalium.native.media.core.so",
        "libjalium.native.media.so",
        "libjalium.native.text.so",
        "libjalium.native.vulkan.so",
        "libjalium.native.software.so",
    ];

    private static int Main(string[] args)
    {
        if (!OperatingSystem.IsLinux())
        {
            Console.Error.WriteLine("This NuGet consumer smoke must run on Linux.");
            return 2;
        }

        foreach (string library in NativeLibraries)
        {
            string path = Path.Combine(AppContext.BaseDirectory, library);
            if (!File.Exists(path))
            {
                string? bundleExtractBase = Environment.GetEnvironmentVariable(
                    "DOTNET_BUNDLE_EXTRACT_BASE_DIR");
                if (!string.IsNullOrWhiteSpace(bundleExtractBase) && Directory.Exists(bundleExtractBase))
                {
                    string[] matches = Directory
                        .EnumerateFiles(bundleExtractBase, library, SearchOption.AllDirectories)
                        .Take(2)
                        .ToArray();
                    if (matches.Length == 1)
                    {
                        path = matches[0];
                    }
                    else if (matches.Length > 1)
                    {
                        Console.Error.WriteLine(
                            $"Ambiguous extracted native payload '{library}' under {bundleExtractBase}.");
                        return 3;
                    }
                }

                if (!File.Exists(path))
                {
                    Console.Error.WriteLine(
                        $"Missing packaged native payload '{library}' beside the app or under the bundle extraction root.");
                    return 3;
                }
            }

            // Loading by absolute path proves both that the package copied the
            // current RID and that every transitive ELF dependency is present.
            // A single-file publish resolves the same payload from the clean
            // DOTNET_BUNDLE_EXTRACT_BASE_DIR populated before Main starts.
            _ = NativeLibrary.Load(path);
            Console.WriteLine($"[nuget-consumer] loaded {library} from {path}");
        }

        if (args.Contains("--load-only", StringComparer.Ordinal))
        {
            Console.WriteLine("[nuget-consumer] load-only passed");
            return 0;
        }

        int lifetimeMilliseconds = int.TryParse(
            Environment.GetEnvironmentVariable("JALIUM_NUGET_SMOKE_MS"),
            out int parsed)
            ? Math.Clamp(parsed, 250, 10_000)
            : 1_500;

        var builder = AppBuilder.CreateBuilder(new AppBuilderSettings
        {
            Args = args,
            DisableDefaults = true,
        });

        builder.ConfigureApplication(application =>
        {
            var window = new Window
            {
                Title = "Jalium.UI Linux NuGet consumer",
                Width = 480,
                Height = 240,
                Content = new TextBlock
                {
                    Text = "Restored exclusively from generated NuGet packages",
                    Margin = new Thickness(24),
                },
            };

            window.Shown += (_, _) =>
            {
                Console.WriteLine("[nuget-consumer] ready");
                var closer = new Thread(() =>
                {
                    Thread.Sleep(lifetimeMilliseconds);
                    window.Dispatcher.BeginInvoke((Action)window.Close);
                })
                {
                    IsBackground = true,
                    Name = "NuGet consumer smoke timeout",
                };
                closer.Start();
            };
            window.Closed += (_, _) => Console.WriteLine("[nuget-consumer] completed");
            application.MainWindow = window;
        });

        using var host = builder.Build();
        return host.Run();
    }
}

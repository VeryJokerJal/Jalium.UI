using System.Diagnostics;
using System.IO.Pipes;
using System.Text.RegularExpressions;
using Jalium.UI.Markup;

// Jalium JALXAML hot-reload file watcher.
//
//   Usage: Jalium.UI.HotReload.Watcher <app.exe> <watch-dir> [-- <app-args>...]
//
// Launches <app.exe> with JALIUM_HOTRELOAD_PIPE set, watches *.jalxaml under <watch-dir>, and on every
// save reads the file, infers its x:Class, and pushes (xClass, path, content) over the named pipe to the
// running app's HotReloadAgent using the shared HotReloadProtocol. This is the "client" half the
// framework repo previously lacked — the agent was only ever driven by unit tests before.

return Watcher.Run(args);

internal static class Watcher
{
    public static int Run(string[] args)
    {
        if (args.Length < 2 || args[0] is "-h" or "--help")
        {
            Console.Error.WriteLine("Usage: Jalium.UI.HotReload.Watcher <app.exe> <watch-dir> [-- <app-args>...]");
            return 1;
        }

        var appPath = Path.GetFullPath(args[0]);
        var watchDir = Path.GetFullPath(args[1]);
        var appArgs = args.SkipWhile(static a => a != "--").Skip(1).ToArray();

        if (!File.Exists(appPath))
        {
            Console.Error.WriteLine($"[watcher] app not found: {appPath}");
            return 1;
        }

        if (!Directory.Exists(watchDir))
        {
            Console.Error.WriteLine($"[watcher] watch directory not found: {watchDir}");
            return 1;
        }

        var pipeName = "jalium-hr-" + Guid.NewGuid().ToString("N");

        var psi = new ProcessStartInfo(appPath)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(appPath)!,
        };
        psi.Environment[HotReloadProtocol.PipeEnvironmentVariable] = pipeName;
        foreach (var a in appArgs)
        {
            psi.ArgumentList.Add(a);
        }

        Console.WriteLine($"[watcher] launching  {appPath}");
        Console.WriteLine($"[watcher] pipe       {pipeName}");
        Console.WriteLine($"[watcher] watching   {watchDir}\\**\\*.jalxaml");

        var app = Process.Start(psi);
        if (app is null)
        {
            Console.Error.WriteLine("[watcher] failed to start app.");
            return 1;
        }

        var lastPush = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
        var gate = new object();

        void OnChanged(object? sender, FileSystemEventArgs e)
        {
            // Editors fire several events per save; debounce per file.
            lock (gate)
            {
                var now = DateTime.UtcNow;
                if (lastPush.TryGetValue(e.FullPath, out var last) && (now - last).TotalMilliseconds < 250)
                {
                    return;
                }

                lastPush[e.FullPath] = now;
            }

            // Brief settle so the editor has finished writing before we read.
            Thread.Sleep(80);
            Push(e.FullPath, pipeName);
        }

        using var watcher = new FileSystemWatcher(watchDir, "*.jalxaml")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true,
        };
        watcher.Changed += OnChanged;
        watcher.Created += OnChanged;
        watcher.Renamed += (s, e) => OnChanged(s, new FileSystemEventArgs(WatcherChangeTypes.Changed, Path.GetDirectoryName(e.FullPath)!, Path.GetFileName(e.FullPath)));

        Console.WriteLine("[watcher] ready — edit a .jalxaml file to hot reload. Close the app (or Ctrl+C) to stop.");
        app.WaitForExit();
        Console.WriteLine("[watcher] app exited; stopping.");
        return 0;
    }

    private static void Push(string path, string pipeName)
    {
        string content;
        try
        {
            content = ReadWithRetry(path);
        }
        catch (IOException ex)
        {
            Console.WriteLine($"[watcher] could not read {Path.GetFileName(path)}: {ex.Message}");
            return;
        }

        var xClass = ExtractXClass(content);
        if (xClass is null)
        {
            Console.WriteLine($"[watcher] {Path.GetFileName(path)}: no x:Class — skipped (only x:Class pages can be patched).");
            return;
        }

        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
            client.Connect(3000);
            HotReloadProtocol.WriteRequest(client, xClass, path, content);
            var result = HotReloadProtocol.ReadResult(client);

            var mark = result.FailedElements == 0 ? "OK " : "ERR";
            var detail = $"updated={result.UpdatedElements} fallback={result.FallbackReplacements} failed={result.FailedElements}";
            var message = string.IsNullOrEmpty(result.Message) ? string.Empty : $" — {result.Message}";
            Console.WriteLine($"[watcher] {mark} {Path.GetFileName(path)} → {xClass}: {detail}{message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[watcher] push failed for {Path.GetFileName(path)}: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static string ReadWithRetry(string path)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(60);
            }
        }
    }

    private static string? ExtractXClass(string jalxaml)
    {
        var match = Regex.Match(jalxaml, "x:Class\\s*=\\s*\"([^\"]+)\"");
        return match.Success ? match.Groups[1].Value : null;
    }
}

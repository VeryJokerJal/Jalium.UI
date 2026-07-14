// Inline MSBuild task compiled by RoslynCodeTaskFactory from
// JaliumStaleNativeGuard.targets. Validates, at pack time, that no native
// binary about to be embedded in a NuGet package is provably stale relative
// to the git history of the sources it was built from. See the .targets file
// for the finding categories ([JALSTALE01]..[JALSTALE08]) and the escape
// hatch (-p:JaliumAllowStaleNative=true).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Build.Framework;

public class JaliumValidateStaleNative : Microsoft.Build.Utilities.Task
{
    [Required]
    public string RepoRoot { get; set; }

    public ITaskItem[] Binaries { get; set; }

    public ITaskItem[] ModuleMap { get; set; }

    public string SharedPathSpec { get; set; }

    public string ExtraPathSpec { get; set; }

    public string TestsExcludePathSpec { get; set; }

    public bool AllowStale { get; set; }

    public bool WarnIfEmpty { get; set; }

    public string StampFileName { get; set; }

    private bool _gitUnavailable;

    public override bool Execute()
    {
        var findings = new List<string>();
        var stampName = string.IsNullOrEmpty(StampFileName) ? ".jalium-native-complete" : StampFileName;

        // Only binaries built from src/native are guarded; third-party payload
        // (WebView2Loader.dll, libc++_shared.so) has no git history to compare.
        var guarded = new List<KeyValuePair<string, string>>();
        foreach (var item in Binaries ?? new ITaskItem[0])
        {
            var fullPath = item.GetMetadata("FullPath");
            if (string.IsNullOrEmpty(fullPath))
                fullPath = item.ItemSpec;
            var baseName = Path.GetFileName(fullPath);
            if (baseName.StartsWith("lib", StringComparison.Ordinal))
                baseName = baseName.Substring(3);
            baseName = Path.GetFileNameWithoutExtension(baseName);
            if (baseName.EndsWith(".static", StringComparison.OrdinalIgnoreCase))
                baseName = baseName.Substring(0, baseName.Length - ".static".Length);
            if (!baseName.StartsWith("jalium.native.", StringComparison.OrdinalIgnoreCase))
                continue;
            guarded.Add(new KeyValuePair<string, string>(Path.GetFullPath(fullPath), baseName));
        }

        if (guarded.Count == 0)
        {
            if (WarnIfEmpty)
                LogAlways(true, "JaliumStaleNativeGuard: this pack contains NO Jalium native binaries — the produced package will have no native payload. Build at least one stamped RID payload first if that is not intended.");
            else
                Log.LogMessage(MessageImportance.Low, "JaliumStaleNativeGuard: no Jalium-built native binaries in this pack; nothing to validate.");
            return true;
        }

        var head = GitFirstLine("rev-parse HEAD");
        if (string.IsNullOrEmpty(head))
        {
            head = null;
            findings.Add("[JALSTALE07] git is unavailable or '" + RepoRoot + "' is not a usable git checkout, so native binary freshness cannot be verified.");
        }

        // [JALSTALE01] Uncommitted native changes make "last commit touching
        // the sources" meaningless as a freshness baseline. Untracked files are
        // included explicitly so a user-level status.showUntrackedFiles=no
        // cannot silence new not-yet-added sources.
        if (head != null)
        {
            var dirty = GitLines("status --porcelain --untracked-files=normal -- src/native");
            if (dirty == null)
            {
                findings.Add("[JALSTALE07] 'git status --porcelain -- src/native' failed; cannot verify the native tree is clean.");
            }
            else if (dirty.Count > 0)
            {
                var sample = string.Join(", ", dirty.Take(6).Select(l => l.Trim()));
                var more = dirty.Count > 6 ? string.Format(" (+{0} more)", dirty.Count - 6) : string.Empty;
                findings.Add(string.Format(
                    "[JALSTALE01] src/native has {0} uncommitted change(s) ({1}{2}); the packed binaries cannot be matched to any commit. Commit or revert the native sources, rebuild, then pack.",
                    dirty.Count, sample, more));
            }
        }

        // [JALSTALE02] Every packed binary must be at least as new as the last
        // commit that touched the sources compiled into it.
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in ModuleMap ?? new ITaskItem[0])
            map[entry.ItemSpec] = entry.GetMetadata("PathSpec");
        var lastCommit = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var binary in guarded)
        {
            string spec;
            if (!map.TryGetValue(binary.Value, out spec) || string.IsNullOrWhiteSpace(spec))
            {
                if (!lastCommit.ContainsKey(binary.Value))
                {
                    lastCommit[binary.Value] = new string[0];
                    findings.Add(string.Format(
                        "[JALSTALE06] '{0}' has no JaliumNativeModuleMap entry; add its source directories to eng/msbuild/JaliumStaleNativeGuard.targets so its freshness can be verified.",
                        binary.Value));
                }
                continue;
            }
            if (head == null)
                continue;
            string[] commit;
            if (!lastCommit.TryGetValue(binary.Value, out commit))
            {
                var specs = spec.Split(';')
                    .Concat((SharedPathSpec ?? string.Empty).Split(';'))
                    .Concat((ExtraPathSpec ?? string.Empty).Split(';'))
                    .Select(s => s.Trim())
                    .Where(s => s.Length > 0)
                    .Select(s => "\"" + s + "\"")
                    .ToList();
                if (!string.IsNullOrWhiteSpace(TestsExcludePathSpec))
                    specs.Add("\"" + TestsExcludePathSpec.Trim() + "\"");
                var line = GitFirstLine("log -1 --no-show-signature --format=%H,%ct -- " + string.Join(" ", specs));
                string[] parsed = null;
                if (!string.IsNullOrEmpty(line) && line.Contains(","))
                {
                    var parts = line.Split(',');
                    long probe;
                    if (parts.Length == 2 && parts[0].Length >= 7 && long.TryParse(parts[1], out probe))
                        parsed = parts;
                }
                if (parsed == null)
                {
                    findings.Add(string.Format(
                        "[JALSTALE06] could not determine the last commit touching the sources of '{0}' (pathspec: {1}); check its JaliumNativeModuleMap entry.",
                        binary.Value, spec));
                    lastCommit[binary.Value] = new string[0];
                }
                else
                {
                    lastCommit[binary.Value] = parsed;
                }
                commit = lastCommit[binary.Value];
            }
            if (commit.Length != 2)
                continue;
            long commitTime;
            if (!long.TryParse(commit[1], out commitTime))
                continue;
            if (!File.Exists(binary.Key))
            {
                findings.Add(string.Format("[JALSTALE02] packed native binary '{0}' does not exist on disk.", binary.Key));
                continue;
            }
            var mtimeUtc = File.GetLastWriteTimeUtc(binary.Key);
            var mtime = (long)(mtimeUtc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            if (mtime < commitTime)
            {
                findings.Add(string.Format(
                    "[JALSTALE02] STALE: '{0}' was built {1:yyyy-MM-dd HH:mm:ss}Z, but its sources last changed in commit {2} at {3:yyyy-MM-dd HH:mm:ss}Z. Rebuild the native payload before packing.",
                    binary.Key, mtimeUtc, Short(commit[0]), DateTimeOffset.FromUnixTimeSeconds(commitTime).UtcDateTime));
            }
        }

        // [JALSTALE03..05, 08] Each payload directory must carry a completion
        // stamp written at a commit whose src/native content matches HEAD,
        // produced from a clean native tree, for the RID/configuration the
        // directory actually represents.
        foreach (var dir in guarded.Select(g => Path.GetDirectoryName(g.Key)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var stampPath = Path.Combine(dir, stampName);
            if (!File.Exists(stampPath))
            {
                findings.Add(string.Format(
                    "[JALSTALE03] payload directory '{0}' has no {1} stamp; rebuild it with the current build scripts (they record build provenance).",
                    dir, stampName));
                continue;
            }
            string stampHead = null, stampDirty = null, stampRid = null, stampAbi = null, stampConfig = null;
            foreach (var raw in File.ReadAllLines(stampPath))
            {
                var line = raw.Trim();
                if (line.StartsWith("head=", StringComparison.Ordinal))
                    stampHead = line.Substring(5).Trim();
                else if (line.StartsWith("dirty=", StringComparison.Ordinal))
                    stampDirty = line.Substring(6).Trim();
                else if (line.StartsWith("rid=", StringComparison.Ordinal))
                    stampRid = line.Substring(4).Trim();
                else if (line.StartsWith("abi=", StringComparison.Ordinal))
                    stampAbi = line.Substring(4).Trim();
                else if (line.StartsWith("configuration=", StringComparison.Ordinal))
                    stampConfig = line.Substring(14).Trim();
            }
            if (string.IsNullOrEmpty(stampHead))
            {
                findings.Add(string.Format(
                    "[JALSTALE03] stamp '{0}' carries no provenance (no head= line); it predates the stale-native guard. Rebuild the payload once to refresh it.",
                    stampPath));
                continue;
            }
            if (stampHead == "unknown")
            {
                findings.Add(string.Format(
                    "[JALSTALE03] stamp '{0}' recorded head=unknown (git was unavailable while the payload was built), so its provenance cannot be verified.",
                    stampPath));
            }
            else if (head != null && !string.Equals(stampHead, head, StringComparison.OrdinalIgnoreCase))
            {
                if (GitExitCode("cat-file -e " + stampHead + "^{commit}") != 0)
                {
                    findings.Add(string.Format(
                        "[JALSTALE04] stamp '{0}' was written at commit {1}, which does not exist in this repository (rewritten history, or a payload copied from another checkout?). Rebuild the payload.",
                        stampPath, Short(stampHead)));
                }
                else
                {
                    // Content comparison, not commit-graph distance: a rebase or
                    // squash-merge that leaves src/native byte-identical stays
                    // fresh; test-only changes are excluded like in JALSTALE02.
                    var diffSpecs = new List<string> { "\"src/native\"" };
                    diffSpecs.AddRange((ExtraPathSpec ?? string.Empty).Split(';')
                        .Select(s => s.Trim()).Where(s => s.Length > 0).Select(s => "\"" + s + "\""));
                    if (!string.IsNullOrWhiteSpace(TestsExcludePathSpec))
                        diffSpecs.Add("\"" + TestsExcludePathSpec.Trim() + "\"");
                    var pathspec = string.Join(" ", diffSpecs);
                    var rc = GitExitCode("diff --quiet " + stampHead + " " + head + " -- " + pathspec);
                    if (rc == 1)
                    {
                        var changed = GitLines("diff --name-only " + stampHead + " " + head + " -- " + pathspec) ?? new List<string>();
                        var sample = string.Join(", ", changed.Take(4));
                        var more = changed.Count > 4 ? string.Format(" (+{0} more)", changed.Count - 4) : string.Empty;
                        findings.Add(string.Format(
                            "[JALSTALE04] payload '{0}' was built at commit {1}, but native build inputs differ from HEAD in {2} file(s): {3}{4}. Rebuild the native payload.",
                            dir, Short(stampHead), changed.Count, sample, more));
                    }
                    else if (rc != 0)
                    {
                        findings.Add(string.Format(
                            "[JALSTALE07] could not compare stamp commit {0} with HEAD for '{1}' (git diff exited {2}).",
                            Short(stampHead), stampPath, rc));
                    }
                }
            }
            if (stampDirty == "1")
            {
                findings.Add(string.Format(
                    "[JALSTALE05] stamp '{0}' records dirty=1 — the payload was built from uncommitted native sources and cannot be traced to a commit. Commit, rebuild, then pack.",
                    stampPath));
            }
            else if (stampDirty != "0")
            {
                findings.Add(string.Format(
                    "[JALSTALE05] stamp '{0}' records dirty={1}; the state of the native tree at build time is unknown.",
                    stampPath, stampDirty ?? "<missing>"));
            }

            // [JALSTALE08] A payload wholesale-copied into another RID or
            // configuration directory carries a stamp whose rid/abi/
            // configuration no longer matches the directory it sits in.
            var segments = new HashSet<string>(
                dir.Replace('\\', '/').Split('/').Where(s => s.Length > 0),
                StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrEmpty(stampRid) && stampRid != "unknown")
            {
                var ridMatches = segments.Contains(stampRid)
                    || (!string.IsNullOrEmpty(stampAbi) && segments.Contains(stampAbi));
                if (!ridMatches)
                {
                    findings.Add(string.Format(
                        "[JALSTALE08] stamp '{0}' records rid={1}{2}, which does not match the payload directory '{3}' — was the payload copied from another RID? Rebuild it in place.",
                        stampPath, stampRid,
                        string.IsNullOrEmpty(stampAbi) ? string.Empty : "/abi=" + stampAbi, dir));
                }
            }
            // Only meaningful when the directory layout has a configuration
            // segment at all (bin/native/<rid>/<Config>, bin/native-static/
            // <Config>); the Android libs/<abi> layout has none, and its
            // stamps legitimately record configuration=Release.
            var knownConfigs = new[] { "Debug", "Release", "MinSizeRel", "RelWithDebInfo" };
            if (!string.IsNullOrEmpty(stampConfig) && stampConfig != "unknown"
                && knownConfigs.Any(segments.Contains) && !segments.Contains(stampConfig))
            {
                findings.Add(string.Format(
                    "[JALSTALE08] stamp '{0}' records configuration={1}, which does not match the payload directory '{2}' — was the payload copied from another configuration? Rebuild it in place.",
                    stampPath, stampConfig, dir));
            }
        }

        if (findings.Count == 0)
        {
            Log.LogMessage(MessageImportance.High, string.Format(
                "JaliumStaleNativeGuard: {0} native binaries verified fresh against git history (HEAD {1}).",
                guarded.Count, Short(head)));
            return true;
        }

        const string remediation =
            "Rebuild the native payloads (Windows/Linux CMake: cmake --build <builddir> --target jalium.native.package.complete [--config <Config>]; " +
            "Android: src/native/build-android.sh all; NativeAOT static libs: samples/build-native-static.ps1), " +
            "or pass -p:JaliumAllowStaleNative=true to pack anyway at your own risk.";
        foreach (var finding in findings)
            LogAlways(AllowStale, "JaliumStaleNativeGuard: " + finding);
        if (AllowStale)
            LogAlways(true, string.Format("JaliumStaleNativeGuard: packing anyway despite {0} finding(s) because JaliumAllowStaleNative=true. {1}", findings.Count, remediation));
        else
            LogAlways(false, string.Format("JaliumStaleNativeGuard: {0} finding(s) blocked this pack. {1}", findings.Count, remediation));
        return AllowStale;
    }

    private void LogAlways(bool asWarning, string message)
    {
        // "{0}" keeps literal braces in git output from being treated as
        // format placeholders.
        if (asWarning)
            Log.LogWarning(null, "JALSTALE", null, null, 0, 0, 0, 0, "{0}", message);
        else
            Log.LogError(null, "JALSTALE", null, null, 0, 0, 0, 0, "{0}", message);
    }

    private static string Short(string sha)
    {
        return string.IsNullOrEmpty(sha) ? "<none>" : sha.Substring(0, Math.Min(12, sha.Length));
    }

    private string GitFirstLine(string arguments)
    {
        var lines = GitLines(arguments);
        if (lines == null)
            return null;
        return lines.Count > 0 ? lines[0].Trim() : string.Empty;
    }

    private List<string> GitLines(string arguments)
    {
        string output;
        if (RunGit(arguments, out output) != 0 || output == null)
            return null;
        return output
            .Split('\n')
            .Select(l => l.TrimEnd('\r'))
            .Where(l => l.Length > 0)
            .ToList();
    }

    private int GitExitCode(string arguments)
    {
        string ignored;
        return RunGit(arguments, out ignored);
    }

    // Returns the git exit code, or -1 when git could not be started at all.
    private int RunGit(string arguments, out string stdout)
    {
        stdout = null;
        if (_gitUnavailable)
            return -1;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = RepoRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using (var process = Process.Start(psi))
            {
                var stderr = process.StandardError.ReadToEndAsync();
                var text = process.StandardOutput.ReadToEnd();
                if (!process.WaitForExit(60000))
                {
                    try { process.Kill(); } catch { }
                    Log.LogMessage(MessageImportance.Low, "JaliumStaleNativeGuard: 'git " + arguments + "' timed out.");
                    return -1;
                }
                if (process.ExitCode != 0)
                    Log.LogMessage(MessageImportance.Low, "JaliumStaleNativeGuard: 'git " + arguments + "' exited " + process.ExitCode + ": " + stderr.Result.Trim());
                else
                    stdout = text;
                return process.ExitCode;
            }
        }
        catch (Exception ex)
        {
            _gitUnavailable = true;
            Log.LogMessage(MessageImportance.Low, "JaliumStaleNativeGuard: git could not be started: " + ex.Message);
            return -1;
        }
    }
}

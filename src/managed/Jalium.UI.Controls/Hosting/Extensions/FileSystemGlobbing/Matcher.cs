using System.Text.RegularExpressions;

namespace Jalium.Extensions.FileSystemGlobbing;

/// <summary>
/// Glob-pattern matcher. Supports MS-compatible patterns:
/// <list type="bullet">
///   <item><c>*</c> — any chars except <c>/</c></item>
///   <item><c>?</c> — any single char except <c>/</c></item>
///   <item><c>**</c> — any chars including <c>/</c> (zero-or-more path segments)</item>
///   <item><c>**/foo.cs</c> — match at any depth</item>
///   <item><c>foo/**</c> — everything under <c>foo</c></item>
/// </list>
/// </summary>
public class Matcher
{
    private readonly List<Regex> _includes = new();
    private readonly List<Regex> _excludes = new();
    private readonly StringComparison _comparison;

    public Matcher() : this(StringComparison.OrdinalIgnoreCase) { }
    public Matcher(StringComparison comparisonType) { _comparison = comparisonType; }

    public Matcher AddInclude(string pattern)
    {
        ArgumentException.ThrowIfNullOrEmpty(pattern);
        _includes.Add(BuildRegex(pattern, _comparison));
        return this;
    }

    public Matcher AddExclude(string pattern)
    {
        ArgumentException.ThrowIfNullOrEmpty(pattern);
        _excludes.Add(BuildRegex(pattern, _comparison));
        return this;
    }

    /// <summary>Match <paramref name="file"/> against include/exclude patterns. Path uses <c>/</c>.</summary>
    public PatternMatchingResult Match(string file)
    {
        ArgumentNullException.ThrowIfNull(file);
        var normalized = file.Replace('\\', '/');

        for (int i = 0; i < _excludes.Count; i++)
            if (_excludes[i].IsMatch(normalized)) return new PatternMatchingResult(Array.Empty<FilePatternMatch>(), false);

        for (int i = 0; i < _includes.Count; i++)
        {
            if (_includes[i].IsMatch(normalized))
            {
                return new PatternMatchingResult(
                    new[] { new FilePatternMatch(normalized, normalized) },
                    hasMatches: true);
            }
        }
        return new PatternMatchingResult(Array.Empty<FilePatternMatch>(), false);
    }

    /// <summary>Run patterns against an in-memory file list. Returns the matched subset.</summary>
    public PatternMatchingResult Match(string rootDir, IEnumerable<string> files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var rootNorm = (rootDir ?? string.Empty).Replace('\\', '/').TrimEnd('/');
        var matches = new List<FilePatternMatch>();
        foreach (var f in files)
        {
            var rel = f.Replace('\\', '/');
            if (!string.IsNullOrEmpty(rootNorm) && rel.StartsWith(rootNorm + "/", StringComparison.OrdinalIgnoreCase))
                rel = rel.Substring(rootNorm.Length + 1);
            var r = Match(rel);
            if (r.HasMatches) matches.Add(r.Files.First());
        }
        return new PatternMatchingResult(matches, matches.Count > 0);
    }

    /// <summary>Enumerate the file system under <paramref name="rootDir"/> and apply patterns.</summary>
    public PatternMatchingResult Execute(DirectoryInfoBase rootDir)
    {
        ArgumentNullException.ThrowIfNull(rootDir);
        var matches = new List<FilePatternMatch>();
        var rootPath = rootDir.FullName.Replace('\\', '/');
        foreach (var f in EnumerateFilesRecursive(rootDir))
        {
            var full = f.FullName.Replace('\\', '/');
            var rel = full.StartsWith(rootPath + "/", StringComparison.OrdinalIgnoreCase) ? full.Substring(rootPath.Length + 1) : full;
            var r = Match(rel);
            if (r.HasMatches) matches.Add(new FilePatternMatch(rel, rel));
        }
        return new PatternMatchingResult(matches, matches.Count > 0);
    }

    private static IEnumerable<FileInfoBase> EnumerateFilesRecursive(DirectoryInfoBase dir)
    {
        foreach (var entry in dir.EnumerateFileSystemInfos())
        {
            if (entry is FileInfoBase f) yield return f;
            else if (entry is DirectoryInfoBase d)
                foreach (var sub in EnumerateFilesRecursive(d)) yield return sub;
        }
    }

    private static Regex BuildRegex(string pattern, StringComparison comp)
    {
        var p = pattern.Replace('\\', '/');
        var sb = new System.Text.StringBuilder();
        sb.Append('^');
        int i = 0;
        while (i < p.Length)
        {
            var c = p[i];
            if (c == '*' && i + 1 < p.Length && p[i + 1] == '*')
            {
                // ** — any path including slashes; allow leading/trailing slash absorption
                if (i + 2 < p.Length && p[i + 2] == '/')
                {
                    sb.Append("(?:.*/)?");
                    i += 3;
                }
                else
                {
                    sb.Append(".*");
                    i += 2;
                }
            }
            else if (c == '*')
            {
                sb.Append("[^/]*");
                i++;
            }
            else if (c == '?')
            {
                sb.Append("[^/]");
                i++;
            }
            else if (c == '.' || c == '+' || c == '(' || c == ')' || c == '|' || c == '$' || c == '^' || c == '{' || c == '}' || c == '[' || c == ']')
            {
                sb.Append('\\').Append(c);
                i++;
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }
        sb.Append('$');
        var options = RegexOptions.CultureInvariant;
        if (comp == StringComparison.OrdinalIgnoreCase || comp == StringComparison.CurrentCultureIgnoreCase || comp == StringComparison.InvariantCultureIgnoreCase)
            options |= RegexOptions.IgnoreCase;
        return new Regex(sb.ToString(), options);
    }
}

public sealed class PatternMatchingResult
{
    public PatternMatchingResult(IEnumerable<FilePatternMatch> files, bool hasMatches)
    {
        Files = files;
        HasMatches = hasMatches;
    }
    public bool HasMatches { get; }
    public IEnumerable<FilePatternMatch> Files { get; }
}

public readonly record struct FilePatternMatch(string Path, string Stem);

// ─── Filesystem abstractions so the matcher works against either real files or test fakes ───
public abstract class FileSystemInfoBase
{
    public abstract string Name { get; }
    public abstract string FullName { get; }
    public abstract DirectoryInfoBase? ParentDirectory { get; }
}

public abstract class FileInfoBase : FileSystemInfoBase { }

public abstract class DirectoryInfoBase : FileSystemInfoBase
{
    public abstract IEnumerable<FileSystemInfoBase> EnumerateFileSystemInfos();
    public abstract DirectoryInfoBase? GetDirectory(string path);
    public abstract FileInfoBase? GetFile(string path);
}

public sealed class DirectoryInfoWrapper : DirectoryInfoBase
{
    private readonly DirectoryInfo _info;
    private readonly DirectoryInfoWrapper? _parent;
    public DirectoryInfoWrapper(DirectoryInfo info) : this(info, null) { }
    private DirectoryInfoWrapper(DirectoryInfo info, DirectoryInfoWrapper? parent) { _info = info; _parent = parent; }
    public override string Name => _info.Name;
    public override string FullName => _info.FullName;
    public override DirectoryInfoBase? ParentDirectory => _info.Parent != null ? new DirectoryInfoWrapper(_info.Parent) : null;
    public override IEnumerable<FileSystemInfoBase> EnumerateFileSystemInfos()
    {
        foreach (var e in _info.EnumerateFileSystemInfos())
        {
            if (e is DirectoryInfo d) yield return new DirectoryInfoWrapper(d, this);
            else if (e is FileInfo f) yield return new FileInfoWrapper(f, this);
        }
    }
    public override DirectoryInfoBase? GetDirectory(string path)
    {
        var sub = new DirectoryInfo(Path.Combine(_info.FullName, path));
        return sub.Exists ? new DirectoryInfoWrapper(sub, this) : null;
    }
    public override FileInfoBase? GetFile(string path)
    {
        var sub = new FileInfo(Path.Combine(_info.FullName, path));
        return sub.Exists ? new FileInfoWrapper(sub, this) : null;
    }
}

public sealed class FileInfoWrapper : FileInfoBase
{
    private readonly FileInfo _info;
    private readonly DirectoryInfoWrapper? _parent;
    public FileInfoWrapper(FileInfo info) : this(info, null) { }
    internal FileInfoWrapper(FileInfo info, DirectoryInfoWrapper? parent) { _info = info; _parent = parent; }
    public override string Name => _info.Name;
    public override string FullName => _info.FullName;
    public override DirectoryInfoBase? ParentDirectory => _parent;
}

public static class MatcherExtensions
{
    public static IEnumerable<string> GetResultsInFullPath(this Matcher matcher, string directoryPath)
    {
        ArgumentNullException.ThrowIfNull(matcher);
        ArgumentException.ThrowIfNullOrEmpty(directoryPath);
        var dir = new DirectoryInfoWrapper(new DirectoryInfo(directoryPath));
        var result = matcher.Execute(dir);
        foreach (var m in result.Files)
            yield return Path.GetFullPath(Path.Combine(directoryPath, m.Path));
    }
}

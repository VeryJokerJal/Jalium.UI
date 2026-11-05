using System.Collections;
using System.Globalization;
using System.Runtime.CompilerServices;
using Jalium.UI.Media;

namespace Jalium.UI.Controls.DevTools;

/// <summary>The kind of row in a resource resolution chain.</summary>
public enum DevToolsResourceRowKind { Scope, Dictionary, Resource, Note }

/// <summary>The semantic type used by a resource value preview.</summary>
public enum DevToolsResourceValueKind { Object, Null, Color, Brush, Style, Template, Thickness, Number, Boolean, String, Enum, Reference }

internal readonly record struct DevToolsResourceRowIdentity(object Owner, string Path, object? Key, DevToolsResourceRowKind Kind);
internal sealed record DevToolsResourceRowData(
    DevToolsResourceRowIdentity Identity, DevToolsResourceRowKind Kind, string KeyText,
    string ValueText, string DetailText, DevToolsResourceValueKind ValueKind,
    string TypeLabel, Brush? Swatch, bool IsWinner, bool IsInactive, int Depth);

/// <summary>Captures presentation data without constructing UI or allocating empty resource dictionaries.</summary>
internal sealed class DevToolsResourceSnapshot
{
    private static readonly ConditionalWeakTable<object, object> OwnerIdentities = new();
    private sealed record Scope(object Owner, string Path, string Name, ResourceDictionary? Dictionary, bool Application = false);
    private sealed record Declaration(Scope Scope, ResourceDictionary Dictionary, string Path, object Key, bool Active, int Depth);
    private readonly List<DevToolsResourceRowData> _rows = new();
    private readonly List<Declaration> _declarations = new();
    private readonly Dictionary<(Scope, string), List<Declaration>> _declarationsByPath = new();
    private readonly List<Scope> _scopes = new();
    private readonly Dictionary<object, ResourceDictionary?> _winners = new();
    private readonly HashSet<object> _shownWinners = new();
    private readonly string _filter;
    private readonly bool _winnersOnly;
    private const int MaximumDepth = 128;
    internal IReadOnlyList<DevToolsResourceRowData> Rows => _rows;
    internal int ScopeCount => _scopes.Count;
    internal int DeclarationCount => _declarations.Count;
    internal int WinnerCount => _winners.Count(pair => pair.Value != null);

    private DevToolsResourceSnapshot(string filter, bool winnersOnly) { _filter = filter; _winnersOnly = winnersOnly; }

    internal static DevToolsResourceSnapshot Capture(FrameworkElement target, Application? application, string? filter, bool winnersOnly)
    {
        var snapshot = new DevToolsResourceSnapshot(filter?.Trim() ?? string.Empty, winnersOnly);
        var ancestors = new HashSet<FrameworkElement>(ReferenceEqualityComparer.Instance);
        for (FrameworkElement? current = target; current != null && ancestors.Add(current) && ancestors.Count <= 2048;)
        {
            var name = current.GetType().Name + (string.IsNullOrEmpty(current.Name) ? string.Empty : " #" + current.Name);
            snapshot._scopes.Add(new Scope(current, "local", name, current.ResourcesOrNull));
            if (current.Style?.ResourcesOrNull is { } style)
                snapshot._scopes.Add(new Scope(current, "style", name + " · style", style));
            var redirect = ResourceLookup.AncestorRedirectLookup?.Invoke(current);
            current = redirect != null && !ReferenceEquals(redirect, current) ? redirect : current.FrameworkParent;
        }
        if (application != null) snapshot._scopes.Add(new Scope(application, "application", "Application", application.ResourcesOrNull, true));

        using var theme = ResourceLookup.EnterThemeLookup(target);
        foreach (var scope in snapshot._scopes)
            if (scope.Dictionary != null) snapshot.Collect(scope, scope.Dictionary, scope.Path, true, 0, new HashSet<ResourceDictionary>(ReferenceEqualityComparer.Instance));

        foreach (var key in snapshot._declarations.Select(entry => entry.Key).Distinct())
        {
            ResourceDictionary? winner = null;
            foreach (var scope in snapshot._scopes)
            {
                try
                {
                    if (scope.Application && ReferenceEquals(application, Application.Current) && ResourceLookup.ApplicationResourceLookupWithSource is { } lookup)
                    {
                        winner = lookup(key).Dictionary;
                        break;
                    }
                    if (scope.Dictionary?.TryGetValue(key, out _, out winner) == true) break;
                }
                catch
                {
                    // A deferred resource factory can fail while being inspected.
                    // Keep its declaration visible so the preview can report it.
                    winner = snapshot._declarations.FirstOrDefault(entry => ReferenceEquals(entry.Scope, scope) && entry.Active && Equals(entry.Key, key))?.Dictionary;
                    break;
                }
            }
            snapshot._winners[key] = winner;
        }

        for (int index = 0; index < snapshot._scopes.Count; index++)
        {
            var scope = snapshot._scopes[index];
            int start = snapshot._rows.Count;
            snapshot.Header(scope, scope.Path, DevToolsResourceRowKind.Scope, scope.Name, $"{index:00} · {scope.Dictionary?.Count ?? 0} local", false, 0);
            if (scope.Dictionary != null) snapshot.AppendDictionary(scope, scope.Dictionary, scope.Path, true, 0, new HashSet<ResourceDictionary>(ReferenceEqualityComparer.Instance));
            if ((snapshot._filter.Length > 0 || winnersOnly) && snapshot._rows.Count == start + 1)
                snapshot._rows.RemoveAt(start);
        }
        return snapshot;
    }

    private void Collect(Scope scope, ResourceDictionary dictionary, string path, bool active, int depth, HashSet<ResourceDictionary> visited)
    {
        if (depth > MaximumDepth || !visited.Add(dictionary)) return;
        var declarations = new List<Declaration>();
        _declarationsByPath[(scope, path)] = declarations;
        foreach (var key in dictionary.Keys.Cast<object>().ToArray())
        {
            var entry = new Declaration(scope, dictionary, path, key, active, depth);
            _declarations.Add(entry); declarations.Add(entry);
        }
        foreach (var child in Children(dictionary, path, active)) Collect(scope, child.Dictionary, child.Path, child.Active, depth + 1, visited);
        visited.Remove(dictionary);
    }

    private static IEnumerable<(ResourceDictionary Dictionary, string Path, string Name, bool Active)> Children(ResourceDictionary dictionary, string path, bool active)
    {
        var themes = dictionary.ThemeDictionariesOrNull?.ToArray() ?? [];
        // Follow the same priority as lookup, including reverse merge order.
        for (int index = 0; index < themes.Length; index++)
            if (Equals(themes[index].Key, ResourceDictionary.EffectiveLookupThemeKey))
                yield return (themes[index].Value, path + "/theme:" + index, "Theme · " + SafeText(themes[index].Key), active);
        var merged = dictionary.MergedDictionaries.ToArray();
        for (int index = merged.Length - 1; index >= 0; index--)
            yield return (merged[index], path + "/merged:" + index, "Merged " + index, active);
        for (int index = 0; index < themes.Length; index++)
            if (!Equals(themes[index].Key, ResourceDictionary.EffectiveLookupThemeKey))
                yield return (themes[index].Value, path + "/theme:" + index, "Theme · " + SafeText(themes[index].Key), false);
    }

    private void AppendDictionary(Scope scope, ResourceDictionary dictionary, string path, bool active, int depth, HashSet<ResourceDictionary> visited)
    {
        if (depth > MaximumDepth || !visited.Add(dictionary))
        {
            _rows.Add(new(Identity(scope, path, null, DevToolsResourceRowKind.Note), DevToolsResourceRowKind.Note,
                depth > MaximumDepth ? "Dictionary depth limit" : "Dictionary cycle", "", "", DevToolsResourceValueKind.Object, "", null, false, !active, depth));
            return;
        }
        foreach (var entry in _declarationsByPath.GetValueOrDefault((scope, path)) ?? [])
        {
            string key = SafeText(entry.Key);
            bool winner = active && _winners.TryGetValue(entry.Key, out var source) && ReferenceEquals(source, dictionary) && _shownWinners.Add(entry.Key);
            if ((_filter.Length > 0 && key.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) < 0) || (_winnersOnly && !winner)) continue;
            object? value; string? error = null;
            try { value = dictionary[entry.Key]; }
            catch (Exception exception) { value = null; error = exception.GetType().Name; }
            var preview = Preview(value);
            _rows.Add(new(Identity(scope, path, entry.Key, DevToolsResourceRowKind.Resource), DevToolsResourceRowKind.Resource,
                key, error == null ? preview.Text : "<" + error + ">", error ?? preview.Detail, preview.Kind, preview.Label,
                preview.Swatch, winner, !active, depth));
        }
        foreach (var child in Children(dictionary, path, active))
        {
            int start = _rows.Count;
            Header(scope, child.Path, DevToolsResourceRowKind.Dictionary, child.Name, child.Dictionary.Source?.ToString() ?? "Inline", !child.Active, depth + 1);
            AppendDictionary(scope, child.Dictionary, child.Path, child.Active, depth + 1, visited);
            if ((_filter.Length > 0 || _winnersOnly) && _rows.Count == start + 1) _rows.RemoveAt(start);
        }
        visited.Remove(dictionary);
    }

    private void Header(Scope scope, string path, DevToolsResourceRowKind kind, string name, string detail, bool inactive, int depth) =>
        _rows.Add(new(Identity(scope, path, null, kind), kind, name, "", detail, DevToolsResourceValueKind.Object, "", null, false, inactive, depth));

    private static DevToolsResourceRowIdentity Identity(Scope scope, string path, object? key, DevToolsResourceRowKind kind) =>
        new(OwnerIdentities.GetValue(scope.Owner, static _ => new object()), path, key, kind);

    internal static (string Text, string Detail, DevToolsResourceValueKind Kind, string Label, Brush? Swatch) Preview(object? value) => value switch
    {
        null => ("<null>", "", DevToolsResourceValueKind.Null, "∅", null),
        SolidColorBrush brush => (Hex(brush.Color), "SolidColorBrush", DevToolsResourceValueKind.Color, "", new SolidColorBrush(brush.Color)),
        Color color => (Hex(color), "Color", DevToolsResourceValueKind.Color, "", new SolidColorBrush(color)),
        Brush brush => (SafeText(brush), brush.GetType().Name, DevToolsResourceValueKind.Brush, "B", null),
        Style style => ("Style · " + (style.TargetType?.Name ?? "?"), $"{style.Setters.Count} setters · {style.Triggers.Count} triggers" + (style.BasedOn == null ? "" : " · based on " + style.BasedOn.TargetType?.Name), DevToolsResourceValueKind.Style, "S", null),
        ControlTemplate template => ("ControlTemplate · " + (template.TargetType?.Name ?? "?"), "", DevToolsResourceValueKind.Template, "T", null),
        DataTemplate template => ("DataTemplate · " + SafeText(template.DataType), "", DevToolsResourceValueKind.Template, "D", null),
        IDynamicResourceReference reference => ("DynamicResource · " + SafeText(reference.ResourceKey), "", DevToolsResourceValueKind.Reference, "R", null),
        Thickness thickness => (SafeText(thickness), "Thickness", DevToolsResourceValueKind.Thickness, "T", null),
        bool boolean => (boolean ? "true" : "false", "Boolean", DevToolsResourceValueKind.Boolean, "✓", null),
        string text => ("\"" + text.Replace("\r", "\\r").Replace("\n", "\\n") + "\"", "String", DevToolsResourceValueKind.String, "Aa", null),
        Enum enumeration => (SafeText(enumeration), enumeration.GetType().Name, DevToolsResourceValueKind.Enum, "E", null),
        byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal => (((IFormattable)value).ToString(null, CultureInfo.InvariantCulture), value.GetType().Name, DevToolsResourceValueKind.Number, "#", null),
        _ => (SafeText(value), value.GetType().Name, DevToolsResourceValueKind.Object, "O", null)
    };
    private static string Hex(Color color) => $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
    private static string SafeText(object? value) { try { return value?.ToString() ?? "<null>"; } catch { return "<" + value?.GetType().Name + ">"; } }
}

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Dynamic;
using System.Globalization;
using System.Reflection;
using Jalium.UI.Threading;

namespace Jalium.UI.Interop;

/// <summary>Provides information about the legacy browser host, when one exists.</summary>
public static class BrowserInteropHelper
{
    private static object? s_clientSite;
    private static object? s_hostScript;
    private static Uri? s_source;
    private static bool s_isBrowserHosted;

    /// <summary>Gets the browser's COM client site, or <see langword="null"/> outside a browser host.</summary>
    public static object? ClientSite => s_clientSite;

    /// <summary>Gets the browser script object, or <see langword="null"/> outside a browser host.</summary>
    public static object? HostScript => s_hostScript;

    /// <summary>Gets whether the application is running in a legacy browser host.</summary>
    public static bool IsBrowserHosted => s_isBrowserHosted;

    /// <summary>Gets the browser-provided deployment source.</summary>
    public static Uri? Source => s_source;

    /// <summary>Updates browser-host state for an embedding implementation.</summary>
    internal static void SetHostState(object? clientSite, object? hostScript, Uri? source)
    {
        s_clientSite = clientSite;
        s_hostScript = hostScript is null or DynamicScriptObject
            ? hostScript
            : new DynamicScriptObject(hostScript);
        s_source = source;
        s_isBrowserHosted = clientSite is not null || hostScript is not null;
    }

    /// <summary>Clears references supplied by a browser embedding implementation.</summary>
    internal static void ClearHostState() => SetHostState(null, null, null);
}

/// <summary>
/// Provides dynamic access to an object supplied by an embedding host. Managed
/// dictionaries, lists, delegates and reflection-visible members are supported
/// on every platform; COM automation objects use the same reflection dispatch on
/// Windows.
/// </summary>
[UnconditionalSuppressMessage(
    "Trimming",
    "IL2026",
    Justification = "Dynamic host objects are explicitly reflection-driven and are supplied by the embedding host at runtime.")]
[UnconditionalSuppressMessage(
    "Trimming",
    "IL2075",
    Justification = "Dynamic host objects cannot carry static member annotations; failed lookups are reported through DynamicObject's false return contract.")]
[UnconditionalSuppressMessage(
    "AOT",
    "IL3050",
    Justification = "Dynamic host automation deliberately uses runtime reflection and is not available to statically generated NativeAOT call sites.")]
public sealed class DynamicScriptObject : DynamicObject
{
    private const BindingFlags PublicInstance =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

    private readonly object _scriptObject;

    internal DynamicScriptObject(object scriptObject)
    {
        ArgumentNullException.ThrowIfNull(scriptObject);
        _scriptObject = scriptObject;
    }

    /// <inheritdoc />
    public override bool TryGetMember(GetMemberBinder binder, out object? result)
    {
        ArgumentNullException.ThrowIfNull(binder);
        if (_scriptObject is IDictionary<string, object?> genericDictionary &&
            genericDictionary.TryGetValue(binder.Name, out result))
        {
            result = WrapResult(result);
            return true;
        }

        if (_scriptObject is IDictionary dictionary && dictionary.Contains(binder.Name))
        {
            result = WrapResult(dictionary[binder.Name]);
            return true;
        }

        Type type = _scriptObject.GetType();
        PropertyInfo? property = type.GetProperty(binder.Name, PublicInstance);
        if (property?.CanRead == true)
        {
            result = WrapResult(property.GetValue(_scriptObject));
            return true;
        }

        FieldInfo? field = type.GetField(binder.Name, PublicInstance);
        if (field is not null)
        {
            result = WrapResult(field.GetValue(_scriptObject));
            return true;
        }

        try
        {
            result = WrapResult(type.InvokeMember(
                binder.Name,
                BindingFlags.GetProperty | PublicInstance,
                binder: null,
                target: _scriptObject,
                args: null,
                CultureInfo.InvariantCulture));
            return true;
        }
        catch (MissingMemberException)
        {
            result = null;
            return false;
        }
    }

    /// <inheritdoc />
    public override bool TrySetMember(SetMemberBinder binder, object? value)
    {
        ArgumentNullException.ThrowIfNull(binder);
        if (_scriptObject is IDictionary<string, object?> genericDictionary)
        {
            genericDictionary[binder.Name] = value;
            return true;
        }

        if (_scriptObject is IDictionary dictionary)
        {
            dictionary[binder.Name] = value;
            return true;
        }

        Type type = _scriptObject.GetType();
        PropertyInfo? property = type.GetProperty(binder.Name, PublicInstance);
        if (property?.CanWrite == true)
        {
            property.SetValue(_scriptObject, value);
            return true;
        }

        FieldInfo? field = type.GetField(binder.Name, PublicInstance);
        if (field is not null && !field.IsInitOnly)
        {
            field.SetValue(_scriptObject, value);
            return true;
        }

        try
        {
            type.InvokeMember(
                binder.Name,
                BindingFlags.SetProperty | PublicInstance,
                binder: null,
                target: _scriptObject,
                args: [value],
                CultureInfo.InvariantCulture);
            return true;
        }
        catch (MissingMemberException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public override bool TryGetIndex(GetIndexBinder binder, object[] indexes, out object? result)
    {
        ArgumentNullException.ThrowIfNull(binder);
        ArgumentNullException.ThrowIfNull(indexes);
        if (indexes.Length != 1)
        {
            result = null;
            return false;
        }

        object index = indexes[0];
        if (_scriptObject is IDictionary dictionary)
        {
            if (!dictionary.Contains(index))
            {
                result = null;
                return false;
            }

            result = WrapResult(dictionary[index]);
            return true;
        }

        if (_scriptObject is IList list && TryGetIndex(index, list.Count, out int listIndex))
        {
            result = WrapResult(list[listIndex]);
            return true;
        }

        PropertyInfo? indexer = FindIndexer(canWrite: false, indexes);
        if (indexer is null)
        {
            result = null;
            return false;
        }

        result = WrapResult(indexer.GetValue(_scriptObject, indexes));
        return true;
    }

    /// <inheritdoc />
    public override bool TrySetIndex(SetIndexBinder binder, object[] indexes, object? value)
    {
        ArgumentNullException.ThrowIfNull(binder);
        ArgumentNullException.ThrowIfNull(indexes);
        if (indexes.Length != 1)
        {
            return false;
        }

        object index = indexes[0];
        if (_scriptObject is IDictionary dictionary)
        {
            dictionary[index] = value;
            return true;
        }

        if (_scriptObject is IList list && TryGetIndex(index, list.Count, out int listIndex))
        {
            list[listIndex] = value;
            return true;
        }

        PropertyInfo? indexer = FindIndexer(canWrite: true, indexes);
        if (indexer is null)
        {
            return false;
        }

        indexer.SetValue(_scriptObject, value, indexes);
        return true;
    }

    /// <inheritdoc />
    public override bool TryInvokeMember(InvokeMemberBinder binder, object?[]? args, out object? result)
    {
        ArgumentNullException.ThrowIfNull(binder);
        args ??= [];
        try
        {
            result = WrapResult(_scriptObject.GetType().InvokeMember(
                binder.Name,
                BindingFlags.InvokeMethod | PublicInstance,
                binder: null,
                target: _scriptObject,
                args: args,
                CultureInfo.InvariantCulture));
            return true;
        }
        catch (MissingMethodException)
        {
            result = null;
            return false;
        }
    }

    /// <inheritdoc />
    public override bool TryInvoke(InvokeBinder binder, object?[]? args, out object? result)
    {
        ArgumentNullException.ThrowIfNull(binder);
        args ??= [];
        if (_scriptObject is Delegate callback)
        {
            result = WrapResult(callback.DynamicInvoke(args));
            return true;
        }

        try
        {
            result = WrapResult(_scriptObject.GetType().InvokeMember(
                string.Empty,
                BindingFlags.InvokeMethod | PublicInstance,
                binder: null,
                target: _scriptObject,
                args: args,
                CultureInfo.InvariantCulture));
            return true;
        }
        catch (MissingMethodException)
        {
            result = null;
            return false;
        }
    }

    /// <inheritdoc />
    public override string ToString() => _scriptObject.ToString() ?? string.Empty;

    private PropertyInfo? FindIndexer(bool canWrite, object[] indexes) =>
        _scriptObject.GetType()
            .GetDefaultMembers()
            .OfType<PropertyInfo>()
            .FirstOrDefault(property =>
                property.GetIndexParameters().Length == indexes.Length &&
                (canWrite ? property.CanWrite : property.CanRead));

    private static bool TryGetIndex(object value, int count, out int index)
    {
        try
        {
            index = Convert.ToInt32(value, CultureInfo.InvariantCulture);
            return index >= 0 && index < count;
        }
        catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException)
        {
            index = -1;
            return false;
        }
    }

    private static object? WrapResult(object? value)
    {
        if (value is null || value is string || value.GetType().IsValueType || value is DynamicScriptObject)
        {
            return value;
        }

        return OperatingSystem.IsWindows() && System.Runtime.InteropServices.Marshal.IsComObject(value)
            ? new DynamicScriptObject(value)
            : value;
    }
}

/// <summary>Describes the error page used by a browser-hosted deployment.</summary>
public interface IErrorPage
{
    Uri? DeploymentPath { get; set; }
    bool ErrorFlag { get; set; }
    string? ErrorText { get; set; }
    string? ErrorTitle { get; set; }
    DispatcherOperationCallback? GetWinFxCallback { get; set; }
    string? LogFilePath { get; set; }
    DispatcherOperationCallback? RefreshCallback { get; set; }
    Uri? SupportUri { get; set; }
}

/// <summary>Describes the progress page used by a browser-hosted deployment.</summary>
public interface IProgressPage
{
    string? ApplicationName { get; set; }
    Uri? DeploymentPath { get; set; }
    string? PublisherName { get; set; }
    DispatcherOperationCallback? RefreshCallback { get; set; }
    DispatcherOperationCallback? StopCallback { get; set; }
    void UpdateProgress(long bytesDownloaded, long bytesTotal);
}

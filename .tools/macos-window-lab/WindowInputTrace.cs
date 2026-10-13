using AppKit;
using Jalium.UI;
using ObjCRuntime;
using Expression = System.Linq.Expressions.Expression;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace MacOSWindowLab;

internal static class WindowInputTrace
{
    internal static void Attach(Window window)
    {
        var platform = typeof(Window).GetField("_platformWindow", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window);
        var field = platform?.GetType().GetField("_eventHandler", BindingFlags.Instance | BindingFlags.NonPublic);
        if (field?.GetValue(platform) is not Delegate original) return;
        var parameter = Expression.Parameter(original.GetType().GenericTypeArguments[0], "evt");
        var method = typeof(WindowInputTrace).GetMethod(nameof(Dispatch), BindingFlags.Static | BindingFlags.NonPublic)!;
        var body = Expression.Call(method, Expression.Constant(window), Expression.Constant(original, typeof(Delegate)), Expression.Convert(parameter, typeof(object)));
        field.SetValue(platform, Expression.Lambda(original.GetType(), body, parameter).Compile());
    }

    private static void Dispatch(Window window, Delegate original, object evt)
    {
        Record(window, evt, "before");
        try { original.DynamicInvoke(evt); }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); }
        finally { Record(window, evt, "after"); }
    }

    private static void Record(Window window, object evt, string phase)
    {
        try
        {
            object? Read(string name) => evt.GetType().GetField(name)?.GetValue(evt);
            string? type = Read("Type")?.ToString();
            if (type is not ("MouseDown" or "MouseMove" or "MouseUp" or "DragEnter" or "DragOver" or "DragLeave" or "Drop" or "DragFinished")) return;
            var native = window.Handle == 0 ? null : Runtime.GetNSObject<NSView>(window.Handle)?.Window;
            var current = NSApplication.SharedApplication.CurrentEvent;
            File.AppendAllText(Program.LogPrefix + "-input.jsonl",
                System.Text.Json.JsonSerializer.Serialize(new
                {
                    pid = Environment.ProcessId, phase, type, window.Title, window.Width, window.Height,
                    window.IsEnabled, window.IsActive, state = window.WindowState.ToString(),
                    x = Read("MouseX"), y = Read("MouseY"), button = Read("Button"),
                    session = Read("DragSessionId"), keys = Read("DragKeyStates"), allowed = Read("DragAllowedEffects"), formats = Read("DragMimeTypes"),
                    dpi = typeof(Window).GetField("_dpiScale", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window),
                    nativeScale = native is null ? (double?)null : (double)native.BackingScaleFactor,
                    nativeWindow = native is null ? (long?)null : (long)native.WindowNumber,
                    currentWindow = current is null ? (long?)null : (long)current.WindowNumber,
                    currentType = current?.Type.ToString(),
                    currentX = current is null ? (double?)null : (double)current.LocationInWindow.X,
                    currentY = current is null ? (double?)null : (double)current.LocationInWindow.Y,
                    nativeKey = native?.IsKeyWindow, nativeMain = native?.IsMainWindow
                }) + Environment.NewLine);
        }
        catch { /* Diagnostics must not change native input dispatch. */ }
    }
}

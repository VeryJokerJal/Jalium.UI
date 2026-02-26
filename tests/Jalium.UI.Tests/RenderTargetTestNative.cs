using Jalium.UI;
using Jalium.UI.Interop;

namespace Jalium.UI.Tests;

internal sealed class RenderTargetTestNative : IRenderTargetNative
{
    public nint CreatedHandle { get; set; } = new(0xCAFE);
    public int ContextLastError { get; set; } = (int)JaliumResult.Unknown;
    public int ResizeResult { get; set; } = (int)JaliumResult.Ok;
    public int BeginDrawResult { get; set; } = (int)JaliumResult.Ok;
    public int EndDrawResult { get; set; } = (int)JaliumResult.Ok;

    public nint CreateForHwnd(nint context, nint hwnd, int width, int height) => CreatedHandle;

    public nint CreateForComposition(nint context, nint hwnd, int width, int height) => CreatedHandle;

    public int GetContextLastError(nint context) => ContextLastError;

    public int Resize(nint renderTarget, int width, int height) => ResizeResult;

    public int BeginDraw(nint renderTarget) => BeginDrawResult;

    public int EndDraw(nint renderTarget) => EndDrawResult;

    public void SetFullInvalidation(nint renderTarget)
    {
    }

    public void Destroy(nint renderTarget)
    {
    }
}

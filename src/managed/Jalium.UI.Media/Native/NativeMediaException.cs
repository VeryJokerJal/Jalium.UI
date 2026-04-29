namespace Jalium.UI.Media.Native;

/// <summary>
/// 由 <see cref="NativeMediaInterop"/> 调用失败时抛出。
/// </summary>
public sealed class NativeMediaException : Exception
{
    /// <summary>
    /// 初始化新的 <see cref="NativeMediaException"/>。
    /// </summary>
    public NativeMediaException(NativeMediaStatus status, string operation)
        : base(BuildMessage(status, operation))
    {
        Status = status;
        Operation = operation;
    }

    /// <summary>原生状态码。</summary>
    public NativeMediaStatus Status { get; }

    /// <summary>失败时正在执行的操作（用于诊断）。</summary>
    public string Operation { get; }

    private static string BuildMessage(NativeMediaStatus status, string operation)
        => $"jalium.native.media: {operation} failed with status {status} ({(int)status}: {NativeMediaInterop.GetStatusString(status)}).";

    /// <summary>
    /// 状态码非 <see cref="NativeMediaStatus.Ok"/> 时抛出。
    /// </summary>
    public static void ThrowIfFailed(NativeMediaStatus status, string operation)
    {
        if (status != NativeMediaStatus.Ok)
        {
            throw new NativeMediaException(status, operation);
        }
    }
}

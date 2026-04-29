namespace Jalium.UI.Media.Pipeline;

/// <summary>
/// 摄像头设备朝向。
/// </summary>
public enum CameraFacing
{
    /// <summary>未知或不适用。</summary>
    Unknown = 0,

    /// <summary>前置摄像头（手机自拍 / 笔记本前摄）。</summary>
    Front = 1,

    /// <summary>后置摄像头。</summary>
    Back = 2,

    /// <summary>外接摄像头（USB 摄像头等）。</summary>
    External = 3,
}

/// <summary>
/// 摄像头采集格式：分辨率 + 帧率。
/// </summary>
public readonly record struct CameraFormat(int Width, int Height, double Fps);

/// <summary>
/// 摄像头设备信息。
/// </summary>
public sealed class CameraDeviceInfo
{
    /// <summary>
    /// 初始化新的 <see cref="CameraDeviceInfo"/>。
    /// </summary>
    public CameraDeviceInfo(string id, string friendlyName, CameraFacing facing, IReadOnlyList<CameraFormat> supportedFormats)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        ArgumentNullException.ThrowIfNull(friendlyName);
        ArgumentNullException.ThrowIfNull(supportedFormats);

        Id = id;
        FriendlyName = friendlyName;
        Facing = facing;
        SupportedFormats = supportedFormats;
    }

    /// <summary>平台稳定设备 ID（Windows 用 MF symbolic link，Android 用 cameraId）。</summary>
    public string Id { get; }

    /// <summary>友好名称（"Integrated Camera"、"Logitech HD"、"Camera 0 (back)"）。</summary>
    public string FriendlyName { get; }

    /// <summary>设备朝向。</summary>
    public CameraFacing Facing { get; }

    /// <summary>设备支持的分辨率与帧率组合。</summary>
    public IReadOnlyList<CameraFormat> SupportedFormats { get; }

    /// <inheritdoc />
    public override string ToString() => $"{FriendlyName} [{Facing}] ({Id})";
}

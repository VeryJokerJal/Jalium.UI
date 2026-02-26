using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Jalium.UI.Gpu.Shaders;

/// <summary>
/// Shader 编译器 - 支持从预编译资源加载、磁盘缓存、运行时 D3DCompile 三级缓存
/// 优先级：1. 内存缓存 → 2. 程序集嵌入资源(.cso) → 3. 磁盘缓存 → 4. 运行时编译
/// </summary>
public sealed class ShaderCompiler : IDisposable
{
    private readonly string _cacheDirectory;
    private readonly Dictionary<ShaderType, CompiledShader> _shaderCache = new();
    private readonly Assembly? _resourceAssembly;
    private readonly string _resourcePrefix;
    private bool _disposed;

    /// <summary>
    /// 创建编译器实例
    /// </summary>
    /// <param name="cacheDirectory">磁盘缓存目录，null 则使用默认目录</param>
    /// <param name="resourceAssembly">包含预编译着色器资源的程序集，null 则使用调用方程序集</param>
    /// <param name="resourcePrefix">资源名称前缀（如 "Jalium.UI.Gallery.Shaders."），null 则自动推断</param>
    public ShaderCompiler(
        string? cacheDirectory = null,
        Assembly? resourceAssembly = null,
        string? resourcePrefix = null)
    {
        _cacheDirectory = cacheDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Jalium.UI", "ShaderCache");

        if (!Directory.Exists(_cacheDirectory))
        {
            Directory.CreateDirectory(_cacheDirectory);
        }

        // 设置资源加载：优先使用指定的程序集，其次使用入口程序集
        _resourceAssembly = resourceAssembly ?? Assembly.GetEntryAssembly();
        _resourcePrefix = resourcePrefix
            ?? (_resourceAssembly?.GetName().Name + ".Shaders.")
            ?? "Shaders.";
    }

    /// <summary>
    /// 获取或编译指定类型的着色器
    /// 查找顺序：内存缓存 → 嵌入资源 → 磁盘缓存 → 运行时编译
    /// </summary>
    public CompiledShader GetOrCompile(ShaderType type)
    {
        // 1. 内存缓存
        if (_shaderCache.TryGetValue(type, out var cached))
            return cached;

        var stage = GetStage(type);

        // 2. 尝试从程序集嵌入资源加载预编译着色器
        var precompiled = TryLoadFromResource(type);
        if (precompiled != null)
        {
            var compiled = new CompiledShader(type, stage, precompiled);
            _shaderCache[type] = compiled;
            return compiled;
        }

        var source = ShaderLibrary.GetFullSource(type);
        var entryPoint = ShaderLibrary.GetEntryPoint(type);
        var target = ShaderLibrary.GetTarget(type);

        // 计算源码哈希用于磁盘缓存
        var sourceHash = ComputeHash(source, entryPoint, target);
        var cacheFile = Path.Combine(_cacheDirectory, $"{type}_{sourceHash}.cso");

        byte[] bytecode;

        // 3. 尝试从磁盘缓存加载
        if (File.Exists(cacheFile))
        {
            bytecode = File.ReadAllBytes(cacheFile);
        }
        else
        {
            // 4. 运行时编译
            bytecode = CompileFromSource(source, entryPoint, target);

            // 写入磁盘缓存
            try
            {
                File.WriteAllBytes(cacheFile, bytecode);
            }
            catch (IOException)
            {
                // 缓存写入失败不影响功能
            }
        }

        var result = new CompiledShader(type, stage, bytecode);
        _shaderCache[type] = result;

        return result;
    }

    /// <summary>
    /// 尝试从程序集嵌入资源加载预编译的着色器字节码
    /// 资源名称格式: {RootNamespace}.Shaders.{ShaderType}.cso
    /// </summary>
    private byte[]? TryLoadFromResource(ShaderType type)
    {
        if (_resourceAssembly == null) return null;

        var resourceName = $"{_resourcePrefix}{type}.cso";

        try
        {
            using var stream = _resourceAssembly.GetManifestResourceStream(resourceName);
            if (stream == null) return null;

            var bytecode = new byte[stream.Length];
            stream.ReadExactly(bytecode);
            return bytecode;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 编译所有预定义着色器
    /// </summary>
    public void CompileAll()
    {
        foreach (var type in Enum.GetValues<ShaderType>())
        {
            GetOrCompile(type);
        }
    }

    /// <summary>
    /// 编译自定义 HLSL 源码
    /// </summary>
    public byte[] CompileCustom(string source, string entryPoint, ShaderStage stage)
    {
        var target = stage switch
        {
            ShaderStage.Vertex => "vs_5_1",
            ShaderStage.Pixel => "ps_5_1",
            ShaderStage.Compute => "cs_5_1",
            _ => throw new ArgumentOutOfRangeException(nameof(stage))
        };

        return CompileFromSource(source, entryPoint, target);
    }

    /// <summary>
    /// 清除所有缓存
    /// </summary>
    public void ClearCache()
    {
        _shaderCache.Clear();

        if (Directory.Exists(_cacheDirectory))
        {
            foreach (var file in Directory.GetFiles(_cacheDirectory, "*.cso"))
            {
                try { File.Delete(file); }
                catch (IOException) { }
            }
        }
    }

    private static byte[] CompileFromSource(string source, string entryPoint, string target)
    {
        var sourceBytes = Encoding.UTF8.GetBytes(source);

        var hr = NativeShaderMethods.jalium_shader_compile(
            sourceBytes,
            sourceBytes.Length,
            entryPoint,
            target,
            ShaderCompileFlags.OptimizationLevel3 | ShaderCompileFlags.WarningsAreErrors,
            out var bytecodePtr,
            out var bytecodeSize,
            out var errorPtr,
            out var errorSize);

        if (hr < 0)
        {
            var errorMessage = errorPtr != nint.Zero && errorSize > 0
                ? Marshal.PtrToStringUTF8(errorPtr, errorSize) ?? "Unknown shader compilation error"
                : $"Shader compilation failed with HRESULT 0x{hr:X8}";

            if (errorPtr != nint.Zero)
                NativeShaderMethods.jalium_shader_free_blob(errorPtr);

            throw new ShaderCompilationException(errorMessage);
        }

        var bytecode = new byte[bytecodeSize];
        if (bytecodePtr != nint.Zero && bytecodeSize > 0)
        {
            Marshal.Copy(bytecodePtr, bytecode, 0, bytecodeSize);
            NativeShaderMethods.jalium_shader_free_blob(bytecodePtr);
        }

        if (errorPtr != nint.Zero)
            NativeShaderMethods.jalium_shader_free_blob(errorPtr);

        return bytecode;
    }

    private static string ComputeHash(string source, string entryPoint, string target)
    {
        var input = Encoding.UTF8.GetBytes($"{source}|{entryPoint}|{target}");
        var hash = SHA256.HashData(input);
        return Convert.ToHexString(hash)[..16]; // 前 16 字符足够
    }

    private static ShaderStage GetStage(ShaderType type) => type switch
    {
        ShaderType.UIRectVS or ShaderType.TextVS or ShaderType.ImageVS
            or ShaderType.PathVS or ShaderType.CompositeVS => ShaderStage.Vertex,
        ShaderType.UIRectPS or ShaderType.TextPS or ShaderType.ImagePS
            or ShaderType.PathPS or ShaderType.CompositePS => ShaderStage.Pixel,
        ShaderType.GaussianBlurHorizontalCS or ShaderType.GaussianBlurVerticalCS
            or ShaderType.BackdropFilterCS => ShaderStage.Compute,
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _shaderCache.Clear();
    }
}

/// <summary>
/// 编译后的着色器
/// </summary>
public sealed class CompiledShader
{
    /// <summary>
    /// 着色器类型
    /// </summary>
    public ShaderType Type { get; }

    /// <summary>
    /// 着色器阶段
    /// </summary>
    public ShaderStage Stage { get; }

    /// <summary>
    /// 编译后的字节码 (DXBC/DXIL)
    /// </summary>
    public byte[] Bytecode { get; }

    public CompiledShader(ShaderType type, ShaderStage stage, byte[] bytecode)
    {
        Type = type;
        Stage = stage;
        Bytecode = bytecode;
    }
}

/// <summary>
/// Shader 编译异常
/// </summary>
public sealed class ShaderCompilationException : Exception
{
    public ShaderCompilationException(string message) : base(message) { }
}

/// <summary>
/// Shader 编译标志
/// </summary>
[Flags]
internal enum ShaderCompileFlags : uint
{
    None = 0,
    Debug = 1 << 0,
    SkipValidation = 1 << 1,
    SkipOptimization = 1 << 2,
    PackMatrixRowMajor = 1 << 3,
    PackMatrixColumnMajor = 1 << 4,
    WarningsAreErrors = 1 << 18,
    OptimizationLevel0 = 1 << 14,
    OptimizationLevel1 = 0,
    OptimizationLevel2 = (1 << 14) | (1 << 15),
    OptimizationLevel3 = 1 << 15,
}

/// <summary>
/// Native shader 编译 P/Invoke
/// </summary>
internal static partial class NativeShaderMethods
{
    /// <summary>
    /// 编译 HLSL 着色器
    /// </summary>
    [LibraryImport("jalium.native.d3d12", StringMarshalling = StringMarshalling.Utf8)]
    internal static partial int jalium_shader_compile(
        byte[] sourceData,
        int sourceSize,
        string entryPoint,
        string target,
        ShaderCompileFlags flags,
        out nint bytecodePtr,
        out int bytecodeSize,
        out nint errorPtr,
        out int errorSize);

    /// <summary>
    /// 释放编译 blob
    /// </summary>
    [LibraryImport("jalium.native.d3d12")]
    internal static partial void jalium_shader_free_blob(nint blob);
}

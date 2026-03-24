using System.Runtime.InteropServices;
using System.Text;
using Jalium.UI.Media.Effects;

namespace Jalium.UI.ShaderDemo;

/// <summary>
/// 运行时编译 HLSL 并缓存 PixelShader 实例。
/// 直接调用系统 d3dcompiler_47.dll，不依赖 jalium.native.d3d12 的编译接口。
/// </summary>
internal static class ShaderHelper
{
    private static PixelShader? _sepiaVignetteShader;

    /// <summary>
    /// HLSL 源码：棕褐色调 + 暗角 Pixel Shader (ps_5_1)
    /// </summary>
    private const string SepiaVignetteHlsl = """
        cbuffer Constants : register(b0)
        {
            float4 c0; // x = Intensity
            float4 c1; // x = VignetteRadius, y = VignetteSoftness
        };

        Texture2D InputTexture : register(t0);
        SamplerState InputSampler : register(s0);

        float4 main(float2 uv : TEXCOORD0) : SV_Target
        {
            float4 color = InputTexture.Sample(InputSampler, uv);

            // Sepia 色调
            float intensity = c0.x;
            float gray = dot(color.rgb, float3(0.299, 0.587, 0.114));
            float3 sepia = float3(gray * 1.2, gray * 1.0, gray * 0.8);
            color.rgb = lerp(color.rgb, sepia, saturate(intensity));

            // Vignette 暗角
            float radius = c1.x;
            float softness = c1.y;
            float2 center = uv - 0.5;
            float dist = length(center);
            float vignette = smoothstep(radius, radius - softness, dist);
            color.rgb *= vignette;

            return color;
        }
        """;

    public static PixelShader GetSepiaVignetteShader()
    {
        if (_sepiaVignetteShader != null)
            return _sepiaVignetteShader;

        var bytecode = CompileHlsl(SepiaVignetteHlsl, "main", "ps_5_1");

        var ps = new PixelShader();
        ps.SetStreamSource(new MemoryStream(bytecode));

        _sepiaVignetteShader = ps;
        return ps;
    }

    #region D3DCompiler P/Invoke

    /// <summary>
    /// 直接调用 Windows 系统自带的 d3dcompiler_47.dll 编译 HLSL。
    /// </summary>
    private static byte[] CompileHlsl(string source, string entryPoint, string target)
    {
        var sourceBytes = Encoding.UTF8.GetBytes(source);

        var hr = D3DCompile(
            sourceBytes, (nuint)sourceBytes.Length,
            null, nint.Zero, nint.Zero,
            entryPoint, target,
            D3DCOMPILE_OPTIMIZATION_LEVEL3, // flags1
            0,                               // flags2
            out var codeBlob,
            out var errorBlob);

        if (hr < 0)
        {
            var errorMsg = "Shader compilation failed.";
            if (errorBlob != nint.Zero)
            {
                var ptr = D3D10GetBlobBufferPointer(errorBlob);
                var size = D3D10GetBlobBufferSize(errorBlob);
                if (ptr != nint.Zero && size > 0)
                    errorMsg = Marshal.PtrToStringUTF8(ptr, (int)size) ?? errorMsg;
                Marshal.Release(errorBlob);
            }
            throw new InvalidOperationException(errorMsg);
        }

        var blobPtr = D3D10GetBlobBufferPointer(codeBlob);
        var blobSize = (int)D3D10GetBlobBufferSize(codeBlob);
        var bytecode = new byte[blobSize];
        Marshal.Copy(blobPtr, bytecode, 0, blobSize);
        Marshal.Release(codeBlob);

        if (errorBlob != nint.Zero)
            Marshal.Release(errorBlob);

        return bytecode;
    }

    private const uint D3DCOMPILE_OPTIMIZATION_LEVEL3 = 1 << 15;

    [DllImport("d3dcompiler_47.dll", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    private static extern int D3DCompile(
        byte[] pSrcData, nuint srcDataSize,
        string? pSourceName,
        nint pDefines, // D3D_SHADER_MACRO*
        nint pInclude, // ID3DInclude*
        string pEntrypoint,
        string pTarget,
        uint flags1, uint flags2,
        out nint ppCode,    // ID3DBlob**
        out nint ppErrorMsgs // ID3DBlob**
    );

    // ID3DBlob vtable: QueryInterface(0), AddRef(1), Release(2), GetBufferPointer(3), GetBufferSize(4)
    private static nint D3D10GetBlobBufferPointer(nint blob)
    {
        var vtable = Marshal.ReadIntPtr(blob); // pVtbl
        var fn = Marshal.ReadIntPtr(vtable, 3 * nint.Size); // GetBufferPointer
        var del = Marshal.GetDelegateForFunctionPointer<GetBufferPointerDelegate>(fn);
        return del(blob);
    }

    private static nuint D3D10GetBlobBufferSize(nint blob)
    {
        var vtable = Marshal.ReadIntPtr(blob);
        var fn = Marshal.ReadIntPtr(vtable, 4 * nint.Size); // GetBufferSize
        var del = Marshal.GetDelegateForFunctionPointer<GetBufferSizeDelegate>(fn);
        return del(blob);
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate nint GetBufferPointerDelegate(nint thisPtr);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate nuint GetBufferSizeDelegate(nint thisPtr);

    #endregion
}

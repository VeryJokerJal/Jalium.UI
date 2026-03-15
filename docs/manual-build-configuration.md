# Jalium.UI 手动配置构建说明

本文档面向“**不修改仓库源码**，只在用户本机做配置”的场景。

结论先说：

- 通过本机配置，可以让仓库的 `restore` 正常通过。
- 通过本机配置，可以让底层 managed 项目里的 `Jalium.UI.Input` 和 `Jalium.UI.Media` 编译通过。
- **仅靠本机配置，不能保证整个仓库直接编译通过。**
- 在当前未改源码的状态下，`Jalium.UI.Interop`、`Jalium.UI.Controls`、`Jalium.UI.Xaml`、测试项目以及整套 native 方案，仍可能失败。

## 1. 前置环境

请先在目标电脑安装：

- `.NET SDK 10.0.200` 或兼容的 `net10.0-windows` SDK
- Visual Studio 的 `Desktop development with C++`

说明：

- 如果只编 managed 子项目，`dotnet build` 可以使用。
- 如果要编整套解决方案，仍建议使用 Visual Studio 或 Developer Command Prompt 下的 `MSBuild`，因为仓库包含 native/C++ 工程。

## 2. 先补本地包源目录

仓库里的 `nuget.config` 指向了相对路径 `./packages` 作为本地源。

第一次拉仓库后，请在仓库根目录手动创建：

```powershell
mkdir packages\build
```

这一步的目的不是放包，而是避免 `NU1301`：

```text
本地源“...\packages”不存在
```

## 3. 先做还原

在仓库根目录执行：

```powershell
dotnet restore .\Jalium.UI.slnx -v minimal -p:GenerateAssemblyInfo=true
```

我已经验证过：在不改源码、只补 `packages\build` 的情况下，这一步可以通过。

## 4. 已验证可行的 managed 构建命令

下面两条命令我已经做过实际验证，能够在未改源码的副本上编译通过。

### 4.1 编译 Jalium.UI.Input

```powershell
dotnet build .\src\managed\Jalium.UI.Input\Jalium.UI.Input.csproj `
  -v minimal `
  -p:GenerateAssemblyInfo=true `
  -p:ProduceReferenceAssembly=false `
  -p:BaseIntermediateOutputPath=obj_local_input\ `
  -p:IntermediateOutputPath=obj_local_input\Debug\net10.0-windows\ `
  -p:OutputPath=bin_local_input\Debug\net10.0-windows\
```

### 4.2 编译 Jalium.UI.Media

```powershell
dotnet build .\src\managed\Jalium.UI.Media\Jalium.UI.Media.csproj `
  -v minimal `
  -p:GenerateAssemblyInfo=true `
  -p:ProduceReferenceAssembly=false `
  -p:BaseIntermediateOutputPath=obj_local_media\ `
  -p:IntermediateOutputPath=obj_local_media\Debug\net10.0-windows\ `
  -p:OutputPath=bin_local_media\Debug\net10.0-windows\
```

这些参数的意义：

- `GenerateAssemblyInfo=true`
  让项目文件里的程序集元数据重新生成。
- `ProduceReferenceAssembly=false`
  避免引用程序集路径带来的内部 API 可见性问题。
- `BaseIntermediateOutputPath` / `IntermediateOutputPath` / `OutputPath`
  把本次构建输出放到独立目录里，尽量绕开已有 `obj/bin` 和编译器文件锁。

## 5. 当前没有验证通过的部分

下面这些在“完全不改源码”的前提下，我**没有验证通过**：

- `Jalium.UI.Interop`
- `Jalium.UI.Controls`
- `Jalium.UI.Xaml`
- `tests/Jalium.UI.Tests`
- 整个 `Jalium.UI.slnx` 的完整编译

其中我实际见到过的失败类型包括：

- 友元程序集相关的内部 API 可见性问题
- `GenerateAssemblyInfo=true` 后出现重复程序集特性
- `Controls` 层更高层的 API 漂移
- native/C++ 工程在 `dotnet build` 下无法完整处理

所以如果你的目标是：

- “先让用户恢复依赖，并编底层 managed 库”
  这份教程可以直接用。
- “让用户 clone 后一条命令编完整仓库”
  仅靠用户本机配置还不够，最终仍需要仓库侧修复。

## 6. 给用户的最小操作建议

如果你要把这套流程发给用户，建议只给下面这组最小步骤：

1. 安装 `.NET 10 SDK`
2. 安装 Visual Studio C++ 工作负载
3. 在仓库根目录创建 `packages\build`
4. 执行 `dotnet restore .\Jalium.UI.slnx -p:GenerateAssemblyInfo=true`
5. 如果只需要底层库，再分别执行上面的 `Input` / `Media` 构建命令

不要承诺：

- “整仓一定能一键编过”
- “只要装好 SDK 就行”
- “不需要 Visual Studio / MSBuild / C++ 工具链”

这些承诺和当前仓库实际状态不一致。

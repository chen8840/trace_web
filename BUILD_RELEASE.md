# TraceWeb 自包含 Release 编译指南

本文档用于在 Windows 上编译 `TraceWeb` 的自包含（Self-contained）Release 版本。

## 1. 环境要求

1. 已安装 .NET SDK 10（建议使用 `10.0.102` 或更高版本）
2. 在 Windows 系统执行（WPF 项目）
3. 当前目录为项目根目录：`d:\work\test\trace_web`

## 2. 推荐编译命令（Win-x64）

```powershell
dotnet publish .\TraceWeb.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  /p:PublishSingleFile=true `
  /p:IncludeNativeLibrariesForSelfExtract=true `
  -o .\publish\win-x64
```

## 3. 参数说明

1. `-c Release`：使用 Release 配置编译
2. `-r win-x64`：目标运行时（64 位 Windows）
3. `--self-contained true`：打包 .NET 运行时，目标机器无需预装 .NET
4. `/p:PublishSingleFile=true`：尽量生成单文件主程序
5. `/p:IncludeNativeLibrariesForSelfExtract=true`：确保本地库可正常加载（对 WebView2/SQLite 场景更稳妥）
6. `-o .\publish\win-x64`：指定输出目录

## 4. 产物位置

发布完成后，输出在：

```text
.\publish\win-x64\
```

主程序通常是：

```text
TraceWeb.exe
```

## 5. 其他常用目标平台

1. `win-x86`（32 位）：

```powershell
dotnet publish .\TraceWeb.csproj -c Release -r win-x86 --self-contained true -o .\publish\win-x86
```

2. `win-arm64`（ARM64）：

```powershell
dotnet publish .\TraceWeb.csproj -c Release -r win-arm64 --self-contained true -o .\publish\win-arm64
```

## 6. 常见问题

1. 文件被占用导致发布失败  
如果你正在运行 `TraceWeb.exe`，发布时可能报文件锁定。先关闭正在运行的程序再执行 `publish`。

2. 出现 `NETSDK1206` 警告  
这是部分依赖包的 RID 提示，通常不影响 Windows 发布结果；如功能正常可先忽略。

3. WebView2 运行依赖  
应用使用 WebView2。多数 Windows 10/11 已内置 Runtime。若目标机器缺失，请安装 WebView2 Runtime。

## 7. 一键脚本（可选）

你也可以直接执行下面这条：

```powershell
dotnet publish .\TraceWeb.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o .\publish\win-x64
```


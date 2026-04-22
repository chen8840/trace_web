# TraceWeb

TraceWeb 是一个基于 **WPF + WebView2 + SQLite** 的“有痕归档浏览器”。  
它将传统按时间线的历史记录，升级为按网站（一级域名）归档的资料库视图。

## 功能特性

1. 按一级域名归档历史（如 `cloud.baidu.com` 与 `www.baidu.com` 归为 `baidu.com`）
2. 首页按域名聚合展示：最后访问时间、访问次数、最后访问地址
3. 浏览视图左侧展示当前域名历史轨迹（最多 100 条）
4. 每个域名数据库历史自动裁剪为最多 100 条
5. 新窗口请求自动转为新标签页打开，并继续写入历史
6. 标签页支持关闭
7. 左侧历史栏支持折叠/展开
8. 首页支持直接输入网址开始浏览
9. 首页域名入口支持删除（带确认弹窗）

## 技术栈

- .NET 10
- WPF
- Microsoft.Web.WebView2
- SQLite (`sqlite-net-pcl`)

## 快速开始

### 运行（Debug）

```powershell
dotnet run
```

### 构建

```powershell
dotnet build
```

## 发布（自包含 Release）

推荐发布命令（`win-x64`）：

```powershell
dotnet publish .\TraceWeb.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  /p:PublishSingleFile=true `
  /p:IncludeNativeLibrariesForSelfExtract=true `
  -o .\publish\win-x64
```

更完整说明见：[`BUILD_RELEASE.md`](./BUILD_RELEASE.md)

## 数据存储

- 本地数据库文件：`traceweb_archive.db`
- 默认位于程序当前工作目录

## 项目结构

```text
.
├─ App.xaml
├─ MainWindow.xaml
├─ MainWindow.xaml.cs
├─ Models
│  ├─ DomainGroup.cs
│  └─ HistoryRecord.cs
├─ BUILD_RELEASE.md
└─ TraceWeb.csproj
```

## 路线图（可选）

1. 导出/备份历史数据
2. 域名搜索与收藏
3. 标签页右键菜单（关闭其他、关闭右侧）
4. 更完整的公共后缀规则（Public Suffix List）


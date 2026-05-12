# ADOFAIWebBridge

ADOFAIWebBridge 是给 ADOFAI / UnityModManager 模组使用的 WebUI Bridge。它让模组可以启动本地 WebSocket RPC 服务，并用 Steam Overlay 打开一个 Vite/Vue 前端页面。

## 目录结构

```text
ADOFAIWebBridge/
├── src/                         # Vite + Vue + TypeScript WebUI
├── src-cs/ADOFAIWebBridge/       # C# SDK, net481
├── build/ADOFAIWebBridge.props
├── ADOFAIWebBridge.sln
└── README.md
```

## 安装方式

### 方式一：源码引用

推荐开发阶段使用。常见工作区结构：

```text
Workspace/
├── ADOFAIWebBridge/
└── YourMod/
```

在 `YourMod.csproj` 中引用：

```xml
<ProjectReference Include="..\ADOFAIWebBridge\src-cs\ADOFAIWebBridge\ADOFAIWebBridge.csproj" PrivateAssets="all" />
```

### 方式二：把 SDK 克隆进模组目录

如果结构是：

```text
YourMod/
├── ADOFAIWebBridge/
└── YourMod.csproj
```

需要先导入 SDK 提供的 props，避免 SDK 源码被父项目重复编译：

```xml
<Import Project="ADOFAIWebBridge\build\ADOFAIWebBridge.props"
        Condition="Exists('ADOFAIWebBridge\build\ADOFAIWebBridge.props')" />

<ItemGroup>
  <ProjectReference Include="ADOFAIWebBridge\src-cs\ADOFAIWebBridge\ADOFAIWebBridge.csproj" PrivateAssets="all" />
</ItemGroup>
```

如果 SDK 文件夹不是 `ADOFAIWebBridge`，在 `Import` 前设置：

```xml
<PropertyGroup>
  <ADOFAIWebBridgeSourceRoot>$(MSBuildProjectDirectory)\Vendor\ADOFAIWebBridge\</ADOFAIWebBridgeSourceRoot>
</PropertyGroup>
```

### 方式三：NuGet 包引用

```xml
<PackageReference Include="ADOFAIWebBridge" Version="0.1.1" />
```

包引用只提供 C# SDK。前端模板仍然需要从仓库的 `src` 目录复制或自行实现。

## C# 基础用法

```csharp
using System.IO;
using ADOFAIWebBridge;

var bridge = WebBridge.ForUMM(modEntry, new WebBridgeOptions
{
    ModId = "com.example.your-mod",
    DisplayName = "Your Mod",
    PreferredPort = 39800,
    WebRoot = Path.Combine(modEntry.Path, "webui", "dist"),
    DevServerUrl = "http://127.0.0.1:5173/",
    Mode = BridgeMode.Production,
    UseSteamOverlay = true,
    RequireToken = true
});

bridge.RegisterCommand("yourMod.getStatus", _ => new
{
    ready = true
});

bridge.Start();
```

常用 API：

```csharp
bridge.Start();
bridge.Stop();
bridge.OpenSteamOverlay();
bridge.RegisterCommand("yourMod.command", parameters => result);
bridge.Emit("yourMod.event", data);
bridge.ExposeFile(filePath, "image/png", TimeSpan.FromMinutes(5));
bridge.ExposeBytes(bytes, "image/png", TimeSpan.FromMinutes(5));
```

命令名建议始终带模组命名空间，例如 `yourMod.getStatus`，避免和其他模组冲突。

## 前端开发

安装依赖：

```powershell
cd ADOFAIWebBridge\src
pnpm install
```

启动开发服务器：

```powershell
$env:VITE_BRIDGE_PORT="39800"
pnpm dev
```

`VITE_BRIDGE_PORT` 必须和 C# 里的 `PreferredPort` 一致。开发模式下，`/rpc` 会代理到：

```text
ws://127.0.0.1:<VITE_BRIDGE_PORT>/rpc
```

前端调用：

```ts
const result = await bridge.invoke("yourMod.getStatus")

bridge.listen("yourMod.changed", data => {
  console.log(data)
})
```

## 暴露本地资源给前端

如果 C# 读取了用户磁盘上的图片、音频或临时生成的资源，不要把真实路径返回给前端。应该让 Bridge 生成一个临时 URL：

```csharp
bridge.RegisterCommand("yourMod.getCover", _ =>
{
    string url = bridge.ExposeFile(
        @"C:\Users\You\Pictures\cover.png",
        "image/png",
        TimeSpan.FromMinutes(5));

    return new { url };
});
```

前端：

```ts
const cover = await bridge.invoke<{ url: string }>("yourMod.getCover")
image.src = cover.url
```

也可以暴露内存里的字节：

```csharp
string url = bridge.ExposeBytes(pngBytes, "image/png", TimeSpan.FromMinutes(5));
```

资源 URL 形如：

```text
http://127.0.0.1:39800/__bridge_file/<id>?bridgeToken=<token>
```

安全规则：

- 前端看不到真实磁盘路径。
- 只有 C# 主动暴露过的资源能访问。
- 默认需要 `bridgeToken`。
- 默认有效期由调用方传入；不传时是 10 分钟。
- 资源只允许本机访问。

## 生产构建

构建前端：

```powershell
cd ADOFAIWebBridge\src
pnpm build
```

把产物复制到模组输出目录：

```text
ADOFAIWebBridge\src\dist -> Mods\YourMod\webui\dist
```

模组发布时需要带上：

```text
YourMod.dll
ADOFAIWebBridge.dll
EmbedIO.dll
Newtonsoft.Json.dll
Swan.Lite.dll
System.ValueTuple.dll
webui/dist/**
Info.json
```

## 打包 SDK

```powershell
dotnet build .\ADOFAIWebBridge.sln -c Release
dotnet pack .\src-cs\ADOFAIWebBridge\ADOFAIWebBridge.csproj -c Release
```

生成的包在：

```text
src-cs/ADOFAIWebBridge/bin/Release/ADOFAIWebBridge.0.1.1.nupkg
```

## 安全说明

- 默认只监听 `127.0.0.1`。
- 默认启用 `RequireToken`，Steam Overlay URL 会携带一次性的 `bridgeToken`。
- 不要把删除文件、执行进程、上传隐私数据等危险能力直接暴露成 Web 命令。
- 命令参数来自前端，C# 侧必须校验。

## 常见问题

### 构建出现 CS0436 类型冲突

通常是把 SDK 克隆到了模组目录内部，父项目把 SDK 源码也编译进来了。导入：

```xml
<Import Project="ADOFAIWebBridge\build\ADOFAIWebBridge.props"
        Condition="Exists('ADOFAIWebBridge\build\ADOFAIWebBridge.props')" />
```

### Steam Overlay 打开 404

生产模式下通常是 `webui/dist` 没复制到模组目录。先运行 `pnpm build`，再构建模组。

### 前端显示已打开但调用没反应

检查三件事：

- C# Bridge 是否启动成功。
- Vite 的 `VITE_BRIDGE_PORT` 是否等于 `PreferredPort`。
- 前端命令名是否和 C# `RegisterCommand` 完全一致。

### 开发模式还是打开生产页面

UMM 设置保存后需要重启游戏，Bridge 才会按新的 `Mode` 启动。

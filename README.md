# Collab Charting

`Collab Charting` 是一个普通 ADOFAI/UMM 模组项目，用来测试和使用 `ADOFAIWebBridge`。

SDK 已经作为普通源码目录放在本项目内，不再是嵌套 Git 仓库：

```text
E:\Documents\.NET\CollabCharting\ADOFAIWebBridge
```

项目通过 SDK 自带的 `ADOFAIWebBridge\build\ADOFAIWebBridge.props` 排除内嵌 SDK 源码，避免 C# 项目把 SDK 源码重复编译进 `CollabCharting.dll`。

前端在：

```text
E:\Documents\.NET\CollabCharting\ADOFAIWebBridge\src
```

## 开发前端

```powershell
cd E:\Documents\.NET\CollabCharting\ADOFAIWebBridge\src
pnpm install
$env:VITE_BRIDGE_PORT="39800"
pnpm dev
```

然后在 UMM 面板勾选开发模式，重启游戏，按 `F8` 打开 Steam Overlay。

## 生产构建

```powershell
cd E:\Documents\.NET\CollabCharting\ADOFAIWebBridge\src
pnpm build

cd E:\Documents\.NET\CollabCharting
dotnet build .\CollabCharting.csproj -c Release
```

构建会自动复制：

```text
CollabCharting/ADOFAIWebBridge/src/dist -> CollabCharting/out/webui/dist
CollabCharting/out      -> ADOFAI/Mods/CollabCharting
```

## 当前命令

前端当前会调用这些 C# 命令：

```text
collabCharting.getBridgeInfo
collabCharting.emitMessage
collabCharting.getStatus
collabCharting.echo
collabCharting.getSampleImage
```

`collabCharting.getSampleImage` 会通过 `ADOFAIWebBridge.ExposeBytes` 返回一个临时图片 URL，可用于验证本地资源暴露能力。

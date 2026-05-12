# Collab Charting

`Collab Charting` 是一个普通 ADOFAI/UMM 模组项目，用来测试和使用 `ADOFAIWebBridge`。

SDK 已经克隆在本项目内：

```text
E:\Documents\.NET\CollabCharting\ADOFAIWebBridge
```

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
```

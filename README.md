# Collab Charting

`Collab Charting` 是一个普通 ADOFAI/UMM 模组项目，用来测试和使用 `ADOFAIWebBridge`。

这个项目本身不放前端工程。前端在：

```text
E:\Documents\.NET\ADOFAIWebBridge\src
```

C# SDK 在：

```text
E:\Documents\.NET\ADOFAIWebBridge\src-cs\ADOFAIWebBridge
```

## 开发前端

```powershell
cd E:\Documents\.NET\ADOFAIWebBridge\src
pnpm install
pnpm dev
```

然后在 UMM 面板勾选开发模式，重启游戏，按 `F8` 打开 Steam Overlay。

## 生产构建

```powershell
cd E:\Documents\.NET\ADOFAIWebBridge\src
pnpm build

cd E:\Documents\.NET\CollabCharting
dotnet build .\CollabCharting.csproj -c Release
```

构建会自动复制：

```text
ADOFAIWebBridge/src/dist -> CollabCharting/out/webui/dist
CollabCharting/out      -> ADOFAI/Mods/CollabCharting
```

## 当前命令

前端当前会调用这些 C# 命令：

```text
bridge.getInfo
bridge.emitDemoEvent
project.getStatus
project.echo
```

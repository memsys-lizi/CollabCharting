# Collab Charting

`Collab Charting` 是一个 ADOFAI/UMM 多人制谱模组项目，用来测试和使用 `ADOFAIWebBridge`。

协作网络层已改为固定服务器模式：游戏客户端连接 Node.js WebSocket/HTTP Relay，服务器只负责登录、房间、成员状态、消息中转和资源暂存；谱面协作逻辑仍由房主客户端权威处理。

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

## 开发 Relay 服务器

```powershell
cd E:\Documents\.NET\CollabCharting\Server
pnpm install
Copy-Item .env.example .env
pnpm dev
```

Relay 服务器生产地址：

```text
https://collabcharting.adofaitools.top
wss://collabcharting.adofaitools.top/ws
```

本地开发时可以用环境变量覆盖为：

```text
http://127.0.0.1:39810
ws://127.0.0.1:39810/ws
```

ADOFAITools OAuth 配置通过 `Server\.env` 提供：

```text
ADOFAITOOLS_CLIENT_ID
ADOFAITOOLS_CLIENT_SECRET
ADOFAITOOLS_REDIRECT_URI
RELAY_TOKEN_SECRET
```

## 生产构建

```powershell
cd E:\Documents\.NET\CollabCharting\ADOFAIWebBridge\src
pnpm build

cd E:\Documents\.NET\CollabCharting\Server
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
collab.startAuth
collab.pollAuth
collab.loginWithToken
collab.getStatus
collab.createLobby
collab.joinLobby
collab.leaveLobby
collabCharting.listScenes
collabCharting.loadScene
```

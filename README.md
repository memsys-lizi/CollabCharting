# Collab Charting

`Collab Charting` 是一个 ADOFAI/UMM 多人制谱模组。协作网络层使用固定 Node.js WebSocket/HTTP Relay，服务器只负责 ADOFAITools 登录、房间、成员状态、消息中转和资源暂存；谱面协作逻辑仍由房主客户端权威处理。

## 使用方式

在游戏内打开 UnityModManager 面板，进入 `Collab Charting`：

1. 点击“登录 ADOFAITools”，浏览器会打开授权页面。
2. 授权完成后回到游戏，UMM 面板会自动刷新账号状态。
3. 房主点击“创建房间”，把房间码发给成员。
4. 成员在 UMM 面板输入房间码并点击“加入房间”。
5. 协作结束后点击“离开房间”，或离开编辑器时自动退出房间。

UMM 面板会显示服务器地址、账号状态、当前房间码、房主/成员、同步状态、最近事件和错误信息。

## Relay 服务器

当前生产服务器：

```text
https://collabcharting.adofaitools.top
wss://collabcharting.adofaitools.top/ws
```

本地开发 Relay：

```powershell
cd E:\Documents\.NET\CollabCharting\Server
pnpm install
Copy-Item .env.example .env
pnpm dev
```

ADOFAITools OAuth 配置通过 `Server\.env` 提供：

```text
ADOFAITOOLS_CLIENT_ID
ADOFAITOOLS_CLIENT_SECRET
ADOFAITOOLS_REDIRECT_URI
RELAY_TOKEN_SECRET
```

## 构建

```powershell
cd E:\Documents\.NET\CollabCharting\Server
pnpm install
pnpm build

cd E:\Documents\.NET\CollabCharting
dotnet build .\CollabCharting.csproj -c Release
```

Mod 构建输出会复制到 `out\`，并在配置允许时部署到游戏的 `Mods\CollabCharting\`。构建链路不再包含游戏内网页面板或本地浏览器桥接。

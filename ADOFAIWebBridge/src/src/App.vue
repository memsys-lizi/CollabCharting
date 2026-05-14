<template>
  <main class="shell">
    <header class="topbar">
      <div>
        <p class="eyebrow">Collab Charting</p>
        <h1>多人制谱</h1>
      </div>
      <span class="connection" :class="{ online: connected }">
        <Wifi v-if="connected" class="icon" />
        <WifiOff v-else class="icon" />
        {{ connected ? "Bridge 已连接" : "Bridge 未连接" }}
      </span>
    </header>

    <section class="workspace">
      <div class="column main-column">
        <section class="panel session-panel">
          <div class="session-heading">
            <div>
              <p class="panel-kicker">协作房间</p>
              <h2>{{ lobbyLabel }}</h2>
              <p>{{ roleLabel }}</p>
            </div>
            <button type="button" class="ghost icon-button" :disabled="busy" title="刷新" @click="refreshStatus">
              <RefreshCw class="icon" />
              刷新
            </button>
          </div>

          <div class="session-summary">
            <div>
              <Radio class="summary-icon" />
              <span>Steam</span>
              <strong :class="steamClass">
                {{ steamLabel }}
              </strong>
            </div>
            <div>
              <Gauge class="summary-icon" />
              <span>关卡</span>
              <strong>{{ status?.LevelName || "未打开" }}</strong>
            </div>
            <div>
              <Activity class="summary-icon" />
              <span>同步版本</span>
              <strong>r{{ status?.Revision ?? 0 }}</strong>
            </div>
          </div>

          <div class="actions">
            <button type="button" :disabled="busy || status?.InLobby" @click="createLobby">
              <Users class="icon" />
              创建房间
            </button>
            <button type="button" :disabled="busy || !status?.InLobby" @click="openInviteDialog">
              <UserPlus class="icon" />
              Steam 邀请
            </button>
            <button type="button" class="danger" :disabled="busy || !status?.InLobby" @click="leaveLobby">
              <DoorOpen class="icon" />
              离开
            </button>
          </div>

          <div class="join-row">
            <input v-model.trim="joinLobbyId" type="text" placeholder="Steam Lobby ID" />
            <button type="button" :disabled="busy || !joinLobbyId" @click="joinLobby">
              <LogIn class="icon" />
              加入
            </button>
          </div>

          <dl class="details compact-details">
            <div>
              <dt>本机</dt>
              <dd>{{ status?.LocalName || "未知" }} {{ status?.LocalSteamId ? `(${status.LocalSteamId})` : "" }}</dd>
            </div>
            <div>
              <dt>房主</dt>
              <dd>{{ hostLabel }}</dd>
            </div>
            <div>
              <dt>路径</dt>
              <dd>{{ status?.LevelPath || "未打开 .adofai" }}</dd>
            </div>
          </dl>
        </section>

        <section class="panel sync-panel">
          <div class="panel-header">
            <div>
              <h2>同步</h2>
              <p>{{ syncLabel }}</p>
            </div>
            <button type="button" class="ghost icon-button" :disabled="busy || !status?.InLobby" @click="forceSync">
              <HardDriveDownload class="icon" />
              同步状态
            </button>
          </div>

          <div class="progress">
            <div :style="{ width: `${syncPercent}%` }"></div>
          </div>

          <p v-if="status?.LastError" class="notice error">{{ status.LastError }}</p>
          <p v-else-if="message" class="notice">{{ message }}</p>
        </section>

        <section class="panel">
          <div class="panel-header">
            <div>
              <h2>成员</h2>
              <p>{{ status?.Members.length ?? 0 }} 人在线</p>
            </div>
          </div>

          <div class="member-list">
            <div v-for="member in status?.Members ?? []" :key="member.SteamId" class="member-row">
              <span class="avatar"><Crown v-if="member.IsHost" class="avatar-icon" />{{ initials(member.Name) }}</span>
              <span>
                <strong>{{ member.Name || member.SteamId }}</strong>
                <small>{{ member.IsHost ? "房主" : "成员" }}{{ member.IsLocal ? " / 我" : "" }}</small>
              </span>
            </div>
            <p v-if="!status?.Members.length" class="empty">暂无成员</p>
          </div>
        </section>
      </div>

      <aside class="column side-column">
        <section class="panel">
          <div class="panel-header">
            <div>
              <h2>好友</h2>
              <p>Steam 好友邀请</p>
            </div>
            <button type="button" class="ghost icon-button" :disabled="busy || !status?.SteamAvailable" @click="loadFriends">
              <RefreshCw class="icon" />
              读取
            </button>
          </div>

          <div class="friend-list">
            <button
              v-for="friend in friends"
              :key="friend.SteamId"
              type="button"
              class="friend-row"
              :disabled="busy || !status?.InLobby"
              @click="inviteFriend(friend.SteamId)"
            >
              <span>
                <strong>{{ friend.Name || friend.SteamId }}</strong>
                <small>{{ friend.State }}</small>
              </span>
              <span class="invite-label"><Send class="icon" />邀请</span>
            </button>
            <p v-if="!friends.length" class="empty">未读取好友</p>
          </div>
        </section>

        <section class="panel">
          <div class="panel-header">
            <div>
              <h2>最近事件</h2>
              <p>协作日志</p>
            </div>
          </div>

          <ol class="event-list">
            <li v-for="event in status?.RecentEvents ?? []" :key="event">{{ event }}</li>
            <li v-if="!status?.RecentEvents.length" class="empty">暂无事件</li>
          </ol>
        </section>
      </aside>
    </section>
  </main>
</template>

<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref } from "vue";
import {
  Crown,
  DoorOpen,
  Gauge,
  HardDriveDownload,
  Activity,
  LogIn,
  Radio,
  RefreshCw,
  Send,
  UserPlus,
  Users,
  Wifi,
  WifiOff
} from "lucide-vue-next";
import { createBridgeClient } from "./bridge";

type CollabMember = {
  SteamId: string;
  Name: string;
  IsHost: boolean;
  IsLocal: boolean;
};

type CollabFriend = {
  SteamId: string;
  Name: string;
  State: string;
};

type CollabStatus = {
  SteamAvailable: boolean;
  LocalSteamId: string;
  LocalName: string;
  InLobby: boolean;
  IsHost: boolean;
  LobbyId: string;
  HostSteamId: string;
  LevelName: string;
  LevelPath: string;
  Revision: number;
  SyncState: string;
  SyncProgress: number;
  LastError: string;
  Members: CollabMember[];
  RecentEvents: string[];
};

const bridge = createBridgeClient();
const connected = ref(false);
const busy = ref(false);
const status = ref<CollabStatus | null>(null);
const friends = ref<CollabFriend[]>([]);
const joinLobbyId = ref("");
const message = ref("");
const steamChecking = ref(true);
let stopStatusListener: (() => void) | undefined;
let statusRefreshTimer: number | undefined;

const lobbyLabel = computed(() => {
  if (!status.value?.InLobby) {
    return "未加入";
  }

  return status.value.LobbyId || "已加入";
});

const roleLabel = computed(() => {
  if (!status.value?.InLobby) {
    return "先打开一个关卡，然后创建或加入房间";
  }

  return status.value.IsHost ? "房主权威状态" : "成员同步状态";
});

const steamLabel = computed(() => {
  if (status.value?.SteamAvailable) {
    return "可用";
  }

  return steamChecking.value ? "检测中" : "不可用";
});

const steamClass = computed(() => {
  if (status.value?.SteamAvailable) {
    return "good";
  }

  return steamChecking.value ? "pending" : "bad";
});

const hostLabel = computed(() => {
  if (!status.value?.HostSteamId) {
    return "无";
  }

  const host = status.value.Members.find(member => member.SteamId === status.value?.HostSteamId);
  return host ? `${host.Name} (${host.SteamId})` : status.value.HostSteamId;
});

const syncPercent = computed(() => {
  const value = Math.round((status.value?.SyncProgress ?? 0) * 100);
  return Math.min(100, Math.max(0, value));
});

const syncLabel = computed(() => {
  const State = status.value?.SyncState ?? "idle";
  const labels: Record<string, string> = {
    idle: "空闲",
    creating: "创建房间中",
    joining: "加入房间中",
    hosting: "房主待命",
    syncing: "同步资源中",
    sending: "发送资源中",
    synced: "已同步",
    error: "同步错误"
  };

  return `${labels[State] ?? State} / ${syncPercent.value}%`;
});

async function connect() {
  try {
    await bridge.connect();
    connected.value = true;
    stopStatusListener = bridge.listen<CollabStatus>("collab.status", data => {
      status.value = data;
      if (data.SteamAvailable) {
        steamChecking.value = false;
      }
    });
    await refreshStatus();
    scheduleWarmStatusRefresh();
  } catch (reason) {
    connected.value = false;
    message.value = formatError(reason);
  }
}

function scheduleWarmStatusRefresh() {
  steamChecking.value = !status.value?.SteamAvailable;
  window.setTimeout(refreshStatusQuietly, 150);
  window.setTimeout(refreshStatusQuietly, 350);
  window.setTimeout(refreshStatusQuietly, 700);
  window.setTimeout(refreshStatusQuietly, 1200);
  window.setTimeout(refreshStatusQuietly, 2200);
  window.setTimeout(() => {
    if (!status.value?.SteamAvailable) {
      steamChecking.value = false;
    }
  }, 3200);
  window.clearInterval(statusRefreshTimer);
  statusRefreshTimer = window.setInterval(refreshStatusQuietly, 5000);
}

async function withBusy(action: () => Promise<void>) {
  busy.value = true;
  message.value = "";
  try {
    if (!connected.value) {
      await connect();
    }

    await action();
  } catch (reason) {
    message.value = formatError(reason);
  } finally {
    busy.value = false;
  }
}

async function refreshStatus() {
  status.value = await bridge.invoke<CollabStatus>("collab.getStatus");
  if (status.value.SteamAvailable) {
    steamChecking.value = false;
  }
}

async function refreshStatusQuietly() {
  try {
    if (connected.value) {
      await refreshStatus();
    }
  } catch {
    // A transient overlay bridge refresh should not replace user-facing messages.
  }
}

function createLobby() {
  void withBusy(async () => {
    if (!status.value?.LevelPath) {
      message.value = "请先在编辑器中打开或保存一个 .adofai 关卡，再创建协作房间。";
      return;
    }

    status.value = await bridge.invoke<CollabStatus>("collab.createLobby");
  });
}

function joinLobby() {
  void withBusy(async () => {
    status.value = await bridge.invoke<CollabStatus>("collab.joinLobby", { lobbyId: joinLobbyId.value });
  });
}

function leaveLobby() {
  void withBusy(async () => {
    status.value = await bridge.invoke<CollabStatus>("collab.leaveLobby");
  });
}

function openInviteDialog() {
  void withBusy(async () => {
    await bridge.invoke("collab.openInviteDialog");
    await refreshStatus();
  });
}

function forceSync() {
  void withBusy(async () => {
    status.value = await bridge.invoke<CollabStatus>("collab.forceSync");
  });
}

function loadFriends() {
  void withBusy(async () => {
    friends.value = await bridge.invoke<CollabFriend[]>("collab.getFriends");
  });
}

function inviteFriend(SteamId: string) {
  void withBusy(async () => {
    await bridge.invoke("collab.inviteFriend", { steamId: SteamId });
    message.value = "邀请已发送";
    await refreshStatus();
  });
}

function initials(Name: string) {
  const clean = (Name || "?").trim();
  return clean.slice(0, 2).toUpperCase();
}

function formatError(reason: unknown) {
  const message = reason instanceof Error ? reason.message : String(reason);
  if (!message || message === "Bridge RPC failed." || message === "Bridge RPC failed") {
    return "操作失败，请确认游戏编辑器和协作 Mod 正常运行。";
  }

  if (message.includes("当前编辑器没有打开可同步") || message.includes("请先在编辑器中打开或保存")) {
    return "请先在编辑器中打开或保存一个 .adofai 关卡。";
  }

  if (message.includes("Steamworks 尚未初始化")) {
    return "Steam 尚未初始化，请确认通过 Steam 启动游戏。";
  }

  if (message.includes("Bridge WebSocket")) {
    return "WebUI 尚未连接到游戏内桥接服务，请关闭后从 Mod 入口重新打开。";
  }

  return message;
}

onMounted(connect);

onUnmounted(() => {
  window.clearInterval(statusRefreshTimer);
  stopStatusListener?.();
  bridge.disconnect();
});
</script>

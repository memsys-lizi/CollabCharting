<template>
  <main class="shell">
    <section class="panel">
      <div class="header">
        <div>
          <p class="eyebrow">Collab Charting</p>
          <h1>场景跳转</h1>
        </div>
        <span class="status" :class="{ connected }">{{ connected ? "已连接" : "未连接" }}</span>
      </div>

      <p class="subtitle">当前场景：{{ activeSceneLabel }}</p>

      <div class="toolbar">
        <button type="button" :disabled="loading" @click="refreshScenes">
          {{ loading ? "读取中..." : "刷新场景列表" }}
        </button>
      </div>

      <p v-if="message" class="message">{{ message }}</p>
      <p v-if="error" class="error">{{ error }}</p>

      <div class="scene-list">
        <button
          v-for="scene in scenes"
          :key="scene.buildIndex"
          type="button"
          class="scene-button"
          :class="{ active: scene.active }"
          @click="loadScene(scene)"
        >
          <span class="scene-index">#{{ scene.buildIndex }}</span>
          <span class="scene-main">
            <strong>{{ scene.name || "(未命名场景)" }}</strong>
            <small>{{ scene.path }}</small>
          </span>
          <span v-if="scene.active" class="scene-active">当前</span>
        </button>
      </div>
    </section>
  </main>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { createBridgeClient } from "./bridge";

type SceneInfo = {
  buildIndex: number;
  name: string;
  path: string;
  active: boolean;
};

type SceneListResult = {
  active: {
    buildIndex: number;
    name: string;
    path: string;
  };
  scenes: SceneInfo[];
};

type LoadSceneResult = {
  ok: boolean;
  buildIndex: number;
  name: string;
  path: string;
};

const bridge = createBridgeClient();
const connected = ref(false);
const loading = ref(false);
const scenes = ref<SceneInfo[]>([]);
const activeScene = ref<SceneListResult["active"] | null>(null);
const message = ref("");
const error = ref("");

const activeSceneLabel = computed(() => {
  if (!activeScene.value) {
    return "尚未读取";
  }

  const index = activeScene.value.buildIndex >= 0 ? `#${activeScene.value.buildIndex} ` : "";
  return `${index}${activeScene.value.name || "(未命名场景)"}`;
});

async function connect() {
  try {
    await bridge.connect();
    connected.value = true;
    error.value = "";
  } catch (reason) {
    connected.value = false;
    error.value = formatError(reason);
  }
}

async function refreshScenes() {
  loading.value = true;
  message.value = "";
  error.value = "";

  try {
    if (!connected.value) {
      await connect();
    }

    const result = await bridge.invoke<SceneListResult>("collabCharting.listScenes");
    activeScene.value = result.active;
    scenes.value = result.scenes;
  } catch (reason) {
    error.value = formatError(reason);
  } finally {
    loading.value = false;
  }
}

async function loadScene(scene: SceneInfo) {
  loading.value = true;
  message.value = "";
  error.value = "";

  try {
    const result = await bridge.invoke<LoadSceneResult>("collabCharting.loadScene", {
      buildIndex: scene.buildIndex
    });
    message.value = `正在跳转到 #${result.buildIndex} ${result.name}`;
    await refreshScenes();
  } catch (reason) {
    error.value = formatError(reason);
  } finally {
    loading.value = false;
  }
}

function formatError(reason: unknown) {
  return reason instanceof Error ? reason.message : String(reason);
}

onMounted(async () => {
  await connect();
  await refreshScenes();
});
</script>

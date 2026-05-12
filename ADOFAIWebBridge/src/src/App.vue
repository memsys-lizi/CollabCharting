<template>
  <main class="shell">
    <section class="panel">
      <p class="eyebrow">Collab Charting</p>
      <h1>多人制谱示例</h1>
      <p class="subtitle">输入名字，前端会调用 C#，并显示 C# 返回的问候语。</p>

      <form class="form" @submit.prevent="sayHello">
        <label>
          <span>你的名字</span>
          <input v-model="name" autocomplete="name" placeholder="例如：LK130" />
        </label>
        <button type="submit" :disabled="loading">{{ loading ? "发送中..." : "发送给 C#" }}</button>
      </form>

      <p class="status" :class="{ connected }">{{ connected ? "Bridge 已连接" : "Bridge 未连接" }}</p>
      <p class="result">{{ result || "C# 返回结果会显示在这里" }}</p>
      <p v-if="error" class="error">{{ error }}</p>
    </section>
  </main>
</template>

<script setup lang="ts">
import { onMounted, ref } from "vue";
import { createBridgeClient } from "./bridge";

const bridge = createBridgeClient();
const connected = ref(false);
const loading = ref(false);
const name = ref("");
const result = ref("");
const error = ref("");

type HelloResult = {
  message: string;
};

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

async function sayHello() {
  loading.value = true;
  error.value = "";

  try {
    if (!connected.value) {
      await connect();
    }

    const response = await bridge.invoke<HelloResult>("collabCharting.sayHello", {
      name: name.value
    });
    result.value = response.message;
  } catch (reason) {
    error.value = formatError(reason);
  } finally {
    loading.value = false;
  }
}

function formatError(reason: unknown) {
  return reason instanceof Error ? reason.message : String(reason);
}

onMounted(connect);
</script>

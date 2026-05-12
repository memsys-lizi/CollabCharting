<template>
  <main class="shell">
    <section class="hero">
      <div>
        <p class="eyebrow">ADOFAIWebBridge</p>
        <h1>Bridge Console</h1>
        <p class="subtitle">通用 WebUI 模板。命令名和参数由使用方模组自己定义。</p>
      </div>
      <span class="status" :class="{ connected }">{{ connected ? "已连接" : "未连接" }}</span>
    </section>

    <section class="toolbar">
      <button type="button" @click="connect">重新连接</button>
      <button type="button" @click="disconnect">断开连接</button>
    </section>

    <section class="panel">
      <h2>调用 C# 命令</h2>
      <label>
        <span>命令名</span>
        <input v-model="method" placeholder="yourMod.commandName" />
      </label>
      <label>
        <span>参数 JSON</span>
        <textarea v-model="paramsText" spellcheck="false" />
      </label>
      <button type="button" @click="invokeCommand">发送</button>
      <pre>{{ result || "返回值会显示在这里" }}</pre>
    </section>

    <section class="panel">
      <h2>监听事件</h2>
      <div class="input-row">
        <input v-model="eventName" placeholder="yourMod.eventName" />
        <button type="button" @click="listenEvent">开始监听</button>
      </div>
      <ul>
        <li v-for="item in events" :key="item">{{ item }}</li>
      </ul>
    </section>
  </main>
</template>

<script setup lang="ts">
import { onMounted, ref } from "vue";
import { createBridgeClient } from "./bridge";

const bridge = createBridgeClient();
const connected = ref(false);
const method = ref("collabCharting.getStatus");
const paramsText = ref("{}");
const eventName = ref("collabCharting.message");
const result = ref("");
const events = ref<string[]>([]);
let stopListening: (() => void) | null = null;

async function connect() {
  try {
    await bridge.connect();
    connected.value = true;
    addEvent("Bridge connected.");
  } catch (error) {
    connected.value = false;
    addEvent(`Connect failed: ${formatError(error)}`);
  }
}

function disconnect() {
  bridge.disconnect();
  connected.value = false;
  addEvent("Bridge disconnected.");
}

async function invokeCommand() {
  try {
    const params = paramsText.value.trim() ? JSON.parse(paramsText.value) : undefined;
    const value = await bridge.invoke(method.value.trim(), params);
    result.value = JSON.stringify(value, null, 2);
  } catch (error) {
    result.value = formatError(error);
  }
}

function listenEvent() {
  stopListening?.();
  const name = eventName.value.trim();
  stopListening = bridge.listen(name, data => addEvent(`${name}: ${JSON.stringify(data)}`));
  addEvent(`Listening: ${name}`);
}

function addEvent(message: string) {
  events.value = [`${new Date().toLocaleTimeString()} ${message}`, ...events.value].slice(0, 20);
}

function formatError(error: unknown) {
  return error instanceof Error ? error.message : String(error);
}

onMounted(async () => {
  await connect();
  listenEvent();
});
</script>

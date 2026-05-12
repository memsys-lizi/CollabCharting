import { defineConfig, loadEnv } from "vite";
import vue from "@vitejs/plugin-vue";

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), "");
  const bridgeHost = env.VITE_BRIDGE_HOST || "127.0.0.1";
  const bridgePort = env.VITE_BRIDGE_PORT || "39800";

  return {
    plugins: [vue()],
    server: {
      host: "127.0.0.1",
      port: 5173,
      strictPort: true,
      proxy: {
        "/rpc": {
          target: `ws://${bridgeHost}:${bridgePort}`,
          ws: true
        }
      }
    }
  };
});

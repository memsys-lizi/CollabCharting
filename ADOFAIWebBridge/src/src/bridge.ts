type RpcId = number;

type RpcResponse = {
  id: RpcId;
  result?: unknown;
  error?: {
    code?: string;
    message?: string;
  };
};

type RpcEvent = {
  event: string;
  data?: unknown;
};

type PendingCall = {
  resolve: (value: unknown) => void;
  reject: (reason?: unknown) => void;
};

export type BridgeClientOptions = {
  url?: string;
  token?: string;
};

export function createBridgeClient(options: BridgeClientOptions = {}) {
  let socket: WebSocket | null = null;
  let nextId = 1;
  const pending = new Map<RpcId, PendingCall>();
  const listeners = new Map<string, Set<(data: unknown) => void>>();

  function getUrl() {
    if (options.url) {
      return options.url;
    }

    const protocol = location.protocol === "https:" ? "wss:" : "ws:";
    const token = options.token ?? new URLSearchParams(location.search).get("bridgeToken") ?? "";
    const tokenQuery = token ? `?token=${encodeURIComponent(token)}` : "";
    return `${protocol}//${location.host}/rpc${tokenQuery}`;
  }

  function connect() {
    if (socket?.readyState === WebSocket.OPEN) {
      return Promise.resolve();
    }

    socket?.close();

    return new Promise<void>((resolve, reject) => {
      const ws = new WebSocket(getUrl());
      socket = ws;

      ws.onopen = () => resolve();
      ws.onerror = () => reject(new Error("Bridge WebSocket connection failed."));
      ws.onclose = () => {
        for (const call of pending.values()) {
          call.reject(new Error("Bridge WebSocket disconnected."));
        }

        pending.clear();
      };
      ws.onmessage = event => handleMessage(event.data);
    });
  }

  function invoke<T>(method: string, params?: unknown): Promise<T> {
    if (!socket || socket.readyState !== WebSocket.OPEN) {
      return Promise.reject(new Error("Bridge is not connected."));
    }

    const id = nextId++;
    socket.send(JSON.stringify({ id, method, params }));

    return new Promise<T>((resolve, reject) => {
      pending.set(id, {
        resolve: value => resolve(value as T),
        reject
      });
    });
  }

  function listen<T>(eventName: string, callback: (data: T) => void) {
    let callbacks = listeners.get(eventName);
    if (!callbacks) {
      callbacks = new Set();
      listeners.set(eventName, callbacks);
    }

    callbacks.add(callback as (data: unknown) => void);
    return () => callbacks?.delete(callback as (data: unknown) => void);
  }

  function disconnect() {
    socket?.close();
    socket = null;
  }

  function handleMessage(raw: string) {
    const message = JSON.parse(raw) as RpcResponse | RpcEvent;

    if ("id" in message) {
      const call = pending.get(message.id);
      if (!call) {
        return;
      }

      pending.delete(message.id);
      if (message.error) {
        call.reject(new Error(message.error.message ?? message.error.code ?? "Bridge RPC failed."));
      } else {
        call.resolve(message.result);
      }

      return;
    }

    const callbacks = listeners.get(message.event);
    callbacks?.forEach(callback => callback(message.data));
  }

  return {
    connect,
    invoke,
    listen,
    disconnect
  };
}

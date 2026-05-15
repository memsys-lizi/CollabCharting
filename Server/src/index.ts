import cors from "@fastify/cors";
import websocket from "@fastify/websocket";
import Fastify from "fastify";
import { z } from "zod";
import { createLoginSession, completeLogin, pollLogin } from "./auth.js";
import { config } from "./config.js";
import { loadResource, saveResource } from "./resources.js";
import {
  createRoom,
  joinRoom,
  leaveRoomBySocket,
  registerClient,
  relayBroadcast,
  relayToHost,
  relayToUser,
  removeClient
} from "./rooms.js";
import { verifyRelayToken } from "./tokens.js";
import type { RawData } from "ws";
import type { RelayPayload } from "./types.js";

const app = Fastify({ logger: true });

await app.register(cors, { origin: true });
await app.register(websocket);
app.addContentTypeParser("application/octet-stream", { parseAs: "buffer" }, (_request, body, done) => {
  done(null, body);
});

const wsMessageSchema = z.discriminatedUnion("type", [
  z.object({ type: z.literal("room.create") }),
  z.object({ type: z.literal("room.join"), roomId: z.string().min(1) }),
  z.object({ type: z.literal("room.leave") }),
  z.object({
    type: z.literal("relay.toHost"),
    payload: z.object({ type: z.string(), revision: z.number().optional(), payload: z.unknown().optional() })
  }),
  z.object({
    type: z.literal("relay.toUser"),
    targetUserId: z.string(),
    payload: z.object({ type: z.string(), revision: z.number().optional(), payload: z.unknown().optional() })
  }),
  z.object({
    type: z.literal("relay.broadcast"),
    payload: z.object({ type: z.string(), revision: z.number().optional(), payload: z.unknown().optional() })
  })
]);

function tokenFromRequest(request: { headers: Record<string, string | string[] | undefined>; query: unknown }) {
  const auth = request.headers.authorization;
  if (typeof auth === "string" && auth.startsWith("Bearer ")) {
    return auth.substring("Bearer ".length);
  }

  const query = request.query as { token?: string };
  return query.token ?? "";
}

app.get("/api/health", async () => ({ ok: true }));

app.get("/api/auth/start", async () => createLoginSession());

app.get("/api/auth/callback", async (request, reply) => {
  const query = z.object({ code: z.string(), state: z.string() }).parse(request.query);
  await completeLogin(query.code, query.state);
  reply.type("text/html; charset=utf-8").send("<!doctype html><meta charset=\"utf-8\"><title>登录完成</title><p>登录完成，可以回到协作面板。</p>");
});

app.get("/api/auth/poll", async (request) => {
  const query = z.object({ loginId: z.string() }).parse(request.query);
  return pollLogin(query.loginId);
});

app.put("/api/rooms/:roomId/resources/:sha256", async (request, reply) => {
  const user = verifyRelayToken(tokenFromRequest(request));
  if (!user) {
    reply.code(401).send({ message: "未登录。" });
    return;
  }

  const params = z.object({ roomId: z.string(), sha256: z.string() }).parse(request.params);
  const bytes = await request.body;
  if (!Buffer.isBuffer(bytes)) {
    reply.code(400).send({ message: "请求体必须是二进制资源。" });
    return;
  }

  await saveResource(params.roomId, params.sha256, bytes);
  return { ok: true };
});

app.get("/api/rooms/:roomId/resources/:sha256", async (request, reply) => {
  const user = verifyRelayToken(tokenFromRequest(request));
  if (!user) {
    reply.code(401).send({ message: "未登录。" });
    return;
  }

  const params = z.object({ roomId: z.string(), sha256: z.string() }).parse(request.params);
  const bytes = await loadResource(params.roomId, params.sha256);
  reply.type("application/octet-stream").send(bytes);
});

app.get("/ws", { websocket: true }, (socket, request) => {
  const user = verifyRelayToken(tokenFromRequest(request));
  if (!user) {
    socket.close(1008, "未登录");
    return;
  }

  registerClient(socket, user);
  socket.on("message", (raw: RawData) => {
    try {
      const message = wsMessageSchema.parse(JSON.parse(raw.toString()));
      switch (message.type) {
        case "room.create":
          createRoom(socket);
          break;
        case "room.join":
          joinRoom(socket, message.roomId);
          break;
        case "room.leave":
          leaveRoomBySocket(socket);
          break;
        case "relay.toHost":
          relayToHost(socket, message.payload as RelayPayload);
          break;
        case "relay.toUser":
          relayToUser(socket, message.targetUserId, message.payload as RelayPayload);
          break;
        case "relay.broadcast":
          relayBroadcast(socket, message.payload as RelayPayload);
          break;
      }
    } catch (error) {
      socket.send(JSON.stringify({
        type: "error",
        payload: { message: error instanceof Error ? error.message : String(error) }
      }));
    }
  });
  socket.on("close", () => removeClient(socket));
});

await app.listen({ port: config.port, host: "0.0.0.0" });

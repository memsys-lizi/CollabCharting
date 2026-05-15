import type WebSocket from "ws";
import { nanoid } from "nanoid";
import type { RelayPayload, RoomMember, RoomState, ServerEvent, UserInfo } from "./types.js";

type Client = {
  socket: WebSocket;
  user: UserInfo;
  roomId?: string;
};

type Room = {
  roomId: string;
  hostUserId: string;
  members: Map<string, Client>;
};

const rooms = new Map<string, Room>();
const clients = new Map<WebSocket, Client>();

function send(client: Client, event: ServerEvent) {
  if (client.socket.readyState !== client.socket.OPEN) {
    return;
  }

  client.socket.send(JSON.stringify(event));
}

function roomState(room: Room): RoomState {
  const members: RoomMember[] = [];
  for (const member of room.members.values()) {
    members.push({
      userId: member.user.userId,
      name: member.user.nickname || member.user.username || member.user.userId,
      isHost: member.user.userId === room.hostUserId
    });
  }

  return {
    roomId: room.roomId,
    hostUserId: room.hostUserId,
    members
  };
}

function broadcastState(room: Room) {
  const state = roomState(room);
  for (const member of room.members.values()) {
    send(member, { type: "room.state", payload: state });
  }
}

function getClient(socket: WebSocket) {
  const client = clients.get(socket);
  if (!client) {
    throw new Error("连接尚未认证。");
  }

  return client;
}

function getRoom(client: Client) {
  if (!client.roomId) {
    throw new Error("当前不在协作房间中。");
  }

  const room = rooms.get(client.roomId);
  if (!room) {
    client.roomId = undefined;
    throw new Error("协作房间不存在。");
  }

  return room;
}

export function registerClient(socket: WebSocket, user: UserInfo) {
  clients.set(socket, { socket, user });
}

export function removeClient(socket: WebSocket) {
  const client = clients.get(socket);
  if (!client) {
    return;
  }

  leaveRoom(client, "成员已断开连接。");
  clients.delete(socket);
}

export function createRoom(socket: WebSocket) {
  const client = getClient(socket);
  leaveRoom(client, "成员已离开房间。");

  const room: Room = {
    roomId: nanoid(8).toUpperCase(),
    hostUserId: client.user.userId,
    members: new Map([[client.user.userId, client]])
  };
  client.roomId = room.roomId;
  rooms.set(room.roomId, room);
  broadcastState(room);
}

export function joinRoom(socket: WebSocket, roomId: string) {
  const client = getClient(socket);
  const normalizedRoomId = roomId.trim().toUpperCase();
  const room = rooms.get(normalizedRoomId);
  if (!room) {
    throw new Error("协作房间不存在。");
  }

  leaveRoom(client, "成员已离开房间。");
  room.members.set(client.user.userId, client);
  client.roomId = room.roomId;
  broadcastState(room);
}

export function leaveRoom(client: Client, reason: string) {
  if (!client.roomId) {
    return;
  }

  const room = rooms.get(client.roomId);
  client.roomId = undefined;
  if (!room) {
    return;
  }

  room.members.delete(client.user.userId);
  if (client.user.userId === room.hostUserId) {
    for (const member of room.members.values()) {
      member.roomId = undefined;
      send(member, { type: "room.closed", payload: { reason: "房主已离开，协作结束。" } });
    }

    rooms.delete(room.roomId);
    return;
  }

  if (room.members.size === 0) {
    rooms.delete(room.roomId);
    return;
  }

  broadcastState(room);
}

export function leaveRoomBySocket(socket: WebSocket) {
  leaveRoom(getClient(socket), "成员已离开房间。");
}

export function relayToHost(socket: WebSocket, payload: RelayPayload) {
  const client = getClient(socket);
  const room = getRoom(client);
  const host = room.members.get(room.hostUserId);
  if (!host) {
    throw new Error("房主不在线。");
  }

  if (host.user.userId === client.user.userId) {
    return;
  }

  send(host, { type: "relay", senderUserId: client.user.userId, payload });
}

export function relayToUser(socket: WebSocket, targetUserId: string, payload: RelayPayload) {
  const client = getClient(socket);
  const room = getRoom(client);
  if (client.user.userId !== room.hostUserId) {
    throw new Error("只有房主可以向指定成员发送协作消息。");
  }

  const target = room.members.get(targetUserId);
  if (target) {
    send(target, { type: "relay", senderUserId: client.user.userId, payload });
  }
}

export function relayBroadcast(socket: WebSocket, payload: RelayPayload) {
  const client = getClient(socket);
  const room = getRoom(client);
  for (const member of room.members.values()) {
    if (member.user.userId === client.user.userId) {
      continue;
    }

    send(member, { type: "relay", senderUserId: client.user.userId, payload });
  }
}

export function roomExists(roomId: string) {
  return rooms.has(roomId.trim().toUpperCase());
}

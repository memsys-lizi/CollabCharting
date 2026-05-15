export type UserInfo = {
  userId: string;
  username: string;
  nickname: string;
  avatarUrl: string;
  email: string;
};

export type RoomMember = {
  userId: string;
  name: string;
  isHost: boolean;
};

export type RoomState = {
  roomId: string;
  hostUserId: string;
  members: RoomMember[];
};

export type RelayPayload = {
  type: string;
  revision?: number;
  payload?: unknown;
};

export type ServerEvent =
  | { type: "room.state"; payload: RoomState }
  | { type: "room.closed"; payload: { reason: string } }
  | { type: "relay"; senderUserId: string; payload: RelayPayload }
  | { type: "error"; payload: { message: string } };

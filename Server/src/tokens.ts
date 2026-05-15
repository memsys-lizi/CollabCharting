import { createHmac, timingSafeEqual } from "node:crypto";
import { config } from "./config.js";
import type { UserInfo } from "./types.js";

type TokenBody = {
  exp: number;
  user: UserInfo;
};

function base64url(input: Buffer | string) {
  return Buffer.from(input).toString("base64url");
}

export function issueRelayToken(user: UserInfo, ttlSeconds = 60 * 60 * 24) {
  const body: TokenBody = {
    exp: Math.floor(Date.now() / 1000) + ttlSeconds,
    user
  };
  const payload = base64url(JSON.stringify(body));
  const signature = createHmac("sha256", config.relayTokenSecret)
    .update(payload)
    .digest("base64url");
  return `${payload}.${signature}`;
}

export function verifyRelayToken(token: string): UserInfo | null {
  const [payload, signature] = token.split(".");
  if (!payload || !signature) {
    return null;
  }

  const expected = createHmac("sha256", config.relayTokenSecret)
    .update(payload)
    .digest("base64url");
  const actualBuffer = Buffer.from(signature);
  const expectedBuffer = Buffer.from(expected);
  if (
    actualBuffer.length !== expectedBuffer.length ||
    !timingSafeEqual(actualBuffer, expectedBuffer)
  ) {
    return null;
  }

  const body = JSON.parse(Buffer.from(payload, "base64url").toString("utf8")) as TokenBody;
  if (!body.user?.userId || body.exp < Math.floor(Date.now() / 1000)) {
    return null;
  }

  return body.user;
}

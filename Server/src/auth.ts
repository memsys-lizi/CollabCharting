import { createHash, randomBytes } from "node:crypto";
import { nanoid } from "nanoid";
import { z } from "zod";
import { config } from "./config.js";
import { issueRelayToken } from "./tokens.js";
import type { UserInfo } from "./types.js";

type LoginSession = {
  loginId: string;
  state: string;
  codeVerifier: string;
  createdAt: number;
  user?: UserInfo;
  relayToken?: string;
  error?: string;
};

const sessions = new Map<string, LoginSession>();

const userinfoSchema = z.object({
  sub: z.string(),
  username: z.string().optional().default(""),
  nickname: z.string().optional().default(""),
  avatar_url: z.string().optional().default(""),
  picture: z.string().optional().default(""),
  email: z.string().optional().default("")
});

function base64url(bytes: Buffer) {
  return bytes.toString("base64url");
}

function buildAuthorizationUrl(params: URLSearchParams) {
  const base = config.oauth.authorizeUrl;
  const separator = base.includes("?") ? "&" : "?";
  return `${base}${separator}${params.toString()}`;
}

function pruneSessions() {
  const cutoff = Date.now() - 10 * 60 * 1000;
  for (const [id, session] of sessions) {
    if (session.createdAt < cutoff) {
      sessions.delete(id);
    }
  }
}

export function createLoginSession() {
  if (!config.oauth.clientId) {
    throw new Error("服务器未配置 ADOFAITOOLS_CLIENT_ID。");
  }

  pruneSessions();
  const loginId = nanoid(16);
  const state = nanoid(32);
  const codeVerifier = base64url(randomBytes(48));
  const codeChallenge = base64url(createHash("sha256").update(codeVerifier).digest());
  sessions.set(loginId, {
    loginId,
    state,
    codeVerifier,
    createdAt: Date.now()
  });

  const params = new URLSearchParams({
    response_type: "code",
    client_id: config.oauth.clientId,
    redirect_uri: config.oauth.redirectUri,
    scope: "openid profile email",
    state: `${loginId}.${state}`,
    code_challenge: codeChallenge,
    code_challenge_method: "S256"
  });

  return { loginId, authorizationUrl: buildAuthorizationUrl(params) };
}

export async function completeLogin(code: string, packedState: string) {
  const [loginId, state] = packedState.split(".");
  const session = loginId ? sessions.get(loginId) : undefined;
  if (!session || session.state !== state) {
    throw new Error("OAuth state 无效或已过期。");
  }

  try {
    const tokenResponse = await fetch(config.oauth.tokenUrl, {
      method: "POST",
      headers: { "content-type": "application/x-www-form-urlencoded" },
      body: new URLSearchParams({
        grant_type: "authorization_code",
        client_id: config.oauth.clientId,
        client_secret: config.oauth.clientSecret,
        code,
        redirect_uri: config.oauth.redirectUri,
        code_verifier: session.codeVerifier
      })
    });
    if (!tokenResponse.ok) {
      throw new Error(`换取 token 失败：${tokenResponse.status}`);
    }

    const tokenJson = (await tokenResponse.json()) as { access_token?: string };
    if (!tokenJson.access_token) {
      throw new Error("ADOFAITools 未返回 access_token。");
    }

    const userResponse = await fetch(config.oauth.userinfoUrl, {
      headers: { authorization: `Bearer ${tokenJson.access_token}` }
    });
    if (!userResponse.ok) {
      throw new Error(`获取用户信息失败：${userResponse.status}`);
    }

    const rawUser = userinfoSchema.parse(await userResponse.json());
    const user: UserInfo = {
      userId: rawUser.sub,
      username: rawUser.username,
      nickname: rawUser.nickname || rawUser.username || rawUser.sub,
      avatarUrl: rawUser.avatar_url || rawUser.picture,
      email: rawUser.email
    };

    session.user = user;
    session.relayToken = issueRelayToken(user);
    return session;
  } catch (error) {
    session.error = error instanceof Error ? error.message : String(error);
    throw error;
  }
}

export function pollLogin(loginId: string) {
  pruneSessions();
  const session = sessions.get(loginId);
  if (!session) {
    return { status: "expired" as const };
  }

  if (session.error) {
    return { status: "error" as const, message: session.error };
  }

  if (!session.user || !session.relayToken) {
    return { status: "pending" as const };
  }

  return {
    status: "ok" as const,
    relayToken: session.relayToken,
    user: session.user
  };
}

import { existsSync } from "node:fs";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { config as loadEnv } from "dotenv";

const moduleDir = dirname(fileURLToPath(import.meta.url));
const envCandidates = [
  resolve(moduleDir, "..", ".env"),
  resolve(process.cwd(), ".env")
];

for (const envPath of envCandidates) {
  if (existsSync(envPath)) {
    loadEnv({ path: envPath, quiet: true });
    break;
  }
}

export const config = {
  port: Number(process.env.PORT ?? 39810),
  publicBaseUrl: process.env.PUBLIC_BASE_URL ?? "https://collabcharting.adofaitools.top",
  relayTokenSecret: process.env.RELAY_TOKEN_SECRET ?? "dev-relay-token-secret",
  oauth: {
    clientId: process.env.ADOFAITOOLS_CLIENT_ID ?? "",
    clientSecret: process.env.ADOFAITOOLS_CLIENT_SECRET ?? "",
    redirectUri:
      process.env.ADOFAITOOLS_REDIRECT_URI ??
      "https://collabcharting.adofaitools.top/api/auth/callback",
    authorizeUrl: "https://www.adofaitools.top/#/oauth/authorize",
    tokenUrl: "https://server.adofaitools.top/oauth/token",
    userinfoUrl: "https://server.adofaitools.top/oauth/userinfo"
  }
};

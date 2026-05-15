export const config = {
  port: Number(process.env.PORT ?? 39810),
  publicBaseUrl: process.env.PUBLIC_BASE_URL ?? "http://127.0.0.1:39810",
  relayTokenSecret: process.env.RELAY_TOKEN_SECRET ?? "dev-relay-token-secret",
  oauth: {
    clientId: process.env.ADOFAITOOLS_CLIENT_ID ?? "",
    clientSecret: process.env.ADOFAITOOLS_CLIENT_SECRET ?? "",
    redirectUri:
      process.env.ADOFAITOOLS_REDIRECT_URI ??
      "http://127.0.0.1:39810/api/auth/callback",
    authorizeUrl: "https://www.adofaitools.top/#/oauth/authorize",
    tokenUrl: "https://server.adofaitools.top/oauth/token",
    userinfoUrl: "https://server.adofaitools.top/oauth/userinfo"
  }
};

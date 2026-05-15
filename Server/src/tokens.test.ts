import { describe, expect, it } from "vitest";
import { issueRelayToken, verifyRelayToken } from "./tokens.js";

describe("relay tokens", () => {
  it("round-trips a signed user payload", () => {
    const token = issueRelayToken({
      userId: "user-1",
      username: "tester",
      nickname: "测试用户",
      avatarUrl: "",
      email: ""
    });

    expect(verifyRelayToken(token)?.userId).toBe("user-1");
  });

  it("rejects tampered tokens", () => {
    const token = issueRelayToken({
      userId: "user-1",
      username: "tester",
      nickname: "测试用户",
      avatarUrl: "",
      email: ""
    });

    expect(verifyRelayToken(token + "x")).toBeNull();
  });
});

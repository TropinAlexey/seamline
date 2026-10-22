import { decodeSession, isExpired } from "./token";

function makeToken(payload: Record<string, unknown>): string {
  const encode = (value: unknown) =>
    btoa(JSON.stringify(value)).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
  return `${encode({ alg: "HS256", typ: "JWT" })}.${encode(payload)}.signature`;
}

const validPayload = {
  "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier": "user-id",
  "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name": "trader",
  "http://schemas.microsoft.com/ws/2008/06/identity/claims/role": "FO",
  tenant_id: "11111111-1111-1111-1111-111111111111",
  exp: 2_000_000_000,
  iss: "seamline-mini-ctrm",
};

describe("decodeSession", () => {
  it("reads login, role, tenant and expiry from the long claim names", () => {
    const token = makeToken(validPayload);
    expect(decodeSession(token)).toEqual({
      token,
      login: "trader",
      role: "FO",
      tenantId: "11111111-1111-1111-1111-111111111111",
      expiresAt: 2_000_000_000_000,
    });
  });

  it("rejects a token with an unknown role", () => {
    const token = makeToken({
      ...validPayload,
      "http://schemas.microsoft.com/ws/2008/06/identity/claims/role": "ADMIN",
    });
    expect(decodeSession(token)).toBeNull();
  });

  it("rejects garbage", () => {
    expect(decodeSession("not-a-jwt")).toBeNull();
    expect(decodeSession("a.%%%.c")).toBeNull();
  });

  it("knows when the session has expired", () => {
    const session = decodeSession(makeToken(validPayload))!;
    expect(isExpired(session, 1_999_999_999_000)).toBe(false);
    expect(isExpired(session, 2_000_000_000_000)).toBe(true);
  });
});

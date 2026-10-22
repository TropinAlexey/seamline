import { isRole, type Role } from "./roles";

// JwtTokenFactory builds the token from System.Security.Claims.ClaimTypes
// and passes the claims straight into JwtSecurityToken, so the payload keys
// are the long WS-* URIs, not the short "role"/"unique_name" names
// (verified against a real token; see JwtTokenFactoryTests).
const claimKeys = {
  name: "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name",
  role: "http://schemas.microsoft.com/ws/2008/06/identity/claims/role",
  tenantId: "tenant_id",
} as const;

export interface Session {
  token: string;
  login: string;
  role: Role;
  tenantId: string;
  // Expiry as epoch milliseconds.
  expiresAt: number;
}

function decodeBase64Url(segment: string): string {
  const base64 = segment.replace(/-/g, "+").replace(/_/g, "/");
  const padded = base64.padEnd(base64.length + ((4 - (base64.length % 4)) % 4), "=");
  const binary = atob(padded);
  const bytes = Uint8Array.from(binary, (c) => c.charCodeAt(0));
  return new TextDecoder().decode(bytes);
}

// Decodes without verifying the signature — that is the server's job. The
// result drives display (role badge, hidden actions) only; every action is
// still authorised server-side.
export function decodeSession(token: string): Session | null {
  const payloadSegment = token.split(".")[1];
  if (!payloadSegment) return null;

  let payload: Record<string, unknown>;
  try {
    payload = JSON.parse(decodeBase64Url(payloadSegment)) as Record<string, unknown>;
  } catch {
    return null;
  }

  const login = payload[claimKeys.name];
  const role = payload[claimKeys.role];
  const tenantId = payload[claimKeys.tenantId];
  const exp = payload.exp;
  if (
    typeof login !== "string" ||
    !isRole(role) ||
    typeof tenantId !== "string" ||
    typeof exp !== "number"
  ) {
    return null;
  }

  return { token, login, role, tenantId, expiresAt: exp * 1000 };
}

export function isExpired(session: Session, now: number = Date.now()): boolean {
  return session.expiresAt <= now;
}

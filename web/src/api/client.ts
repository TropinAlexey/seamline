import createClient, { type Middleware } from "openapi-fetch";
import { getSession, setSession } from "../auth/sessionStore";
import type { components, paths } from "./schema";

export type Schemas = components["schemas"];

// Same-origin: every proxy in front of the SPA strips /api (ADR-0030).
export const api = createClient<paths>({ baseUrl: "/api" });

export type LogoutReason = "expired" | null;
let pendingLogoutReason: LogoutReason = null;

export function takeLogoutReason(): LogoutReason {
  const reason = pendingLogoutReason;
  pendingLogoutReason = null;
  return reason;
}

export function markSessionExpired(): void {
  pendingLogoutReason = "expired";
  setSession(null);
}

const auth: Middleware = {
  onRequest({ request }) {
    const session = getSession();
    if (session) request.headers.set("Authorization", `Bearer ${session.token}`);
    return request;
  },
  onResponse({ request, response }) {
    // A 401 on login itself just means wrong credentials; anywhere else the
    // token is gone or expired, so drop it and send the user back to login.
    if (response.status === 401 && !new URL(request.url).pathname.endsWith("/auth/login")) {
      markSessionExpired();
    }
    return response;
  },
};

api.use(auth);

export class ApiError extends Error {
  readonly status: number;

  constructor(status: number, message: string) {
    super(message);
    this.name = "ApiError";
    this.status = status;
  }
}

function messageFor(status: number, body: unknown, fallback: string): string {
  if (body && typeof body === "object" && "error" in body && typeof body.error === "string") {
    return body.error;
  }
  if (status === 403) return "Your role can't do this.";
  if (status === 429) return "Too many attempts, try again in a minute.";
  return fallback || `Request failed (${status})`;
}

interface FetchResult<T> {
  data?: T;
  error?: unknown;
  response: Response;
}

// Turns openapi-fetch's { data, error } into a value or a thrown ApiError,
// so TanStack Query sees failures as failures.
export async function unwrap<T>(request: Promise<FetchResult<T>>): Promise<T> {
  let result: FetchResult<T>;
  try {
    result = await request;
  } catch {
    throw new ApiError(0, "Network error — is the API running?");
  }

  const { data, error, response } = result;
  if (!response.ok) {
    const apiError = new ApiError(
      response.status,
      messageFor(response.status, error, response.statusText),
    );
    if (response.status === 403) {
      // Role gating should have hidden the action — log so it gets noticed.
      console.warn("Forbidden:", response.url, apiError.message);
    }
    throw apiError;
  }
  return data as T;
}

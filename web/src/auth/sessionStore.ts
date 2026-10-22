import { decodeSession, isExpired, type Session } from "./token";

// In memory, mirrored to sessionStorage so a reload keeps the session but a
// new tab does not (ADR-0030). Kept outside React so the API client's
// middleware can read the token without a hook.
const storageKey = "seamline.token";

let current: Session | null = load();
const listeners = new Set<() => void>();

function load(): Session | null {
  try {
    const token = sessionStorage.getItem(storageKey);
    const session = token ? decodeSession(token) : null;
    return session && !isExpired(session) ? session : null;
  } catch {
    return null;
  }
}

export function getSession(): Session | null {
  return current;
}

export function setSession(session: Session | null): void {
  current = session;
  try {
    if (session) sessionStorage.setItem(storageKey, session.token);
    else sessionStorage.removeItem(storageKey);
  } catch {
    // Storage blocked (private mode): the in-memory session still works for this page.
  }
  listeners.forEach((listener) => listener());
}

export function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  return () => listeners.delete(listener);
}

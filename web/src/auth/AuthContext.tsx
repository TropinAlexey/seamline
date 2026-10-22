import { createContext, use, useEffect, useSyncExternalStore, type ReactNode } from "react";
import { api, ApiError, markSessionExpired, unwrap } from "../api/client";
import { getSession, setSession, subscribe } from "./sessionStore";
import { decodeSession, type Session } from "./token";

interface AuthValue {
  session: Session | null;
  login: (tenantId: string, login: string, password: string) => Promise<Session>;
  logout: () => void;
}

const AuthContext = createContext<AuthValue | null>(null);

export function AuthProvider({ children }: { children: ReactNode }) {
  const session = useSyncExternalStore(subscribe, getSession, getSession);

  // Proactive logout when the 8-hour token expires, rather than waiting for
  // the next request to come back 401.
  useEffect(() => {
    if (!session) return;
    const timeout = setTimeout(markSessionExpired, Math.max(0, session.expiresAt - Date.now()));
    return () => clearTimeout(timeout);
  }, [session]);

  const value: AuthValue = {
    session,
    async login(tenantId, login, password) {
      const { token } = await unwrap(
        api.POST("/auth/login", { body: { tenantId, login, password } }),
      );
      const decoded = decodeSession(token);
      if (!decoded) throw new ApiError(500, "The server returned a token this UI can't read.");
      setSession(decoded);
      return decoded;
    },
    logout() {
      setSession(null);
    },
  };

  return <AuthContext value={value}>{children}</AuthContext>;
}

export function useAuth(): AuthValue {
  const value = use(AuthContext);
  if (!value) throw new Error("useAuth must be used inside <AuthProvider>.");
  return value;
}

export function useSession(): Session {
  const { session } = useAuth();
  if (!session) throw new Error("useSession requires an authenticated route.");
  return session;
}

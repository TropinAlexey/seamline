import type { ReactNode } from "react";
import { Navigate, useLocation } from "react-router";
import { useAuth } from "./AuthContext";
import { homeFor, type Role } from "./roles";

// Route guard. Hiding is UX only — the API enforces every role with 403.
export function RequireRole({ roles, children }: { roles?: readonly Role[]; children: ReactNode }) {
  const { session } = useAuth();
  const location = useLocation();

  if (!session) {
    return <Navigate to="/login" replace state={{ from: location.pathname }} />;
  }
  if (roles && !roles.includes(session.role)) {
    return <Navigate to={homeFor(session.role)} replace />;
  }
  return <>{children}</>;
}

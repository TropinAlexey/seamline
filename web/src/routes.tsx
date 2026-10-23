import { Navigate, Route, Routes } from "react-router";
import { useAuth } from "./auth/AuthContext";
import { RequireRole } from "./auth/RequireRole";
import { homeFor } from "./auth/roles";
import { AppLayout } from "./components/AppLayout";
import { ApprovalsPage } from "./features/approvals/ApprovalsPage";
import { AuditPage } from "./features/audit/AuditPage";
import { CommoditiesPage } from "./features/commodities/CommoditiesPage";
import { CounterpartiesPage } from "./features/counterparties/CounterpartiesPage";
import { CurvesPage } from "./features/curves/CurvesPage";
import { InvoicesPage } from "./features/invoices/InvoicesPage";
import { LoginPage } from "./features/login/LoginPage";
import { PositionsPage } from "./features/positions/PositionsPage";
import { ShipmentsPage } from "./features/shipments/ShipmentsPage";
import { StressPage } from "./features/stress/StressPage";
import { TradeDetailPage } from "./features/trades/TradeDetailPage";
import { TradesPage } from "./features/trades/TradesPage";

function Home() {
  const { session } = useAuth();
  return <Navigate to={session ? homeFor(session.role) : "/login"} replace />;
}

// Role lists match AppLayout's navItems and the API's RequireRole policies.
export function AppRoutes() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route
        element={
          <RequireRole>
            <AppLayout />
          </RequireRole>
        }
      >
        <Route path="/trades" element={<TradesPage />} />
        <Route path="/trades/:id" element={<TradeDetailPage />} />
        <Route path="/counterparties" element={<CounterpartiesPage />} />
        <Route path="/commodities" element={<CommoditiesPage />} />
        <Route path="/shipments" element={<ShipmentsPage />} />
        <Route path="/positions" element={<PositionsPage />} />
        <Route path="/curves" element={<CurvesPage />} />
        <Route
          path="/stress"
          element={
            <RequireRole roles={["MO", "BO"]}>
              <StressPage />
            </RequireRole>
          }
        />
        <Route
          path="/audit"
          element={
            <RequireRole roles={["MO", "BO"]}>
              <AuditPage />
            </RequireRole>
          }
        />
        <Route
          path="/approvals"
          element={
            <RequireRole roles={["MO"]}>
              <ApprovalsPage />
            </RequireRole>
          }
        />
        <Route
          path="/invoices"
          element={
            <RequireRole roles={["BO"]}>
              <InvoicesPage />
            </RequireRole>
          }
        />
      </Route>
      <Route path="*" element={<Home />} />
    </Routes>
  );
}

import type { Role } from "../auth/roles";
import { navItems } from "./AppLayout";

const visibleTo = (role: Role) =>
  navItems.filter((item) => item.roles.includes(role)).map((item) => item.to);

describe("navigation role gating", () => {
  it("shows each role only the screens its API calls allow", () => {
    expect(visibleTo("FO")).toEqual([
      "/trades",
      "/shipments",
      "/storage",
      "/positions",
      "/curves",
      "/counterparties",
      "/commodities",
    ]);
    expect(visibleTo("MO")).toEqual([
      "/trades",
      "/approvals",
      "/shipments",
      "/storage",
      "/positions",
      "/stress",
      "/curves",
      "/counterparties",
      "/commodities",
      "/audit",
    ]);
    expect(visibleTo("BO")).toEqual([
      "/trades",
      "/shipments",
      "/storage",
      "/positions",
      "/stress",
      "/curves",
      "/counterparties",
      "/commodities",
      "/invoices",
      "/audit",
    ]);
  });
});

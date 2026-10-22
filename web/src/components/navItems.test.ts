import type { Role } from "../auth/roles";
import { navItems } from "./AppLayout";

const visibleTo = (role: Role) =>
  navItems.filter((item) => item.roles.includes(role)).map((item) => item.to);

describe("navigation role gating", () => {
  it("shows each role only the screens its API calls allow", () => {
    expect(visibleTo("FO")).toEqual(["/trades", "/positions", "/curves", "/counterparties"]);
    expect(visibleTo("MO")).toEqual([
      "/trades",
      "/approvals",
      "/positions",
      "/stress",
      "/curves",
      "/counterparties",
      "/audit",
    ]);
    expect(visibleTo("BO")).toEqual([
      "/trades",
      "/positions",
      "/stress",
      "/curves",
      "/counterparties",
      "/invoices",
      "/audit",
    ]);
  });
});

export type Role = "FO" | "MO" | "BO";

export const roleLabels: Record<Role, string> = {
  FO: "Front office",
  MO: "Middle office",
  BO: "Back office",
};

export function isRole(value: unknown): value is Role {
  return value === "FO" || value === "MO" || value === "BO";
}

// Where each role lands after login (spec §4.1).
export function homeFor(role: Role): string {
  switch (role) {
    case "FO":
      return "/trades";
    case "MO":
      return "/approvals";
    case "BO":
      return "/invoices";
  }
}

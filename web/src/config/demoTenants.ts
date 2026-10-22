// Static on purpose (ADR-0030): the API has no tenant-discovery endpoint,
// and a public tenant list would be an information leak in a real system.
// These are the two tenants the Identity migrations seed demo users into.
export interface DemoTenant {
  id: string;
  name: string;
}

export const demoTenants: readonly DemoTenant[] = [
  { id: "11111111-1111-1111-1111-111111111111", name: "Tenant A — Alder Energy" },
  { id: "22222222-2222-2222-2222-222222222222", name: "Tenant B — Birch Power" },
];

export const demoLogins = ["trader", "risk", "backoffice"] as const;

// Seeded by migration and documented in the README — not a secret.
export const demoPassword = "Demo-Password-123!";

export function tenantName(id: string): string {
  return demoTenants.find((t) => t.id === id)?.name ?? id;
}

export function otherTenant(id: string): DemoTenant {
  return demoTenants.find((t) => t.id !== id) ?? demoTenants[0]!;
}

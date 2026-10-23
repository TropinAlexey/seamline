import { request, type Page } from "@playwright/test";

export const tenants = {
  A: "11111111-1111-1111-1111-111111111111",
  B: "22222222-2222-2222-2222-222222222222",
} as const;

export type TenantKey = keyof typeof tenants;
export type DemoUser = "trader" | "risk" | "backoffice";

const baseURL = process.env.E2E_BASE_URL ?? "http://localhost:8081";
const password = "Demo-Password-123!";

// One login per (tenant, user) for the whole run: the API rate-limits login
// per client IP. The token goes into sessionStorage before the app boots —
// Playwright's storageState doesn't capture sessionStorage, which is where
// the UI keeps it (ADR-0030).
const tokens = new Map<string, string>();

export async function tokenFor(tenant: TenantKey, user: DemoUser): Promise<string> {
  const key = `${tenant}:${user}`;
  const cached = tokens.get(key);
  if (cached) return cached;

  const context = await request.newContext({ baseURL });
  const response = await context.post("/api/auth/login", {
    data: { tenantId: tenants[tenant], login: user, password },
  });
  if (!response.ok()) throw new Error(`Login ${key} failed: ${response.status()}`);
  const { token } = (await response.json()) as { token: string };
  await context.dispose();
  tokens.set(key, token);
  return token;
}

export async function loginAs(page: Page, tenant: TenantKey, user: DemoUser): Promise<void> {
  const token = await tokenFor(tenant, user);
  await page.addInitScript((value) => sessionStorage.setItem("seamline.token", value), token);
}

// API-side setup, so each spec arranges its own data instead of depending
// on what the seed tool or an earlier spec left behind.
export async function api(tenant: TenantKey, user: DemoUser) {
  const token = await tokenFor(tenant, user);
  const context = await request.newContext({
    baseURL: `${baseURL}/api/`,
    extraHTTPHeaders: { Authorization: `Bearer ${token}` },
  });

  async function post<T>(path: string, data?: unknown): Promise<T> {
    const response = await context.post(path, { data });
    if (!response.ok())
      throw new Error(`POST ${path} → ${response.status()}: ${await response.text()}`);
    const text = await response.text();
    return (text ? JSON.parse(text) : undefined) as T;
  }

  return {
    async counterparty(name: string, creditLimit: number) {
      return post<{ id: string; name: string }>("counterparties", { name, creditLimit });
    },
    async trade(body: {
      commodityCode: string;
      deliveryPeriod: string;
      direction: "Buy" | "Sell";
      volume: number;
      price: number;
      counterpartyId: string;
      cargoTerms?: { tolerancePct: number; laycanFrom: string; laycanTo: string };
    }) {
      return (await post<{ id: string }>("trades", body)).id;
    },
    async action(tradeId: string, action: "submit" | "approve" | "reject" | "deliver") {
      return post(`trades/${tradeId}/${action}`);
    },
    dispose: () => context.dispose(),
  };
}

export function unique(prefix: string): string {
  return `${prefix} ${Date.now().toString(36)}`;
}

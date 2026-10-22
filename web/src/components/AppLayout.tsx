import { AppShell, Badge, Button, Group, NavLink, Stack, Text, Title } from "@mantine/core";
import { NavLink as RouterNavLink, Outlet, useLocation, useNavigate } from "react-router";
import { useAuth, useSession } from "../auth/AuthContext";
import { roleLabels, type Role } from "../auth/roles";
import { otherTenant, tenantName } from "../config/demoTenants";

interface NavItem {
  to: string;
  label: string;
  roles: readonly Role[];
}

// Nav mirrors the route guards in routes.tsx: an item a role can't open is
// not shown at all (spec §4).
export const navItems: readonly NavItem[] = [
  { to: "/trades", label: "Trades", roles: ["FO", "MO", "BO"] },
  { to: "/approvals", label: "Approvals", roles: ["MO"] },
  { to: "/positions", label: "Positions & MTM", roles: ["FO", "MO", "BO"] },
  { to: "/stress", label: "Stress", roles: ["MO", "BO"] },
  { to: "/curves", label: "Market data", roles: ["FO", "MO", "BO"] },
  { to: "/counterparties", label: "Counterparties", roles: ["FO", "MO", "BO"] },
  { to: "/invoices", label: "Invoices", roles: ["BO"] },
  { to: "/audit", label: "Audit trail", roles: ["MO", "BO"] },
];

export function AppLayout() {
  const session = useSession();
  const { logout } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();

  const switchTenant = () => {
    const target = otherTenant(session.tenantId);
    logout();
    navigate(`/login?tenant=${target.id}&login=${encodeURIComponent(session.login)}`);
  };

  return (
    <AppShell header={{ height: 60 }} navbar={{ width: 200, breakpoint: "sm" }} padding="md">
      <AppShell.Header>
        <Group h="100%" px="md" justify="space-between" wrap="nowrap">
          <Group gap="sm" wrap="nowrap">
            <Title order={3}>Seamline</Title>
            <Text c="dimmed" size="sm" data-testid="tenant-name">
              {tenantName(session.tenantId)}
            </Text>
          </Group>
          <Group gap="sm" wrap="nowrap">
            <Text size="sm">{session.login}</Text>
            <Badge variant="filled" data-testid="role-badge" title={roleLabels[session.role]}>
              {session.role}
            </Badge>
            <Button size="xs" variant="light" onClick={switchTenant}>
              Switch tenant
            </Button>
            <Button size="xs" variant="subtle" onClick={logout}>
              Log out
            </Button>
          </Group>
        </Group>
      </AppShell.Header>
      <AppShell.Navbar p="xs">
        <Stack gap={2}>
          {navItems
            .filter((item) => item.roles.includes(session.role))
            .map((item) => (
              <NavLink
                key={item.to}
                component={RouterNavLink}
                to={item.to}
                label={item.label}
                active={location.pathname.startsWith(item.to)}
              />
            ))}
        </Stack>
      </AppShell.Navbar>
      <AppShell.Main>
        <Outlet />
      </AppShell.Main>
    </AppShell>
  );
}

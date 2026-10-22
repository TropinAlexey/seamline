import {
  Alert,
  Anchor,
  Button,
  Center,
  Code,
  Divider,
  Group,
  Paper,
  PasswordInput,
  Select,
  Stack,
  Table,
  Text,
  TextInput,
  Title,
} from "@mantine/core";
import { useState, type FormEvent } from "react";
import { Navigate, useNavigate, useSearchParams } from "react-router";
import { ApiError, takeLogoutReason } from "../../api/client";
import { useAuth } from "../../auth/AuthContext";
import { homeFor } from "../../auth/roles";
import { demoLogins, demoPassword, demoTenants } from "../../config/demoTenants";

function loginErrorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 401) return "Invalid credentials.";
    if (error.status === 429) return "Too many attempts, try again in a minute.";
    return error.message;
  }
  return String(error);
}

export function LoginPage() {
  const { session, login } = useAuth();
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();

  const [tenantId, setTenantId] = useState(searchParams.get("tenant") ?? demoTenants[0]!.id);
  const [userLogin, setUserLogin] = useState(searchParams.get("login") ?? "");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const [expired] = useState(() => takeLogoutReason() === "expired");

  if (session) return <Navigate to={homeFor(session.role)} replace />;

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    setSubmitting(true);
    setError(null);
    try {
      const newSession = await login(tenantId, userLogin, password);
      navigate(homeFor(newSession.role), { replace: true });
    } catch (e) {
      setError(loginErrorMessage(e));
    } finally {
      setSubmitting(false);
    }
  };

  const fill = (tenant: string, demoLogin: string) => {
    setTenantId(tenant);
    setUserLogin(demoLogin);
    setPassword(demoPassword);
  };

  return (
    <Center mih="100vh" p="md">
      <Stack w={460} maw="100%">
        <Title order={2}>Seamline</Title>
        <Text c="dimmed" size="sm">
          Mini-CTRM demo: power &amp; gas forwards, four-eyes credit approval, two isolated tenants.
        </Text>
        {expired && (
          <Alert color="yellow" title="Session expired">
            Please log in again.
          </Alert>
        )}
        <Paper withBorder p="lg" component="form" onSubmit={submit}>
          <Stack>
            <Select
              label="Tenant"
              data={demoTenants.map((t) => ({ value: t.id, label: t.name }))}
              value={tenantId}
              onChange={(value) => value && setTenantId(value)}
              allowDeselect={false}
            />
            <TextInput
              label="Login"
              value={userLogin}
              onChange={(e) => setUserLogin(e.currentTarget.value)}
              autoComplete="username"
              required
            />
            <PasswordInput
              label="Password"
              value={password}
              onChange={(e) => setPassword(e.currentTarget.value)}
              autoComplete="current-password"
              required
            />
            {error && (
              <Alert color="red" role="alert">
                {error}
              </Alert>
            )}
            <Button type="submit" loading={submitting}>
              Log in
            </Button>
          </Stack>
        </Paper>

        <Paper withBorder p="lg">
          <Text fw={600}>Demo accounts</Text>
          <Text size="sm" c="dimmed">
            Demo only — password <Code>{demoPassword}</Code> for every account.
          </Text>
          <Divider my="sm" />
          <Table>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Tenant</Table.Th>
                <Table.Th>Fill</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {demoTenants.map((tenant) => (
                <Table.Tr key={tenant.id}>
                  <Table.Td>{tenant.name}</Table.Td>
                  <Table.Td>
                    <Group gap="xs">
                      {demoLogins.map((demoLogin) => (
                        <Anchor
                          key={demoLogin}
                          component="button"
                          type="button"
                          size="sm"
                          onClick={() => fill(tenant.id, demoLogin)}
                        >
                          {demoLogin}
                        </Anchor>
                      ))}
                    </Group>
                  </Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
          <Text size="xs" c="dimmed" mt="xs">
            trader = front office (FO), risk = middle office (MO), backoffice = back office (BO)
          </Text>
        </Paper>
      </Stack>
    </Center>
  );
}

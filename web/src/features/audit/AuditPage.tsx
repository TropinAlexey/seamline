import { Anchor, Group, Pagination, Stack, Table, Text, TextInput, Title } from "@mantine/core";
import { useState } from "react";
import { Link } from "react-router";
import { useAuditEvents } from "../../api/queries/audit";
import { QueryState } from "../../components/QueryState";
import { formatDateTime, shortId } from "../../format/date";

const pageSize = 50;
const guidPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

// Written by Audit's consumers from integration events only (ADR-0010) —
// it never reads another module's schema.
export function AuditPage() {
  const [page, setPage] = useState(1);
  const [entityFilter, setEntityFilter] = useState("");
  const entityId = guidPattern.test(entityFilter.trim()) ? entityFilter.trim() : undefined;
  const events = useAuditEvents(page, pageSize, entityId);
  const total = events.data?.total ?? 0;

  return (
    <Stack>
      <Title order={2}>Audit trail</Title>
      <TextInput
        label="Entity id"
        description="Paste a full trade id to see its trail"
        value={entityFilter}
        onChange={(e) => {
          setEntityFilter(e.currentTarget.value);
          setPage(1);
        }}
        w={420}
      />
      <QueryState
        isLoading={events.isLoading}
        error={events.error}
        onRetry={() => events.refetch()}
      >
        {events.data?.items.length ? (
          <Table striped>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Time</Table.Th>
                <Table.Th>Actor</Table.Th>
                <Table.Th>Action</Table.Th>
                <Table.Th>Entity</Table.Th>
                <Table.Th>Context</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {events.data.items.map((event) => (
                <Table.Tr key={event.id} data-testid="audit-row">
                  <Table.Td>{formatDateTime(event.occurredAt)}</Table.Td>
                  <Table.Td>{event.actor}</Table.Td>
                  <Table.Td>{event.action}</Table.Td>
                  <Table.Td>
                    {event.entityType === "Trade" ? (
                      <Anchor component={Link} to={`/trades/${event.entityId}`} ff="monospace">
                        Trade {shortId(event.entityId)}
                      </Anchor>
                    ) : (
                      `${event.entityType} ${shortId(event.entityId)}`
                    )}
                  </Table.Td>
                  <Table.Td>{event.context}</Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        ) : (
          <Text c="dimmed">No audit events.</Text>
        )}
        <Group justify="space-between">
          <Text size="sm" c="dimmed">
            {total} event{total === 1 ? "" : "s"}
          </Text>
          <Pagination
            total={Math.max(1, Math.ceil(total / pageSize))}
            value={page}
            onChange={setPage}
          />
        </Group>
      </QueryState>
    </Stack>
  );
}

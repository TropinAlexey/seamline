import { Anchor, Stack, Table, Text, Title } from "@mantine/core";
import { Link } from "react-router";
import { useCounterpartyNames } from "../../api/queries/reference";
import { useInvoices } from "../../api/queries/settlement";
import { QueryState } from "../../components/QueryState";
import { formatDateTime, shortId } from "../../format/date";
import { formatAmount } from "../../format/money";

export function InvoicesPage() {
  const invoices = useInvoices();
  const counterpartyName = useCounterpartyNames();

  return (
    <Stack>
      <Title order={2}>Invoices</Title>
      <Text c="dimmed" size="sm">
        Issued by Settlement when it consumes a TradeDelivered event.
      </Text>
      <QueryState
        isLoading={invoices.isLoading}
        error={invoices.error}
        onRetry={() => invoices.refetch()}
      >
        {invoices.data?.length ? (
          <Table striped>
            <Table.Thead>
              <Table.Tr>
                <Table.Th>Trade</Table.Th>
                <Table.Th>Counterparty</Table.Th>
                <Table.Th ta="right">Amount</Table.Th>
                <Table.Th>Issued at</Table.Th>
              </Table.Tr>
            </Table.Thead>
            <Table.Tbody>
              {invoices.data.map((invoice) => (
                <Table.Tr
                  key={invoice.tradeId}
                  data-testid="invoice-row"
                  data-trade-id={invoice.tradeId}
                >
                  <Table.Td>
                    <Anchor component={Link} to={`/trades/${invoice.tradeId}`} ff="monospace">
                      {shortId(invoice.tradeId)}
                    </Anchor>
                  </Table.Td>
                  <Table.Td>{counterpartyName(invoice.counterpartyId)}</Table.Td>
                  <Table.Td ta="right">{formatAmount(invoice.amount)}</Table.Td>
                  <Table.Td>{formatDateTime(invoice.issuedAt)}</Table.Td>
                </Table.Tr>
              ))}
            </Table.Tbody>
          </Table>
        ) : (
          <Text c="dimmed">No invoices yet — deliver an active trade to issue one.</Text>
        )}
      </QueryState>
    </Stack>
  );
}

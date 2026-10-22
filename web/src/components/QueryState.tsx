import { Alert, Button, Center, Group, Loader } from "@mantine/core";
import type { ReactNode } from "react";
import { errorMessage } from "./notify";

interface Props {
  isLoading: boolean;
  error: unknown;
  onRetry?: () => void;
  children: ReactNode;
}

// Loading / error / content for a query-backed section, so no page silently
// renders an empty table when the request actually failed.
export function QueryState({ isLoading, error, onRetry, children }: Props) {
  if (isLoading) {
    return (
      <Center py="xl">
        <Loader />
      </Center>
    );
  }
  if (error) {
    return (
      <Alert color="red" title="Could not load data">
        <Group justify="space-between">
          {errorMessage(error)}
          {onRetry && (
            <Button size="xs" variant="light" onClick={onRetry}>
              Retry
            </Button>
          )}
        </Group>
      </Alert>
    );
  }
  return <>{children}</>;
}

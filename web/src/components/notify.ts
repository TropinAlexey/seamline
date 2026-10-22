import { notifications } from "@mantine/notifications";
import { ApiError } from "../api/client";

export function errorMessage(error: unknown): string {
  return error instanceof Error ? error.message : String(error);
}

export function notifyError(error: unknown, title = "Request failed") {
  notifications.show({ color: "red", title, message: errorMessage(error) });
}

export function notifySuccess(message: string, title = "Done") {
  notifications.show({ color: "green", title, message });
}

export function isConflict(error: unknown): boolean {
  return error instanceof ApiError && error.status === 409;
}

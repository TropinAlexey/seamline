import "@mantine/core/styles.css";
import "@mantine/notifications/styles.css";
import { MantineProvider } from "@mantine/core";
import { Notifications } from "@mantine/notifications";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter } from "react-router";
import { ApiError } from "./api/client";
import { AuthProvider } from "./auth/AuthContext";
import { AppRoutes } from "./routes";

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      // 4xx won't fix itself on retry; network blips and 5xx might.
      retry: (failureCount, error) =>
        failureCount < 2 &&
        !(error instanceof ApiError && error.status >= 400 && error.status < 500),
      refetchOnWindowFocus: true,
    },
  },
});

createRoot(document.getElementById("root")!).render(
  <StrictMode>
    <MantineProvider defaultColorScheme="auto">
      <Notifications position="top-right" />
      <QueryClientProvider client={queryClient}>
        <AuthProvider>
          <BrowserRouter>
            <AppRoutes />
          </BrowserRouter>
        </AuthProvider>
      </QueryClientProvider>
    </MantineProvider>
  </StrictMode>,
);

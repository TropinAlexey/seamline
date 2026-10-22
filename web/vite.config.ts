/// <reference types="vitest/config" />
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

// Same-origin in every environment (ADR-0030): the SPA calls /api/*, and the
// proxy — this dev server, nginx in compose, the ingress in k8s — strips the
// prefix and forwards to the API. Point VITE_API_TARGET at
// http://localhost:5298 when running the API with `dotnet run`.
const apiTarget = process.env.VITE_API_TARGET ?? "http://localhost:5000";

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    proxy: {
      "/api": {
        target: apiTarget,
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/api/, ""),
      },
    },
  },
  test: {
    environment: "jsdom",
    globals: true,
    setupFiles: ["./src/test/setup.ts"],
    include: ["src/**/*.test.{ts,tsx}"],
    css: false,
  },
});
